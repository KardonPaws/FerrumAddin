using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI.Selection;
using System;
using System.Globalization;
using System.Net;
using FerrumAddinDev.GrillageCreator_v2;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows;
using Autodesk.Revit.DB.Structure;
using System.Security.Cryptography;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Rebar = Autodesk.Revit.DB.Structure.Rebar;
using MessageBox = System.Windows.MessageBox;
using System.Xml.Linq;

namespace FerrumAddinDev.GrillageCreator_v3
{
    [Transaction(TransactionMode.Manual)]
    public class CommandGrillageCreator_v3 : IExternalCommand
    {
        public static ExternalEvent createGrillage;
        public static ExternalEvent createGrillageLines;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            List<Element> rebarTypes = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x => x.Name.Contains("к_")).ToList();
            List<Element> rebarTypesCorner = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x => x.Name.Contains("д_")).ToList();
            List<Element> rebarTypesHorizontal = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x => !x.Name.Contains("_")).ToList();
            List<Element> rebarTypesKnitted = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x => !x.Name.Contains("_") || x.Name.StartsWith("мп_")).ToList();

            createGrillage = ExternalEvent.Create(new CreateGrillage_v3(false));
            createGrillageLines = ExternalEvent.Create(new CreateGrillage_v3(true));
            WindowGrillageCreator_v3 window = new WindowGrillageCreator_v3(rebarTypes, rebarTypesHorizontal, rebarTypesCorner, rebarTypesKnitted);
            window.Show();

            return Result.Succeeded;
        }
    }
    public class CreateGrillage_v3 : IExternalEventHandler
    {
        private const double GeometryTolerance = 1e-6;
        private const string GrillageLineStyleName = "Ferrum_Ростверк_Ось_армирования";
        private static readonly Guid GrillageLineSchemaGuid = new Guid("9A48B51C-8B0D-46F7-B22A-FE9A0A630D2B");
        private readonly bool createModelLinesOnly;

        public string message = "";
        public static Document d;

        public CreateGrillage_v3() : this(false)
        {
        }

        public CreateGrillage_v3(bool createModelLinesOnly)
        {
            this.createModelLinesOnly = createModelLinesOnly;
        }

        public void Execute(UIApplication uiApp)
        {
            try
            {
                if (createModelLinesOnly)
                    ExecuteCreateModelLines(uiApp);
                else
                    ExecuteCreateRebarsFromSelectedLines(uiApp);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
            }
            catch (Exception ex)
            {

            }
        }


        private void ExecuteCreateModelLines(UIApplication uiApp)
        {
            if (createModelLinesOnly)
            {
                CreateModelLinesFromSelectedFloors(uiApp);
                return;
            }

            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            d = doc;
            List<Element> rebarTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).WhereElementIsElementType().ToList();
            List<Element> rearCoverTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarCoverType)).ToList();
            // Получаем выбранный элемент (перекрытие)
            List<Reference> elements = (List<Reference>)uiDoc.Selection.PickObjects(ObjectType.Element);
            corners = new List<XYZ>();

            if (elements == null)
            {
                message = "Элементы не выбраны.";
                return;

            }
            using (TransactionGroup tg = new TransactionGroup(doc))
            {
                tg.Start("Армирование ростверков");
                foreach (Reference reference in elements)
                {
                    Element element = doc.GetElement(reference.ElementId);
                    // 23.10.25 - исправления в ростверках
                    if (!(element is Floor))
                        continue;
                    // Получаем SketchId перекрытия
                    Sketch sketch = doc.GetElement((element as Floor).SketchId) as Sketch;
                    if (sketch == null)
                    {
                        message = "Не удалось получить Sketch перекрытия.";
                        return;
                    }

                    // Получаем Profile из Sketch
                    CurveArrArray profile = sketch.Profile;
                    if (profile == null)
                    {
                        message = "Не удалось получить Profile из Sketch.";
                        return;
                    }

                    Parameter thicknessParam = element.LookupParameter("Толщина");
                    if (thicknessParam == null || thicknessParam.StorageType != StorageType.Double)
                    {
                        MessageBox.Show("Не удалось получить параметр 'Толщина'.", "Ошибка");
                        return;
                    }
                    double thickness = thicknessParam.AsDouble();
                    var th = (thickness * XYZ.BasisZ);
                    List<Line> allCurves = new List<Line>();
                    foreach (CurveArray array in profile)
                    {
                        // Собираем все кривые из профиля

                        foreach (Line curveLoop in array)
                        {
                            Line l1 = Line.CreateBound(curveLoop.GetEndPoint(0) + XYZ.BasisZ * element.LookupParameter("Смещение от уровня").AsDouble() - th,
                                curveLoop.GetEndPoint(1) + XYZ.BasisZ * element.LookupParameter("Смещение от уровня").AsDouble() - th);
                            allCurves.Add(l1);
                        }
                    }

                    // Вычисляем средние линии для боковых граней
                    List<Line> centerLines = ComputeCenterLines(allCurves);
                    //CreateModelLines(doc, centerLines);

                    centerLines = PrepareCenterLinesForModelLines(centerLines, allCurves);

                    Dictionary<Line, List<Line>> dictTop = new Dictionary<Line, List<Line>>();
                    Dictionary<Line, List<Line>> dictBottom = new Dictionary<Line, List<Line>>();


                    foreach (Line centerLine in centerLines)
                    {
                        XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

                        // Перпендикулярное направление
                        XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0);

                        // Вычисляем смещения 24.07.25 - отдельное смещение сверху
                        XYZ offsetBottomRight = perpendicularDirection * (modLength - WindowGrillageCreator_v3.leftRightOffset / 304.8) + WindowGrillageCreator_v3.bottomOffset / 304.8 * XYZ.BasisZ;
                        XYZ offsetBottomLeft = perpendicularDirection * (-modLength + WindowGrillageCreator_v3.leftRightOffset / 304.8) + WindowGrillageCreator_v3.bottomOffset / 304.8 * XYZ.BasisZ;
                        XYZ offsetTopRight = perpendicularDirection * (modLength - WindowGrillageCreator_v3.leftRightOffset / 304.8) + (thickness - WindowGrillageCreator_v3.topOffset / 304.8) * XYZ.BasisZ;
                        XYZ offsetTopLeft = perpendicularDirection * (-modLength + WindowGrillageCreator_v3.leftRightOffset / 304.8) + (thickness - WindowGrillageCreator_v3.topOffset / 304.8) * XYZ.BasisZ;

                        // Создаем 4 линии - крайние верхние и нижние линии
                        Line lineBR = Line.CreateBound(centerLine.GetEndPoint(0) + offsetBottomRight, centerLine.GetEndPoint(1) + offsetBottomRight);
                        Line lineBL = Line.CreateBound(centerLine.GetEndPoint(0) + offsetBottomLeft, centerLine.GetEndPoint(1) + offsetBottomLeft);

                        Line lineTR = Line.CreateBound(centerLine.GetEndPoint(0) + offsetTopRight, centerLine.GetEndPoint(1) + offsetTopRight);
                        Line lineTL = Line.CreateBound(centerLine.GetEndPoint(0) + offsetTopLeft, centerLine.GetEndPoint(1) + offsetTopLeft);


                        List<Line> intermediateLinesTop = new List<Line>();
                        List<Line> intermediateLinesBottom = new List<Line>();


                        // Расстояние между линиями
                        double distanceBetweenLines = lineBR.GetEndPoint(0).DistanceTo(lineBL.GetEndPoint(0));
                        // Делим расстояние на равные участки
                        double step = distanceBetweenLines / (WindowGrillageCreator_v3.horizontalCount - 1);

                        intermediateLinesTop.Add(lineTL);
                        intermediateLinesBottom.Add(lineBL);

                        for (int i = 1; i <= WindowGrillageCreator_v3.horizontalCount - 2; i++)
                        {
                            XYZ offset_ = perpendicularDirection * (step * i);
                            Line intermediateLine = Line.CreateBound(lineBL.GetEndPoint(0) + offset_, lineBL.GetEndPoint(1) + offset_);
                            intermediateLinesBottom.Add(intermediateLine);
                            intermediateLine = Line.CreateBound(lineTL.GetEndPoint(0) + offset_, lineTL.GetEndPoint(1) + offset_);
                            intermediateLinesTop.Add(intermediateLine);
                        }

                        intermediateLinesTop.Add(lineTR);
                        dictTop.Add(centerLine, intermediateLinesTop);
                        intermediateLinesBottom.Add(lineBR);
                        dictBottom.Add(centerLine, intermediateLinesBottom);

                        RebarBarType typeTop = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v3.topDiameter).FirstOrDefault() as RebarBarType;
                        RebarBarType typeBot = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v3.bottomDiameter).FirstOrDefault() as RebarBarType;

                        List<Element> rebs = CreateRebarFromLines(doc, intermediateLinesBottom, typeTop, RebarStyle.Standard, element, true);
                        rebs.AddRange(CreateRebarFromLines(doc, intermediateLinesTop, typeBot, RebarStyle.Standard, element, false));

                        using (Transaction trans = new Transaction(doc))
                        {
                            trans.Start("Группа");
                            Group group = doc.Create.NewGroup(rebs.Select(x => x.Id).ToList());
                            trans.Commit();
                        }

                        // Вертикальные линии
                        RebarBarType typeVertical = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v3.vertDiameter).FirstOrDefault() as RebarBarType;

                        // Получаем диаметры арматуры в футах
                        double topRadius = typeTop.BarModelDiameter / 2;
                        double bottomRadius = typeBot.BarModelDiameter / 2;
                        double verticalRadius = typeVertical == null ? 0 : typeVertical.BarModelDiameter / 2;

                        // Вычисляем смещение от края
                        double offsetFromEdge = Math.Max(topRadius, bottomRadius) + verticalRadius;

                        Line verticalLineRightStart = Line.CreateBound(lineBR.GetEndPoint(0), lineTR.GetEndPoint(0));
                        Line verticalLineLeftStart = Line.CreateBound(lineBL.GetEndPoint(0), lineTL.GetEndPoint(0));

                        double verticalCount = WindowGrillageCreator_v3.verticalCount / 304.8;

                        List<Line> verticalLines = new List<Line>();


                        // Начальная и конечная точки для вертикальных линий
                        XYZ startPoint1 = verticalLineRightStart.GetEndPoint(0); // Начальная точка первой линии
                        XYZ endPoint1 = verticalLineRightStart.GetEndPoint(1);   // Конечная точка первой линии
                        XYZ startPoint2 = verticalLineLeftStart.GetEndPoint(0); // Начальная точка второй линии
                        XYZ endPoint2 = verticalLineLeftStart.GetEndPoint(1);   // Конечная точка второй линии

                        // Направление для вертикальных линий 
                        XYZ verticalDirection = (startPoint2 - startPoint1).Normalize();
                        XYZ centerPoint = (startPoint1 + startPoint2) / 2;

                        XYZ rightOffset = verticalDirection * offsetFromEdge;
                        XYZ leftOffset = -verticalDirection * offsetFromEdge;

                        Line offsetRightLine = Line.CreateBound(
                            verticalLineRightStart.GetEndPoint(0) + rightOffset,
                            verticalLineRightStart.GetEndPoint(1) + rightOffset);
                        verticalLines.Add(offsetRightLine);

                        for (int i = 1; i <= WindowGrillageCreator_v3.horizontalCount - 2; i++)
                        {
                            // Вычисляем смещение для текущей линии
                            XYZ offset_ = verticalDirection * (step * i);

                            // Начальная и конечная точки для текущей вертикальной линии
                            XYZ currentStart = startPoint1 + offset_;
                            XYZ currentEnd = endPoint1 + offset_;
                            XYZ curDir = (centerPoint - currentStart).Normalize();

                            if (curDir.IsAlmostEqualTo(verticalDirection))
                            {
                                currentStart = currentStart + offsetFromEdge * verticalDirection;
                                currentEnd = currentEnd + offsetFromEdge * verticalDirection;
                            }
                            else
                            {
                                currentStart = currentStart - offsetFromEdge * verticalDirection;
                                currentEnd = currentEnd - offsetFromEdge * verticalDirection;
                            }
                            // Создаем линию и добавляем ее в список
                            Line currentLine = Line.CreateBound(currentStart, currentEnd);
                            verticalLines.Add(currentLine);
                        }

                        Line offsetLeftLine = Line.CreateBound(
                            verticalLineLeftStart.GetEndPoint(0) + leftOffset,
                            verticalLineLeftStart.GetEndPoint(1) + leftOffset);
                        verticalLines.Add(offsetLeftLine);
                        // Длина центральной линии (centerLine)
                        double centerLineLength = centerLine.Length;

                        // Количество линий, которые нужно создать
                        int numberOfLinesTop = (int)(centerLineLength / verticalCount) + 1;

                        // Направление для создания линий
                        XYZ direction = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();



                        //Горизонтальные линии
                        RebarBarType typeHorizontal = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v3.horizontDiameter).FirstOrDefault() as RebarBarType;

                        List<Line> horizontalLines = new List<Line>();
                        // Количество линий, которые нужно создать
                        int numberOfLinesBot = (int)(centerLineLength / (WindowGrillageCreator_v3.horizontCount / 304.8)) + 1;

                        // Вычисляем смещение для текущей линии
                        double offsetTop = topRadius + (typeHorizontal.BarModelDiameter / 2);
                        double offsetBot = bottomRadius + (typeHorizontal.BarModelDiameter / 2);
                        double offsetLen = verticalRadius + (typeHorizontal.BarModelDiameter / 2);


                        XYZ offsetT = XYZ.BasisZ * offsetTop;
                        XYZ offsetB = XYZ.BasisZ * offsetBot;
                        XYZ offsetL = centerLine.Direction * offsetLen;


                        // Линия между verticalLineRightStart(0) и verticalLineLeftStart(0)
                        XYZ start3 = verticalLineRightStart.GetEndPoint(0) - offsetT + offsetL;
                        XYZ end3 = verticalLineLeftStart.GetEndPoint(0) - offsetT + offsetL;
                        Line line3 = Line.CreateBound(start3, end3);
                        horizontalLines.Add(line3);

                        // Линия между verticalLineRightStart(1) и verticalLineLeftStart(1)
                        XYZ start4 = verticalLineRightStart.GetEndPoint(1) + offsetB + offsetL;
                        XYZ end4 = verticalLineLeftStart.GetEndPoint(1) + offsetB + offsetL;
                        Line line4 = Line.CreateBound(start4, end4);
                        horizontalLines.Add(line4);

                        //if (WindowGrillageCreator_v3.isKnittedMode)
                        //{
                        //    XYZ dirKnitted = direction.CrossProduct(XYZ.BasisZ);
                        //    List<Line> lines = new List<Line>() 
                        //    {
                        //    Line.CreateBound(
                        //        verticalLineRightStart.GetEndPoint(0) - 
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ - 
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ -
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted -
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted, 

                        //        verticalLineRightStart.GetEndPoint(1) +
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ +
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ -
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted -
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted),

                        //    Line.CreateBound(
                        //        verticalLineRightStart.GetEndPoint(1) +
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ +
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ -
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted -
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted, 

                        //        verticalLineLeftStart.GetEndPoint(1) +
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ +
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ +
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted +
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted),

                        //    Line.CreateBound(
                        //        verticalLineLeftStart.GetEndPoint(1) +
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ +
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ +
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted +
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted, 

                        //        verticalLineLeftStart.GetEndPoint(0) -
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ -
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ +
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted +
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted),

                        //    Line.CreateBound(
                        //        verticalLineLeftStart.GetEndPoint(0) -
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ -
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ +
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted +
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted, 

                        //        verticalLineRightStart.GetEndPoint(0) -
                        //        typeHorizontal.BarModelDiameter / 2 * XYZ.BasisZ -
                        //        Math.Max(bottomRadius, topRadius) * XYZ.BasisZ -
                        //        typeHorizontal.BarModelDiameter / 2 * dirKnitted -
                        //        Math.Max(bottomRadius, topRadius) * dirKnitted)
                        //    };
                        //    CreateRebarSet(doc, lines, typeHorizontal, RebarStyle.StirrupTie, element, direction, numberOfLinesTop, verticalCount, true);
                        //}
                        if (WindowGrillageCreator_v3.isKnittedMode)
                        {
                            double maxStep = 400.0 / 304.8;    // 400 мм в футах
                            XYZ depthDir = direction.CrossProduct(XYZ.BasisZ).Normalize();
                            double dzBot = bottomRadius + typeHorizontal.BarModelDiameter / 2.0;
                            double dzTop = topRadius + typeHorizontal.BarModelDiameter / 2.0;
                            double dz = Math.Max(dzBot, dzTop) + typeHorizontal.BarModelDiameter / 2.0;

                            // списки ваших линий
                            var topLines = intermediateLinesTop;
                            var botLines = intermediateLinesBottom;

                            int i = 0;
                            const double TOLERANCE = 1e-6; // ~0.3 мм в футах

                            while (i < botLines.Count - 1)
                            {
                                // ищем максимальный j > i, такой что расстояние от линии i до j <= maxStep (включая ровно maxStep)
                                int j = i + 1;
                                while (j + 1 < botLines.Count)
                                {
                                    double d = botLines[i]
                                                .GetEndPoint(0)
                                                .DistanceTo(botLines[j + 1].GetEndPoint(0));
                                    if (d <= maxStep + TOLERANCE)
                                        j++;
                                    else
                                        break;
                                }

                                // проверяем, что хотя бы до ближайшей линии расстояние не больше maxStep
                                double d0 = botLines[i]
                                             .GetEndPoint(0)
                                             .DistanceTo(botLines[j].GetEndPoint(0));
                                if (d0 > maxStep + TOLERANCE)
                                {
                                    // ни одна следующая линия не подходит — можно выйти из цикла
                                    break;
                                }

                                // строим хомут между линиями i и j

                                // центры нижней и верхней граней
                                XYZ botC = botLines[i].GetEndPoint(0)
                                         + (botLines[j].GetEndPoint(0) - botLines[i].GetEndPoint(0)) * 0.5;
                                XYZ topC = topLines[i].GetEndPoint(0)
                                         + (topLines[j].GetEndPoint(0) - topLines[i].GetEndPoint(0)) * 0.5;

                                // направление хомута (от i к j)
                                XYZ horDir = (botLines[j].GetEndPoint(0) - botLines[i].GetEndPoint(0)).Normalize();
                                double halfW = botLines[i].GetEndPoint(0)
                                                   .DistanceTo(botLines[j].GetEndPoint(0)) * 0.5;

                                // «сырые» углы
                                XYZ br0 = botC + horDir * halfW;
                                XYZ bl0 = botC - horDir * halfW;
                                XYZ tl0 = topC - horDir * halfW;
                                XYZ tr0 = topC + horDir * halfW;

                                // применяем смещения по Z и глубине
                                XYZ pBR = br0 - XYZ.BasisZ * dzBot - depthDir * dz;
                                XYZ pBL = bl0 - XYZ.BasisZ * dzBot + depthDir * dz;
                                XYZ pTL = tl0 + XYZ.BasisZ * dzTop + depthDir * dz;
                                XYZ pTR = tr0 + XYZ.BasisZ * dzTop - depthDir * dz;

                                var rect = new List<Line>
                                        {
                                        Line.CreateBound(pBL - XYZ.BasisZ * (5/304.8), pTL),
                                            Line.CreateBound(pTL, pTR),
                                            Line.CreateBound(pTR, pBR),
                                            Line.CreateBound(pBR, pBL + depthDir * (5/304.8))
                                        };

                                CreateRebarSet(
                                    doc,
                                    rect,
                                    typeHorizontal,
                                    RebarStyle.StirrupTie,
                                    element,
                                    direction,
                                    numberOfLinesTop,
                                    verticalCount,
                                    true
                                );


                                // следующий «старт» с линии j
                                i = j;
                            }
                        }
                        else
                        {
                            CreateRebarSet(doc, verticalLines, typeVertical, RebarStyle.Standard, element, direction, numberOfLinesTop, verticalCount, false);
                            CreateRebarSet(doc, horizontalLines, typeHorizontal, RebarStyle.Standard, element, direction, numberOfLinesBot, WindowGrillageCreator_v3.horizontCount / 304.8, true);
                        }

                        RebarBarType type2 = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v3.cornerDiameter).FirstOrDefault() as RebarBarType;
                        CreateCornerRebarsAtIntersections(doc, dictTop, dictBottom, type2, element);
                    }
                    using (Transaction tx = new Transaction(doc))
                    {
                        tx.Start("Защитный слой");
                        Element coverLeftRight = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (WindowGrillageCreator_v3.leftRightOffset / 304.8 - 25 / 304.8)).FirstOrDefault();
                        if (coverLeftRight != null)
                            element.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER).Set(coverLeftRight.Id);
                        //24.07.25 - отдельное смещение сверху
                        Element coverTopBottom = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (Math.Min(WindowGrillageCreator_v3.topOffset, WindowGrillageCreator_v3.bottomOffset) / 304.8 - 25 / 304.8)).FirstOrDefault();
                        if (coverTopBottom != null)
                        {
                            // 23.10.25 - исправления в ростверках
                            try
                            {
                                element.get_Parameter(BuiltInParameter.CLEAR_COVER_TOP).Set(coverTopBottom.Id);
                                element.get_Parameter(BuiltInParameter.CLEAR_COVER_BOTTOM).Set(coverTopBottom.Id);
                            }
                            catch
                            {

                            }
                        }
                        tx.Commit();
                    }
                }
                tg.Assimilate();
            }
        }
        public string GetName()
        {
            return "xxx";
        }

        private void CreateModelLinesFromSelectedFloors(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            d = doc;

            List<Reference> references = uiDoc.Selection.PickObjects(
                ObjectType.Element,
                new FloorSelectionFilter(),
                "Выберите ростверки для создания осевых линий армирования").ToList();

            if (references.Count == 0)
                return;

            using (TransactionGroup tg = new TransactionGroup(doc, "Создание осевых линий ростверков"))
            {
                tg.Start();

                foreach (Reference reference in references)
                {
                    Floor floor = doc.GetElement(reference.ElementId) as Floor;
                    FloorContext context = CreateFloorContext(doc, floor);
                    if (context == null)
                        continue;

                    List<Line> originalCenterLines = ComputeCenterLines(context.Profile);
                    List<Line> centerLines = PrepareCenterLinesForModelLines(originalCenterLines, context.Profile);

                    List<GrillageModelLine> modelLines = new List<GrillageModelLine>();
                    for (int lineIndex = 0; lineIndex < centerLines.Count; lineIndex++)
                    {
                        Line centerLine = centerLines[lineIndex];
                        // 24.07.26 - фикс перемычек + игнор линий в ростверках меньше 700
                        if (centerLine.Length <= 700 / 304.8)
                            continue;
                        BoundaryDistances distances = CalculateBoundaryDistances(centerLine, context.Profile);
                        if (!AreBoundaryDistancesValid(distances))
                            continue;
                        // 18.06.2026 - настройки всегда из окна
                        double originalLength = lineIndex < originalCenterLines.Count
                            ? originalCenterLines[lineIndex].Length
                            : centerLine.Length;
                        XYZ originalStartPoint = lineIndex < originalCenterLines.Count
                            ? originalCenterLines[lineIndex].GetEndPoint(0)
                            : centerLine.GetEndPoint(0);
                        XYZ originalEndPoint = lineIndex < originalCenterLines.Count
                            ? originalCenterLines[lineIndex].GetEndPoint(1)
                            : centerLine.GetEndPoint(1);
                        GrillageLineData data = CreateLineData(
                            context, distances, originalLength, originalStartPoint, originalEndPoint);
                        modelLines.Add(new GrillageModelLine
                        {
                            Curve = centerLine,
                            Data = data
                        });
                    }

                    CreateModelLines(doc, modelLines);
                }

                tg.Assimilate();
            }
        }

        private List<Line> PrepareCenterLinesForModelLines(List<Line> centerLines, List<Line> profile)
        {
            const double maxExtendDistance = 1000.0 / 304.8;
            const double boundaryGap = 50.0 / 304.8;

            return ApplyJunctionExtensionRules(centerLines, profile, maxExtendDistance, boundaryGap);
        }

        private List<Line> ApplyJunctionExtensionRules(List<Line> centerLines, List<Line> profile, double maxDistance, double boundaryGap)
        {
            List<JunctionEndCandidate> candidates = CollectJunctionEndCandidates(centerLines, profile, maxDistance, boundaryGap);
            List<List<JunctionEndCandidate>> junctions = GroupJunctionEndCandidates(candidates);
            Random random = new Random(Guid.NewGuid().GetHashCode());

            foreach (List<JunctionEndCandidate> junction in junctions)
                ApplyJunctionRule(junction, random);

            XYZ[][] endPoints = centerLines
                .Select(line => new[] { line.GetEndPoint(0), line.GetEndPoint(1) })
                .ToArray();

            foreach (JunctionEndCandidate candidate in candidates)
                endPoints[candidate.LineIndex][candidate.EndIndex] = candidate.ResultPoint;

            ApplyCollinearJunctionRules(centerLines, candidates, endPoints, maxDistance, random);

            List<Line> result = new List<Line>();
            for (int i = 0; i < centerLines.Count; i++)
            {
                if (endPoints[i][0].DistanceTo(endPoints[i][1]) > GeometryTolerance)
                    result.Add(Line.CreateBound(endPoints[i][0], endPoints[i][1]));
            }

            return result;
        }

        private void ApplyCollinearJunctionRules(List<Line> centerLines, List<JunctionEndCandidate> candidates,
            XYZ[][] endPoints, double maxGapDistance, Random random)
        {
            List<CollinearGapPair> gapPairs = CollectCollinearGapPairs(centerLines, maxGapDistance);
            foreach (List<CollinearGapPair> junctionPairs in GroupCollinearGapPairs(gapPairs))
            {
                CollinearGapPair firstPair = junctionPairs[0];
                CollinearGapPair perpendicularPair = junctionPairs.FirstOrDefault(pair =>
                    !AreParallelInXY(firstPair.Direction, pair.Direction));

                if (perpendicularPair != null)
                {
                    // Крест: обе пары сначала остаются у ближних граней,
                    // затем случайно выбранная пара дотягивается до центра узла.
                    KeepGapPairEnds(junctionPairs, endPoints);
                    KeepCandidatesPassingThroughJunction(candidates, endPoints, firstPair.JunctionPoint, null);

                    CollinearGapPair selectedPair = random.Next(2) == 0 ? firstPair : perpendicularPair;
                    ConnectGapPair(selectedPair, endPoints);
                    continue;
                }

                // T-узел (или обычный разрыв одной оси): соосные линии соединяются,
                // а пересекающая центр перпендикулярная ветвь остаётся у ближней грани.
                ConnectGapPair(firstPair, endPoints);
                KeepCandidatesPassingThroughJunction(candidates, endPoints, firstPair.JunctionPoint, firstPair.Direction);
            }
        }

        private List<CollinearGapPair> CollectCollinearGapPairs(List<Line> centerLines, double maxGapDistance)
        {
            List<CollinearGapPair> possiblePairs = new List<CollinearGapPair>();

            for (int i = 0; i < centerLines.Count; i++)
            {
                for (int j = i + 1; j < centerLines.Count; j++)
                {
                    if (!AreLinesCollinearInXY(centerLines[i], centerLines[j]))
                        continue;

                    LineEndPair closestEnds = GetClosestLineEndPair(centerLines[i], i, centerLines[j], j);
                    if (closestEnds.Distance <= GeometryTolerance || closestEnds.Distance >= maxGapDistance)
                        continue;

                    possiblePairs.Add(new CollinearGapPair
                    {
                        First = closestEnds.First,
                        Second = closestEnds.Second,
                        Direction = GetHorizontalDirection(centerLines[i]),
                        JunctionPoint = (closestEnds.First.Point + closestEnds.Second.Point) / 2,
                        Distance = closestEnds.Distance
                    });
                }
            }

            // Один конец линии может участвовать только в одном ближайшем разрыве.
            List<CollinearGapPair> result = new List<CollinearGapPair>();
            HashSet<string> usedEnds = new HashSet<string>();
            foreach (CollinearGapPair pair in possiblePairs.OrderBy(pair => pair.Distance))
            {
                string firstKey = GetLineEndKey(pair.First);
                string secondKey = GetLineEndKey(pair.Second);
                if (usedEnds.Contains(firstKey) || usedEnds.Contains(secondKey))
                    continue;

                usedEnds.Add(firstKey);
                usedEnds.Add(secondKey);
                result.Add(pair);
            }

            return result;
        }

        private List<List<CollinearGapPair>> GroupCollinearGapPairs(List<CollinearGapPair> pairs)
        {
            const double junctionTolerance = 10.0 / 304.8;
            List<List<CollinearGapPair>> groups = new List<List<CollinearGapPair>>();
            HashSet<CollinearGapPair> visited = new HashSet<CollinearGapPair>();

            foreach (CollinearGapPair pair in pairs)
            {
                if (visited.Contains(pair))
                    continue;

                List<CollinearGapPair> group = new List<CollinearGapPair>();
                Queue<CollinearGapPair> queue = new Queue<CollinearGapPair>();
                queue.Enqueue(pair);
                visited.Add(pair);

                while (queue.Count > 0)
                {
                    CollinearGapPair current = queue.Dequeue();
                    group.Add(current);

                    foreach (CollinearGapPair other in pairs)
                    {
                        if (visited.Contains(other))
                            continue;

                        XYZ offset = other.JunctionPoint - current.JunctionPoint;
                        double distanceInXY = new XYZ(offset.X, offset.Y, 0).GetLength();
                        if (distanceInXY >= junctionTolerance
                            || Math.Abs(other.JunctionPoint.Z - current.JunctionPoint.Z) >= junctionTolerance)
                            continue;

                        visited.Add(other);
                        queue.Enqueue(other);
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        private bool AreLinesCollinearInXY(Line line1, Line line2)
        {
            if (!AreParallelInXY(line1.Direction, line2.Direction))
                return false;

            const double collinearTolerance = 1.0 / 304.8;
            XYZ direction = GetHorizontalDirection(line1);
            XYZ perpendicular = new XYZ(-direction.Y, direction.X, 0).Normalize();
            double offset = Math.Abs((GetLineMidPoint(line2) - GetLineMidPoint(line1)).DotProduct(perpendicular));
            double zOffset = Math.Abs(GetLineAverageZ(line2) - GetLineAverageZ(line1));
            return offset < collinearTolerance && zOffset < collinearTolerance;
        }

        private LineEndPair GetClosestLineEndPair(Line line1, int lineIndex1, Line line2, int lineIndex2)
        {
            LineEndPair closest = new LineEndPair { Distance = double.MaxValue };

            for (int endIndex1 = 0; endIndex1 < 2; endIndex1++)
            {
                XYZ point1 = line1.GetEndPoint(endIndex1);
                for (int endIndex2 = 0; endIndex2 < 2; endIndex2++)
                {
                    XYZ point2 = line2.GetEndPoint(endIndex2);
                    double distance = point1.DistanceTo(point2);
                    if (distance >= closest.Distance)
                        continue;

                    closest.First = new LineEndReference(lineIndex1, endIndex1, point1);
                    closest.Second = new LineEndReference(lineIndex2, endIndex2, point2);
                    closest.Distance = distance;
                }
            }

            return closest;
        }

        private void KeepGapPairEnds(List<CollinearGapPair> pairs, XYZ[][] endPoints)
        {
            foreach (CollinearGapPair pair in pairs)
            {
                endPoints[pair.First.LineIndex][pair.First.EndIndex] = pair.First.Point;
                endPoints[pair.Second.LineIndex][pair.Second.EndIndex] = pair.Second.Point;
            }
        }

        private void ConnectGapPair(CollinearGapPair pair, XYZ[][] endPoints)
        {
            endPoints[pair.First.LineIndex][pair.First.EndIndex] = pair.JunctionPoint;
            endPoints[pair.Second.LineIndex][pair.Second.EndIndex] = pair.JunctionPoint;
        }

        private void KeepCandidatesPassingThroughJunction(List<JunctionEndCandidate> candidates, XYZ[][] endPoints,
            XYZ junctionPoint, XYZ connectedDirection)
        {
            foreach (JunctionEndCandidate candidate in candidates)
            {
                if (connectedDirection != null && AreParallelInXY(candidate.Direction, connectedDirection))
                    continue;

                Line fullExtension = Line.CreateBound(candidate.Point, candidate.BoundaryPoint);
                if (!IsPointOnLineSegment(junctionPoint, fullExtension))
                    continue;

                endPoints[candidate.LineIndex][candidate.EndIndex] = candidate.Point;
            }
        }

        private string GetLineEndKey(LineEndReference lineEnd)
        {
            return lineEnd.LineIndex.ToString(CultureInfo.InvariantCulture)
                + ":" + lineEnd.EndIndex.ToString(CultureInfo.InvariantCulture);
        }

        private List<JunctionEndCandidate> CollectJunctionEndCandidates(List<Line> centerLines, List<Line> profile, double maxDistance, double boundaryGap)
        {
            List<JunctionEndCandidate> candidates = new List<JunctionEndCandidate>();

            for (int lineIndex = 0; lineIndex < centerLines.Count; lineIndex++)
            {
                Line centerLine = centerLines[lineIndex];
                XYZ start = centerLine.GetEndPoint(0);
                XYZ end = centerLine.GetEndPoint(1);
                XYZ direction = (end - start).Normalize();

                AddJunctionEndCandidate(candidates, lineIndex, 0, start, -direction, profile, maxDistance, boundaryGap);
                AddJunctionEndCandidate(candidates, lineIndex, 1, end, direction, profile, maxDistance, boundaryGap);
            }

            return candidates;
        }

        private void AddJunctionEndCandidate(List<JunctionEndCandidate> candidates, int lineIndex, int endIndex,
            XYZ point, XYZ direction, List<Line> profile, double maxDistance, double boundaryGap)
        {
            double distance;
            if (!TryFindNearestBoundaryDistance(point, direction, profile, maxDistance, out distance))
                return;

            double extension = distance - boundaryGap;
            if (extension <= GeometryTolerance)
                return;

            XYZ normalizedDirection = direction.Normalize();
            XYZ boundaryPoint = point + normalizedDirection * distance;
            candidates.Add(new JunctionEndCandidate
            {
                LineIndex = lineIndex,
                EndIndex = endIndex,
                Point = point,
                Direction = normalizedDirection,
                BoundaryPoint = boundaryPoint,
                JunctionPoint = (point + boundaryPoint) / 2,
                ExtendedPoint = point + normalizedDirection * extension,
                ResultPoint = point + normalizedDirection * extension
            });
        }

        private List<List<JunctionEndCandidate>> GroupJunctionEndCandidates(List<JunctionEndCandidate> candidates)
        {
            List<List<JunctionEndCandidate>> groups = new List<List<JunctionEndCandidate>>();
            HashSet<JunctionEndCandidate> visited = new HashSet<JunctionEndCandidate>();

            foreach (JunctionEndCandidate candidate in candidates)
            {
                if (visited.Contains(candidate))
                    continue;

                List<JunctionEndCandidate> group = new List<JunctionEndCandidate>();
                Queue<JunctionEndCandidate> queue = new Queue<JunctionEndCandidate>();
                queue.Enqueue(candidate);
                visited.Add(candidate);

                while (queue.Count > 0)
                {
                    JunctionEndCandidate current = queue.Dequeue();
                    group.Add(current);

                    foreach (JunctionEndCandidate other in candidates)
                    {
                        if (visited.Contains(other) || !DoCandidateExtensionsMeet(current, other))
                            continue;

                        visited.Add(other);
                        queue.Enqueue(other);
                    }
                }

                groups.Add(group);
            }

            return groups;
        }

        private bool DoCandidateExtensionsMeet(JunctionEndCandidate candidate1, JunctionEndCandidate candidate2)
        {
            if (candidate1.LineIndex == candidate2.LineIndex)
                return false;

            const double junctionTolerance = 1.0 / 304.8;
            if (Math.Abs(candidate1.Point.Z - candidate2.Point.Z) >= junctionTolerance)
                return false;

            // Для всех ветвей одного L-, T- или крестообразного узла середина
            // отрезка между ближней и дальней гранями совпадает с центром узла.
            // Эта проверка надёжнее пересечения укороченных конечных отрезков.
            const double junctionCenterTolerance = 10.0 / 304.8;
            XYZ centerOffset = candidate2.JunctionPoint - candidate1.JunctionPoint;
            double centerDistanceInXY = new XYZ(centerOffset.X, centerOffset.Y, 0).GetLength();
            if (centerDistanceInXY < junctionCenterTolerance)
                return true;

            Line extension1 = Line.CreateBound(candidate1.Point, candidate1.BoundaryPoint);
            Line extension2 = Line.CreateBound(candidate2.Point, candidate2.BoundaryPoint);

            if (!AreParallelInXY(candidate1.Direction, candidate2.Direction))
                return GetBoundedIntersectionPoint(extension1, extension2) != null;

            if (!AreCandidateAxesCollinear(candidate1, candidate2))
                return false;

            XYZ direction = candidate1.Direction;
            double min1 = GetProjectionMin(extension1, direction);
            double max1 = GetProjectionMax(extension1, direction);
            double min2 = GetProjectionMin(extension2, direction);
            double max2 = GetProjectionMax(extension2, direction);
            return Math.Max(min1, min2) <= Math.Min(max1, max2) + junctionTolerance;
        }

        private void ApplyJunctionRule(List<JunctionEndCandidate> junction, Random random)
        {
            List<JunctionCandidatePair> alignedPairs = FindOppositeDirectionPairs(junction);

            // L-узел: только одна из двух ветвей случайно проходит до дальней грани.
            if (junction.Count == 2 && alignedPairs.Count == 0)
            {
                KeepAllJunctionEnds(junction);
                JunctionEndCandidate selected = junction[random.Next(junction.Count)];
                selected.ResultPoint = selected.ExtendedPoint;
                return;
            }

            // Две встречные соосные линии образуют одну непрерывную линию.
            if (junction.Count == 2 && alignedPairs.Count == 1)
            {
                KeepAllJunctionEnds(junction);
                ConnectCandidatePair(alignedPairs[0]);
                return;
            }

            // T-узел: соединяется только соосная пара, перпендикулярная ветвь не меняется.
            if (junction.Count == 3)
            {
                KeepAllJunctionEnds(junction);
                if (alignedPairs.Count > 0)
                    ConnectCandidatePair(alignedPairs[0]);
                return;
            }

            // Крест: случайно выбирается одна из двух осей; вторая пара остаётся у ближних граней.
            if (junction.Count == 4)
            {
                KeepAllJunctionEnds(junction);
                if (IsCrossPairSet(alignedPairs, junction))
                    ConnectCandidatePair(alignedPairs[random.Next(alignedPairs.Count)]);
                return;
            }
        }

        private List<JunctionCandidatePair> FindOppositeDirectionPairs(List<JunctionEndCandidate> junction)
        {
            List<JunctionCandidatePair> pairs = new List<JunctionCandidatePair>();

            for (int i = 0; i < junction.Count; i++)
            {
                for (int j = i + 1; j < junction.Count; j++)
                {
                    if (!AreParallelInXY(junction[i].Direction, junction[j].Direction))
                        continue;

                    XYZ direction1 = new XYZ(junction[i].Direction.X, junction[i].Direction.Y, 0).Normalize();
                    XYZ direction2 = new XYZ(junction[j].Direction.X, junction[j].Direction.Y, 0).Normalize();
                    if (direction1.DotProduct(direction2) >= 0)
                        continue;

                    pairs.Add(new JunctionCandidatePair(junction[i], junction[j]));
                }
            }

            return pairs;
        }

        private bool AreCandidateAxesCollinear(JunctionEndCandidate candidate1, JunctionEndCandidate candidate2)
        {
            if (!AreParallelInXY(candidate1.Direction, candidate2.Direction))
                return false;

            const double junctionTolerance = 1.0 / 304.8;
            XYZ direction = new XYZ(candidate1.Direction.X, candidate1.Direction.Y, 0).Normalize();
            XYZ perpendicular = new XYZ(-direction.Y, direction.X, 0).Normalize();
            double offset = Math.Abs((candidate2.Point - candidate1.Point).DotProduct(perpendicular));
            double zOffset = Math.Abs(candidate2.Point.Z - candidate1.Point.Z);
            return offset < junctionTolerance && zOffset < junctionTolerance;
        }

        private bool IsCrossPairSet(List<JunctionCandidatePair> pairs, List<JunctionEndCandidate> junction)
        {
            return pairs.Count == 2
                && pairs.SelectMany(pair => new[] { pair.First, pair.Second }).Distinct().Count() == junction.Count;
        }

        private void KeepAllJunctionEnds(List<JunctionEndCandidate> junction)
        {
            foreach (JunctionEndCandidate candidate in junction)
                candidate.ResultPoint = candidate.Point;
        }

        private void ConnectCandidatePair(JunctionCandidatePair pair)
        {
            XYZ joinPoint = (pair.First.Point + pair.Second.Point) / 2;
            pair.First.ResultPoint = joinPoint;
            pair.Second.ResultPoint = joinPoint;
        }

        private bool TryFindNearestBoundaryDistance(XYZ point, XYZ direction, List<Line> profile, double maxDistance, out double distance)
        {
            distance = double.MaxValue;
            XYZ rayEnd = point + direction.Normalize() * maxDistance;
            Line ray = Line.CreateBound(point, rayEnd);

            foreach (Line boundaryLine in profile)
            {
                XYZ intersection = GetIntersectionPoint(ray, boundaryLine);
                if (intersection == null)
                    continue;

                double currentDistance = point.DistanceTo(intersection);
                if (currentDistance <= GeometryTolerance || currentDistance > maxDistance + GeometryTolerance)
                    continue;

                if (!IsPointOnLineSegment(intersection, ray) || !IsPointOnLineSegment(intersection, boundaryLine))
                    continue;

                if (currentDistance < distance)
                    distance = currentDistance;
            }

            return distance < double.MaxValue;
        }

        private void ExecuteCreateRebarsFromSelectedLines(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            d = doc;

            List<Element> rebarTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).WhereElementIsElementType().ToList();
            List<Element> rearCoverTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarCoverType)).ToList();
            List<DetailCurve> DetailCurves = GetSelectedModelLines(uiDoc, doc);
            if (DetailCurves.Count == 0)
                return;
            // 18.06.2026 - настройки всегда из окна
            GrillageCurrentSettings currentSettings = CreateCurrentSettings();
            corners = new List<XYZ>();
            Dictionary<long, RebarBuildGroup> groups = new Dictionary<long, RebarBuildGroup>();

            using (TransactionGroup tg = new TransactionGroup(doc, "Армирование ростверков по линиям направления"))
            {
                tg.Start();

                foreach (DetailCurve DetailCurve in DetailCurves)
                {
                    Line rawLine = DetailCurve.GeometryCurve as Line;
                    if (rawLine == null)
                        continue;

                    GrillageLineData storedData;
                    bool hasStoredData = TryReadLineData(DetailCurve, out storedData);

                    FloorContext context;
                    Line centerLine;
                    if (!TryFindFloorContextForLine(doc, DetailCurve, rawLine, hasStoredData ? storedData : null,
                        currentSettings, out context, out centerLine))
                        continue;

                    BoundaryDistances distances = CalculateBoundaryDistances(centerLine, context.Profile);
                    if (!AreBoundaryDistancesValid(distances) && hasStoredData && AreBoundaryDistancesValid(storedData))
                        distances = new BoundaryDistances(storedData.LeftBoundaryDistance, storedData.RightBoundaryDistance);

                    if (!AreBoundaryDistancesValid(distances))
                        continue;
                    // 18.06.2026 - настройки всегда из окна
                    OriginalLineGeometry originalGeometry = GetOriginalCenterLineGeometry(
                        rawLine, centerLine, context, hasStoredData ? storedData : null);
                    GrillageLineData lineData = CreateLineData(
                        context,
                        distances,
                        originalGeometry.Length,
                        originalGeometry.StartPoint,
                        originalGeometry.EndPoint);
                    UpdateStoredLineData(doc, DetailCurve, lineData);

                    long hostKey = context.Floor.Id.Value;
                    // 18.06.2026 - настройки всегда из окна
                    CenterLineRebarResult result = CreateRebarForCenterLine(doc, rebarTypes, context.Floor, centerLine, context.Thickness, lineData, currentSettings);
                    if (result == null)
                        continue;

                    if (!groups.ContainsKey(hostKey))
                    {
                        groups[hostKey] = new RebarBuildGroup
                        {
                            Host = context.Floor,
                            CornerDiameter = currentSettings.CornerDiameter
                        };
                    }
                    // 07.08.26 - отдельная кнопка тип основы в перемычках, новая логика армирования ростверка
                    groups[hostKey].Results.Add(result);
                    groups[hostKey].HalfWidths.Add(Math.Max(distances.Left, distances.Right));
                    // 18.06.2026 - настройки всегда из окна
                    ApplyRebarCover(doc, rearCoverTypes, context.Floor, currentSettings);
                    modLength = Math.Max(distances.Left, distances.Right);
                }

                foreach (RebarBuildGroup group in groups.Values)
                {
                    if (group.Results.Count < 2)
                        continue;

                    modLength = CalculateModeDistance(group.HalfWidths);
                    RebarBarType cornerType = rebarTypes.Where(x => x.Name == group.CornerDiameter).FirstOrDefault() as RebarBarType;
                    CreateJunctionRebars(doc, group.Results, cornerType, group.Host);
                }

                tg.Assimilate();
            }
        }
        // 18.06.2026 - настройки всегда из окна
        private CenterLineRebarResult CreateRebarForCenterLine(Document doc, List<Element> rebarTypes, Floor host, Line centerLine, double thickness, GrillageLineData lineData, GrillageCurrentSettings settings)
        {
            if (settings.HorizontalCount < 2)
                return null;

            double rightRebarHalfWidth = lineData.RightBoundaryDistance - settings.LeftRightOffset / 304.8;
            double leftRebarHalfWidth = lineData.LeftBoundaryDistance - settings.LeftRightOffset / 304.8;
            if (rightRebarHalfWidth <= GeometryTolerance || leftRebarHalfWidth <= GeometryTolerance)
                return null;

            double verticalRebarHeight = thickness
                - settings.TopOffset / 304.8
                - settings.BottomOffset / 304.8;
            if (verticalRebarHeight <= doc.Application.ShortCurveTolerance)
                return null;

            XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();
            XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0).Normalize();

            XYZ offsetBottomRight = perpendicularDirection * rightRebarHalfWidth + settings.BottomOffset / 304.8 * XYZ.BasisZ;
            XYZ offsetBottomLeft = perpendicularDirection * -leftRebarHalfWidth + settings.BottomOffset / 304.8 * XYZ.BasisZ;
            XYZ offsetTopRight = perpendicularDirection * rightRebarHalfWidth + (thickness - settings.TopOffset / 304.8) * XYZ.BasisZ;
            XYZ offsetTopLeft = perpendicularDirection * -leftRebarHalfWidth + (thickness - settings.TopOffset / 304.8) * XYZ.BasisZ;

            Line lineBR = Line.CreateBound(centerLine.GetEndPoint(0) + offsetBottomRight, centerLine.GetEndPoint(1) + offsetBottomRight);
            Line lineBL = Line.CreateBound(centerLine.GetEndPoint(0) + offsetBottomLeft, centerLine.GetEndPoint(1) + offsetBottomLeft);
            Line lineTR = Line.CreateBound(centerLine.GetEndPoint(0) + offsetTopRight, centerLine.GetEndPoint(1) + offsetTopRight);
            Line lineTL = Line.CreateBound(centerLine.GetEndPoint(0) + offsetTopLeft, centerLine.GetEndPoint(1) + offsetTopLeft);

            List<Line> intermediateLinesTop = new List<Line>();
            List<Line> intermediateLinesBottom = new List<Line>();
            double distanceBetweenLines = lineBR.GetEndPoint(0).DistanceTo(lineBL.GetEndPoint(0));
            double step = distanceBetweenLines / (settings.HorizontalCount - 1);

            intermediateLinesTop.Add(lineTL);
            intermediateLinesBottom.Add(lineBL);

            for (int i = 1; i <= settings.HorizontalCount - 2; i++)
            {
                XYZ offset = perpendicularDirection * (step * i);
                intermediateLinesBottom.Add(Line.CreateBound(lineBL.GetEndPoint(0) + offset, lineBL.GetEndPoint(1) + offset));
                intermediateLinesTop.Add(Line.CreateBound(lineTL.GetEndPoint(0) + offset, lineTL.GetEndPoint(1) + offset));
            }

            intermediateLinesTop.Add(lineTR);
            intermediateLinesBottom.Add(lineBR);

            RebarBarType typeTop = rebarTypes.Where(x => x.Name == settings.TopDiameter).FirstOrDefault() as RebarBarType;
            RebarBarType typeBot = rebarTypes.Where(x => x.Name == settings.BottomDiameter).FirstOrDefault() as RebarBarType;
            RebarBarType typeVertical = rebarTypes.Where(x => x.Name == settings.VertDiameter).FirstOrDefault() as RebarBarType;
            RebarBarType typeHorizontal = rebarTypes.Where(x => x.Name == settings.HorizontDiameter).FirstOrDefault() as RebarBarType;

            if (typeTop == null || typeBot == null || typeHorizontal == null || (!settings.IsKnittedMode && typeVertical == null))
                return null;

            List<Element> rebs = CreateRebarFromLines(doc, intermediateLinesBottom, typeTop, RebarStyle.Standard, host, true, settings.IsKnittedMode);
            rebs.AddRange(CreateRebarFromLines(doc, intermediateLinesTop, typeBot, RebarStyle.Standard, host, false, settings.IsKnittedMode));

            if (rebs.Count > 0)
            {
                using (Transaction trans = new Transaction(doc, "Группа"))
                {
                    trans.Start();
                    doc.Create.NewGroup(rebs.Select(x => x.Id).ToList());
                    trans.Commit();
                }
            }

            double topRadius = typeTop.BarModelDiameter / 2;
            double bottomRadius = typeBot.BarModelDiameter / 2;
            double verticalRadius = typeVertical == null ? 0 : typeVertical.BarModelDiameter / 2;
            double offsetFromEdge = Math.Max(topRadius, bottomRadius) + verticalRadius;

            XYZ distributionStartPoint = GetRebarDistributionStartPoint(centerLine, lineData);
            XYZ distributionStartOffset = distributionStartPoint - centerLine.GetEndPoint(0);
            Line verticalLineRightStart = Line.CreateBound(
                lineBR.GetEndPoint(0) + distributionStartOffset,
                lineTR.GetEndPoint(0) + distributionStartOffset);
            Line verticalLineLeftStart = Line.CreateBound(
                lineBL.GetEndPoint(0) + distributionStartOffset,
                lineTL.GetEndPoint(0) + distributionStartOffset);
            double verticalStep = settings.VerticalStep / 304.8;
            if (verticalStep <= GeometryTolerance)
                verticalStep = 200 / 304.8;

            List<Line> verticalLines = new List<Line>();
            XYZ startPoint1 = verticalLineRightStart.GetEndPoint(0);
            XYZ endPoint1 = verticalLineRightStart.GetEndPoint(1);
            XYZ startPoint2 = verticalLineLeftStart.GetEndPoint(0);
            XYZ verticalDirection = (startPoint2 - startPoint1).Normalize();
            XYZ centerPoint = (startPoint1 + startPoint2) / 2;

            verticalLines.Add(Line.CreateBound(
                verticalLineRightStart.GetEndPoint(0) + verticalDirection * offsetFromEdge,
                verticalLineRightStart.GetEndPoint(1) + verticalDirection * offsetFromEdge));

            for (int i = 1; i <= settings.HorizontalCount - 2; i++)
            {
                XYZ offset = verticalDirection * (step * i);
                XYZ currentStart = startPoint1 + offset;
                XYZ currentEnd = endPoint1 + offset;
                XYZ curDir = (centerPoint - currentStart).Normalize();

                if (curDir.IsAlmostEqualTo(verticalDirection))
                {
                    currentStart = currentStart + offsetFromEdge * verticalDirection;
                    currentEnd = currentEnd + offsetFromEdge * verticalDirection;
                }
                else
                {
                    currentStart = currentStart - offsetFromEdge * verticalDirection;
                    currentEnd = currentEnd - offsetFromEdge * verticalDirection;
                }

                verticalLines.Add(Line.CreateBound(currentStart, currentEnd));
            }

            verticalLines.Add(Line.CreateBound(
                verticalLineLeftStart.GetEndPoint(0) - verticalDirection * offsetFromEdge,
                verticalLineLeftStart.GetEndPoint(1) - verticalDirection * offsetFromEdge));

            double centerLineLength = IsUsableDistance(lineData.OriginalLength)
                ? lineData.OriginalLength
                : centerLine.Length;
            int numberOfLinesTop = (int)(centerLineLength / verticalStep) + 1;
            XYZ direction = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

            double horizontalStep = settings.HorizontalStep / 304.8;
            if (horizontalStep <= GeometryTolerance)
                horizontalStep = 200 / 304.8;
            int numberOfLinesBot = (int)(centerLineLength / horizontalStep) + 1;

            double offsetTop = topRadius + typeHorizontal.BarModelDiameter / 2;
            double offsetBot = bottomRadius + typeHorizontal.BarModelDiameter / 2;
            double offsetLen = verticalRadius + typeHorizontal.BarModelDiameter / 2;
            XYZ offsetL = centerLine.Direction * offsetLen;

            List<Line> horizontalLines = new List<Line>
            {
                Line.CreateBound(verticalLineRightStart.GetEndPoint(0) - XYZ.BasisZ * offsetTop + offsetL,
                    verticalLineLeftStart.GetEndPoint(0) - XYZ.BasisZ * offsetTop + offsetL),
                Line.CreateBound(verticalLineRightStart.GetEndPoint(1) + XYZ.BasisZ * offsetBot + offsetL,
                    verticalLineLeftStart.GetEndPoint(1) + XYZ.BasisZ * offsetBot + offsetL)
            };

            if (settings.IsKnittedMode)
            {
                CreateKnittedRebarSets(doc, host, direction, intermediateLinesTop, intermediateLinesBottom,
                    typeHorizontal, typeTop, typeBot, numberOfLinesTop, verticalStep, distributionStartOffset);
            }
            else
            {
                CreateRebarSet(doc, verticalLines, typeVertical, RebarStyle.Standard, host, direction, numberOfLinesTop, verticalStep, false, false);
                CreateRebarSet(doc, horizontalLines, typeHorizontal, RebarStyle.Standard, host, direction, numberOfLinesBot, horizontalStep, true, false);
            }
            // 07.08.26 - отдельная кнопка тип основы в перемычках, новая логика армирования ростверка
            return new CenterLineRebarResult
            {
                CenterLine = centerLine,
                OriginalCenterLine = CreateOriginalCenterLine(centerLine, lineData),
                TopLines = intermediateLinesTop,
                BottomLines = intermediateLinesBottom,
                LeftBoundaryDistance = lineData.LeftBoundaryDistance,
                RightBoundaryDistance = lineData.RightBoundaryDistance
            };
        }

        private Line CreateOriginalCenterLine(Line centerLine, GrillageLineData lineData)
        {
            if (lineData == null
                || !IsUsablePoint(lineData.OriginalStartPoint)
                || !IsUsablePoint(lineData.OriginalEndPoint))
                return centerLine;

            double z = centerLine.GetEndPoint(0).Z;
            XYZ startPoint = new XYZ(lineData.OriginalStartPoint.X, lineData.OriginalStartPoint.Y, z);
            XYZ endPoint = new XYZ(lineData.OriginalEndPoint.X, lineData.OriginalEndPoint.Y, z);
            if (startPoint.DistanceTo(endPoint) <= GeometryTolerance)
                return centerLine;

            return Line.CreateBound(startPoint, endPoint);
        }

        private void CreateKnittedRebarSets(Document doc, Element host, XYZ direction, List<Line> topLines,
            List<Line> botLines, RebarBarType typeHorizontal, RebarBarType typeTop, RebarBarType typeBot,
            int numberOfLinesTop, double verticalStep, XYZ distributionStartOffset)
        {
            double maxStep = 400.0 / 304.8;
            XYZ depthDir = direction.CrossProduct(XYZ.BasisZ).Normalize();
            double dzBot = typeBot.BarModelDiameter / 2 + typeHorizontal.BarModelDiameter / 2.0;
            double dzTop = typeTop.BarModelDiameter / 2 + typeHorizontal.BarModelDiameter / 2.0;
            double dz = Math.Max(dzBot, dzTop) + typeHorizontal.BarModelDiameter / 2.0;

            int i = 0;
            const double tolerance = 1e-6;

            while (i < botLines.Count - 1)
            {
                int j = i + 1;
                while (j + 1 < botLines.Count)
                {
                    double d = botLines[i].GetEndPoint(0).DistanceTo(botLines[j + 1].GetEndPoint(0));
                    if (d <= maxStep + tolerance)
                        j++;
                    else
                        break;
                }

                double d0 = botLines[i].GetEndPoint(0).DistanceTo(botLines[j].GetEndPoint(0));
                if (d0 > maxStep + tolerance)
                    break;

                XYZ botC = botLines[i].GetEndPoint(0)
                    + (botLines[j].GetEndPoint(0) - botLines[i].GetEndPoint(0)) * 0.5
                    + distributionStartOffset;
                XYZ topC = topLines[i].GetEndPoint(0)
                    + (topLines[j].GetEndPoint(0) - topLines[i].GetEndPoint(0)) * 0.5
                    + distributionStartOffset;
                XYZ horDir = (botLines[j].GetEndPoint(0) - botLines[i].GetEndPoint(0)).Normalize();
                double halfW = botLines[i].GetEndPoint(0).DistanceTo(botLines[j].GetEndPoint(0)) * 0.5;

                XYZ br0 = botC + horDir * halfW;
                XYZ bl0 = botC - horDir * halfW;
                XYZ tl0 = topC - horDir * halfW;
                XYZ tr0 = topC + horDir * halfW;

                XYZ pBR = br0 - XYZ.BasisZ * dzBot - depthDir * dz;
                XYZ pBL = bl0 - XYZ.BasisZ * dzBot + depthDir * dz;
                XYZ pTL = tl0 + XYZ.BasisZ * dzTop + depthDir * dz;
                XYZ pTR = tr0 + XYZ.BasisZ * dzTop - depthDir * dz;

                List<Line> rect = new List<Line>
                {
                    Line.CreateBound(pBL - XYZ.BasisZ * (5 / 304.8), pTL),
                    Line.CreateBound(pTL, pTR),
                    Line.CreateBound(pTR, pBR),
                    Line.CreateBound(pBR, pBL + depthDir * (5 / 304.8))
                };

                CreateRebarSet(doc, rect, typeHorizontal, RebarStyle.StirrupTie, host, direction, numberOfLinesTop, verticalStep, true, true);
                i = j;
            }
        }
        // 18.06.2026 - настройки всегда из окна
        private void ApplyRebarCover(Document doc, List<Element> rearCoverTypes, Floor host, GrillageCurrentSettings settings)
        {
            using (Transaction tx = new Transaction(doc, "Защитный слой"))
            {
                tx.Start();
                Element coverLeftRight = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (settings.LeftRightOffset / 304.8 - 25 / 304.8)).FirstOrDefault();
                if (coverLeftRight != null)
                    host.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER).Set(coverLeftRight.Id);

                Element coverTopBottom = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (Math.Min(settings.TopOffset, settings.BottomOffset) / 304.8 - 25 / 304.8)).FirstOrDefault();
                if (coverTopBottom != null)
                {
                    try
                    {
                        host.get_Parameter(BuiltInParameter.CLEAR_COVER_TOP).Set(coverTopBottom.Id);
                        host.get_Parameter(BuiltInParameter.CLEAR_COVER_BOTTOM).Set(coverTopBottom.Id);
                    }
                    catch
                    {
                    }
                }
                tx.Commit();
            }
        }

        private List<DetailCurve> GetSelectedModelLines(UIDocument uiDoc, Document doc)
        {
            List<DetailCurve> selectedLines = uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<DetailCurve>()
                .Where(x => x.GeometryCurve is Line)
                .ToList();

            if (selectedLines.Count > 0)
                return selectedLines;

            return uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new ModelLineSelectionFilter(),
                    "Выберите осевые линии для армирования")
                .Select(x => doc.GetElement(x.ElementId))
                .OfType<DetailCurve>()
                .Where(x => x.GeometryCurve is Line)
                .ToList();
        }

        private FloorContext CreateFloorContext(Document doc, Floor floor)
        {
            if (floor == null)
                return null;

            Sketch sketch = doc.GetElement(floor.SketchId) as Sketch;
            if (sketch == null || sketch.Profile == null)
                return null;

            Parameter thicknessParam = floor.LookupParameter("Толщина");
            if (thicknessParam == null || thicknessParam.StorageType != StorageType.Double)
                return null;

            double thickness = thicknessParam.AsDouble();
            double levelOffset = floor.LookupParameter("Смещение от уровня") == null
                ? 0
                : floor.LookupParameter("Смещение от уровня").AsDouble();

            List<Line> profileLines = new List<Line>();
            foreach (CurveArray array in sketch.Profile)
            {
                foreach (Curve curve in array)
                {
                    Line line = curve as Line;
                    if (line == null)
                        continue;

                    profileLines.Add(Line.CreateBound(
                        line.GetEndPoint(0) + XYZ.BasisZ * levelOffset - XYZ.BasisZ * thickness,
                        line.GetEndPoint(1) + XYZ.BasisZ * levelOffset - XYZ.BasisZ * thickness));
                }
            }

            if (profileLines.Count == 0)
                return null;

            return new FloorContext
            {
                Floor = floor,
                Profile = profileLines,
                Thickness = thickness
            };
        }

        private bool TryFindFloorContextForLine(Document doc, DetailCurve detailCurve, Line modelLine,
            GrillageLineData data, GrillageCurrentSettings settings, out FloorContext context, out Line centerLine)
        {
            context = null;
            centerLine = null;

            Autodesk.Revit.DB.View ownerView = doc.GetElement(detailCurve.OwnerViewId) as Autodesk.Revit.DB.View;
            Level ownerLevel = ownerView == null ? null : ownerView.GenLevel;

            if (data != null && data.HostElementId > 0)
            {
                Floor storedFloor = doc.GetElement(new ElementId(data.HostElementId)) as Floor;
                bool storedFloorMatchesLevel = ownerLevel == null || storedFloor == null
                    || storedFloor.LevelId.Value == ownerLevel.Id.Value;
                FloorContext storedContext = storedFloorMatchesLevel
                    ? CreateFloorContext(doc, storedFloor)
                    : null;
                if (HasUsableVerticalRebarHeight(doc, storedContext, settings))
                {
                    context = storedContext;
                    centerLine = ProjectLineToFloorBottom(modelLine, storedContext);
                    // 19.06.26 - армирование по длине линии
                    centerLine = TrimCenterLineEnds(centerLine, doc.Application.ShortCurveTolerance);
                    return centerLine != null;
                }
            }

            FloorContext bestContext = null;
            Line bestLine = null;
            int bestLevelRank = int.MaxValue;
            double bestElevationDistance = double.MaxValue;
            double modelLineZ = (modelLine.GetEndPoint(0).Z + modelLine.GetEndPoint(1).Z) / 2;

            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                FloorContext candidateContext = CreateFloorContext(doc, floor);
                if (!HasUsableVerticalRebarHeight(doc, candidateContext, settings))
                    continue;

                Line candidateLine = ProjectLineToFloorBottom(modelLine, candidateContext);
                BoundaryDistances distances = CalculateBoundaryDistances(candidateLine, candidateContext.Profile);
                XYZ midPoint = (candidateLine.GetEndPoint(0) + candidateLine.GetEndPoint(1)) / 2;
                XYZ direction = (candidateLine.GetEndPoint(1) - candidateLine.GetEndPoint(0)).Normalize();

                if (AreBoundaryDistancesValid(distances) && IsPointInsideBoundary(midPoint, candidateContext.Profile, direction))
                {
                    // 19.06.26 - армирование по длине линии
                    centerLine = TrimCenterLineEnds(candidateLine, doc.Application.ShortCurveTolerance);
                    if (centerLine == null)
                        continue;

                    int levelRank = ownerLevel != null && floor.LevelId.Value == ownerLevel.Id.Value ? 0 : 1;
                    double bottomZ = candidateContext.Profile[0].GetEndPoint(0).Z;
                    double topZ = bottomZ + candidateContext.Thickness;
                    double elevationDistance = Math.Min(
                        Math.Abs(modelLineZ - bottomZ),
                        Math.Abs(modelLineZ - topZ));

                    if (levelRank < bestLevelRank
                        || (levelRank == bestLevelRank && elevationDistance < bestElevationDistance))
                    {
                        bestContext = candidateContext;
                        bestLine = centerLine;
                        bestLevelRank = levelRank;
                        bestElevationDistance = elevationDistance;
                    }
                }
            }

            context = bestContext;
            centerLine = bestLine;
            return context != null && centerLine != null;
        }

        private bool HasUsableVerticalRebarHeight(Document doc, FloorContext context, GrillageCurrentSettings settings)
        {
            if (context == null || settings == null)
                return false;

            double height = context.Thickness
                - settings.TopOffset / 304.8
                - settings.BottomOffset / 304.8;
            return height > doc.Application.ShortCurveTolerance;
        }

        private Line TrimCenterLineEnds(Line line, double shortCurveTolerance)
        {
            const double endOffset = 25.0 / 304.8;
            if (line == null || line.Length <= 2 * endOffset + shortCurveTolerance)
                return null;

            XYZ direction = line.Direction;
            XYZ startPoint = line.GetEndPoint(0) + endOffset * direction;
            XYZ endPoint = line.GetEndPoint(1) - endOffset * direction;
            if (startPoint.DistanceTo(endPoint) <= shortCurveTolerance)
                return null;

            return Line.CreateBound(startPoint, endPoint);
        }

        private Line ProjectLineToFloorBottom(Line line, FloorContext context)
        {
            double z = context.Profile[0].GetEndPoint(0).Z;
            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            return Line.CreateBound(new XYZ(start.X, start.Y, z), new XYZ(end.X, end.Y, z));
        }

        private BoundaryDistances CalculateBoundaryDistances(Line centerLine, List<Line> profile)
        {
            XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();
            XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0).Normalize();
            List<XYZ> checkPoints = new List<XYZ>
            {
                centerLine.GetEndPoint(0),
                (centerLine.GetEndPoint(0) + centerLine.GetEndPoint(1)) / 2,
                centerLine.GetEndPoint(1)
            };

            return new BoundaryDistances(
                FindMinimumDistanceToIntersection(checkPoints, -perpendicularDirection, profile),
                FindMinimumDistanceToIntersection(checkPoints, perpendicularDirection, profile));
        }

        private double FindMinimumDistanceToIntersection(List<XYZ> startPoints, XYZ direction, List<Line> profile)
        {
            double minDistance = double.MaxValue;
            foreach (XYZ point in startPoints)
            {
                double distance = FindDistanceToIntersection(point, direction, profile);
                if (IsUsableDistance(distance) && distance < minDistance)
                    minDistance = distance;
            }

            return minDistance;
        }

        private bool AreBoundaryDistancesValid(BoundaryDistances distances)
        {
            return distances != null
                && IsUsableDistance(distances.Left)
                && IsUsableDistance(distances.Right);
        }

        private bool AreBoundaryDistancesValid(GrillageLineData data)
        {
            return data != null
                && IsUsableDistance(data.LeftBoundaryDistance)
                && IsUsableDistance(data.RightBoundaryDistance);
        }

        private bool IsUsableDistance(double distance)
        {
            return !double.IsNaN(distance)
                && !double.IsInfinity(distance)
                && distance > GeometryTolerance
                && distance < double.MaxValue;
        }

        private bool IsUsablePoint(XYZ point)
        {
            return point != null
                && !double.IsNaN(point.X) && !double.IsInfinity(point.X)
                && !double.IsNaN(point.Y) && !double.IsInfinity(point.Y)
                && !double.IsNaN(point.Z) && !double.IsInfinity(point.Z);
        }

        private OriginalLineGeometry GetOriginalCenterLineGeometry(
            Line modelLine, Line fallbackLine, FloorContext context, GrillageLineData storedData)
        {
            Line projectedModelLine = ProjectLineToFloorBottom(modelLine, context);

            // У вручную созданной линии нет данных нашей схемы. В этом случае сама линия
            // является исходной: восстановление осей по контуру здесь не требуется.
            if (storedData == null)
            {
                return new OriginalLineGeometry
                {
                    Length = projectedModelLine.Length,
                    StartPoint = projectedModelLine.GetEndPoint(0),
                    EndPoint = projectedModelLine.GetEndPoint(1)
                };
            }

            bool hasStoredLength = storedData != null && IsUsableDistance(storedData.OriginalLength);
            bool hasStoredPoints = storedData != null
                && IsUsablePoint(storedData.OriginalStartPoint)
                && IsUsablePoint(storedData.OriginalEndPoint);

            if (hasStoredLength && hasStoredPoints)
            {
                return new OriginalLineGeometry
                {
                    Length = storedData.OriginalLength,
                    StartPoint = storedData.OriginalStartPoint,
                    EndPoint = storedData.OriginalEndPoint
                };
            }

            XYZ direction = GetHorizontalDirection(projectedModelLine);
            Line bestMatch = null;
            double bestOverlap = GeometryTolerance;
            double bestMidPointDistance = double.MaxValue;

            // Поддержка осевых линий, созданных до сохранения исходной геометрии:
            // находим соответствующую непродлённую ветвь в исходном контуре.
            foreach (Line originalLine in ComputeCenterLines(context.Profile))
            {
                if (!AreLinesCollinearInXY(projectedModelLine, originalLine))
                    continue;

                double overlap = Math.Max(0,
                    Math.Min(GetProjectionMax(projectedModelLine, direction), GetProjectionMax(originalLine, direction))
                    - Math.Max(GetProjectionMin(projectedModelLine, direction), GetProjectionMin(originalLine, direction)));
                double midPointDistance = GetLineMidPoint(projectedModelLine).DistanceTo(GetLineMidPoint(originalLine));

                if (overlap > bestOverlap + GeometryTolerance
                    || (Math.Abs(overlap - bestOverlap) <= GeometryTolerance && midPointDistance < bestMidPointDistance))
                {
                    bestMatch = originalLine;
                    bestOverlap = overlap;
                    bestMidPointDistance = midPointDistance;
                }
            }

            if (bestMatch == null)
            {
                return new OriginalLineGeometry
                {
                    Length = hasStoredLength ? storedData.OriginalLength : fallbackLine.Length,
                    StartPoint = hasStoredPoints ? storedData.OriginalStartPoint : fallbackLine.GetEndPoint(0),
                    EndPoint = hasStoredPoints ? storedData.OriginalEndPoint : fallbackLine.GetEndPoint(1)
                };
            }

            XYZ firstPoint = bestMatch.GetEndPoint(0);
            XYZ secondPoint = bestMatch.GetEndPoint(1);
            bool firstIsStart = projectedModelLine.GetEndPoint(0).DistanceTo(firstPoint)
                <= projectedModelLine.GetEndPoint(0).DistanceTo(secondPoint);

            return new OriginalLineGeometry
            {
                Length = hasStoredLength ? storedData.OriginalLength : bestMatch.Length,
                StartPoint = firstIsStart ? firstPoint : secondPoint,
                EndPoint = firstIsStart ? secondPoint : firstPoint
            };
        }

        private XYZ GetRebarDistributionStartPoint(Line centerLine, GrillageLineData lineData)
        {
            XYZ currentStart = centerLine.GetEndPoint(0);
            if (lineData == null)
                return currentStart;

            List<XYZ> originalPoints = new List<XYZ>();
            if (IsUsablePoint(lineData.OriginalStartPoint))
                originalPoints.Add(lineData.OriginalStartPoint);
            if (IsUsablePoint(lineData.OriginalEndPoint))
                originalPoints.Add(lineData.OriginalEndPoint);

            if (originalPoints.Count == 0)
                return currentStart;

            XYZ originalPoint = originalPoints.OrderBy(point => point.DistanceTo(currentStart)).First();
            XYZ direction = centerLine.Direction;
            XYZ projectedOriginalPoint = new XYZ(originalPoint.X, originalPoint.Y, currentStart.Z);
            double projection = (projectedOriginalPoint - currentStart).DotProduct(direction);
            XYZ pointOnCenterLine = currentStart + direction * projection;

            // TryFindFloorContextForLine укорачивает рабочую ось на 25 мм с каждого конца.
            // Сохраняем этот штатный отступ, но считаем его от исходной, непродлённой точки.
            return pointOnCenterLine + direction * (25.0 / 304.8);
        }

        // 18.06.2026 - настройки всегда из окна
        private GrillageLineData CreateLineData(FloorContext context, BoundaryDistances distances,
            double originalLength, XYZ originalStartPoint, XYZ originalEndPoint)
        {
            return new GrillageLineData
            {
                HostElementId = context.Floor.Id.Value,
                LeftBoundaryDistance = distances.Left,
                RightBoundaryDistance = distances.Right,
                OriginalLength = originalLength,
                OriginalStartPoint = originalStartPoint,
                OriginalEndPoint = originalEndPoint
            };
        }

        private GrillageCurrentSettings CreateCurrentSettings()
        {
            return new GrillageCurrentSettings
            {
                TopDiameter = WindowGrillageCreator_v3.topDiameter,
                BottomDiameter = WindowGrillageCreator_v3.bottomDiameter,
                VertDiameter = WindowGrillageCreator_v3.vertDiameter,
                HorizontDiameter = WindowGrillageCreator_v3.horizontDiameter,
                CornerDiameter = WindowGrillageCreator_v3.cornerDiameter,
                HorizontalCount = WindowGrillageCreator_v3.horizontalCount,
                VerticalStep = WindowGrillageCreator_v3.verticalCount,
                HorizontalStep = WindowGrillageCreator_v3.horizontCount,
                LeftRightOffset = WindowGrillageCreator_v3.leftRightOffset,
                BottomOffset = WindowGrillageCreator_v3.bottomOffset,
                TopOffset = WindowGrillageCreator_v3.topOffset,
                IsKnittedMode = WindowGrillageCreator_v3.isKnittedMode
            };
        }

        private void CreateModelLines(Document doc, List<GrillageModelLine> lines)
        {
            if (lines.Count == 0)
                return;

            using (Transaction trans = new Transaction(doc, "Создание осевых линий армирования"))
            {
                trans.Start();
                GraphicsStyle lineStyle = EnsureGrillageLineStyle(doc);
                Schema schema = GetOrCreateGrillageLineSchema();

                foreach (GrillageModelLine line in lines)
                {
                    Plane plane = Math.Abs(line.Curve.Direction.Z) > GeometryTolerance
                        ? Plane.CreateByThreePoints(line.Curve.GetEndPoint(0), line.Curve.GetEndPoint(1), line.Curve.GetEndPoint(0) + XYZ.BasisX)
                        : Plane.CreateByThreePoints(line.Curve.GetEndPoint(0), line.Curve.GetEndPoint(1), line.Curve.GetEndPoint(0) + XYZ.BasisZ);

                    var DetailCurve = doc.Create.NewDetailCurve(doc.ActiveView, line.Curve);//, SketchPlane.Create(doc, plane));
                    if (DetailCurve == null)
                        continue;

                    if (lineStyle != null)
                        DetailCurve.LineStyle = lineStyle;

                    WriteLineData(DetailCurve, schema, line.Data);
                }

                trans.Commit();
            }
        }

        private GraphicsStyle EnsureGrillageLineStyle(Document doc)
        {
            Category linesCategory = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
            Category subcategory = linesCategory.SubCategories.Cast<Category>().FirstOrDefault(x => x.Name == GrillageLineStyleName);

            if (subcategory == null)
                subcategory = doc.Settings.Categories.NewSubcategory(linesCategory, GrillageLineStyleName);

            subcategory.LineColor = new Color(220, 0, 220);
            subcategory.SetLineWeight(6, GraphicsStyleType.Projection);
            try
            {
                subcategory.SetLinePatternId(LinePatternElement.GetSolidPatternId(), GraphicsStyleType.Projection);
            }
            catch
            {
            }

            return subcategory.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        private Schema GetOrCreateGrillageLineSchema()
        {
            Schema schema = Schema.Lookup(GrillageLineSchemaGuid);
            if (schema != null)
                return schema;

            SchemaBuilder builder = new SchemaBuilder(GrillageLineSchemaGuid);
            builder.SetSchemaName("FerrumGrillageRebarLineData");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("Data", typeof(string));
            return builder.Finish();
        }

        private void WriteLineData(DetailCurve DetailCurve, Schema schema, GrillageLineData data)
        {
            if (DetailCurve == null || schema == null || data == null)
                return;

            Entity entity = new Entity(schema);
            entity.Set(schema.GetField("Data"), SerializeLineData(data));
            DetailCurve.SetEntity(entity);
        }
        // 18.06.2026 - настройки всегда из окна
        private void UpdateStoredLineData(Document doc, DetailCurve DetailCurve, GrillageLineData data)
        {
            if (doc == null || DetailCurve == null || data == null)
                return;

            Schema schema = GetOrCreateGrillageLineSchema();
            using (Transaction tx = new Transaction(doc, "Обновление данных осевой линии"))
            {
                tx.Start();
                WriteLineData(DetailCurve, schema, data);
                tx.Commit();
            }
        }

        private bool TryReadLineData(Element element, out GrillageLineData data)
        {
            data = null;
            Schema schema = Schema.Lookup(GrillageLineSchemaGuid);
            if (schema == null)
                return false;

            Entity entity = element.GetEntity(schema);
            if (!entity.IsValid())
                return false;

            try
            {
                string xml = entity.Get<string>(schema.GetField("Data"));
                return TryDeserializeLineData(xml, out data);
            }
            catch
            {
                return false;
            }
        }
        // 18.06.2026 - настройки всегда из окна
        private string SerializeLineData(GrillageLineData data)
        {
            XElement element = new XElement("GrillageLineData",
                new XAttribute("Host", data.HostElementId),
                new XAttribute("Left", FormatDouble(data.LeftBoundaryDistance)),
                new XAttribute("Right", FormatDouble(data.RightBoundaryDistance)),
                new XAttribute("OriginalLength", FormatDouble(data.OriginalLength)));

            WritePointAttributes(element, "OriginalStart", data.OriginalStartPoint);
            WritePointAttributes(element, "OriginalEnd", data.OriginalEndPoint);

            return element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        }

        private bool TryDeserializeLineData(string xml, out GrillageLineData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(xml))
                return false;

            try
            {
                XElement element = XElement.Parse(xml);
                data = new GrillageLineData
                {
                    HostElementId = ReadLong(element, "Host", ReadLong(element, "HostElementId", 0)),
                    LeftBoundaryDistance = ReadDouble(element, "Left", ReadDouble(element, "LeftBoundaryDistance", 0)),
                    RightBoundaryDistance = ReadDouble(element, "Right", ReadDouble(element, "RightBoundaryDistance", 0)),
                    OriginalLength = ReadDouble(element, "OriginalLength", 0),
                    OriginalStartPoint = ReadPointAttributes(element, "OriginalStart"),
                    OriginalEndPoint = ReadPointAttributes(element, "OriginalEnd")
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string FormatDouble(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private void WritePointAttributes(XElement element, string prefix, XYZ point)
        {
            if (!IsUsablePoint(point))
                return;

            element.SetAttributeValue(prefix + "X", FormatDouble(point.X));
            element.SetAttributeValue(prefix + "Y", FormatDouble(point.Y));
            element.SetAttributeValue(prefix + "Z", FormatDouble(point.Z));
        }

        private XYZ ReadPointAttributes(XElement element, string prefix)
        {
            double x = ReadDouble(element, prefix + "X", double.NaN);
            double y = ReadDouble(element, prefix + "Y", double.NaN);
            double z = ReadDouble(element, prefix + "Z", double.NaN);
            if (double.IsNaN(x) || double.IsInfinity(x)
                || double.IsNaN(y) || double.IsInfinity(y)
                || double.IsNaN(z) || double.IsInfinity(z))
                return null;

            return new XYZ(x, y, z);
        }

        private long ReadLong(XElement element, string name, long defaultValue)
        {
            XAttribute attribute = element.Attribute(name);
            long value;
            return attribute != null && long.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        private double ReadDouble(XElement element, string name, double defaultValue)
        {
            XAttribute attribute = element.Attribute(name);
            double value;
            return attribute != null && double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        private class FloorSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Floor;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        private class ModelLineSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                DetailCurve DetailCurve = elem as DetailCurve;
                return DetailCurve != null && DetailCurve.GeometryCurve is Line;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }

        private class FloorContext
        {
            public Floor Floor { get; set; }
            public List<Line> Profile { get; set; }
            public double Thickness { get; set; }
        }

        private class BoundaryDistances
        {
            public BoundaryDistances(double left, double right)
            {
                Left = left;
                Right = right;
            }

            public double Left { get; set; }
            public double Right { get; set; }
        }

        private class GrillageModelLine
        {
            public Line Curve { get; set; }
            public GrillageLineData Data { get; set; }
        }

        private class GrillageLineData
        {
            public long HostElementId { get; set; }
            public double LeftBoundaryDistance { get; set; }
            public double RightBoundaryDistance { get; set; }
            public double OriginalLength { get; set; }
            public XYZ OriginalStartPoint { get; set; }
            public XYZ OriginalEndPoint { get; set; }
        }

        private class OriginalLineGeometry
        {
            public double Length { get; set; }
            public XYZ StartPoint { get; set; }
            public XYZ EndPoint { get; set; }
        }
        // 18.06.2026 - настройки всегда из окна
        private class GrillageCurrentSettings
        {
            public string TopDiameter { get; set; }
            public string BottomDiameter { get; set; }
            public string VertDiameter { get; set; }
            public string HorizontDiameter { get; set; }
            public string CornerDiameter { get; set; }
            public int HorizontalCount { get; set; }
            public int VerticalStep { get; set; }
            public int HorizontalStep { get; set; }
            public int LeftRightOffset { get; set; }
            public int BottomOffset { get; set; }
            public int TopOffset { get; set; }
            public bool IsKnittedMode { get; set; }
        }
        // 07.08.26 - отдельная кнопка тип основы в перемычках, новая логика армирования ростверка
        private class CenterLineRebarResult
        {
            public Line CenterLine { get; set; }
            public Line OriginalCenterLine { get; set; }
            public List<Line> TopLines { get; set; }
            public List<Line> BottomLines { get; set; }
            public double LeftBoundaryDistance { get; set; }
            public double RightBoundaryDistance { get; set; }
        }

        private class RebarBuildGroup
        {
            public RebarBuildGroup()
            {
                Results = new List<CenterLineRebarResult>();
                HalfWidths = new List<double>();
            }

            public Floor Host { get; set; }
            public string CornerDiameter { get; set; }
            public List<CenterLineRebarResult> Results { get; private set; }
            public List<double> HalfWidths { get; private set; }
        }

        private class JunctionEndCandidate
        {
            public int LineIndex { get; set; }
            public int EndIndex { get; set; }
            public XYZ Point { get; set; }
            public XYZ Direction { get; set; }
            public XYZ BoundaryPoint { get; set; }
            public XYZ JunctionPoint { get; set; }
            public XYZ ExtendedPoint { get; set; }
            public XYZ ResultPoint { get; set; }
        }

        private class JunctionCandidatePair
        {
            public JunctionCandidatePair(JunctionEndCandidate first, JunctionEndCandidate second)
            {
                First = first;
                Second = second;
            }

            public JunctionEndCandidate First { get; private set; }
            public JunctionEndCandidate Second { get; private set; }
        }

        private class LineEndReference
        {
            public LineEndReference(int lineIndex, int endIndex, XYZ point)
            {
                LineIndex = lineIndex;
                EndIndex = endIndex;
                Point = point;
            }

            public int LineIndex { get; private set; }
            public int EndIndex { get; private set; }
            public XYZ Point { get; private set; }
        }

        private class LineEndPair
        {
            public LineEndReference First { get; set; }
            public LineEndReference Second { get; set; }
            public double Distance { get; set; }
        }

        private class CollinearGapPair
        {
            public LineEndReference First { get; set; }
            public LineEndReference Second { get; set; }
            public XYZ Direction { get; set; }
            public XYZ JunctionPoint { get; set; }
            public double Distance { get; set; }
        }
        // 07.08.26 - отдельная кнопка тип основы в перемычках, новая логика армирования ростверка
        private class RebarJunction
        {
            public RebarJunction(XYZ point)
            {
                Point = point;
                ResultIndexes = new HashSet<int>();
            }

            public XYZ Point { get; private set; }
            public HashSet<int> ResultIndexes { get; private set; }
        }

        private class JunctionBranch
        {
            public JunctionBranch(int resultIndex, XYZ direction)
            {
                ResultIndex = resultIndex;
                Direction = new XYZ(direction.X, direction.Y, 0).Normalize();
            }

            public int ResultIndex { get; private set; }
            public XYZ Direction { get; private set; }
        }

        private class ExistingRebarLineGroup
        {
            public ExistingRebarLineGroup(Line line)
            {
                Lines = new List<Line>();
                Direction = GetHorizontalDirection(line);
                PerpendicularDirection = new XYZ(-Direction.Y, Direction.X, 0).Normalize();
                Z = GetLineAverageZ(line);
                MinProjection = GetProjectionMin(line, Direction);
                MaxProjection = GetProjectionMax(line, Direction);
                Lines.Add(line);
            }

            public XYZ Direction { get; private set; }
            public XYZ PerpendicularDirection { get; private set; }
            public double Z { get; private set; }
            public double MinProjection { get; private set; }
            public double MaxProjection { get; private set; }
            public List<Line> Lines { get; private set; }

            public bool CanAdd(Line line)
            {
                XYZ direction = GetHorizontalDirection(line);
                return AreParallelInXY(Direction, direction)
                    && Math.Abs(Z - GetLineAverageZ(line)) < 1.0 / 304.8
                    && Math.Abs(MinProjection - GetProjectionMin(line, Direction)) < 1.0 / 304.8
                    && Math.Abs(MaxProjection - GetProjectionMax(line, Direction)) < 1.0 / 304.8;
            }

            public void Add(Line line)
            {
                Lines.Add(line);
            }

            public void Sort()
            {
                Lines = Lines.OrderBy(line => GetLineMidPoint(line).DotProduct(PerpendicularDirection)).ToList();
            }
        }

        private List<ExistingRebarLineGroup> CollectExistingLongitudinalRebarGroups(Document doc, Element host)
        {
            List<ExistingRebarLineGroup> groups = new List<ExistingRebarLineGroup>();
            List<Rebar> rebars = new FilteredElementCollector(doc)
                .OfClass(typeof(Rebar))
                .Cast<Rebar>()
                .ToList();

            foreach (Rebar rebar in rebars)
            {
                ElementId hostId = rebar.GetHostId();
                if (hostId == null || hostId.Value != host.Id.Value)
                    continue;

                if (GetRebarQuantity(rebar) > 1)
                    continue;

                Line line = GetSingleHorizontalRebarLine(rebar);
                if (line == null)
                    continue;

                ExistingRebarLineGroup group = groups.FirstOrDefault(x => x.CanAdd(line));
                if (group == null)
                    groups.Add(new ExistingRebarLineGroup(line));
                else
                    group.Add(line);
            }

            foreach (ExistingRebarLineGroup group in groups)
                group.Sort();

            return groups;
        }

        private int GetRebarQuantity(Rebar rebar)
        {
            Parameter quantityParameter = rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS);
            return quantityParameter == null ? 1 : quantityParameter.AsInteger();
        }

        private Line GetSingleHorizontalRebarLine(Rebar rebar)
        {
            try
            {
                IList<Curve> curves = rebar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
                List<Line> lines = curves.OfType<Line>().Where(x => x.Length > GeometryTolerance).ToList();
                if (lines.Count != 1)
                    return null;

                Line line = lines[0];
                return Math.Abs(line.Direction.Z) < GeometryTolerance ? line : null;
            }
            catch
            {
                return null;
            }
        }

        private void CreateCornerRebarsWithExistingRebars(Document doc, CenterLineRebarResult currentResult, List<ExistingRebarLineGroup> existingGroups, RebarBarType barType, Element host)
        {
            if (barType == null || currentResult == null || existingGroups == null || existingGroups.Count == 0)
                return;

            using (Transaction tx = new Transaction(doc, "Создание угловых арматур по существующей арматуре"))
            {
                tx.Start();

                foreach (ExistingRebarLineGroup existingGroup in existingGroups)
                {
                    if (AreParallelInXY(existingGroup.Direction, currentResult.CenterLine.Direction))
                        continue;

                    CreateCornersBetweenNewAndExistingLines(doc, currentResult.TopLines, existingGroup, barType, host);
                    CreateCornersBetweenNewAndExistingLines(doc, currentResult.BottomLines, existingGroup, barType, host);
                }

                tx.Commit();
            }
        }

        private void CreateCornersBetweenNewAndExistingLines(Document doc, List<Line> currentLines, ExistingRebarLineGroup existingGroup, RebarBarType barType, Element host)
        {
            if (currentLines == null || currentLines.Count == 0)
                return;

            if (Math.Abs(GetLineAverageZ(currentLines[0]) - existingGroup.Z) > 1.0 / 304.8)
                return;

            List<Line> existingLines = GetBestExistingLineOrder(currentLines, existingGroup.Lines);
            int minCount = Math.Min(currentLines.Count, existingLines.Count);

            for (int i = 0; i < minCount; i++)
            {
                Line currentLine = currentLines[i];
                Line existingLine = existingLines[i];
                XYZ intersection = GetBoundedIntersectionPoint(currentLine, existingLine);
                if (intersection == null || !TryRegisterCornerPoint(intersection))
                    continue;

                XYZ dir1 = GetCorrectDirection(currentLine, intersection);
                XYZ dir2 = GetCorrectDirection(existingLine, intersection);
                CreateCornerRebar(doc, currentLine, existingLine, intersection, dir1, dir2, barType, host);
            }
        }

        private List<Line> GetBestExistingLineOrder(List<Line> currentLines, List<Line> existingLines)
        {
            List<Line> direct = existingLines.ToList();
            List<Line> reversed = existingLines.AsEnumerable().Reverse().ToList();
            return CountBoundedPairIntersections(currentLines, reversed) > CountBoundedPairIntersections(currentLines, direct)
                ? reversed
                : direct;
        }

        private int CountBoundedPairIntersections(List<Line> lines1, List<Line> lines2)
        {
            int minCount = Math.Min(lines1.Count, lines2.Count);
            int count = 0;
            for (int i = 0; i < minCount; i++)
            {
                if (GetBoundedIntersectionPoint(lines1[i], lines2[i]) != null)
                    count++;
            }

            return count;
        }

        private XYZ GetBoundedIntersectionPoint(Line line1, Line line2)
        {
            XYZ intersection = GetIntersectionPoint(line1, line2);
            if (intersection == null)
                return null;

            return IsPointOnLineSegment(intersection, line1) && IsPointOnLineSegment(intersection, line2)
                ? intersection
                : null;
        }

        private bool IsPointOnLineSegment(XYZ point, Line line)
        {
            double distanceToEnds = point.DistanceTo(line.GetEndPoint(0)) + point.DistanceTo(line.GetEndPoint(1));
            return Math.Abs(distanceToEnds - line.Length) < 1.0 / 304.8;
        }

        private bool TryRegisterCornerPoint(XYZ intersection)
        {
            const double tol = 1e-6;

            if (corners.Any(c => c.DistanceTo(intersection) < tol))
                return false;

            var aligned = corners
                .Where(c => (Math.Abs(c.X - intersection.X) < tol
                         || Math.Abs(c.Y - intersection.Y) < tol) && Math.Abs(c.Z - intersection.Z) < tol);

            if (aligned.Any() && aligned.Any(c => c.DistanceTo(intersection) <= (modLength * 2.5)))
                return false;

            corners.Add(intersection);
            return true;
        }

        private static XYZ GetHorizontalDirection(Line line)
        {
            XYZ direction = line.Direction;
            return new XYZ(direction.X, direction.Y, 0).Normalize();
        }

        private static XYZ GetLineMidPoint(Line line)
        {
            return (line.GetEndPoint(0) + line.GetEndPoint(1)) / 2;
        }

        private static double GetLineAverageZ(Line line)
        {
            return (line.GetEndPoint(0).Z + line.GetEndPoint(1).Z) / 2;
        }

        private static double GetProjectionMin(Line line, XYZ direction)
        {
            return Math.Min(line.GetEndPoint(0).DotProduct(direction), line.GetEndPoint(1).DotProduct(direction));
        }

        private static double GetProjectionMax(Line line, XYZ direction)
        {
            return Math.Max(line.GetEndPoint(0).DotProduct(direction), line.GetEndPoint(1).DotProduct(direction));
        }

        private static bool AreParallelInXY(XYZ direction1, XYZ direction2)
        {
            XYZ dir1 = new XYZ(direction1.X, direction1.Y, 0).Normalize();
            XYZ dir2 = new XYZ(direction2.X, direction2.Y, 0).Normalize();
            return dir1.IsAlmostEqualTo(dir2) || dir1.IsAlmostEqualTo(-dir2);
        }
        // 07.08.26 - отдельная кнопка тип основы в перемычках, новая логика армирования ростверка
        private void CreateJunctionRebars(Document doc, List<CenterLineRebarResult> results,
            RebarBarType barType, Element host)
        {
            if (barType == null || results == null || results.Count < 2)
                return;

            List<RebarJunction> junctions = FindRebarJunctions(results);
            if (junctions.Count == 0)
                return;

            using (Transaction tx = new Transaction(doc, "Соединительная арматура в узлах"))
            {
                tx.Start();

                foreach (RebarJunction junction in junctions)
                {
                    List<JunctionBranch> branches = BuildJunctionBranches(junction, results);
                    List<List<JunctionBranch>> axes = GroupJunctionBranchesByAxis(branches);

                    if (IsCrossJunction(branches, axes))
                    {
                        CreateCrossStraightRebars(doc, junction.Point, axes, results, barType, host);
                        continue;
                    }

                    if (IsLJunction(branches) || IsTJunction(branches, axes))
                        CreateLOrTCornerRebars(doc, junction.Point, branches, results, barType, host);
                }

                tx.Commit();
            }
        }

        private List<RebarJunction> FindRebarJunctions(List<CenterLineRebarResult> results)
        {
            const double maxJunctionDistance = 1000.0 / 304.8;
            const double junctionTolerance = 10.0 / 304.8;
            List<RebarJunction> junctions = new List<RebarJunction>();

            for (int firstIndex = 0; firstIndex < results.Count; firstIndex++)
            {
                Line firstLine = results[firstIndex].OriginalCenterLine ?? results[firstIndex].CenterLine;
                for (int secondIndex = firstIndex + 1; secondIndex < results.Count; secondIndex++)
                {
                    Line secondLine = results[secondIndex].OriginalCenterLine ?? results[secondIndex].CenterLine;
                    if (AreParallelInXY(firstLine.Direction, secondLine.Direction))
                        continue;

                    XYZ intersection = GetIntersectionPoint(firstLine, secondLine);
                    if (intersection == null
                        || GetDistanceToLineSegmentInXY(intersection, firstLine) > maxJunctionDistance
                        || GetDistanceToLineSegmentInXY(intersection, secondLine) > maxJunctionDistance)
                        continue;

                    RebarJunction junction = junctions.FirstOrDefault(item =>
                        GetDistanceInXY(item.Point, intersection) < junctionTolerance);
                    if (junction == null)
                    {
                        junction = new RebarJunction(intersection);
                        junctions.Add(junction);
                    }

                    junction.ResultIndexes.Add(firstIndex);
                    junction.ResultIndexes.Add(secondIndex);
                }
            }

            return junctions;
        }

        private double GetDistanceToLineSegmentInXY(XYZ point, Line line)
        {
            XYZ start = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, 0);
            XYZ end = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, 0);
            XYZ pointInXY = new XYZ(point.X, point.Y, 0);
            XYZ vector = end - start;
            double lengthSquared = vector.DotProduct(vector);
            if (lengthSquared <= GeometryTolerance)
                return pointInXY.DistanceTo(start);

            double parameter = (pointInXY - start).DotProduct(vector) / lengthSquared;
            parameter = Math.Max(0, Math.Min(1, parameter));
            return pointInXY.DistanceTo(start + vector * parameter);
        }

        private double GetDistanceInXY(XYZ firstPoint, XYZ secondPoint)
        {
            return new XYZ(firstPoint.X - secondPoint.X, firstPoint.Y - secondPoint.Y, 0).GetLength();
        }

        private List<JunctionBranch> BuildJunctionBranches(RebarJunction junction,
            List<CenterLineRebarResult> results)
        {
            const double branchTolerance = 10.0 / 304.8;
            List<JunctionBranch> branches = new List<JunctionBranch>();

            foreach (int resultIndex in junction.ResultIndexes)
            {
                Line line = results[resultIndex].OriginalCenterLine ?? results[resultIndex].CenterLine;
                XYZ direction = GetHorizontalDirection(line);
                XYZ junctionAtLineZ = new XYZ(junction.Point.X, junction.Point.Y, line.GetEndPoint(0).Z);
                double firstProjection = (line.GetEndPoint(0) - junctionAtLineZ).DotProduct(direction);
                double secondProjection = (line.GetEndPoint(1) - junctionAtLineZ).DotProduct(direction);
                double minProjection = Math.Min(firstProjection, secondProjection);
                double maxProjection = Math.Max(firstProjection, secondProjection);

                if (maxProjection > branchTolerance)
                    branches.Add(new JunctionBranch(resultIndex, direction));
                if (minProjection < -branchTolerance)
                    branches.Add(new JunctionBranch(resultIndex, -direction));

                if (minProjection >= -branchTolerance && maxProjection <= branchTolerance)
                {
                    XYZ middlePoint = GetLineMidPoint(line);
                    XYZ middleDirection = new XYZ(
                        middlePoint.X - junction.Point.X,
                        middlePoint.Y - junction.Point.Y,
                        0);
                    if (middleDirection.GetLength() > GeometryTolerance)
                        branches.Add(new JunctionBranch(resultIndex, middleDirection.Normalize()));
                }
            }

            return branches;
        }

        private List<List<JunctionBranch>> GroupJunctionBranchesByAxis(List<JunctionBranch> branches)
        {
            List<List<JunctionBranch>> axes = new List<List<JunctionBranch>>();
            foreach (JunctionBranch branch in branches)
            {
                List<JunctionBranch> axis = axes.FirstOrDefault(item =>
                    AreParallelInXY(item[0].Direction, branch.Direction));
                if (axis == null)
                {
                    axis = new List<JunctionBranch>();
                    axes.Add(axis);
                }

                axis.Add(branch);
            }

            return axes;
        }

        private bool IsLJunction(List<JunctionBranch> branches)
        {
            return branches.Count == 2
                && !AreParallelInXY(branches[0].Direction, branches[1].Direction);
        }

        private bool IsTJunction(List<JunctionBranch> branches, List<List<JunctionBranch>> axes)
        {
            return branches.Count == 3
                && axes.Count == 2
                && axes.Any(axis => axis.Count == 2 && AreOppositeDirections(axis[0].Direction, axis[1].Direction));
        }

        private bool IsCrossJunction(List<JunctionBranch> branches, List<List<JunctionBranch>> axes)
        {
            return branches.Count == 4
                && axes.Count == 2
                && axes.All(axis => axis.Count == 2
                    && AreOppositeDirections(axis[0].Direction, axis[1].Direction));
        }

        private bool AreOppositeDirections(XYZ firstDirection, XYZ secondDirection)
        {
            XYZ first = new XYZ(firstDirection.X, firstDirection.Y, 0).Normalize();
            XYZ second = new XYZ(secondDirection.X, secondDirection.Y, 0).Normalize();
            return first.DotProduct(second) < -0.999;
        }

        private void CreateLOrTCornerRebars(Document doc, XYZ junctionPoint,
            List<JunctionBranch> branches, List<CenterLineRebarResult> results,
            RebarBarType barType, Element host)
        {
            JunctionBranch longBranch = branches
                .OrderByDescending(branch => GetDistanceToLineSegmentInXY(
                    junctionPoint, results[branch.ResultIndex].CenterLine))
                .ThenBy(branch => branch.ResultIndex)
                .First();
            // 10.08.26 - новые узлы для ростверков
            List<JunctionBranch> shortBranches = branches
                .Where(branch => !AreParallelInXY(branch.Direction, longBranch.Direction))
                .OrderBy(branch => branch.ResultIndex)
                .ThenBy(branch => branch.Direction.X)
                .ThenBy(branch => branch.Direction.Y)
                .ToList();
            if (shortBranches.Count == 0)
                return;

            CenterLineRebarResult longResult = results[longBranch.ResultIndex];
            CreateCornerRebarsForLayer(doc, junctionPoint, longResult.TopLines, shortBranches,
                results, true, longBranch.Direction, barType, host);
            CreateCornerRebarsForLayer(doc, junctionPoint, longResult.BottomLines, shortBranches,
                results, false, longBranch.Direction, barType, host);
        }

        private void CreateCornerRebarsForLayer(Document doc, XYZ junctionPoint,
            List<Line> longLines, List<JunctionBranch> shortBranches,
            List<CenterLineRebarResult> results, bool isTopLayer, XYZ longDirection,
            RebarBarType barType, Element host)
        {
            const double longLegLength = 600.0 / 304.8;
            const double shortLegLength = 200.0 / 304.8;
            if (longLines == null || longLines.Count == 0 || shortBranches.Count == 0)
                return;

            JunctionBranch referenceShortBranch = shortBranches[0];
            List<Line> referenceShortLines = GetJunctionLayerLines(
                results[referenceShortBranch.ResultIndex], isTopLayer);
            Line referenceFarLine = GetFarJunctionLine(
                referenceShortLines, junctionPoint, longDirection);
            if (referenceFarLine == null)
                return;

            XYZ shortAxis = GetCanonicalAxisDirection(referenceShortBranch.Direction);
            List<Line> orderedLongLines = longLines
                .OrderBy(line => GetCornerPositionAlongAxis(
                    line, referenceFarLine, junctionPoint, shortAxis))
                .ToList();

            JunctionBranch negativeShortBranch = shortBranches
                .OrderBy(branch => branch.Direction.DotProduct(shortAxis))
                .First();
            JunctionBranch positiveShortBranch = shortBranches
                .OrderByDescending(branch => branch.Direction.DotProduct(shortAxis))
                .First();
            int negativeDirectionCount = shortBranches.Count > 1
                ? orderedLongLines.Count / 2
                : orderedLongLines.Count;

            for (int index = 0; index < orderedLongLines.Count; index++)
            {
                JunctionBranch selectedShortBranch = index < negativeDirectionCount
                    ? negativeShortBranch
                    : positiveShortBranch;
                List<Line> selectedShortLines = GetJunctionLayerLines(
                    results[selectedShortBranch.ResultIndex], isTopLayer);
                Line farLine = GetFarJunctionLine(selectedShortLines, junctionPoint, longDirection);
                if (farLine == null)
                    continue;

                XYZ bendPoint = GetIntersectionPoint(orderedLongLines[index], farLine);
                if (bendPoint == null)
                    continue;

                XYZ longDirectionInXY = new XYZ(longDirection.X, longDirection.Y, 0).Normalize();
                XYZ shortDirectionInXY = selectedShortBranch.Direction;
                XYZ longEnd = bendPoint + longDirectionInXY * longLegLength;
                XYZ shortEnd = bendPoint + shortDirectionInXY * shortLegLength;
                List<Curve> curves = new List<Curve>
                {
                    Line.CreateBound(longEnd, bendPoint),
                    Line.CreateBound(bendPoint, shortEnd)
                };

                Rebar rebar = Rebar.CreateFromCurves(doc, RebarStyle.Standard, barType,
                    null, null, host, XYZ.BasisZ, curves,
                    RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);
                if (rebar != null)
                {
                    Parameter position = rebar.LookupParameter("ADSK_Позиция");
                    if (position != null && !position.IsReadOnly)
                        position.Set("1");
                }
            }
        }

        private List<Line> GetJunctionLayerLines(CenterLineRebarResult result, bool isTopLayer)
        {
            return isTopLayer ? result.TopLines : result.BottomLines;
        }

        private Line GetFarJunctionLine(List<Line> lines, XYZ junctionPoint, XYZ longDirection)
        {
            if (lines == null || lines.Count == 0)
                return null;

            XYZ direction = new XYZ(longDirection.X, longDirection.Y, 0).Normalize();
            return lines.OrderBy(line =>
                (GetLineMidPoint(line) - junctionPoint).DotProduct(direction)).First();
        }

        private double GetCornerPositionAlongAxis(Line longLine, Line farLine,
            XYZ junctionPoint, XYZ shortAxis)
        {
            XYZ intersection = GetIntersectionPoint(longLine, farLine);
            if (intersection == null)
                return double.MaxValue;

            return (intersection - junctionPoint).DotProduct(shortAxis);
        }

        private void CreateCrossStraightRebars(Document doc, XYZ junctionPoint,
            List<List<JunctionBranch>> axes, List<CenterLineRebarResult> results,
            RebarBarType barType, Element host)
        {
            const double anchorageLength = 600.0 / 304.8;

            for (int axisIndex = 0; axisIndex < axes.Count; axisIndex++)
            {
                JunctionBranch axisBranch = axes[axisIndex][0];
                JunctionBranch perpendicularBranch = axes[1 - axisIndex][0];
                CenterLineRebarResult axisResult = results[axisBranch.ResultIndex];
                CenterLineRebarResult perpendicularResult = results[perpendicularBranch.ResultIndex];
                XYZ axisDirection = GetCanonicalAxisDirection(axisBranch.Direction);

                double negativeBoundaryDistance;
                double positiveBoundaryDistance;
                GetBoundaryDistancesAlongDirection(perpendicularResult, axisDirection,
                    out negativeBoundaryDistance, out positiveBoundaryDistance);

                CreateStraightRebarsForLayer(doc, junctionPoint, axisResult.TopLines, axisDirection,
                    negativeBoundaryDistance + anchorageLength,
                    positiveBoundaryDistance + anchorageLength,
                    barType, host);
                CreateStraightRebarsForLayer(doc, junctionPoint, axisResult.BottomLines, axisDirection,
                    negativeBoundaryDistance + anchorageLength,
                    positiveBoundaryDistance + anchorageLength,
                    barType, host);
            }
        }

        private XYZ GetCanonicalAxisDirection(XYZ direction)
        {
            XYZ result = new XYZ(direction.X, direction.Y, 0).Normalize();
            if (result.X < -GeometryTolerance
                || (Math.Abs(result.X) <= GeometryTolerance && result.Y < 0))
                result = -result;
            return result;
        }

        private void GetBoundaryDistancesAlongDirection(CenterLineRebarResult perpendicularResult,
            XYZ direction, out double negativeDistance, out double positiveDistance)
        {
            XYZ perpendicularLineDirection = GetHorizontalDirection(perpendicularResult.CenterLine);
            XYZ rightDirection = new XYZ(
                -perpendicularLineDirection.Y,
                perpendicularLineDirection.X,
                0).Normalize();

            if (rightDirection.DotProduct(direction) >= 0)
            {
                negativeDistance = perpendicularResult.LeftBoundaryDistance;
                positiveDistance = perpendicularResult.RightBoundaryDistance;
            }
            else
            {
                negativeDistance = perpendicularResult.RightBoundaryDistance;
                positiveDistance = perpendicularResult.LeftBoundaryDistance;
            }
        }

        private void CreateStraightRebarsForLayer(Document doc, XYZ junctionPoint,
            List<Line> longitudinalLines, XYZ direction, double negativeLength, double positiveLength,
            RebarBarType barType, Element host)
        {
            foreach (Line longitudinalLine in longitudinalLines)
            {
                XYZ lineStart = longitudinalLine.GetEndPoint(0);
                XYZ lineDirection = GetHorizontalDirection(longitudinalLine);
                XYZ junctionAtLineZ = new XYZ(junctionPoint.X, junctionPoint.Y, lineStart.Z);
                XYZ pointOnLine = lineStart
                    + lineDirection * (junctionAtLineZ - lineStart).DotProduct(lineDirection);
                Line straightLine = Line.CreateBound(
                    pointOnLine - direction * negativeLength,
                    pointOnLine + direction * positiveLength);

                Rebar rebar = Rebar.CreateFromCurves(doc, RebarStyle.Standard, barType,
                    null, null, host, XYZ.BasisZ, new List<Curve> { straightLine },
                    RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);
                if (rebar == null)
                    continue;

                Parameter length = rebar.LookupParameter("ADSK_A");
                if (length != null && !length.IsReadOnly)
                    length.Set(straightLine.Length);
                Parameter position = rebar.LookupParameter("ADSK_Позиция");
                if (position != null && !position.IsReadOnly)
                    position.Set("1");
            }
        }

        private void CreateCornerRebarsAtIntersections(Document doc,
    Dictionary<Line, List<Line>> dictTop,
    Dictionary<Line, List<Line>> dictBottom,
    RebarBarType barType,
    Element host)
        {
            if (barType == null || dictTop.Count < 2 || dictBottom.Count < 2)
                return;

            using (Transaction tx = new Transaction(doc, "Создание угловых арматур"))
            {
                tx.Start();

                List<Line> processedCenterLines = new List<Line>();

                foreach (var centerLine1 in dictTop.Keys)
                {
                    processedCenterLines.Add(centerLine1);
                    int i = 0;

                    foreach (var centerLine2 in dictTop.Keys.Except(processedCenterLines))
                    {
                        if (centerLine1.Intersect(centerLine2) == SetComparisonResult.Overlap)
                        {
                            XYZ centerIntersection = GetIntersectionPoint(centerLine1, centerLine2);
                            if (centerIntersection == null) continue;

                            i++;
                            List<Line> topLines1 = dictTop[centerLine1];
                            List<Line> topLines2 = dictTop[centerLine2];
                            List<Line> bottomLines1 = dictBottom[centerLine1];
                            List<Line> bottomLines2 = dictBottom[centerLine2];

                            // Создаем уголки для верхних линий (от первого к последнему)
                            CreateCornersBetweenLines(doc, topLines1, topLines2, centerIntersection, barType, host);

                            // Создаем уголки для нижних линий (от первого к последнему)
                            CreateCornersBetweenLines(doc, bottomLines1, bottomLines2, centerIntersection, barType, host);
                            if (i == topLines1.Count())
                                break;
                        }
                    }
                }

                tx.Commit();
            }
        }
        List<XYZ> corners = new List<XYZ>();
        private void CreateCornersBetweenLines(
    Document doc,
    List<Line> lines1,
    List<Line> lines2,
    XYZ centerIntersection,
    RebarBarType barType,
    Element host)
        {
            int minCount = Math.Min(lines1.Count, lines2.Count);

            for (int i = 0; i < minCount; i++)
            {
                Line line1 = lines1[i];
                Line line2 = lines2[i];

                // точка пересечения линий
                XYZ intersection = GetIntersectionPoint(line1, line2);
                if (intersection == null)
                    continue;

                if (!TryRegisterCornerPoint(intersection))
                    continue;

                XYZ dir1 = GetCorrectDirection(line1, intersection);
                XYZ dir2 = GetCorrectDirection(line2, intersection);
                CreateCornerRebar(doc, line1, line2, intersection, dir1, dir2, barType, host);
            }
        }

        private XYZ GetCorrectDirection(Line line, XYZ intersection)
        {
            // Определяем, к какому концу линии ближе точка пересечения
            double distToStart = intersection.DistanceTo(line.GetEndPoint(0));
            double distToEnd = intersection.DistanceTo(line.GetEndPoint(1));

            // Возвращаем направление от точки пересечения вдоль линии
            return distToStart < distToEnd
                ? (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize()
                : (line.GetEndPoint(0) - line.GetEndPoint(1)).Normalize();
        }

        private void CreateCornerRebar(Document doc,
            Line line1,
            Line line2,
            XYZ intersection,
            XYZ dir1,
            XYZ dir2,
            RebarBarType barType,
            Element host)
        {
            double legLength = 150 / 304.8; // 15 см в каждую сторону

            // Создаем ножки уголка с учетом направления
            Line leg1 = Line.CreateBound(intersection + dir1 * legLength, intersection);
            Line leg2 = Line.CreateBound(intersection, intersection + dir2 * legLength);

            List<Curve> cornerCurves = new List<Curve> { leg1, leg2 };

            Rebar cornerRebar = Rebar.CreateFromCurves(doc, RebarStyle.Standard, barType,
                null, null, host, XYZ.BasisZ, cornerCurves,
                RebarHookOrientation.Right, RebarHookOrientation.Left,
                true, true);
            cornerRebar.LookupParameter("ADSK_Позиция").Set("1");

        }

        private List<Element> CreateRebarFromLines(Document doc, List<Line> lines, RebarBarType barType, RebarStyle style, Element host, bool bottom)
        {
            return CreateRebarFromLines(doc, lines, barType, style, host, bottom, WindowGrillageCreator_v3.isKnittedMode);
        }

        private List<Element> CreateRebarFromLines(Document doc, List<Line> lines, RebarBarType barType, RebarStyle style, Element host, bool bottom, bool isKnittedMode)
        {
            List<Element> result = new List<Element>();
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Создание арматуры");
                double extensionLength = 0.08202; // Расширение в футах

                foreach (Line line in lines)
                {
                    // Получаем направление линии
                    XYZ direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();

                    // Расширяем линию в обе стороны
                    XYZ newStart = line.GetEndPoint(0) - direction * extensionLength;
                    XYZ newEnd = line.GetEndPoint(1) + direction * extensionLength;

                    // Создаем расширенную линию
                    Line extendedLine = Line.CreateBound(newStart, newEnd);

                    // Создаем арматуру из расширенной линии
                    Rebar rebar = Rebar.CreateFromCurves(doc, style, barType, null, null, host,
                        XYZ.BasisZ, new List<Curve>() { extendedLine },
                        RebarHookOrientation.Right, RebarHookOrientation.Left, true, true);
                    result.Add(rebar);
                    rebar.LookupParameter("ADSK_A").Set(extendedLine.Length);

                    if (!isKnittedMode)
                    {
                        rebar.LookupParameter("ADSK_Марка изделия").Set("Кр-1");
                    }
                    if (bottom)
                    {
                        rebar.LookupParameter("ADSK_Главная деталь изделия").Set(1);
                    }
                }
                tx.Commit();
            }
            return result;
        }

        private void CreateRebarSet(Document doc, List<Line> lines, RebarBarType barType, RebarStyle style, Element host, XYZ dir, int count, double step, bool poz)
        {
            CreateRebarSet(doc, lines, barType, style, host, dir, count, step, poz, WindowGrillageCreator_v3.isKnittedMode);
        }

        private void CreateRebarSet(Document doc, List<Line> lines, RebarBarType barType, RebarStyle style, Element host, XYZ dir, int count, double step, bool poz, bool isKnittedMode)
        {
            RebarShape shape = (RebarShape)new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).WhereElementIsElementType().Where(x => x.Name == "Х_51").First();
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Создание арматуры");
                double extensionLength = 0.08202; // Расширение в футах
                if (isKnittedMode)
                {
                    List<Curve> lines2 = new List<Curve>();
                    foreach (Line l in lines)
                    {
                        lines2.Add(l);
                    }
                    RebarHookType hook = (RebarHookType)new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).WhereElementIsElementType().Where(x => x.Name == barType.Name).FirstOrDefault();
                    Rebar rebarSet = Rebar.CreateFromCurves(doc, style, barType, hook, hook, host, dir, lines2, RebarHookOrientation.Left, RebarHookOrientation.Left, true, true);
                    //rebarSet.LookupParameter("ADSK_A").Set(extendedLine.Length);
                    if (rebarSet != null)
                    {
                        rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_LAYOUT_RULE).Set(3);
                        rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING).Set(step);
                        rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS).Set(count);
                        rebarSet.GetShapeDrivenAccessor().BarsOnNormalSide = false;
                        ElementId shapeToDel = rebarSet.GetShapeId();
                        rebarSet.LookupParameter("ADSK_A_bent").Set(rebarSet.LookupParameter("ADSK_A_bent").AsDouble() + barType.BarModelDiameter);
                        rebarSet.LookupParameter("ADSK_B_bent").Set(rebarSet.LookupParameter("ADSK_B_bent").AsDouble() + barType.BarModelDiameter);

                        rebarSet.LookupParameter("Форма").Set(shape.Id);
                        doc.Delete(shapeToDel);
                        rebarSet.LookupParameter("ADSK_Позиция").Set("1");
                    }
                }
                else
                {
                    foreach (Line line in lines)
                    {
                        // Получаем направление линии
                        XYZ direction = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();

                        // Расширяем линию в обе стороны
                        XYZ newStart = line.GetEndPoint(0) - direction * extensionLength;
                        XYZ newEnd = line.GetEndPoint(1) + direction * extensionLength;

                        // Создаем расширенную линию
                        Line extendedLine = Line.CreateBound(newStart, newEnd);

                        // Создаем набор арматуры из расширенной линии
                        Rebar rebarSet = Rebar.CreateFromCurves(doc, style, barType, null, null, host,
                            dir, new List<Curve>() { extendedLine },
                            RebarHookOrientation.Right, RebarHookOrientation.Left, true, false);
                        rebarSet.LookupParameter("ADSK_A").Set(extendedLine.Length);
                        //Plane plane;
                        //if (line.Direction.Z != 0)
                        //{
                        //    plane = Plane.CreateByThreePoints(extendedLine.GetEndPoint(0), extendedLine.GetEndPoint(1), extendedLine.GetEndPoint(0) + 1 * XYZ.BasisX);
                        //}
                        //else
                        //{
                        //    plane = Plane.CreateByThreePoints(extendedLine.GetEndPoint(0), extendedLine.GetEndPoint(1), extendedLine.GetEndPoint(0) + 1 * XYZ.BasisZ);
                        //}
                        //// Создаем модель линии
                        //doc.Create.NewDetailCurve(extendedLine, SketchPlane.Create(doc, plane));

                        if (rebarSet != null)
                        {
                            rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_LAYOUT_RULE).Set(3);
                            rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING).Set(step);
                            rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS).Set(count);
                            rebarSet.GetShapeDrivenAccessor().BarsOnNormalSide = true;
                            if (!poz)
                            {
                                rebarSet.LookupParameter("ADSK_Марка изделия").Set("Кр-1");
                            }
                            else
                            {
                                rebarSet.LookupParameter("ADSK_Позиция").Set("1");
                            }
                        }
                    }
                }
                tx.Commit();
            }
        }

        private List<Line> ExtendCenterLines(List<Line> centerLines, double modLength)
        {
            List<Line> extendedLines = new List<Line>();
            double extensionValue = modLength - (50 / 304.8); // Длина для дотягивания
            double reductionValue = 50 / 304.8; // Длина для уменьшения

            foreach (Line currentLine in centerLines)
            {
                XYZ startPoint = currentLine.GetEndPoint(0);
                XYZ endPoint = currentLine.GetEndPoint(1);
                XYZ lineDirection = (endPoint - startPoint).Normalize();

                // Списки пересечений для каждого конца
                List<Line> startIntersections = new List<Line>();
                List<Line> endIntersections = new List<Line>();

                // Находим все пересечения текущей линии с другими
                foreach (Line otherLine in centerLines)
                {
                    if (currentLine == otherLine) continue;

                    IntersectionResultArray results;
                    currentLine.Intersect(otherLine, out results);

                    if (results != null && results.Size > 0)
                    {
                        foreach (IntersectionResult result in results)
                        {
                            XYZ intersection = result.XYZPoint;
                            // Определяем к какому концу ближе пересечение
                            double distToStart = intersection.DistanceTo(startPoint);
                            double distToEnd = intersection.DistanceTo(endPoint);

                            if (distToStart < distToEnd)
                                startIntersections.Add(otherLine);
                            else
                                endIntersections.Add(otherLine);
                        }
                    }
                }

                // Обрабатываем каждый конец линии
                XYZ newStart = ProcessLineEnd(startPoint, startIntersections, -lineDirection, extensionValue, reductionValue);
                XYZ newEnd = ProcessLineEnd(endPoint, endIntersections, lineDirection, extensionValue, reductionValue);

                extendedLines.Add(Line.CreateBound(newStart, newEnd));
            }

            return extendedLines;
        }

        private XYZ ProcessLineEnd(XYZ point, List<Line> intersectingLines, XYZ lineDirection,
                                  double extensionValue, double reductionValue)
        {
            switch (intersectingLines.Count)
            {
                case 0: // Нет пересечений - уменьшаем
                    return point - lineDirection * reductionValue;

                case 1: // Одно пересечение - дотягиваем
                    return point + lineDirection * extensionValue;

                case 2: // Два пересечения - проверяем перпендикулярность
                    if (AreLinesPerpendicularToBoth(intersectingLines, lineDirection))
                        return point + lineDirection * extensionValue;
                    break;
                default:
                    return point;

                    // Для 3+ пересечений ничего не делаем
            }

            return point; // Возвращаем исходную точку
        }

        private bool AreLinesPerpendicularToBoth(List<Line> lines, XYZ referenceDirection)
        {
            foreach (Line line in lines)
            {
                XYZ otherDirection = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                double dotProduct = Math.Abs(referenceDirection.DotProduct(otherDirection));

                // Если хотя бы одна линия не перпендикулярна - возвращаем false
                if (dotProduct > 1e-6)
                    return false;
            }
            return true;
        }


        private List<Line> ExtendLinesToConnect(List<Line> lines, double modLength)
        {
            List<Line> sortedLines = new List<Line>(lines); // Второй список линий
            sortedLines = sortedLines.OrderBy(line =>
            {
                XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                if (dir.IsAlmostEqualTo(new XYZ(0, 1, 0))) return 0;
                if (dir.IsAlmostEqualTo(new XYZ(0, -1, 0))) return 1;
                if (dir.IsAlmostEqualTo(new XYZ(1, 0, 0))) return 2;
                else return 3;
            })
                    .ToList();
            // Проходим по всем линиям
            for (int i = 0; i < sortedLines.Count; i++)
            {
                Line currentLine = sortedLines[i];
                if (currentLine == null) continue; // Пропускаем уже обработанные линии

                XYZ currentDir = (currentLine.GetEndPoint(1) - currentLine.GetEndPoint(0)).Normalize(); // Направление текущей линии

                // Сортируем второй список по направлению:
                // 1. То же направление.
                // 2. Обратное направление.
                // 3. Другие направления.
                var sortedByDirection = sortedLines.Except(new List<Line>() { currentLine })
                    .Where(line => line != null)
                    .OrderBy(line =>
                    {
                        XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                        if (dir.IsAlmostEqualTo(currentDir)) return 0; // То же направление
                        if (dir.IsAlmostEqualTo(-currentDir)) return 1; // Обратное направление
                        return 2; // Другие направления
                    })
                    .ToList();
                sortedByDirection.Insert(0, currentLine);
                // Проходим по отсортированному списку
                for (int j = 0; j < sortedByDirection.Count; j++)
                {
                    Line otherLine = sortedByDirection[j];
                    if (otherLine == null || otherLine == sortedLines[i]) continue; // Пропускаем текущую линию и уже обработанные

                    XYZ otherDir = (otherLine.GetEndPoint(1) - otherLine.GetEndPoint(0)).Normalize(); // Направление другой линии

                    // Проверяем расстояние между конечными точками

                    XYZ point00 = sortedLines[i].GetEndPoint(0);
                    XYZ point01 = sortedLines[i].GetEndPoint(1);
                    XYZ point10 = otherLine.GetEndPoint(0);
                    XYZ point11 = otherLine.GetEndPoint(1);

                    double dist1 = point00.DistanceTo(point10);
                    double dist2 = point00.DistanceTo(point11);
                    double dist3 = point01.DistanceTo(point10);
                    double dist4 = point01.DistanceTo(point11);

                    double[] distances = { dist1, dist2, dist3, dist4 };
                    double distance = distances.Min();

                    if (distance > modLength * 2 + 1e-6)
                        continue;

                    XYZ closestPointCurrent = null;
                    XYZ closestPointOther = null;
                    XYZ startCurrent = null;
                    XYZ endOther = null;

                    if (distance == dist1)
                    {
                        closestPointCurrent = point00;
                        closestPointOther = point10;
                        startCurrent = point01;
                        endOther = point11;
                    }
                    else if (distance == dist2)
                    {
                        closestPointCurrent = point00;
                        closestPointOther = point11;
                        startCurrent = point01;
                        endOther = point10;
                    }
                    else if (distance == dist3)
                    {
                        closestPointCurrent = point01;
                        closestPointOther = point10;
                        startCurrent = point00;
                        endOther = point11;
                    }
                    else if (distance == dist4)
                    {
                        closestPointCurrent = point01;
                        closestPointOther = point11;
                        startCurrent = point00;
                        endOther = point10;
                    }


                    // Если направление одинаковое или обратное, и расстояние равно modLength*2
                    if ((otherDir.IsAlmostEqualTo(currentDir) || otherDir.IsAlmostEqualTo(-currentDir)) &&
                        Math.Abs(distance - modLength * 2) < 1e-6)
                    {
                        // Дотягиваем линии до середины
                        XYZ midPoint = (closestPointCurrent + closestPointOther) / 2;

                        int sortedByDir1 = 0;
                        int sortedByDir2 = sortedByDirection.IndexOf(otherLine);
                        sortedLines[i] = Line.CreateBound(startCurrent, midPoint);
                        sortedLines[sortedLines.IndexOf(otherLine)] = Line.CreateBound(midPoint, endOther);
                        sortedByDirection[sortedByDir1] = Line.CreateBound(startCurrent, midPoint);
                        sortedByDirection[sortedByDir2] = Line.CreateBound(midPoint, endOther);

                    }

                    // Если направление разное, и расстояние равно sqrt(2) * modLength
                    if (!otherDir.IsAlmostEqualTo(currentDir) && !otherDir.IsAlmostEqualTo(-currentDir) &&
                        Math.Abs(distance - Math.Sqrt(2) * modLength) < 1e-6)
                    {
                        // Дотягиваем каждую линию на modLength
                        XYZ extensionVector1 = (closestPointCurrent - startCurrent).Normalize() * modLength;
                        XYZ extensionVector2 = (closestPointOther - endOther).Normalize() * modLength;

                        int sortedByDir1 = 0;
                        int sortedByDir2 = sortedByDirection.IndexOf(otherLine);

                        sortedLines[i] = Line.CreateBound(startCurrent, closestPointCurrent + extensionVector1);
                        sortedLines[sortedLines.IndexOf(otherLine)] = Line.CreateBound(closestPointOther + extensionVector2, endOther);
                        sortedByDirection[sortedByDir1] = Line.CreateBound(startCurrent, closestPointCurrent + extensionVector1);
                        sortedByDirection[sortedByDir2] = Line.CreateBound(closestPointOther + extensionVector2, endOther);

                    }

                    // Если направление разное, и расстояние равно modLength
                    if (!otherDir.IsAlmostEqualTo(currentDir)
                    && !otherDir.IsAlmostEqualTo(-currentDir)
                    && Math.Abs(currentDir.DotProduct(otherDir)) < 1e-6
                    && Math.Abs(distance - modLength) < 1e-6)
                    {

                        int sortedByDir2 = sortedByDirection.IndexOf(otherLine);
                        double an1 = Line.CreateBound(closestPointCurrent, endOther).Direction.DotProduct(currentDir);
                        double an2 = Line.CreateBound(startCurrent, closestPointOther).Direction.DotProduct(otherDir);
                        // Дотягиваем линии, чтобы конечные точки совпали
                        if (Math.Abs(an1) < 1e-9)//|| Line.CreateBound(closestPointCurrent, endOther).Direction.DotProduct(-currentDir) < 1e-9)
                        {
                            sortedLines[sortedLines.IndexOf(otherLine)] = Line.CreateBound(closestPointCurrent, endOther);
                            sortedByDirection[sortedByDir2] = Line.CreateBound(closestPointCurrent, endOther);
                        }
                        else if (Math.Abs(an2) < 1e-9)// || Line.CreateBound(startCurrent, closestPointOther).Direction.DotProduct(-otherDir) < 1e-9)
                        {
                            sortedLines[i] = Line.CreateBound(startCurrent, closestPointOther);
                            sortedByDirection[0] = Line.CreateBound(startCurrent, closestPointOther);
                        }

                        break;
                    }
                }
            }

            return sortedLines;
        }

        // Метод для создания линий модели в Revit
        private void CreateModelLines(Document doc, List<Line> lines)
        {
            // Получаем плоскость для создания линий (например, плоскость уровня)
            Level level = doc.ActiveView.GenLevel;
            if (level == null)
            {
                MessageBox.Show("Не удалось получить уровень для создания линий.", "Ошибка");
                return;
            }


            // Начинаем транзакцию
            using (Transaction trans = new Transaction(doc, "Создание линий модели"))
            {
                trans.Start();

                foreach (Line line in lines)
                {
                    Plane plane;
                    if (line.Direction.Z != 0)
                    {
                        plane = Plane.CreateByThreePoints(line.GetEndPoint(0), line.GetEndPoint(1), line.GetEndPoint(0) + 1 * XYZ.BasisX);
                    }
                    else
                    {
                        plane = Plane.CreateByThreePoints(line.GetEndPoint(0), line.GetEndPoint(1), line.GetEndPoint(0) + 1 * XYZ.BasisZ);
                    }
                    // Создаем модель линии
                    doc.Create.NewModelCurve(line, SketchPlane.Create(doc, plane));
                }

                trans.Commit();
            }
        }

        public double modLength;

        // Метод для вычисления средних линий
        // БАГ - паралелльные линии с нужными проекциями, но далеко, проверять пересекает ли их общая нормаль кого-то из профиля кроме них
        private List<Line> ComputeCenterLines(List<Line> sideCurves)
        {
            List<Line> centerLines = new List<Line>();

            // Проходим по всем парам линий
            for (int i = 0; i < sideCurves.Count; i++)
            {
                Line line1 = sideCurves[i];
                XYZ dir1 = (line1.GetEndPoint(1) - line1.GetEndPoint(0)).Normalize(); // Направление первой линии

                for (int j = i + 1; j < sideCurves.Count; j++)
                {
                    Line line2 = sideCurves[j];
                    XYZ dir2 = (line2.GetEndPoint(1) - line2.GetEndPoint(0)).Normalize(); // Направление второй линии

                    // Проверяем, параллельны ли линии
                    if (dir1.IsAlmostEqualTo(dir2) || dir1.IsAlmostEqualTo(-dir2))
                    {
                        // 24.04.26 - наложение линий больше нуля
                        if (GetProjectionLength(line1, line2) >= 0.01)
                        {
                            // Проверяем, что линии не пересекаются
                            if (!DoLinesIntersect(line1, line2))
                            {

                                // Создаем среднюю линию
                                Line centerLine = CreateCenterLine(line1, line2);
                                // 15.04.26 - ошибки в коротких линиях
                                if (centerLine != null)
                                {
                                    // Проверяем, что линии полностью внутри контура
                                    if (IsLineInsideBoundary(centerLine, sideCurves) && LinesNormalDoesntIntersectProfile(line1, line2, sideCurves, centerLine) && (centerLine.Direction.IsAlmostEqualTo(line1.Direction) || centerLine.Direction.IsAlmostEqualTo(line1.Direction.Negate())))
                                    {
                                        // Проверяем, что средняя линия еще не была добавлена
                                        if (!IsLineAlreadyAdded(centerLine, centerLines))
                                        {
                                            centerLines.Add(centerLine);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Вычисляем расстояние от середины центральной линии до контура
            List<double> distances = new List<double>();
            foreach (Line centerLine in centerLines)
            {
                double distance = CalculateDistanceToBoundary(centerLine, sideCurves);
                distances.Add(distance);
            }

            // Вычисляем моду расстояний
            modLength = CalculateModeDistance(distances);

            return centerLines;
        }

        private bool LinesNormalDoesntIntersectProfile(Line line1, Line line2, List<Line> profile, Line center)
        {
            XYZ mid = (center.GetEndPoint(0) + center.GetEndPoint(1)) / 2;
            XYZ ProjectOntoLine(XYZ pt, Line ln)
            {
                XYZ p0 = ln.GetEndPoint(0);
                XYZ dir = (ln.GetEndPoint(1) - p0).Normalize();
                double t = (pt - p0).DotProduct(dir);
                t = Math.Max(0.0, Math.Min(t, ln.Length));
                return p0 + dir * t;
            }

            XYZ foot1 = ProjectOntoLine(mid, line1);
            XYZ foot2 = ProjectOntoLine(mid, line2);

            Line normal1 = Line.CreateBound(mid, foot1);
            Line normal2 = Line.CreateBound(mid, foot2);

            foreach (var normal in new[] { normal1, normal2 })
            {
                foreach (var edge in profile)
                {
                    if (edge == line1 || edge == line2)
                        continue;
                    IntersectionResultArray results;
                    normal.Intersect(edge, out results);
                    if (results != null && results.Size > 0)
                        return false;
                }
            }
            return true;
        }


        private double GetProjectionLength(Line line1, Line line2)
        {
            // Направления линий
            XYZ dir1 = (line1.GetEndPoint(1) - line1.GetEndPoint(0)).Normalize();
            XYZ dir2 = (line2.GetEndPoint(1) - line2.GetEndPoint(0)).Normalize();

            // Проверяем, что линии параллельны
            if (!dir1.IsAlmostEqualTo(dir2) && !dir1.IsAlmostEqualTo(-dir2))
            {
                return 0; // Линии не параллельны, проекция равна нулю
            }

            // Проецируем все точки линий на направление dir1
            double line1Start = line1.GetEndPoint(0).DotProduct(dir1);
            double line1End = line1.GetEndPoint(1).DotProduct(dir1);
            double line2Start = line2.GetEndPoint(0).DotProduct(dir1);
            double line2End = line2.GetEndPoint(1).DotProduct(dir1);

            // Находим минимальную и максимальную проекции для каждой линии
            double line1Min = Math.Min(line1Start, line1End);
            double line1Max = Math.Max(line1Start, line1End);
            double line2Min = Math.Min(line2Start, line2End);
            double line2Max = Math.Max(line2Start, line2End);

            // Вычисляем перекрытие проекций
            double overlapStart = Math.Round(Math.Max(line1Min, line2Min), 9);
            double overlapEnd = Math.Round(Math.Min(line1Max, line2Max), 9);

            // Если перекрытие есть, возвращаем его длину
            if (overlapEnd > overlapStart && overlapStart != overlapEnd)
            {
                return overlapEnd - overlapStart;
            }

            // Если перекрытия нет, возвращаем 0
            return 0;
        }

        private List<Line> FilterLinesByDistanceToBoundary(List<Line> centerLines, List<Line> profile, double modLength)
        {
            List<Line> filteredLines = new List<Line>();

            foreach (Line centerLine in centerLines)
            {
                double distance = CalculateDistanceToBoundary(centerLine, profile);

                // Если расстояние равно моде (с учетом погрешности), добавляем линию
                if (Math.Abs(distance - modLength) < 1e-6)
                {
                    filteredLines.Add(centerLine);
                }
            }

            return filteredLines;
        }

        private double CalculateModeDistance(List<double> distances)
        {

            // Группируем расстояния и находим моду
            var distanceGroups = distances
                .GroupBy(d => d)
                .Select(g => new { Distance = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Distance);
            // 23.10.25 - исправления в ростверках
            return distanceGroups.FirstOrDefault() == null ? 0 : distanceGroups.FirstOrDefault().Distance;
        }

        private double CalculateDistanceToBoundary(Line centerLine, List<Line> profile)
        {
            BoundaryDistances distances = CalculateBoundaryDistances(centerLine, profile);
            return Math.Min(distances.Left, distances.Right);
        }

        private double FindDistanceToIntersection(XYZ startPoint, XYZ direction, List<Line> profile)
        {
            double maxDistance = 1000; // Максимальное расстояние для поиска
            Line ray = Line.CreateBound(startPoint, startPoint + direction * maxDistance);

            double minDistance = double.MaxValue;

            // Проходим по всем линиям контура
            foreach (Curve curve in profile)
            {
                if (curve is Line boundaryLine)
                {
                    if (DoLinesIntersect(ray, boundaryLine))
                    {
                        XYZ intersectionPoint = GetIntersectionPoint(ray, boundaryLine);
                        double distance = startPoint.DistanceTo(intersectionPoint);

                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                    }
                }
            }

            return minDistance;
        }

        private XYZ GetIntersectionPoint(Line line1, Line line2)
        {
            XYZ p1 = line1.GetEndPoint(0);
            XYZ p2 = line1.GetEndPoint(1);
            XYZ p3 = line2.GetEndPoint(0);
            XYZ p4 = line2.GetEndPoint(1);

            // Векторы направлений
            XYZ dir1 = p2 - p1;
            XYZ dir2 = p4 - p3;

            // Вектор между начальными точками
            XYZ diff = p3 - p1;

            // Решаем систему уравнений для нахождения точки пересечения
            double cross = dir1.X * dir2.Y - dir1.Y * dir2.X;
            if (Math.Abs(cross) < 1e-6)
            {
                return null; // Линии параллельны
            }

            double t = (diff.X * dir2.Y - diff.Y * dir2.X) / cross;
            return p1 + dir1 * t;
        }

        private bool DoLinesIntersect(Line line1, Line line2)
        {
            XYZ p1 = line1.GetEndPoint(0);
            XYZ p2 = line1.GetEndPoint(1);
            XYZ p3 = line2.GetEndPoint(0);
            XYZ p4 = line2.GetEndPoint(1);

            // Векторы направлений
            // 23.10.25 - исправления в ростверках
            XYZ dir1 = line1.Direction;
            XYZ dir2 = line2.Direction;

            double p1_ = line1.GetEndPoint(0).DotProduct(dir1);
            double p2_ = line1.GetEndPoint(1).DotProduct(dir1);
            double p3_ = line2.GetEndPoint(0).DotProduct(dir2);
            double p4_ = line2.GetEndPoint(1).DotProduct(dir2);

            // Проверка пересечения
            if (line1.Intersect(line2) == SetComparisonResult.Overlap || (p1_ - p2_ == 0 || p1_ - p4_ == 0 || p3_ - p1_ == 0 || p3_ - p4_ == 0))
            {
                return true;
            }

            return false;
        }

        private bool IsLineInsideBoundary(Line line, List<Line> profile)
        {
            // Проверяем начальную и конечную точки линии
            XYZ startPoint = line.GetEndPoint(0);
            XYZ endPoint = line.GetEndPoint(1);
            XYZ dir = (endPoint - startPoint).Normalize();
            return IsPointInsideBoundary(startPoint, profile, dir) && IsPointInsideBoundary(endPoint, profile, -dir) && LineDontIntersectProfile(line, profile, dir);
        }

        private bool LineDontIntersectProfile(Line line, List<Line> profile, XYZ dir)
        {
            int intersectionCount = 0;
            Line L = Line.CreateBound(line.GetEndPoint(0) + 0.0001 * dir, line.GetEndPoint(1) - 0.0001 * dir);

            foreach (Line prof in profile)
            {
                if (DoLinesIntersect(L, prof))
                {
                    intersectionCount++;
                }
            }
            return intersectionCount == 0;
        }

        private bool IsPointInsideBoundary(XYZ point, List<Line> profile, XYZ dir)
        {
            // Проводим луч по оси X и считаем пересечения
            int intersectionCount = 0;
            XYZ direction = dir.CrossProduct(XYZ.BasisZ);
            XYZ rayEnd = point + direction * 1000;
            Line ray = Line.CreateBound(point + 0.000001 * dir, rayEnd + 0.000001 * dir);

            foreach (Line line in profile)
            {
                if (DoLinesIntersect(ray, line))
                {
                    intersectionCount++;
                }
            }

            // Если количество пересечений нечетное, точка внутри контура
            return intersectionCount % 2 != 0;
        }

        private bool IsLineAlreadyAdded(Line newLine, List<Line> existingLines)
        {
            foreach (Line line in existingLines)
            {
                // Проверяем, совпадают ли начальные и конечные точки
                if (line.GetEndPoint(0).IsAlmostEqualTo(newLine.GetEndPoint(0)) &&
                    line.GetEndPoint(1).IsAlmostEqualTo(newLine.GetEndPoint(1)))
                {
                    return true; // Линия уже добавлена
                }

                // Проверяем, совпадают ли начальная и конечная точки в обратном порядке
                if (line.GetEndPoint(0).IsAlmostEqualTo(newLine.GetEndPoint(1)) &&
                    line.GetEndPoint(1).IsAlmostEqualTo(newLine.GetEndPoint(0)))
                {
                    return true; // Линия уже добавлена
                }
            }

            return false; // Линия не найдена в списке
        }
        private double DistanceBetweenParallelLines(Line line1, Line line2)
        {

            if (line1.GetEndPoint(0).X == line1.GetEndPoint(1).X)
            {
                return Math.Abs(line1.GetEndPoint(0).X - line2.GetEndPoint(0).X);
            }
            else
            {
                return Math.Abs(line1.GetEndPoint(0).Y - line2.GetEndPoint(0).Y);
            }
        }
        private Line CreateCenterLine(Line line1, Line line2)
        {
            // Берем начальные и конечные точки линий
            XYZ start1 = line1.GetEndPoint(0);
            XYZ end1 = line1.GetEndPoint(1);
            XYZ start2 = line2.GetEndPoint(0);
            XYZ end2 = line2.GetEndPoint(1);

            // Определяем направление линий
            XYZ dir1 = (end1 - start1).Normalize();
            XYZ dir2 = (end2 - start2).Normalize();
            List<XYZ> points = new List<XYZ>() { start1, start2, end1, end2 };

            // Проверяем, направлены ли линии вдоль оси X (Y-координаты равны)
            if (Math.Abs(dir1.Y) < 1e-6 && Math.Abs(dir2.Y) < 1e-6)
            {
                // Линии направлены вдоль оси X
                points = points.OrderBy(x => x.X).ToList();
                // 15.04.26 - ошибки в коротких линиях
                if (points[2].X - points[1].X < 1e-6)
                    return null;
                XYZ midStart = new XYZ(points[1].X, (start1.Y + start2.Y) / 2, start1.Z);
                XYZ midEnd = new XYZ(points[2].X, (end1.Y + end2.Y) / 2, start1.Z);

                return Line.CreateBound(midStart, midEnd);
            }
            // Проверяем, направлены ли линии вдоль оси Y (X-координаты равны)
            else if (Math.Abs(dir1.X) < 1e-6 && Math.Abs(dir2.X) < 1e-6)
            {
                // Линии направлены вдоль оси Y
                points = points.OrderBy(x => x.Y).ToList();
                // 15.04.26 - ошибки в коротких линиях
                if (points[2].Y - points[1].Y < 1e-6)
                    return null;
                XYZ midStart = new XYZ((start1.X + start2.X) / 2, points[1].Y, start1.Z);
                XYZ midEnd = new XYZ((end1.X + end2.X) / 2, points[2].Y, start1.Z);


                return Line.CreateBound(midStart, midEnd);
            }
            else
            {
                // Если линии не направлены строго по осям, используем общий подход
                // 16.07 - доработка под углом
                XYZ dirA = (line1.GetEndPoint(1) - line1.GetEndPoint(0)).Normalize();
                XYZ dirB = (line2.GetEndPoint(1) - line2.GetEndPoint(0)).Normalize();

                // Точка, которую проецируем
                XYZ pointA = line1.GetEndPoint(0); // начало первой линии
                XYZ pointB = line2.GetEndPoint(0); // начало второй линии

                // Вектор от начала lineB до начала lineA
                XYZ vec = pointA - pointB;

                // Проекция vec на направление линии B
                double projectionLength = vec.DotProduct(dirB);
                XYZ projectedStart = pointB + dirB.Multiply(projectionLength);

                // Теперь проецируем вектор направления lineA на направление lineB
                double lineA_length = line1.Length;
                double dirProjectionLength = dirA.DotProduct(dirB) * lineA_length;
                XYZ projectedEnd = projectedStart + dirB.Multiply(dirProjectionLength);

                Line l = Line.CreateBound(projectedStart, projectedEnd);
                XYZ minPoint = (line2.GetEndPoint(0) + line2.GetEndPoint(1)) / 2;
                XYZ dir = Line.CreateBound(minPoint, line1.Project(minPoint).XYZPoint).Direction;
                // 23.10.25 - исправления в ростверках
                var p = (line1.Project(minPoint).XYZPoint + minPoint) / 2;
                var dist = minPoint.DistanceTo(new XYZ(p.X, p.Y, minPoint.Z));

                return Line.CreateBound(l.GetEndPoint(0) + dir * dist, l.GetEndPoint(1) + dir * dist);
            }
        }


    }
}
