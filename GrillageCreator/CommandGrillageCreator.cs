using Autodesk.Revit.DB;
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

namespace FerrumAddin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandGrillageCreator : IExternalCommand
    {
        public static ExternalEvent createGrillage;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            createGrillage = ExternalEvent.Create(new CreateGrillage());
            WindowGrillageCreator window = new WindowGrillageCreator();
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
                        allCurves.Add(curveLoop);
                    }

                    // Вычисляем средние линии для боковых граней
                    List<Line> centerLines = ComputeCenterLines(allCurves);
                    centerLines = ExtendLinesToConnect(centerLines, modLength);

                    foreach (Line centerLine in centerLines)
                    {
                        XYZ lineDirection = (centerLine.GetEndPoint(1) - centerLine.GetEndPoint(0)).Normalize();

                        // Перпендикулярное направление
                        XYZ perpendicularDirection = new XYZ(-lineDirection.Y, lineDirection.X, 0);

                        // Вычисляем смещения
                        XYZ offsetTop = perpendicularDirection * (modLength / 2) + WindowGrillageCreator.topBottomOffset * XYZ.BasisZ;
                        XYZ offsetBottom = perpendicularDirection * (-modLength / 2) + WindowGrillageCreator.topBottomOffset * XYZ.BasisZ;
                        XYZ offsetThicknessTop = perpendicularDirection * (modLength / 2) + (thickness - WindowGrillageCreator.topBottomOffset) * XYZ.BasisZ;
                        XYZ offsetThicknessBottom = perpendicularDirection * (-modLength / 2) + (thickness - WindowGrillageCreator.topBottomOffset) * XYZ.BasisZ;

                        // Создаем 4 линии
                        Line line1 = Line.CreateBound(centerLine.GetEndPoint(0) + offsetTop, centerLine.GetEndPoint(1) + offsetTop);
                        Line line2 = Line.CreateBound(centerLine.GetEndPoint(0) + offsetBottom, centerLine.GetEndPoint(1) + offsetBottom);
                        Line line3 = Line.CreateBound(centerLine.GetEndPoint(0) + offsetThicknessTop, centerLine.GetEndPoint(1) + offsetThicknessTop);
                        Line line4 = Line.CreateBound(centerLine.GetEndPoint(0) + offsetThicknessBottom, centerLine.GetEndPoint(1) + offsetThicknessBottom);

                        List<Line> intermediateLines = new List<Line>();

                        // Расстояние между линиями
                        double distanceBetweenLines = line1.GetEndPoint(0).DistanceTo(line2.GetEndPoint(0));

                        if (WindowGrillageCreator.isNumber)
                        {
                            // Делим расстояние на равные участки
                            double step = distanceBetweenLines / (WindowGrillageCreator.horizontalCount + 1);

                            for (int i = 1; i <= WindowGrillageCreator.horizontalCount; i++)
                            {
                                XYZ offset = perpendicularDirection * (step * i);
                                Line intermediateLine = Line.CreateBound(line1.GetEndPoint(0) + offset, line1.GetEndPoint(1) + offset);
                                intermediateLines.Add(intermediateLine);
                                intermediateLine = Line.CreateBound(line3.GetEndPoint(0) + offset, line4.GetEndPoint(1) + offset);
                                intermediateLines.Add(intermediateLine);
                            }
                        }
                        else
                        {
                            // Создаем линии с шагом horizontalCount
                            double step = WindowGrillageCreator.horizontalCount;

                            int maxLines = (int)(distanceBetweenLines / step) - 1;
                            if (maxLines > 0)
                            {
                                for (int i = 1; i <= maxLines; i++)
                                {
                                    XYZ offset = perpendicularDirection * (step * i);
                                    Line intermediateLine = Line.CreateBound(line1.GetEndPoint(0) + offset, line1.GetEndPoint(1) + offset);
                                    intermediateLines.Add(intermediateLine);
                                    intermediateLine = Line.CreateBound(line3.GetEndPoint(0) + offset, line4.GetEndPoint(1) + offset);
                                    intermediateLines.Add(intermediateLine);
                                }
                            }
                        }


                        Line verticalLine1 = Line.CreateBound(line1.GetEndPoint(0), line2.GetEndPoint(0));
                        Line verticalLine2 = Line.CreateBound(line3.GetEndPoint(0), line4.GetEndPoint(0));

                        int verticalCount = WindowGrillageCreator.verticalCount;

                        List<Line> verticalLines = new List<Line>();

                        // Начальная и конечная точки для вертикальных линий
                        XYZ startPoint1 = verticalLine1.GetEndPoint(0); // Начальная точка первой линии
                        XYZ endPoint1 = verticalLine1.GetEndPoint(1);   // Конечная точка первой линии
                        XYZ startPoint2 = verticalLine2.GetEndPoint(0); // Начальная точка второй линии
                        XYZ endPoint2 = verticalLine2.GetEndPoint(1);   // Конечная точка второй линии

                        // Направление для вертикальных линий (например, по оси Z)
                        XYZ verticalDirection = (startPoint2 - startPoint1).Normalize();

                        // Расстояние между вертикальными линиями
                        double totalDistance = startPoint2.DistanceTo(startPoint1); // Общее расстояние между линиями

                        for (int i = 1; i <= verticalCount; i++)
                        {
                            double step = totalDistance / (verticalCount + 1);         // Шаг между линиями
                            // Вычисляем смещение для текущей линии
                            XYZ offset = verticalDirection * (step * i);

                            // Начальная и конечная точки для текущей вертикальной линии
                            XYZ currentStart = startPoint1 + offset;
                            XYZ currentEnd = endPoint1 + offset;

                            // Создаем линию и добавляем ее в список
                            Line currentLine = Line.CreateBound(currentStart, currentEnd);
                            verticalLines.Add(currentLine);
                        }

                        verticalLines.AddRange(intermediateLines);
                        verticalLines.Add(line1);
                        verticalLines.Add(line2);
                        verticalLines.Add(line3);
                        verticalLines.Add(line4);

                        CreateModelLines(doc, verticalLines);
                    }
                    
                }

                //CreateModelLines(doc, centerLines);
            }
            
        }
        public string GetName()
        {
            return "xxx";
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


                    // Если направление одинаковое или обратное, и расстояние равно modLength
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
                        plane = Plane.CreateByThreePoints(line.GetEndPoint(0), line.GetEndPoint(1), line.GetEndPoint(0) + 1*XYZ.BasisX);
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