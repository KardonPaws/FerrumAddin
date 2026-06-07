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
                MessageBox.Show(ex.Message, "Ошибка");
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

                    List<Line> centerLines = ComputeCenterLines(context.Profile);
                    centerLines = PrepareCenterLinesForModelLines(centerLines, context.Profile);

                    List<GrillageModelLine> modelLines = new List<GrillageModelLine>();
                    foreach (Line centerLine in centerLines)
                    {
                        if (centerLine.Length < 500 / 304.8)
                            continue;
                        BoundaryDistances distances = CalculateBoundaryDistances(centerLine, context.Profile);
                        if (!AreBoundaryDistancesValid(distances))
                            continue;

                        GrillageLineData data = CreateLineDataFromCurrentSettings(context, distances);
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

            List<Line> extendedToBoundary = ExtendCenterLinesToNearestBoundary(centerLines, profile, maxExtendDistance, boundaryGap);
            return ConnectCollinearCenterLines(extendedToBoundary, maxExtendDistance);
        }

        private List<Line> ExtendCenterLinesToNearestBoundary(List<Line> centerLines, List<Line> profile, double maxDistance, double boundaryGap)
        {
            List<Line> result = new List<Line>();

            foreach (Line centerLine in centerLines)
            {
                XYZ start = centerLine.GetEndPoint(0);
                XYZ end = centerLine.GetEndPoint(1);
                XYZ direction = (end - start).Normalize();

                XYZ newStart = ExtendPointTowardBoundary(start, -direction, profile, maxDistance, boundaryGap);
                XYZ newEnd = ExtendPointTowardBoundary(end, direction, profile, maxDistance, boundaryGap);

                if (newStart.DistanceTo(newEnd) > GeometryTolerance)
                    result.Add(Line.CreateBound(newStart, newEnd));
            }

            return result;
        }

        private XYZ ExtendPointTowardBoundary(XYZ point, XYZ direction, List<Line> profile, double maxDistance, double boundaryGap)
        {
            double distance;
            if (!TryFindNearestBoundaryDistance(point, direction, profile, maxDistance, out distance))
                return point;

            double extension = distance - boundaryGap;
            if (extension <= GeometryTolerance)
                return point;

            return point + direction.Normalize() * extension;
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

        private List<Line> ConnectCollinearCenterLines(List<Line> centerLines, double maxGapDistance)
        {
            List<Line> result = centerLines.ToList();

            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    if (!AreLinesCollinearInXY(result[i], result[j]))
                        continue;

                    ClosestLineEndPair pair = GetClosestLineEndPair(result[i], result[j]);
                    if (pair.Distance <= GeometryTolerance || pair.Distance >= maxGapDistance)
                        continue;

                    XYZ joinPoint = (pair.Point1 + pair.Point2) / 2;
                    result[i] = ReplaceLineEnd(result[i], pair.EndIndex1, joinPoint);
                    result[j] = ReplaceLineEnd(result[j], pair.EndIndex2, joinPoint);
                }
            }

            return result.Where(line => line.Length > GeometryTolerance).ToList();
        }

        private bool AreLinesCollinearInXY(Line line1, Line line2)
        {
            if (!AreParallelInXY(line1.Direction, line2.Direction))
                return false;

            XYZ direction = GetHorizontalDirection(line1);
            XYZ perpendicular = new XYZ(-direction.Y, direction.X, 0).Normalize();
            double offset = Math.Abs((GetLineMidPoint(line2) - GetLineMidPoint(line1)).DotProduct(perpendicular));
            double zOffset = Math.Abs(GetLineAverageZ(line2) - GetLineAverageZ(line1));

            return offset < 1.0 / 304.8 && zOffset < 1.0 / 304.8;
        }

        private ClosestLineEndPair GetClosestLineEndPair(Line line1, Line line2)
        {
            ClosestLineEndPair closest = new ClosestLineEndPair
            {
                Distance = double.MaxValue
            };

            for (int endIndex1 = 0; endIndex1 < 2; endIndex1++)
            {
                XYZ point1 = line1.GetEndPoint(endIndex1);
                for (int endIndex2 = 0; endIndex2 < 2; endIndex2++)
                {
                    XYZ point2 = line2.GetEndPoint(endIndex2);
                    double distance = point1.DistanceTo(point2);
                    if (distance >= closest.Distance)
                        continue;

                    closest.EndIndex1 = endIndex1;
                    closest.EndIndex2 = endIndex2;
                    closest.Point1 = point1;
                    closest.Point2 = point2;
                    closest.Distance = distance;
                }
            }

            return closest;
        }

        private Line ReplaceLineEnd(Line line, int endIndex, XYZ point)
        {
            return endIndex == 0
                ? Line.CreateBound(point, line.GetEndPoint(1))
                : Line.CreateBound(line.GetEndPoint(0), point);
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

            corners = new List<XYZ>();
            Dictionary<long, RebarBuildGroup> groups = new Dictionary<long, RebarBuildGroup>();
            Dictionary<long, List<ExistingRebarLineGroup>> existingRebarGroupsByHost = new Dictionary<long, List<ExistingRebarLineGroup>>();

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
                    if (!TryFindFloorContextForLine(doc, rawLine, hasStoredData ? storedData : null, out context, out centerLine))
                        continue;

                    BoundaryDistances distances = CalculateBoundaryDistances(centerLine, context.Profile);
                    if (!AreBoundaryDistancesValid(distances) && hasStoredData && AreBoundaryDistancesValid(storedData))
                        distances = new BoundaryDistances(storedData.LeftBoundaryDistance, storedData.RightBoundaryDistance);

                    if (!AreBoundaryDistancesValid(distances))
                        continue;

                    GrillageLineData lineData = hasStoredData
                        ? storedData
                        : CreateLineDataFromCurrentSettings(context, distances);

                    lineData.HostElementId = context.Floor.Id.Value;
                    lineData.LeftBoundaryDistance = distances.Left;
                    lineData.RightBoundaryDistance = distances.Right;

                    long hostKey = context.Floor.Id.Value;
                    if (!existingRebarGroupsByHost.ContainsKey(hostKey))
                        existingRebarGroupsByHost[hostKey] = CollectExistingLongitudinalRebarGroups(doc, context.Floor);

                    CenterLineRebarResult result = CreateRebarForCenterLine(doc, rebarTypes, context.Floor, centerLine, context.Thickness, lineData);
                    if (result == null)
                        continue;

                    if (!groups.ContainsKey(hostKey))
                    {
                        groups[hostKey] = new RebarBuildGroup
                        {
                            Host = context.Floor,
                            CornerDiameter = lineData.CornerDiameter
                        };
                    }

                    groups[hostKey].Top.Add(result.CenterLine, result.TopLines);
                    groups[hostKey].Bottom.Add(result.CenterLine, result.BottomLines);
                    groups[hostKey].HalfWidths.Add(Math.Max(distances.Left, distances.Right));

                    ApplyRebarCover(doc, rearCoverTypes, context.Floor, lineData);
                    modLength = Math.Max(distances.Left, distances.Right);

                    RebarBarType cornerType = rebarTypes.Where(x => x.Name == lineData.CornerDiameter).FirstOrDefault() as RebarBarType;
                    CreateCornerRebarsWithExistingRebars(doc, result, existingRebarGroupsByHost[hostKey], cornerType, context.Floor);
                }

                foreach (RebarBuildGroup group in groups.Values)
                {
                    if (group.Top.Count < 2)
                        continue;

                    modLength = CalculateModeDistance(group.HalfWidths);
                    RebarBarType cornerType = rebarTypes.Where(x => x.Name == group.CornerDiameter).FirstOrDefault() as RebarBarType;
                    CreateCornerRebarsAtIntersections(doc, group.Top, group.Bottom, cornerType, group.Host);
                }

                tg.Assimilate();
            }
        }

        private CenterLineRebarResult CreateRebarForCenterLine(Document doc, List<Element> rebarTypes, Floor host, Line centerLine, double thickness, GrillageLineData data)
        {
            if (data.HorizontalCount < 2)
                return null;

            double rightRebarHalfWidth = data.RightBoundaryDistance - data.LeftRightOffset / 304.8;
            double leftRebarHalfWidth = data.LeftBoundaryDistance - data.LeftRightOffset / 304.8;
            if (rightRebarHalfWidth <= GeometryTolerance || leftRebarHalfWidth <= GeometryTolerance)
                return null;

            XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();
            XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0).Normalize();

            XYZ offsetBottomRight = perpendicularDirection * rightRebarHalfWidth + data.BottomOffset / 304.8 * XYZ.BasisZ;
            XYZ offsetBottomLeft = perpendicularDirection * -leftRebarHalfWidth + data.BottomOffset / 304.8 * XYZ.BasisZ;
            XYZ offsetTopRight = perpendicularDirection * rightRebarHalfWidth + (thickness - data.TopOffset / 304.8) * XYZ.BasisZ;
            XYZ offsetTopLeft = perpendicularDirection * -leftRebarHalfWidth + (thickness - data.TopOffset / 304.8) * XYZ.BasisZ;

            Line lineBR = Line.CreateBound(centerLine.GetEndPoint(0) + offsetBottomRight, centerLine.GetEndPoint(1) + offsetBottomRight);
            Line lineBL = Line.CreateBound(centerLine.GetEndPoint(0) + offsetBottomLeft, centerLine.GetEndPoint(1) + offsetBottomLeft);
            Line lineTR = Line.CreateBound(centerLine.GetEndPoint(0) + offsetTopRight, centerLine.GetEndPoint(1) + offsetTopRight);
            Line lineTL = Line.CreateBound(centerLine.GetEndPoint(0) + offsetTopLeft, centerLine.GetEndPoint(1) + offsetTopLeft);

            List<Line> intermediateLinesTop = new List<Line>();
            List<Line> intermediateLinesBottom = new List<Line>();
            double distanceBetweenLines = lineBR.GetEndPoint(0).DistanceTo(lineBL.GetEndPoint(0));
            double step = distanceBetweenLines / (data.HorizontalCount - 1);

            intermediateLinesTop.Add(lineTL);
            intermediateLinesBottom.Add(lineBL);

            for (int i = 1; i <= data.HorizontalCount - 2; i++)
            {
                XYZ offset = perpendicularDirection * (step * i);
                intermediateLinesBottom.Add(Line.CreateBound(lineBL.GetEndPoint(0) + offset, lineBL.GetEndPoint(1) + offset));
                intermediateLinesTop.Add(Line.CreateBound(lineTL.GetEndPoint(0) + offset, lineTL.GetEndPoint(1) + offset));
            }

            intermediateLinesTop.Add(lineTR);
            intermediateLinesBottom.Add(lineBR);

            RebarBarType typeTop = rebarTypes.Where(x => x.Name == data.TopDiameter).FirstOrDefault() as RebarBarType;
            RebarBarType typeBot = rebarTypes.Where(x => x.Name == data.BottomDiameter).FirstOrDefault() as RebarBarType;
            RebarBarType typeVertical = rebarTypes.Where(x => x.Name == data.VertDiameter).FirstOrDefault() as RebarBarType;
            RebarBarType typeHorizontal = rebarTypes.Where(x => x.Name == data.HorizontDiameter).FirstOrDefault() as RebarBarType;

            if (typeTop == null || typeBot == null || typeHorizontal == null || (!data.IsKnittedMode && typeVertical == null))
                return null;

            List<Element> rebs = CreateRebarFromLines(doc, intermediateLinesBottom, typeTop, RebarStyle.Standard, host, true, data.IsKnittedMode);
            rebs.AddRange(CreateRebarFromLines(doc, intermediateLinesTop, typeBot, RebarStyle.Standard, host, false, data.IsKnittedMode));

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

            Line verticalLineRightStart = Line.CreateBound(lineBR.GetEndPoint(0), lineTR.GetEndPoint(0));
            Line verticalLineLeftStart = Line.CreateBound(lineBL.GetEndPoint(0), lineTL.GetEndPoint(0));
            double verticalStep = data.VerticalStep / 304.8;
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

            for (int i = 1; i <= data.HorizontalCount - 2; i++)
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

            double centerLineLength = centerLine.Length;
            int numberOfLinesTop = (int)(centerLineLength / verticalStep) + 1;
            XYZ direction = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

            double horizontalStep = data.HorizontalStep / 304.8;
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

            if (data.IsKnittedMode)
            {
                CreateKnittedRebarSets(doc, host, direction, intermediateLinesTop, intermediateLinesBottom, typeHorizontal, typeTop, typeBot, numberOfLinesTop, verticalStep);
            }
            else
            {
                CreateRebarSet(doc, verticalLines, typeVertical, RebarStyle.Standard, host, direction, numberOfLinesTop, verticalStep, false, false);
                CreateRebarSet(doc, horizontalLines, typeHorizontal, RebarStyle.Standard, host, direction, numberOfLinesBot, horizontalStep, true, false);
            }

            return new CenterLineRebarResult
            {
                CenterLine = centerLine,
                TopLines = intermediateLinesTop,
                BottomLines = intermediateLinesBottom
            };
        }

        private void CreateKnittedRebarSets(Document doc, Element host, XYZ direction, List<Line> topLines, List<Line> botLines, RebarBarType typeHorizontal, RebarBarType typeTop, RebarBarType typeBot, int numberOfLinesTop, double verticalStep)
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

                XYZ botC = botLines[i].GetEndPoint(0) + (botLines[j].GetEndPoint(0) - botLines[i].GetEndPoint(0)) * 0.5;
                XYZ topC = topLines[i].GetEndPoint(0) + (topLines[j].GetEndPoint(0) - topLines[i].GetEndPoint(0)) * 0.5;
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

        private void ApplyRebarCover(Document doc, List<Element> rearCoverTypes, Floor host, GrillageLineData data)
        {
            using (Transaction tx = new Transaction(doc, "Защитный слой"))
            {
                tx.Start();
                Element coverLeftRight = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (data.LeftRightOffset / 304.8 - 25 / 304.8)).FirstOrDefault();
                if (coverLeftRight != null)
                    host.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER).Set(coverLeftRight.Id);

                Element coverTopBottom = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (Math.Min(data.TopOffset, data.BottomOffset) / 304.8 - 25 / 304.8)).FirstOrDefault();
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

        private bool TryFindFloorContextForLine(Document doc, Line modelLine, GrillageLineData data, out FloorContext context, out Line centerLine)
        {
            context = null;
            centerLine = null;

            if (data != null && data.HostElementId > 0)
            {
                Floor storedFloor = doc.GetElement(new ElementId(data.HostElementId)) as Floor;
                context = CreateFloorContext(doc, storedFloor);
                if (context != null)
                {
                    centerLine = ProjectLineToFloorBottom(modelLine, context);
                    return true;
                }
            }

            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
            {
                FloorContext candidateContext = CreateFloorContext(doc, floor);
                if (candidateContext == null)
                    continue;

                Line candidateLine = ProjectLineToFloorBottom(modelLine, candidateContext);
                BoundaryDistances distances = CalculateBoundaryDistances(candidateLine, candidateContext.Profile);
                XYZ midPoint = (candidateLine.GetEndPoint(0) + candidateLine.GetEndPoint(1)) / 2;
                XYZ direction = (candidateLine.GetEndPoint(1) - candidateLine.GetEndPoint(0)).Normalize();

                if (AreBoundaryDistancesValid(distances) && IsPointInsideBoundary(midPoint, candidateContext.Profile, direction))
                {
                    context = candidateContext;
                    centerLine = candidateLine;
                    return true;
                }
            }

            return false;
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

        private GrillageLineData CreateLineDataFromCurrentSettings(FloorContext context, BoundaryDistances distances)
        {
            return new GrillageLineData
            {
                Version = 1,
                HostElementId = context.Floor.Id.Value,
                LeftBoundaryDistance = distances.Left,
                RightBoundaryDistance = distances.Right,
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

        private string SerializeLineData(GrillageLineData data)
        {
            XElement element = new XElement("GrillageLineData",
                new XAttribute("Version", data.Version),
                new XAttribute("HostElementId", data.HostElementId),
                new XAttribute("LeftBoundaryDistance", FormatDouble(data.LeftBoundaryDistance)),
                new XAttribute("RightBoundaryDistance", FormatDouble(data.RightBoundaryDistance)),
                new XAttribute("TopDiameter", data.TopDiameter ?? ""),
                new XAttribute("BottomDiameter", data.BottomDiameter ?? ""),
                new XAttribute("VertDiameter", data.VertDiameter ?? ""),
                new XAttribute("HorizontDiameter", data.HorizontDiameter ?? ""),
                new XAttribute("CornerDiameter", data.CornerDiameter ?? ""),
                new XAttribute("HorizontalCount", data.HorizontalCount),
                new XAttribute("VerticalStep", data.VerticalStep),
                new XAttribute("HorizontalStep", data.HorizontalStep),
                new XAttribute("LeftRightOffset", data.LeftRightOffset),
                new XAttribute("BottomOffset", data.BottomOffset),
                new XAttribute("TopOffset", data.TopOffset),
                new XAttribute("IsKnittedMode", data.IsKnittedMode));

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
                    Version = ReadInt(element, "Version", 1),
                    HostElementId = ReadLong(element, "HostElementId", 0),
                    LeftBoundaryDistance = ReadDouble(element, "LeftBoundaryDistance", 0),
                    RightBoundaryDistance = ReadDouble(element, "RightBoundaryDistance", 0),
                    TopDiameter = ReadString(element, "TopDiameter"),
                    BottomDiameter = ReadString(element, "BottomDiameter"),
                    VertDiameter = ReadString(element, "VertDiameter"),
                    HorizontDiameter = ReadString(element, "HorizontDiameter"),
                    CornerDiameter = ReadString(element, "CornerDiameter"),
                    HorizontalCount = ReadInt(element, "HorizontalCount", 2),
                    VerticalStep = ReadInt(element, "VerticalStep", 200),
                    HorizontalStep = ReadInt(element, "HorizontalStep", 200),
                    LeftRightOffset = ReadInt(element, "LeftRightOffset", 50),
                    BottomOffset = ReadInt(element, "BottomOffset", 50),
                    TopOffset = ReadInt(element, "TopOffset", 50),
                    IsKnittedMode = ReadBool(element, "IsKnittedMode", false)
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

        private string ReadString(XElement element, string name)
        {
            XAttribute attribute = element.Attribute(name);
            return attribute == null ? null : attribute.Value;
        }

        private int ReadInt(XElement element, string name, int defaultValue)
        {
            XAttribute attribute = element.Attribute(name);
            int value;
            return attribute != null && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
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

        private bool ReadBool(XElement element, string name, bool defaultValue)
        {
            XAttribute attribute = element.Attribute(name);
            bool value;
            return attribute != null && bool.TryParse(attribute.Value, out value)
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
            public int Version { get; set; }
            public long HostElementId { get; set; }
            public double LeftBoundaryDistance { get; set; }
            public double RightBoundaryDistance { get; set; }
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

        private class CenterLineRebarResult
        {
            public Line CenterLine { get; set; }
            public List<Line> TopLines { get; set; }
            public List<Line> BottomLines { get; set; }
        }

        private class RebarBuildGroup
        {
            public RebarBuildGroup()
            {
                Top = new Dictionary<Line, List<Line>>();
                Bottom = new Dictionary<Line, List<Line>>();
                HalfWidths = new List<double>();
            }

            public Floor Host { get; set; }
            public string CornerDiameter { get; set; }
            public Dictionary<Line, List<Line>> Top { get; private set; }
            public Dictionary<Line, List<Line>> Bottom { get; private set; }
            public List<double> HalfWidths { get; private set; }
        }

        private class ClosestLineEndPair
        {
            public int EndIndex1 { get; set; }
            public int EndIndex2 { get; set; }
            public XYZ Point1 { get; set; }
            public XYZ Point2 { get; set; }
            public double Distance { get; set; }
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
