using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddin.FBS
{
    public class LayoutGenerator
    {
        // Допустимые длины блоков (в мм)
        private static readonly int[] AllowedBlockLengths = new int[] { 900, 1200, 2400 };

        // Параметры для настройки
        private const double DefaultOvershoot = 200.0; // смещение, если на стороне отсутствует сосед
        private const double CornerThreshold = 10.0;  // порог (мм) для определения углового пересечения

        public static List<LayoutVariant> GenerateVariants(List<WallInfo> walls, int generateCount, int keepCount)
        {
            SetupWallConnections(walls);
            List<LayoutVariant> bestVariants = new List<LayoutVariant>();
            int produced = 0;
            Random rand = new Random();
            foreach (LayoutVariant variant in GenerateVariantsStream(walls, rand))
            {
                if (bestVariants.Count < keepCount)
                    bestVariants.Add(variant);
                else
                {
                    LayoutVariant worst = bestVariants.OrderByDescending(v => v.ErrorCount)
                                                       .ThenByDescending(v => v.WarningCount)
                                                       .First();
                    if (variant.ErrorCount < worst.ErrorCount ||
                        (variant.ErrorCount == worst.ErrorCount && variant.WarningCount < worst.WarningCount))
                    {
                        bestVariants.Remove(worst);
                        bestVariants.Add(variant);
                    }
                }
                produced++;
                if (produced >= generateCount)
                    break;
            }
            return bestVariants.OrderBy(v => v.ErrorCount)
                                .ThenBy(v => v.WarningCount)
                                .ToList();
        }

        private static IEnumerable<LayoutVariant> GenerateVariantsStream(List<WallInfo> walls, Random rand)
        {
            while (true)
                yield return GenerateSingleVariant(walls, rand);
        }

        private static LayoutVariant GenerateSingleVariant(List<WallInfo> walls, Random rand)
        {
            LayoutVariant variant = new LayoutVariant();
            int maxBaseRows = walls.Max(w => (int)Math.Round(w.Height / 600.0));

            for (int row = 1; row <= maxBaseRows; row++)
            {
                foreach (WallInfo wall in walls)
                {
                    int localRow = row;

                    // Базовые границы – физические границы стены
                    double baseLeft = 0;
                    double baseRight = wall.Length;
                    double overshootLeft = (wall.LeftNeighbor == null) ? DefaultOvershoot : 0;
                    double overshootRight = (wall.RightNeighbor == null) ? DefaultOvershoot : 0;

                    // Вычисляем смещения для левой и правой сторон, если сосед есть
                    double deltaLeft = ComputeLeftBoundary(wall, localRow, rand);
                    double deltaRight = ComputeRightBoundary(wall, localRow, rand);

                    double leftBound = baseLeft + deltaLeft;
                    double rightBound = baseRight + deltaRight;

                    // Если итоговый интервал невозможен, пропускаем стену
                    if (leftBound >= rightBound)
                        continue;

                    // Обрабатываем проёмы из окон/дверей и longitudinal-соединений
                    List<(double start, double end)> openings = new List<(double, double)>();
                    foreach (var op in wall.Openings)
                    {
                        if (op.End > leftBound && op.Start < rightBound)
                        {
                            double opStart = Math.Max(op.Start, leftBound);
                            double opEnd = Math.Min(op.End, rightBound);
                            openings.Add((opStart, opEnd));
                        }
                    }
                    // Добавляем longitudinal проёмы для соединённых стен (если есть соединения и ряд первый)
                    if (wall.ConnectedWalls.Count > 0)
                    {
                        foreach (var neighbor in wall.ConnectedWalls)
                        {
                            bool isAngular = false;
                            if (wall.LeftNeighbor == neighbor)
                            {
                                double d1 = wall.StartPoint.DistanceTo(neighbor.StartPoint) * 304.8;
                                double d2 = wall.StartPoint.DistanceTo(neighbor.EndPoint) * 304.8;
                                isAngular = (d1 < LayoutGenerator.CornerThreshold || d2 < LayoutGenerator.CornerThreshold);
                            }
                            else if (wall.RightNeighbor == neighbor)
                            {
                                double d1 = wall.EndPoint.DistanceTo(neighbor.StartPoint) * 304.8;
                                double d2 = wall.EndPoint.DistanceTo(neighbor.EndPoint) * 304.8;
                                isAngular = (d1 < LayoutGenerator.CornerThreshold || d2 < LayoutGenerator.CornerThreshold);
                            }
                            if (isAngular)
                                continue;

                            if ((row % 2 == 1 && neighbor.Id.Value < wall.Id.Value) || (row % 2 == 0 && neighbor.Id.Value > wall.Id.Value))
                                continue;
                            // Вычисляем проекции начала и конца соседа на ось стены
                            XYZ dir = wall.Direction.Normalize();
                            double proj1 = (neighbor.StartPoint - wall.StartPoint - (neighbor.Thickness / 304.8 * dir / 2)).DotProduct(dir) * 304.8;
                            double proj2 = (neighbor.EndPoint - wall.StartPoint + (neighbor.Thickness / 304.8 * dir / 2)).DotProduct(dir) * 304.8;
                            double openStart = Math.Max(0, Math.Min(proj1, proj2));
                            double openEnd = Math.Min(wall.Length, Math.Max(proj1, proj2));
                            if (openEnd > openStart)
                                openings.Add((openStart, openEnd));
                        }
                    }




                    //var mergedOpenings = MergeIntervals(openings);
                    var fillSegments = SubtractIntervals((leftBound, rightBound), openings);
                    if (fillSegments.Count == 0)
                        continue;

                    // Чередование направления заполнения – если имеются пересечения, фиксируем: нечётный ряд начинаем слева, четный – справа
                    bool startFromLeft = (wall.LeftNeighbor != null || wall.RightNeighbor != null) ? (localRow % 2 == 1) : true;
                    foreach (var seg in fillSegments)
                    {
                        double segStart = seg.start, segEnd = seg.end;
                        if (segEnd - segStart <= 0)
                            continue;
                        double leftCursor = segStart;
                        double rightCursor = segEnd;
                        bool leftTurn = startFromLeft; // изменяем по очереди

                        List<double> segmentJoints = new List<double>();
                        while (rightCursor - leftCursor >= AllowedBlockLengths.Min())
                        {
                            double available = rightCursor - leftCursor;
                            List<int> possibleBlocks = AllowedBlockLengths.Where(len => len <= available)
                                                                           .OrderByDescending(len => len)
                                                                           .ToList();
                            int chosenBlockLen = -1;
                            double candidateJoint = 0;
                            List<double> prevJoints = new List<double>();
                            if (localRow > 1 && variant.JointsByWall.ContainsKey(wall) && variant.JointsByWall[wall].ContainsKey(localRow - 1))
                                prevJoints = variant.JointsByWall[wall][localRow - 1];

                            foreach (int len in possibleBlocks)
                            {
                                candidateJoint = leftTurn ? leftCursor + len : rightCursor - len;
                                if (!prevJoints.Any(j => Math.Abs(j - candidateJoint) < 100.0))
                                {
                                    chosenBlockLen = len;
                                    break;
                                }
                            }
                            if (chosenBlockLen == -1)
                            {
                                const double shiftDelta = 50.0;
                                if (leftTurn && leftCursor + shiftDelta + possibleBlocks.Min() <= rightCursor)
                                    leftCursor += shiftDelta;
                                else if (!leftTurn && rightCursor - shiftDelta - possibleBlocks.Min() >= leftCursor)
                                    rightCursor -= shiftDelta;
                                else
                                    break;
                                continue;
                            }
                            if (leftTurn)
                            {
                                double blockStart = leftCursor;
                                double blockEnd = leftCursor + chosenBlockLen;
                                variant.Blocks.Add(new BlockPlacement
                                {
                                    Wall = wall,
                                    Row = localRow,
                                    Length = chosenBlockLen,
                                    Start = blockStart,
                                    End = blockEnd
                                });
                                segmentJoints.Add(blockEnd);
                                leftCursor = blockEnd;
                            }
                            else
                            {
                                double blockStart = rightCursor - chosenBlockLen;
                                double blockEnd = rightCursor;
                                variant.Blocks.Add(new BlockPlacement
                                {
                                    Wall = wall,
                                    Row = localRow,
                                    Length = chosenBlockLen,
                                    Start = blockStart,
                                    End = blockEnd
                                });
                                segmentJoints.Add(blockStart);
                                rightCursor = blockStart;
                            }
                            leftTurn = !leftTurn;
                        }
                        double gap = rightCursor - leftCursor;
                        if (gap > 1e-6)
                        {
                            if (gap > 750)
                                variant.WarningCount++;
                            segmentJoints.Add(leftCursor);
                            variant.Blocks.Add(new BlockPlacement
                            {
                                Wall = wall,
                                Row = localRow,
                                Start = leftCursor,
                                End = rightCursor,
                                Length = gap,
                                IsGapFill = true
                            });
                        }
                        foreach (double j in segmentJoints)
                        {
                            if (j > segStart + 1e-6 && j < segEnd - 1e-6)
                            {
                                if (!variant.JointsByWall.ContainsKey(wall))
                                    variant.JointsByWall[wall] = new Dictionary<int, List<double>>();
                                if (!variant.JointsByWall[wall].ContainsKey(localRow))
                                    variant.JointsByWall[wall][localRow] = new List<double>();
                                variant.JointsByWall[wall][localRow].Add(j);
                            }
                        }
                    } // end foreach fill-segment

                    // Проверка вертикальных швов между рядами
                    if (localRow > 1 &&
                        variant.JointsByWall.ContainsKey(wall) &&
                        variant.JointsByWall[wall].ContainsKey(localRow) &&
                        variant.JointsByWall[wall].ContainsKey(localRow - 1))
                    {
                        foreach (double joint in variant.JointsByWall[wall][localRow])
                        {
                            foreach (double prevJoint in variant.JointsByWall[wall][localRow - 1])
                            {
                                if (Math.Abs(joint - prevJoint) < 100.0)
                                    variant.ErrorCount++;
                            }
                        }
                    }
                } // end foreach wall
            } // end for each row
            return variant;
        }

        private static double ComputeLeftBoundary(WallInfo wall, int localRow, Random rand)
        {
            if (wall.LeftNeighbor == null)
                return 0;

            // Вычисляем расстояния между началом текущей стены и концами левого соседа (перевод в мм)
            double d1 = wall.StartPoint.DistanceTo(wall.LeftNeighbor.StartPoint) * 304.8;
            double d2 = wall.StartPoint.DistanceTo(wall.LeftNeighbor.EndPoint) * 304.8;

            bool isAngular = (d1 < LayoutGenerator.CornerThreshold || d2 < LayoutGenerator.CornerThreshold);
            // Используем сравнение ID – более высокий ID считается приоритетным
            bool highPriority = wall.Id.Value > wall.LeftNeighbor.Id.Value;

            if (isAngular)
            {
                if (highPriority)
                {
                    return (localRow % 2 != 0) ? wall.LeftNeighbor.Thickness / 2.0 : -wall.LeftNeighbor.Thickness / 2.0;
                }
                else
                {
                    return (localRow % 2 != 0) ? -wall.LeftNeighbor.Thickness / 2.0 : wall.LeftNeighbor.Thickness / 2.0;
                }
            }
            else // продольное соединение
            {
                if (highPriority)
                {
                    return (localRow % 2 != 0) ? -wall.LeftNeighbor.Thickness / 2.0 : 0.0;
                }
                else
                {
                    return (localRow % 2 != 0) ? wall.LeftNeighbor.Thickness / 2.0 : 0.0;
                }
            }
        }


        private static double ComputeRightBoundary(WallInfo wall, int localRow, Random rand)
        {
            if (wall.RightNeighbor == null)
                return 0;

            double d1 = wall.EndPoint.DistanceTo(wall.RightNeighbor.StartPoint) * 304.8;
            double d2 = wall.EndPoint.DistanceTo(wall.RightNeighbor.EndPoint) * 304.8;
            bool isAngular = (d1 < LayoutGenerator.CornerThreshold || d2 < LayoutGenerator.CornerThreshold);
            bool highPriority = wall.Id.Value > wall.RightNeighbor.Id.Value;

            if (isAngular)
            {
                if (highPriority)
                {
                    return (localRow % 2 != 0) ? -wall.RightNeighbor.Thickness / 2.0 : wall.RightNeighbor.Thickness / 2.0;
                }
                else
                {
                    return (localRow % 2 != 0) ? wall.RightNeighbor.Thickness / 2.0 : -wall.RightNeighbor.Thickness / 2.0;
                }
            }
            else // продольное соединение
            {
                if (highPriority)
                {
                    return (localRow % 2 != 0) ? wall.RightNeighbor.Thickness / 2.0 : 0.0;
                }
                else
                {
                    return (localRow % 2 != 0) ? -wall.RightNeighbor.Thickness / 2.0 : 0.0;
                }
            }
        }


        // Метод вычитания интервалов (открытий) из базового интервала.
        private static List<(double start, double end)> SubtractIntervals((double start, double end) baseInterval, List<(double start, double end)> subtractIntervals)
        {
            List<(double start, double end)> segments = new List<(double, double)>();
            double current = baseInterval.start;
            var sorted = subtractIntervals.OrderBy(i => i.start).ToList();
            foreach (var interval in sorted)
            {
                if (interval.start > current)
                    segments.Add((current, Math.Min(interval.start, baseInterval.end)));
                current = Math.Max(current, interval.end);
            }
            if (current < baseInterval.end)
                segments.Add((current, baseInterval.end));
            return segments;
        }

        // Обновлённый метод установки соединений между стенами
        private static void SetupWallConnections(List<WallInfo> walls)
        {
            // 1. Сбросить информацию по соседям для каждой стены.
            foreach (WallInfo wall in walls)
            {
                wall.LeftNeighbor = null;
                wall.RightNeighbor = null;
                wall.ConnectedWalls.Clear();
            }

            // Задаём допуск: 10 мм в миллиметрах и переводим его в футы (Revit использует футы)
            double tol_mm = 10.0;
            double tol_ft = tol_mm / 304.8;

            // 2. Перебираем каждую пару стен и определяем пересечение их линий.
            for (int i = 0; i < walls.Count; i++)
            {
                WallInfo wallA = walls[i];
                for (int j = i + 1; j < walls.Count; j++)
                {
                    WallInfo wallB = walls[j];

                    // Пытаемся вычислить пересечение линий стен.
                    IntersectionResultArray results = null;
                    SetComparisonResult intersectRes = wallA.line.Intersect(wallB.line, out results);

                    // Если пересечение найдено
                    if (intersectRes == SetComparisonResult.Overlap)
                    {
                        // Получаем точку пересечения (берём первый результат)
                        XYZ intersectPt = results.get_Item(0).XYZPoint;

                        // Проверяем, совпадает ли точка пересечения с концами линии стен.
                        bool A0Match = wallA.line.GetEndPoint(0).DistanceTo(intersectPt) < tol_ft;
                        bool A1Match = wallA.line.GetEndPoint(1).DistanceTo(intersectPt) < tol_ft;
                        bool B0Match = wallB.line.GetEndPoint(0).DistanceTo(intersectPt) < tol_ft;
                        bool B1Match = wallB.line.GetEndPoint(1).DistanceTo(intersectPt) < tol_ft;

                        // Если хотя бы один из концов совпадает – считаем, что соединение определяется как угловое.
                        if (A0Match || A1Match || B0Match || B1Match)
                        {
                            // Для стены A: если её первый конец совпадает, назначаем как LeftNeighbor; если второй – как RightNeighbor.
                            if (A0Match && wallA.LeftNeighbor == null)
                                wallA.LeftNeighbor = wallB;
                            if (A1Match && wallA.RightNeighbor == null)
                                wallA.RightNeighbor = wallB;
                            // Аналогично для стены B.
                            if (B0Match && wallB.LeftNeighbor == null)
                                wallB.LeftNeighbor = wallA;
                            if (B1Match && wallB.RightNeighbor == null)
                                wallB.RightNeighbor = wallA;
                        }
                        // Если пересечение произошло не на концах (то есть внутри линии) – не назначаем соседей,
                        // но всё равно добавляем связь.

                        // В любом случае добавляем стены друг в друга в список соединённых.
                        if (!wallA.ConnectedWalls.Contains(wallB))
                            wallA.ConnectedWalls.Add(wallB);
                        if (!wallB.ConnectedWalls.Contains(wallA))
                            wallB.ConnectedWalls.Add(wallA);
                    }
                }
            }

            // 3. Назначаем приоритеты соседей по ID и вычисляем RowOffset.
            foreach (WallInfo wall in walls)
            {
                // Назначаем приоритеты для угловых соединений (сравнивая ID: меньший ID — приоритет)
                if (wall.LeftNeighbor != null)
                {
                    wall.LeftPriority = wall.Id.Value < wall.LeftNeighbor.Id.Value;
                    // Можно дополнительно вычислить, насколько соединение перпендикулярно (здесь оставляем прежнюю логику)
                    double dot = Math.Abs(wall.line.Direction.Normalize().DotProduct(wall.LeftNeighbor.line.Direction.Normalize()));
                    wall.LeftNeighborAngleIsPerpendicular = dot < 0.15;
                }
                if (wall.RightNeighbor != null)
                {
                    wall.RightPriority = wall.Id.Value < wall.RightNeighbor.Id.Value;
                    double dot = Math.Abs(wall.line.Direction.Normalize().DotProduct(wall.RightNeighbor.line.Direction.Normalize()));
                    wall.RightNeighborAngleIsPerpendicular = dot < 0.15;
                }
            }

            // 4. Вычисляем RowOffset для каждой стены: позиция стены в отсортированной группе ConnectedWalls.
            foreach (WallInfo wall in walls)
            {
                List<WallInfo> group = new List<WallInfo>(wall.ConnectedWalls);
                if (!group.Contains(wall))
                    group.Add(wall);
                group = group.Distinct().ToList();
                group.Sort((w1, w2) => w1.Id.Value.CompareTo(w2.Id.Value));
                wall.RowOffset = group.IndexOf(wall);
            }
        }


    }
}
