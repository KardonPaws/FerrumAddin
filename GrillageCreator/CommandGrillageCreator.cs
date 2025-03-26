﻿using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI.Selection;
using System;
using System.Net;
using FerrumAddin.GrillageCreator;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows;
using Autodesk.Revit.DB.Structure;
using System.Security.Cryptography;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Rebar = Autodesk.Revit.DB.Structure.Rebar;

namespace FerrumAddin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandGrillageCreator : IExternalCommand
    {
        public static ExternalEvent createGrillage;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            List<Element> rebarTypes = new FilteredElementCollector(commandData.Application.ActiveUIDocument.Document).OfClass(typeof(RebarBarType)).Where(x => x.Name.StartsWith("к")).ToList();

            createGrillage = ExternalEvent.Create(new CreateGrillage());
            WindowGrillageCreator window = new WindowGrillageCreator(rebarTypes);
            window.Show();
            
            return Result.Succeeded;
        }
    }
    public class CreateGrillage : IExternalEventHandler
    {
        public string message = "";
        public void Execute(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;
            List<Element> rebarTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Where(x => x.Name.StartsWith("к")).ToList();

            // Получаем выбранный элемент (перекрытие)
            List<Reference> elements = (List<Reference>)uiDoc.Selection.PickObjects(ObjectType.Element);

            if (elements == null)
            {
                message = "Элементы не выбраны.";
                return;
                
            }
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
                    TaskDialog.Show("Ошибка", "Не удалось получить параметр 'Толщина'.");
                    return;
                }
                double thickness = thicknessParam.AsDouble();

                foreach (CurveArray array in profile)
                {
                    // Собираем все кривые из профиля
                    List<Line> allCurves = new List<Line>();
                    foreach (Line curveLoop in array)
                    {
                        Line l1 = Line.CreateBound(curveLoop.GetEndPoint(0) - XYZ.BasisZ * thickness + XYZ.BasisZ * element.LookupParameter("Смещение от уровня").AsDouble(), 
                            curveLoop.GetEndPoint(1) - XYZ.BasisZ * thickness + XYZ.BasisZ * element.LookupParameter("Смещение от уровня").AsDouble());
                        allCurves.Add(l1);
                    }

                    // Вычисляем средние линии для боковых граней
                    List<Line> centerLines = ComputeCenterLines(allCurves);
                    centerLines = ExtendLinesToConnect(centerLines, modLength);
                    centerLines = ExtendCenterLines(centerLines, modLength);

                    Dictionary<Line, List<Line>> dictTop = new Dictionary<Line, List<Line>>();
                    Dictionary<Line, List<Line>> dictBottom = new Dictionary<Line, List<Line>>();


                    foreach (Line centerLine in centerLines)
                    {
                        XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

                        // Перпендикулярное направление
                        XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0);

                        // Вычисляем смещения
                        XYZ offsetBottomRight = perpendicularDirection * (modLength - WindowGrillageCreator.leftRightOffset / 304.8) + WindowGrillageCreator.topBottomOffset / 304.8 * XYZ.BasisZ;
                        XYZ offsetBottomLeft = perpendicularDirection * (-modLength + WindowGrillageCreator.leftRightOffset / 304.8) + WindowGrillageCreator.topBottomOffset / 304.8 * XYZ.BasisZ;
                        XYZ offsetTopRight = perpendicularDirection * (modLength - WindowGrillageCreator.leftRightOffset / 304.8) + (thickness - WindowGrillageCreator.topBottomOffset / 304.8) * XYZ.BasisZ;
                        XYZ offsetTopLeft = perpendicularDirection * (-modLength + WindowGrillageCreator.leftRightOffset / 304.8) + (thickness - WindowGrillageCreator.topBottomOffset / 304.8) * XYZ.BasisZ;

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
                        double step = distanceBetweenLines / (WindowGrillageCreator.horizontalCount + 1);

                        for (int i = 1; i <= WindowGrillageCreator.horizontalCount; i++)
                        {
                            XYZ offset_ = perpendicularDirection * (step * i);
                            Line intermediateLine = Line.CreateBound(lineBL.GetEndPoint(0) + offset_, lineBL.GetEndPoint(1) + offset_);
                            intermediateLinesBottom.Add(intermediateLine);
                            intermediateLine = Line.CreateBound(lineTL.GetEndPoint(0) + offset_, lineTL.GetEndPoint(1) + offset_);
                            intermediateLinesTop.Add(intermediateLine);
                        }

                        intermediateLinesTop.AddRange(new List<Line>() { lineTL, lineTR });
                        dictTop.Add(centerLine, intermediateLinesTop);
                        intermediateLinesBottom.AddRange(new List<Line>() { lineBL, lineBR });
                        dictTop.Add(centerLine, intermediateLinesBottom);

                        RebarBarType type = rebarTypes.Where(x => x.Name == WindowGrillageCreator.bottomDiameter).FirstOrDefault() as RebarBarType;
                        CreateRebarFromLines(doc, intermediateLinesBottom, type, RebarStyle.Standard, element, true);

                        type = rebarTypes.Where(x => x.Name == WindowGrillageCreator.topDiameter).FirstOrDefault() as RebarBarType;
                        CreateRebarFromLines(doc, intermediateLinesTop, type, RebarStyle.Standard, element, false);

                        Line verticalLineRightStart = Line.CreateBound(lineBR.GetEndPoint(0), lineTR.GetEndPoint(0));
                        Line verticalLineLeftStart = Line.CreateBound(lineBL.GetEndPoint(0), lineTL.GetEndPoint(0));

                        double verticalCount = WindowGrillageCreator.verticalCount / 304.8;

                        List<Line> verticalLines = new List<Line>();
                        verticalLines.Add(verticalLineRightStart);

                        // Начальная и конечная точки для вертикальных линий
                        XYZ startPoint1 = verticalLineRightStart.GetEndPoint(0); // Начальная точка первой линии
                        XYZ endPoint1 = verticalLineRightStart.GetEndPoint(1);   // Конечная точка первой линии
                        XYZ startPoint2 = verticalLineLeftStart.GetEndPoint(0); // Начальная точка второй линии
                        XYZ endPoint2 = verticalLineLeftStart.GetEndPoint(1);   // Конечная точка второй линии

                        // Направление для вертикальных линий 
                        XYZ verticalDirection = (startPoint2 - startPoint1).Normalize();

                        for (int i = 1; i <= WindowGrillageCreator.horizontalCount; i++)
                        {
                            // Вычисляем смещение для текущей линии
                            XYZ offset_ = verticalDirection * (step * i);

                            // Начальная и конечная точки для текущей вертикальной линии
                            XYZ currentStart = startPoint1 + offset_;
                            XYZ currentEnd = endPoint1 + offset_;

                            // Создаем линию и добавляем ее в список
                            Line currentLine = Line.CreateBound(currentStart, currentEnd);
                            verticalLines.Add(currentLine);
                        }
                        verticalLines.Add(verticalLineLeftStart);

                        // Длина центральной линии (centerLine)
                        double centerLineLength = centerLine.Length;

                        // Количество линий, которые нужно создать
                        int numberOfLinesTop = (int)(centerLineLength / verticalCount) + 1;

                        // Направление для создания линий
                        XYZ direction = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

                        type = rebarTypes.Where(x => x.Name == WindowGrillageCreator.vertDiameter).FirstOrDefault() as RebarBarType;
                        CreateRebarSet(doc, verticalLines, type, RebarStyle.Standard, element, direction, numberOfLinesTop, verticalCount);


                        List<Line> horizontalLines = new List<Line>();
                        // Количество линий, которые нужно создать
                        int numberOfLinesBot = (int)(centerLineLength / (WindowGrillageCreator.horizontCount / 304.8)) + 1;

                        // Направление для создания линий
                        direction = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

                        // Вычисляем смещение для текущей линии
                        double offsetDistance = WindowGrillageCreator.horizontCount / 304.8; // Шаг в футах
                        XYZ offset = direction * offsetDistance * 0;

                        // Линия между verticalLineRightStart(0) и verticalLineLeftStart(0)
                        XYZ start3 = verticalLineRightStart.GetEndPoint(0) + offset;
                        XYZ end3 = verticalLineLeftStart.GetEndPoint(0) + offset;
                        Line line3 = Line.CreateBound(start3, end3);
                        horizontalLines.Add(line3);                   

                        // Линия между verticalLineRightStart(1) и verticalLineLeftStart(1)
                        XYZ start4 = verticalLineRightStart.GetEndPoint(1) + offset;
                        XYZ end4 = verticalLineLeftStart.GetEndPoint(1) + offset;
                        Line line4 = Line.CreateBound(start4, end4);
                        horizontalLines.Add(line4);

                        type = rebarTypes.Where(x => x.Name == WindowGrillageCreator.horizontDiameter).FirstOrDefault() as RebarBarType;
                        CreateRebarSet(doc, horizontalLines, type, RebarStyle.Standard, element, direction, numberOfLinesBot, WindowGrillageCreator.horizontCount / 304.8);
                    }
                    
                }
            }
            
        }
        public string GetName()
        {
            return "xxx";
        }

        private void CreateRebarFromLines(Document doc, List<Line> lines, RebarBarType barType, RebarStyle style, Element host, bool bottom)
        {
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Создание арматуры");
                double extensionLength = 15.0 / 304.8; // Расширение в футах

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

                    if (bottom)
                    {
                        rebar.LookupParameter("ADSK_Главная деталь изделия").Set(1);
                    }
                }
                tx.Commit();
            }
        }

        private void CreateRebarSet(Document doc, List<Line> lines, RebarBarType barType, RebarStyle style, Element host, XYZ dir, int count, double step)
        {
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Создание арматуры");
                double extensionLength = 15.0 / 304.8; // Расширение в футах

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

                    if (rebarSet != null)
                    {
                        rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_LAYOUT_RULE).Set(3);
                        rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING).Set(step);
                        rebarSet.get_Parameter(BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS).Set(count);
                        rebarSet.GetShapeDrivenAccessor().BarsOnNormalSide = true;
                    }
                }
                tx.Commit();
            }
        }

        private List<Line> ExtendCenterLines(List<Line> centerLines, double modLength)
        {
            List<Line> extendedLines = new List<Line>();
            double extensionValue = modLength - (25 / 304.8); // Расстояние для продления
            double reductionValue = 25 / 304.8; // Расстояние для сокращения

            foreach (Line centerLine in centerLines)
            {
                XYZ startPoint = centerLine.GetEndPoint(0);
                XYZ endPoint = centerLine.GetEndPoint(1);
                XYZ direction = (endPoint - startPoint).Normalize();

                // Проверяем пересечения для начальной точки
                bool hasStartIntersection = HasIntersectionAtPoint(startPoint, centerLines, centerLine);

                // Проверяем пересечения для конечной точки
                bool hasEndIntersection = HasIntersectionAtPoint(endPoint, centerLines, centerLine);

                // Модифицируем точки в зависимости от наличия пересечений
                XYZ newStartPoint = hasStartIntersection
                    ? startPoint - direction * extensionValue
                    : startPoint + direction * reductionValue;

                XYZ newEndPoint = hasEndIntersection
                    ? endPoint + direction * extensionValue
                    : endPoint - direction * reductionValue;

                // Создаем новую линию с модифицированными точками
                Line extendedLine = Line.CreateBound(newStartPoint, newEndPoint);
                extendedLines.Add(extendedLine);
            }

            return extendedLines;
        }

        private bool HasIntersectionAtPoint(XYZ point, List<Line> lines, Line currentLine)
        {
            double tolerance = 1e-6; // Допустимая погрешность

            foreach (Line line in lines)
            {
                if (line == currentLine) continue; // Пропускаем текущую линию

                // Проверяем, совпадает ли точка с началом или концом другой линии
                if (point.IsAlmostEqualTo(line.GetEndPoint(0), tolerance) ||
                    point.IsAlmostEqualTo(line.GetEndPoint(1), tolerance))
                {
                    return true;
                }
            }

            return false;
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
                var sortedByDirection = sortedLines
                    .Where(line => line != null)
                    .OrderBy(line =>
                    {
                        XYZ dir = (line.GetEndPoint(1) - line.GetEndPoint(0)).Normalize();
                        if (dir.IsAlmostEqualTo(currentDir)) return 0; // То же направление
                        if (dir.IsAlmostEqualTo(-currentDir)) return 1; // Обратное направление
                        return 2; // Другие направления
                    })
                    .ToList();

                // Проходим по отсортированному списку
                for (int j = 0; j < sortedByDirection.Count; j++)
                {
                    Line otherLine = sortedByDirection[j];
                    if (otherLine == null || otherLine == currentLine) continue; // Пропускаем текущую линию и уже обработанные

                    XYZ otherDir = (otherLine.GetEndPoint(1) - otherLine.GetEndPoint(0)).Normalize(); // Направление другой линии

                    // Проверяем расстояние между конечными точками

                    XYZ point00 = currentLine.GetEndPoint(0);
                    XYZ point01 = currentLine.GetEndPoint(1);
                    XYZ point10 = otherLine.GetEndPoint(0);
                    XYZ point11 = otherLine.GetEndPoint(1);

                    double dist1 = point00.DistanceTo(point10);
                    double dist2 = point00.DistanceTo(point11);
                    double dist3 = point01.DistanceTo(point10);
                    double dist4 = point01.DistanceTo(point11);

                    double[] distances = { dist1, dist2, dist3, dist4 };
                    double distance = distances.Min();

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
                        
                        int sortedByDir1 = sortedByDirection.IndexOf(currentLine);
                        int sortedByDir2 = sortedByDirection.IndexOf(otherLine);
                        
                        sortedLines[i] = Line.CreateBound(startCurrent, midPoint);
                        sortedLines[sortedLines.IndexOf(otherLine)] = Line.CreateBound(midPoint, endOther);
                        sortedByDirection[sortedByDir1] = sortedLines[i];
                        sortedByDirection[sortedByDir2] = Line.CreateBound(midPoint, endOther);

                        break;
                    }

                    // Если направление разное, и расстояние равно sqrt(2) * modLength
                    if (!otherDir.IsAlmostEqualTo(currentDir) && !otherDir.IsAlmostEqualTo(-currentDir) &&
                        Math.Abs(distance - Math.Sqrt(2) * modLength) < 1e-6)
                    {
                        // Дотягиваем каждую линию на modLength
                        XYZ extensionVector1 = (closestPointCurrent - startCurrent).Normalize() * modLength;
                        XYZ extensionVector2 = (closestPointOther - endOther).Normalize() * modLength;

                        int sortedByDir1 = sortedByDirection.IndexOf(currentLine);
                        int sortedByDir2 = sortedByDirection.IndexOf(otherLine);

                        sortedLines[i] = Line.CreateBound(startCurrent, closestPointCurrent + extensionVector1);
                        sortedLines[sortedLines.IndexOf(otherLine)] = Line.CreateBound(closestPointOther + extensionVector2, endOther);
                        sortedByDirection[sortedByDir1] = sortedLines[i];
                        sortedByDirection[sortedByDir2] = Line.CreateBound(closestPointOther + extensionVector2, endOther);

                        break;
                    }

                    // Если направление разное, и расстояние равно modLength
                    if (!otherDir.IsAlmostEqualTo(currentDir) && !otherDir.IsAlmostEqualTo(-currentDir) &&
                        Math.Abs(distance - modLength) < 1e-6)
                    {
                        if (sortedLines.Any(x => x != currentLine && (x.GetEndPoint(0).IsAlmostEqualTo(closestPointCurrent) || x.GetEndPoint(1).IsAlmostEqualTo(closestPointCurrent))))
                        {
                            int sortedByDir2 = sortedByDirection.IndexOf(otherLine);

                            // Дотягиваем линии, чтобы конечные точки совпали
                            sortedLines[sortedLines.IndexOf(otherLine)] = Line.CreateBound(closestPointCurrent, endOther);
                            sortedByDirection[sortedByDir2] = Line.CreateBound(closestPointCurrent, endOther);
                        }
                        else
                        {
                            int sortedByDir1 = sortedByDirection.IndexOf(currentLine);

                            // Дотягиваем линии, чтобы конечные точки совпали
                            sortedLines[sortedLines.IndexOf(currentLine)] = Line.CreateBound(startCurrent, closestPointOther);
                            sortedByDirection[sortedByDir1] = Line.CreateBound(startCurrent, closestPointOther);
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
                TaskDialog.Show("Ошибка", "Не удалось получить уровень для создания линий.");
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
                                if (IsLineInsideBoundary(centerLine, sideCurves))
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
            return IsPointInsideBoundary(startPoint, profile, dir) && IsPointInsideBoundary(endPoint, profile, -dir);
        }

        private bool IsPointInsideBoundary(XYZ point, List<Line> profile, XYZ dir)
        {
            // Проводим луч по оси X и считаем пересечения
            int intersectionCount = 0;

            XYZ rayEnd = new XYZ(point.X + 1000, point.Y, point.Z); // Луч вправо по оси X
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
                // Ищем точки с равными Y
                points = points.OrderBy(x => x.X).ToList();
                XYZ midStart = new XYZ(points[1].X, (start1.Y + start2.Y) / 2, start1.Z);
                XYZ midEnd = new XYZ(points[2].X, (end1.Y + end2.Y) / 2, start1.Z);

                return Line.CreateBound(midStart, midEnd);
            }
            // Проверяем, направлены ли линии вдоль оси Y (X-координаты равны)
            else if (Math.Abs(dir1.X) < 1e-6 && Math.Abs(dir2.X) < 1e-6)
            {
                // Линии направлены вдоль оси Y
                // Ищем точки с равными X
                points = points.OrderBy(x => x.Y).ToList();             
                    XYZ midStart = new XYZ((start1.X + start2.X) / 2, points[1].Y, start1.Z);
                XYZ midEnd = new XYZ((end1.X + end2.X) / 2, points[2].Y, start1.Z);
                

                return Line.CreateBound(midStart, midEnd);
            }
            else
            {
                // Если линии не направлены строго по осям, используем общий подход
                XYZ midStart = (start1 + start2) / 2;
                XYZ midEnd = (end1 + end2) / 2;

                return Line.CreateBound(midStart, midEnd);
            }
        }


    }
}