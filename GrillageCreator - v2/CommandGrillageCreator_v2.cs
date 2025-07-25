﻿using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI.Selection;
using System;
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

namespace FerrumAddinDev.GrillageCreator_v2
{
    [Transaction(TransactionMode.Manual)]
    public class CommandGrillageCreator_v2 : IExternalCommand
    {
        public static ExternalEvent createGrillage;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            List<Element> rebarTypes = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x => x.Name.Contains("к_")).ToList();
            List<Element> rebarTypesCorner = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x=>x.Name.Contains("д_")).ToList();
            List<Element> rebarTypesHorizontal = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x=>!x.Name.Contains("_")).ToList();
            List<Element> rebarTypesKnitted = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).WhereElementIsElementType().Where(x => !x.Name.Contains("_") || x.Name.StartsWith("мп_")).ToList();

            createGrillage = ExternalEvent.Create(new CreateGrillage_v2());
            WindowGrillageCreator_v2 window = new WindowGrillageCreator_v2(rebarTypes, rebarTypesHorizontal, rebarTypesCorner, rebarTypesKnitted);
            window.Show();
            
            return Result.Succeeded;
        }
    }
    public class CreateGrillage_v2 : IExternalEventHandler
    {
        public string message = "";
        public static Document d;
        public void Execute(UIApplication uiApp)
        {
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
                    centerLines = ExtendLinesToConnect(centerLines, modLength);
                    //CreateModelLines(doc, centerLines);
                    centerLines = ExtendCenterLines(centerLines, modLength);

                    Dictionary<Line, List<Line>> dictTop = new Dictionary<Line, List<Line>>();
                    Dictionary<Line, List<Line>> dictBottom = new Dictionary<Line, List<Line>>();


                    foreach (Line centerLine in centerLines)
                    {
                        XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

                        // Перпендикулярное направление
                        XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0);

                        // Вычисляем смещения 24.07.25 - отдельное смещение сверху
                        XYZ offsetBottomRight = perpendicularDirection * (modLength - WindowGrillageCreator_v2.leftRightOffset / 304.8) + WindowGrillageCreator_v2.bottomOffset / 304.8 * XYZ.BasisZ;
                        XYZ offsetBottomLeft = perpendicularDirection * (-modLength + WindowGrillageCreator_v2.leftRightOffset / 304.8) + WindowGrillageCreator_v2.bottomOffset / 304.8 * XYZ.BasisZ;
                        XYZ offsetTopRight = perpendicularDirection * (modLength - WindowGrillageCreator_v2.leftRightOffset / 304.8) + (thickness - WindowGrillageCreator_v2.topOffset / 304.8) * XYZ.BasisZ;
                        XYZ offsetTopLeft = perpendicularDirection * (-modLength + WindowGrillageCreator_v2.leftRightOffset / 304.8) + (thickness - WindowGrillageCreator_v2.topOffset / 304.8) * XYZ.BasisZ;

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
                        double step = distanceBetweenLines / (WindowGrillageCreator_v2.horizontalCount - 1);

                        intermediateLinesTop.Add(lineTL);
                        intermediateLinesBottom.Add(lineBL);

                        for (int i = 1; i <= WindowGrillageCreator_v2.horizontalCount - 2; i++)
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

                        RebarBarType typeTop = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v2.topDiameter).FirstOrDefault() as RebarBarType;
                        RebarBarType typeBot = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v2.bottomDiameter).FirstOrDefault() as RebarBarType;

                        List<Element> rebs = CreateRebarFromLines(doc, intermediateLinesBottom, typeTop, RebarStyle.Standard, element, true);
                        rebs.AddRange(CreateRebarFromLines(doc, intermediateLinesTop, typeBot, RebarStyle.Standard, element, false));

                        using (Transaction trans = new Transaction(doc))
                        {
                            trans.Start("Группа");
                            Group group = doc.Create.NewGroup(rebs.Select(x => x.Id).ToList());
                            trans.Commit();
                        }

                        // Вертикальные линии
                        RebarBarType typeVertical = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v2.vertDiameter).FirstOrDefault() as RebarBarType;

                        // Получаем диаметры арматуры в футах
                        double topRadius = typeTop.BarModelDiameter / 2;
                        double bottomRadius = typeBot.BarModelDiameter / 2;
                        double verticalRadius = typeVertical == null? 0 : typeVertical.BarModelDiameter / 2;

                        // Вычисляем смещение от края
                        double offsetFromEdge = Math.Max(topRadius, bottomRadius) + verticalRadius;

                        Line verticalLineRightStart = Line.CreateBound(lineBR.GetEndPoint(0), lineTR.GetEndPoint(0));
                        Line verticalLineLeftStart = Line.CreateBound(lineBL.GetEndPoint(0), lineTL.GetEndPoint(0));

                        double verticalCount = WindowGrillageCreator_v2.verticalCount / 304.8;

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

                        for (int i = 1; i <= WindowGrillageCreator_v2.horizontalCount - 2; i++)
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
                        RebarBarType typeHorizontal = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v2.horizontDiameter).FirstOrDefault() as RebarBarType;

                        List<Line> horizontalLines = new List<Line>();
                        // Количество линий, которые нужно создать
                        int numberOfLinesBot = (int)(centerLineLength / (WindowGrillageCreator_v2.horizontCount / 304.8)) + 1;

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

                        //if (WindowGrillageCreator_v2.isKnittedMode)
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
                        if (WindowGrillageCreator_v2.isKnittedMode)
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
                            CreateRebarSet(doc, horizontalLines, typeHorizontal, RebarStyle.Standard, element, direction, numberOfLinesBot, WindowGrillageCreator_v2.horizontCount / 304.8, true);
                        }

                        RebarBarType type2 = rebarTypes.Where(x => x.Name == WindowGrillageCreator_v2.cornerDiameter).FirstOrDefault() as RebarBarType;
                        CreateCornerRebarsAtIntersections(doc, dictTop, dictBottom, type2, element);
                    }
                    using (Transaction tx = new Transaction(doc))
                    {
                        tx.Start("Защитный слой");
                        Element coverLeftRight = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (WindowGrillageCreator_v2.leftRightOffset / 304.8 - 25 / 304.8)).FirstOrDefault();
                        if (coverLeftRight != null)
                            element.get_Parameter(BuiltInParameter.CLEAR_COVER_OTHER).Set(coverLeftRight.Id);
                        //24.07.25 - отдельное смещение сверху
                        Element coverTopBottom = rearCoverTypes.Where(x => (x as RebarCoverType).CoverDistance == (Math.Min(WindowGrillageCreator_v2.topOffset, WindowGrillageCreator_v2.bottomOffset) / 304.8 - 25 / 304.8)).FirstOrDefault();
                        if (coverTopBottom != null)
                        {
                            element.get_Parameter(BuiltInParameter.CLEAR_COVER_TOP).Set(coverTopBottom.Id);
                            element.get_Parameter(BuiltInParameter.CLEAR_COVER_BOTTOM).Set(coverTopBottom.Id);
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

        private void CreateCornerRebarsAtIntersections(Document doc,
    Dictionary<Line, List<Line>> dictTop,
    Dictionary<Line, List<Line>> dictBottom,
    RebarBarType barType,
    Element host)
        {
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
            const double tol = 1e-6; // точность сравнения координат

            for (int i = 0; i < minCount; i++)
            {
                Line line1 = lines1[i];
                Line line2 = lines2[i];

                // точка пересечения линий
                XYZ intersection = GetIntersectionPoint(line1, line2);
                if (intersection == null)
                    continue;

                // отсекаем почти совпадающие точки
                if (corners.Any(c => c.DistanceTo(intersection) < tol))
                    continue;

                // находим все уже добавленные углы, у которых X или Y совпадает
                var aligned = corners
                    .Where(c => (Math.Abs(c.X - intersection.X) < tol
                             || Math.Abs(c.Y - intersection.Y) < tol) && c.Z == intersection.Z);

                // если среди них нет ни одного, или все они на расстоянии > modLength — добавляем новый
                if (!aligned.Any()
                    || aligned.All(c => c.DistanceTo(intersection) > (modLength*2.5)))
                {
                    corners.Add(intersection);

                    XYZ dir1 = GetCorrectDirection(line1, intersection);
                    XYZ dir2 = GetCorrectDirection(line2, intersection);
                    CreateCornerRebar(doc, line1, line2, intersection, dir1, dir2, barType, host);
                }
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
            double legLength = 150/304.8; // 15 см в каждую сторону

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
            bool firstEl = true;
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

                    if (!WindowGrillageCreator_v2.isKnittedMode)
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
            RebarShape shape = (RebarShape)new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).WhereElementIsElementType().Where(x => x.Name == "Х_51").First();
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Создание арматуры");
                double extensionLength = 0.08202; // Расширение в футах
                if (WindowGrillageCreator_v2.isKnittedMode)
                {
                    List<Curve> lines2 = new List<Curve>();
                    foreach (Line l in lines)
                    {
                        lines2.Add(l);
                    }
                    RebarHookType hook = (RebarHookType)new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).WhereElementIsElementType().Where(x=>x.Name == barType.Name).FirstOrDefault();
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
                        //doc.Create.NewModelCurve(extendedLine, SketchPlane.Create(doc, plane));

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
                        if (Math.Abs(an1) < 1e-9 )//|| Line.CreateBound(closestPointCurrent, endOther).Direction.DotProduct(-currentDir) < 1e-9)
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
                        if (GetProjectionLength(line1, line2) != 0)
                        {
                            // Проверяем, что линии не пересекаются
                            if (!DoLinesIntersect(line1, line2))
                            {

                                // Создаем среднюю линию
                                Line centerLine = CreateCenterLine(line1, line2);
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

            // Вычисляем расстояние от середины центральной линии до контура
            List<double> distances = new List<double>();
            foreach (Line centerLine in centerLines)
            {
                double distance = CalculateDistanceToBoundary(centerLine, sideCurves);
                distances.Add(distance);
            }

            // Вычисляем моду расстояний
            modLength = CalculateModeDistance(distances);

            // Фильтруем центральные линии, оставляя только те, расстояние до контура которых равно моде
            List<Line> filteredCenterLines = FilterLinesByDistanceToBoundary(centerLines, sideCurves, modLength);

            return filteredCenterLines;
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

            return distanceGroups.First().Distance;
        }

        private double CalculateDistanceToBoundary(Line centerLine, List<Line> profile)
        {
            // Находим середину центральной линии
            XYZ midPoint = (centerLine.GetEndPoint(0) + centerLine.GetEndPoint(1)) / 2;

            // Направление, перпендикулярное центральной линии
            XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();
            XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0); // Поворот на 90 градусов

            // Ищем пересечение с контуром вверх и вниз
            double distanceUp = FindDistanceToIntersection(midPoint, perpendicularDirection, profile);
            double distanceDown = FindDistanceToIntersection(midPoint, -perpendicularDirection, profile);

            // Возвращаем минимальное расстояние
            return Math.Min(distanceUp, distanceDown);
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
            XYZ dir1 = p2 - p1;
            XYZ dir2 = p4 - p3;

            double p1_ = line1.GetEndPoint(0).DotProduct(dir1);
            double p2_ = line1.GetEndPoint(1).DotProduct(dir1);
            double p3_ = line2.GetEndPoint(0).DotProduct(dir2);
            double p4_ = line2.GetEndPoint(1).DotProduct(dir2);

            // Проверка пересечения
            if (line1.Intersect(line2) == SetComparisonResult.Overlap|| (p1_ - p2_ == 0 || p1_ - p4_ == 0 || p3_ - p1_ == 0 || p3_ - p4_ == 0))
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
            List<XYZ> points = new List<XYZ>() { start1, start2, end1, end2};

            // Проверяем, направлены ли линии вдоль оси X (Y-координаты равны)
            if (Math.Abs(dir1.Y) < 1e-6 && Math.Abs(dir2.Y) < 1e-6)
            {
                // Линии направлены вдоль оси X
                points = points.OrderBy(x => x.X).ToList();
                XYZ midStart = new XYZ(points[1].X, (start1.Y + start2.Y) / 2, start1.Z);
                XYZ midEnd = new XYZ(points[2].X, (end1.Y + end2.Y) / 2, start1.Z);

                return Line.CreateBound(midStart, midEnd);
            }
            // Проверяем, направлены ли линии вдоль оси Y (X-координаты равны)
            else if (Math.Abs(dir1.X) < 1e-6 && Math.Abs(dir2.X) < 1e-6)
            {
                // Линии направлены вдоль оси Y
                points = points.OrderBy(x => x.Y).ToList();             
                    XYZ midStart = new XYZ((start1.X + start2.X) / 2, points[1].Y, start1.Z);
                XYZ midEnd = new XYZ((end1.X + end2.X) / 2, points[2].Y, start1.Z);
                

                return Line.CreateBound(midStart, midEnd);
            }
            else
            {
                // Если линии не направлены строго по осям, используем общий подход
                // 16.07 - доработка под углом
                if (start1.DistanceTo(start2) > start1.DistanceTo(end2))
                {
                    var s = start2;
                    start2 = end2;
                    end2 = s;
                }
                XYZ midStart = (start1 + start2) / 2;
                XYZ midEnd = (end1 + end2) / 2;

                return Line.CreateBound(midStart, midEnd);
            }
        }


    }
}