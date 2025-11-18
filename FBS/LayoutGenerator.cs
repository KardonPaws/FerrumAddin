using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddinDev.FBS
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
            ComputeCoordZLists(walls);
            List<LayoutVariant> bestVariants = new List<LayoutVariant>();
            int produced = 0;
            foreach (LayoutVariant variant in GenerateVariantsStream(walls))
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
        // 18.11.25 - изменения в ФБС при отступах != 300 и 600
        private static void ComputeCoordZLists(List<WallInfo> walls)
        {
            const double eps = 1e-6;

            // 1) Группируем по BaseElevation в мм и сортируем от самой нижней
            var groups = walls
                .GroupBy(w => Math.Round(w.BaseElevation * 304.8)) // ключ группы — базовая отметка в мм
                .OrderBy(g => g.Key)
                .ToList();

            if (groups.Count == 0)
                return;

            double bottomBaseMm = groups[0].Key; // самая нижняя отметка

            // словари для first300 и количества "снятых" 600 мм
            var hasFirst300 = new Dictionary<WallInfo, bool>();

            foreach (var w in walls)
            {
                hasFirst300[w] = false;
                w.first300 = false;
                w.last300 = false;
            }

            // 2) Для всех групп, кроме самой нижней, считаем расстояние от нижней
            for (int i = 1; i < groups.Count; i++)
            {
                var groupWalls = groups[i].ToList();

                // Берём BaseElevation из первой стены группы (все одинаковые в группе)
                double groupBaseMm = Math.Round(groupWalls[0].BaseElevation * 304.8);
                double delta = groupBaseMm - bottomBaseMm;

                if (delta <= eps)
                    continue; // на всякий случай, если вдруг одинаковые или ниже

                // 2) Если расстояние > 600 — вычитаем по 600, пока не станет < 600
                double diff = delta;
                int removed = 0;
                while (diff > 600.0 + eps)
                {
                    diff -= 600.0;
                    removed++;
                }

                // 3) diff < 300 — смещаем следующую группу на (600 - diff) вверх
                if (diff < 300)
                {
                    double offset = 300.0 - diff;           // на сколько поднять группу
                    double newBaseMm = groupBaseMm + offset;
                    double newBaseFeet = newBaseMm / 304.8;

                    foreach (var w in groupWalls)
                    {
                        hasFirst300[w] = true;
                        w.BaseElevation = newBaseFeet;
                        w.Height = w.Height - offset;
                        w.first300 = true;
                    }
                }
                // 4) diff = 300 — следующей группе first300 = true
                else if (Math.Abs(diff - 300.0) < eps)
                {
                    foreach (var w in groupWalls)
                    {
                        hasFirst300[w] = true;
                        w.first300 = true;
                    }
                }
                // 5) diff > 300 — следующей группе first300 = true и смещаем на (diff - 300)
                else if (diff > 300 && diff < 600) // diff > 300 и < 600
                {
                    double offset = 600 - diff;           // на сколько поднять группу
                    double newBaseMm = groupBaseMm + offset;
                    double newBaseFeet = newBaseMm / 304.8;

                    foreach (var w in groupWalls)
                    {
                        w.BaseElevation = newBaseFeet;
                        w.Height = w.Height - offset;
                    }
                }
            }

            // 6) Формируем coordZList для каждой стены на основе скорректированных BaseElevation и first300
            foreach (var w in walls)
            {
                w.coordZList.Clear();

                double baseMm = Math.Round(w.BaseElevation * 304.8);
                double height = w.Height; // предполагаю, в мм

                bool first = hasFirst300.TryGetValue(w, out bool f) && f;
                w.first300 = first;
                w.last300 = false;

                // 6.1) Первый 300-мм ряд, если он есть
                if (first)
                {
                    w.coordZList.Add(baseMm);
                }

                // 6.2) Полные ряды по 600 мм
                double z = baseMm + (first ? 300.0 : 0.0);
                while (z + 600.0 <= baseMm + height + eps)
                {
                    w.coordZList.Add(z);
                    z += 600.0;
                }

                // 6.3) Если первого 300-мм ряда не было — проверяем последний 300-мм ряд

                 int fullRows = (int)((height - (first ? 300.0 : 0.0)) / 600.0);
                 double rem = (height - (first ? 300.0 : 0.0)) - fullRows * 600.0;

                if (rem >= 300.0 - eps && rem < 600.0 - eps)
                {
                    w.coordZList.Add(baseMm + fullRows * 600.0 + 300.0);
                    w.last300 = true;
                }
                

                // 6.4) Убираем дубли и сортируем
                w.coordZList = w.coordZList
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();
            }
        }


        //private static void ComputeCoordZLists(List<WallInfo> walls)
        //{
        //    // помощник для сравнения с точностью
        //    const double eps = 1e-6;

        //    // 1) Сортируем стены по базовой отметке (в мм)
        //    var sorted = walls
        //        .OrderBy(w => w.BaseElevation * 304.8)
        //        .ToList();

        //    // 2) Для каждой стены вычисляем, будет ли у неё первый ряд в +300 мм
        //    //    — если у какого-то соседа базовая отметка = моя +300,
        //    //      или сосед с той же отметкой уже имеет first300 = true.
        //    var hasFirst300 = new Dictionary<WallInfo, bool>();
        //    foreach (var w in sorted)
        //    {
        //        double myBase = w.BaseElevation * 304.8;
        //        bool first300 = false;

        //        // условие 1: сосед стоит ровно на +300 мм
        //        foreach (var nb in w.ConnectedWalls)
        //        {
        //            double nbBase = nb.BaseElevation * 304.8;
        //            if (Math.Abs(nbBase - (myBase + 300.0)) < eps)
        //            {
        //                first300 = true;
        //                break;
        //            }
        //        }
        //        if (!first300)
        //        {
        //            // условие 2: сосед на той же отметке уже получил first300
        //            foreach (var nb in w.ConnectedWalls)
        //            {
        //                double nbBase = nb.BaseElevation * 304.8;
        //                if (Math.Abs(nbBase - myBase) < eps
        //                    && hasFirst300.TryGetValue(nb, out bool nbFirst)
        //                    && nbFirst)
        //                {
        //                    first300 = true;
        //                    break;
        //                }
        //            }
        //        }

        //        hasFirst300[w] = first300;
        //    }

        //    // 3) Теперь для каждой стены строим полный список coordZList
        //    foreach (var w in walls)
        //    {
        //        w.coordZList.Clear();

        //        double baseMm = Math.Round(w.BaseElevation * 304.8);
        //        double height = w.Height;

        //        bool first = hasFirst300[w];

        //        // 3.1) Если первый 300-мм ряд, добавляем его
        //        if (first)
        //        {
        //            w.coordZList.Add(baseMm);
        //            w.first300 = true;
        //        }

        //        // 3.2) Добавляем все «полные» ряды 600 мм, начиная от base+ (first?300:0)
        //        double z = baseMm + (first ? 300.0 : 0.0);
        //        while (z + 600 <= baseMm + height + eps)
        //        {
        //            w.coordZList.Add(z);
        //            z += 600.0;
        //        }

        //        // 3.3) Если первого 300-мм ряда не было, проверяем условие для «последнего» 300-мм ряда
        //        //     высота = 600*x + rem, где rem∈(300,600)
        //        if (!first)
        //        {
        //            int fullRows = (int)(height / 600.0);
        //            double rem = height - fullRows * 600.0;
        //            if (rem >= 300.0 - eps && rem < 600.0 - eps)
        //            {
        //                w.coordZList.Add(baseMm + fullRows * 600.0 + 300.0);
        //                w.last300 = true;
        //            }
        //        }

        //        // 3.4) Сортируем и убираем дубли
        //        w.coordZList = w.coordZList
        //            .Distinct()
        //            .OrderBy(val => val)
        //            .ToList();
        //    }
        //}

        private static IEnumerable<LayoutVariant> GenerateVariantsStream(List<WallInfo> walls)
        {
            while (true)
                yield return GenerateSingleVariant(walls);
        }

        private static LayoutVariant GenerateSingleVariant(List<WallInfo> walls)
        {
            LayoutVariant variant = new LayoutVariant();
            foreach (WallInfo wall in walls)
            {
                int maxBaseRows = wall.coordZList.Count();

            for (int row = 1; row <= maxBaseRows; row++)
            {
                
                    int localRow = row;

                    // Базовые границы – физические границы стены
                    double baseLeft = 0;
                    double baseRight = wall.Length;
                    double overshootLeft = (wall.LeftNeighbor == null) ? DefaultOvershoot : 0;
                    double overshootRight = (wall.RightNeighbor == null) ? DefaultOvershoot : 0;

                    // Вычисляем смещения для левой и правой сторон, если сосед есть
                    double deltaLeft = ComputeLeftBoundary(wall, localRow);
                    double deltaRight = ComputeRightBoundary(wall, localRow);

                    double leftBound = baseLeft + deltaLeft;
                    double rightBound = baseRight + deltaRight;

                    // Если итоговый интервал невозможен, пропускаем стену
                    if (leftBound >= rightBound)
                        continue;

                    // Обрабатываем проёмы из окон/дверей
                    List<(double start, double end)> openings = new List<(double, double)>();
                    foreach (var op in wall.Openings)
                    {
                        if (op.End > leftBound && op.Start < rightBound && (op.StartZ < wall.coordZList[row-1] + (row == maxBaseRows ? 0 :(wall.coordZList[row] - wall.coordZList[row - 1])) && op.EndZ > wall.coordZList[row-1]))
                        {
                            double opStart = op.Start < 900 ? leftBound : Math.Max(op.Start, leftBound);
                            double opEnd = rightBound - op.End < 900 ? rightBound : Math.Min(op.End, rightBound);
                            openings.Add((opStart, opEnd));


                        }
                    }
                    // Добавляем проёмы для перпендикулярных соединённых стен
                    if (wall.ConnectedWalls.Count > 0)
                    {
                        foreach (var neighbor in wall.ConnectedWalls)
                        {
                            double absDot = Math.Abs(wall.Direction.Normalize()
                                                                         .DotProduct(neighbor.Direction.Normalize()));
                            if (Math.Abs(absDot) > 1e-3) continue;
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
                            List<double> Z = wall.coordZList.Intersect(neighbor.coordZList).ToList();
                            try
                            {
                                double rowWall = wall.coordZList[localRow - 1];
                                int actualRow = Z.IndexOf(rowWall) + 1;
                                if (actualRow == 0)
                                    continue;
                                if ((actualRow % 2 == 1 && neighbor.Id.Value < wall.Id.Value) || (actualRow % 2 == 0 && neighbor.Id.Value > wall.Id.Value))
                                    continue;
                                // Вычисляем проекции начала и конца соседа на ось стены
                                XYZ dir = wall.Direction.Normalize();
                                double proj1 = (neighbor.StartPoint - wall.StartPoint - (neighbor.Thickness / 304.8 * dir / 2)).DotProduct(dir) * 304.8;
                                double proj2 = (neighbor.EndPoint - wall.StartPoint + (neighbor.Thickness / 304.8 * dir / 2)).DotProduct(dir) * 304.8;
                                double openStart = Math.Max(0, Math.Min(proj1, proj2));
                                double openEnd = Math.Min(wall.Length, Math.Max(proj1, proj2));

                                XYZ neighborPoint = wall.line.Distance(neighbor.StartPoint) < wall.line.Distance(neighbor.EndPoint) ? neighbor.StartPoint : neighbor.EndPoint;
                                if (neighborPoint == neighbor.StartPoint)
                                {
                                    if (neighbor.Openings.Count() > 0 && neighbor.Openings.First().Start < 900)
                                        continue;
                                }
                                else
                                {
                                    if (neighbor.Openings.Count() > 0 && neighbor.Length - neighbor.Openings.Last().End < 900)
                                        continue;
                                }

                                if (openEnd > openStart)
                                    openings.Add((openStart, openEnd));
                            }
                            catch
                            { }
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
                            List<int> possibleBlocks = ((row == 1 && wall.first300) || (row == wall.coordZList.Count() && wall.last300))? new List<int>() { 1200 } :
                                AllowedBlockLengths.Where(len => len <= available)
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
                        if (gap > 69)
                        {
                          
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
                    }

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
                }
            }
            return variant;
        }

        private static double ComputeLeftBoundary(WallInfo wall, int localRow)
        {
            double tolFt = LayoutGenerator.CornerThreshold / 304.8;
            var longitudinalNeighbor = wall.ConnectedWalls
                .FirstOrDefault(n =>
                {
                    // коллинеарность направлений
                    double absDot = Math.Abs(wall.Direction.Normalize()
                                             .DotProduct(n.Direction.Normalize()));
                    if (Math.Abs(absDot - 1.0) > 1e-3) return false;
                    // пересечение в начале
                    return n.line.GetEndPoint(0).DistanceTo(wall.StartPoint) < tolFt
                        || n.line.GetEndPoint(1).DistanceTo(wall.StartPoint) < tolFt;
                });
            if (longitudinalNeighbor != null)
            {
                // полный сдвиг = толщина соседа (мм)
                List<double> Zlong = wall.coordZList.Intersect(longitudinalNeighbor.coordZList).ToList();
                double rowWallLong = wall.coordZList[localRow - 1];
                int actualRowLong = Zlong.IndexOf(rowWallLong) + 1;
                if (actualRowLong == 0)
                    return 0;
                double shift = longitudinalNeighbor.Thickness;
                return (actualRowLong % 2 != 0)
                    ? (wall.Id.Value > longitudinalNeighbor.Id.Value ? -shift : shift)
                    : (wall.Id.Value > longitudinalNeighbor.Id.Value ? shift: -shift);
            }

            if (wall.LeftNeighbor == null)
                return 0;

            // Вычисляем расстояния между началом текущей стены и концами левого соседа (перевод в мм)
            double d1 = wall.StartPoint.DistanceTo(wall.LeftNeighbor.StartPoint) * 304.8;
            double d2 = wall.StartPoint.DistanceTo(wall.LeftNeighbor.EndPoint) * 304.8;

            bool isAngular = (d1 < LayoutGenerator.CornerThreshold || d2 < LayoutGenerator.CornerThreshold);
            // Используем сравнение ID – более высокий ID считается приоритетным
            bool highPriority = wall.Id.Value > wall.LeftNeighbor.Id.Value;

            List<double> Z = wall.coordZList.Intersect(wall.LeftNeighbor.coordZList).ToList();
            try
            {
                double rowWall = wall.coordZList[localRow - 1];
                int actualRow = Z.IndexOf(rowWall) + 1;
                if (isAngular)
                {
                    if (highPriority)
                    {
                        return (actualRow % 2 != 0) ? wall.LeftNeighbor.Thickness / 2.0 : -wall.LeftNeighbor.Thickness / 2.0;
                    }
                    else
                    {
                        return (actualRow % 2 != 0) ? -wall.LeftNeighbor.Thickness / 2.0 : wall.LeftNeighbor.Thickness / 2.0;
                    }
                }
                else // продольное соединение
                {
                    if (highPriority)
                    {
                        return (actualRow % 2 != 0) ? -wall.LeftNeighbor.Thickness / 2.0 : wall.LeftNeighbor.Thickness / 2.0;
                    }
                    else
                    {
                        return (actualRow % 2 != 0) ? wall.LeftNeighbor.Thickness / 2.0 : -wall.LeftNeighbor.Thickness / 2.0;
                    }
                }
            }
            catch
            {
                return 0;
            }    
        }


        private static double ComputeRightBoundary(WallInfo wall, int localRow)
        {
            double tolFt = LayoutGenerator.CornerThreshold / 304.8;
            var longitudinalNeighbor = wall.ConnectedWalls
                .FirstOrDefault(n =>
                {
                    double absDot = Math.Abs(wall.Direction.Normalize()
                                             .DotProduct(n.Direction.Normalize()));
                    if (Math.Abs(absDot - 1.0) > 1e-3) return false;
                    return n.line.GetEndPoint(0).DistanceTo(wall.EndPoint) < tolFt
                        || n.line.GetEndPoint(1).DistanceTo(wall.EndPoint) < tolFt;
                });
            if (longitudinalNeighbor != null)
            {
                List<double> Zlong = wall.coordZList.Intersect(longitudinalNeighbor.coordZList).ToList();
                double rowWallLong = wall.coordZList[localRow - 1];
                int actualRowLong = Zlong.IndexOf(rowWallLong) + 1;
                if (actualRowLong == 0)
                    return 0;
                double shift = longitudinalNeighbor.Thickness;
                return (actualRowLong % 2 != 0)
                    ? (wall.Id.Value > longitudinalNeighbor.Id.Value ? shift : -shift)
                    : (wall.Id.Value > longitudinalNeighbor.Id.Value ? -shift : shift);
            }

            if (wall.RightNeighbor == null)
                return 0;

            double d1 = wall.EndPoint.DistanceTo(wall.RightNeighbor.StartPoint) * 304.8;
            double d2 = wall.EndPoint.DistanceTo(wall.RightNeighbor.EndPoint) * 304.8;
            bool isAngular = (d1 < LayoutGenerator.CornerThreshold || d2 < LayoutGenerator.CornerThreshold);
            bool highPriority = wall.Id.Value > wall.RightNeighbor.Id.Value;
            List<double> Z = wall.coordZList.Intersect(wall.RightNeighbor.coordZList).ToList();
            try
            {
                double rowWall = wall.coordZList[localRow - 1];
                int actualRow = Z.IndexOf(rowWall) + 1;

                if (isAngular)
                {
                    if (highPriority)
                    {
                        return (actualRow % 2 != 0) ? -wall.RightNeighbor.Thickness / 2.0 : wall.RightNeighbor.Thickness / 2.0;
                    }
                    else
                    {
                        return (actualRow % 2 != 0) ? wall.RightNeighbor.Thickness / 2.0 : -wall.RightNeighbor.Thickness / 2.0;
                    }
                }
                else // продольное соединение
                {
                    if (highPriority)
                    {
                        return (actualRow % 2 != 0) ? wall.RightNeighbor.Thickness / 2.0 : -wall.RightNeighbor.Thickness / 2.0;
                    }
                    else
                    {
                        return (actualRow % 2 != 0) ? -wall.RightNeighbor.Thickness / 2.0 : wall.RightNeighbor.Thickness / 2.0;
                    }
                }
            }
            catch
            {
                return 0;
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
            // 1. Сброс исходных данных
            foreach (var w in walls)
            {
                w.LeftNeighbor = null;
                w.RightNeighbor = null;
                w.ConnectedWalls.Clear();
            }

            double tolMm = 10.0;
            double tolFt = tolMm / 304.8;
            const double colinearTolerance = 1e-3; // для проверки |dot|-1 ≈ 0

            // 2. Определяем все соединения
            for (int i = 0; i < walls.Count; i++)
            {
                WallInfo wallA = walls[i];
                for (int j = i + 1; j < walls.Count; j++)
                {
                    WallInfo wallB = walls[j];

                    // Любой тип пересечения, не только Overlap
                    IntersectionResultArray results;
                    SetComparisonResult comp = wallA.line.Intersect(wallB.line, out results);

                    if (comp != SetComparisonResult.Disjoint && results != null && results.Size > 0)
                    {
                        // Собираем уникальные точки пересечения
                        var pts = new List<XYZ>();
                        for (int k = 0; k < results.Size; k++)
                        {
                            XYZ p = results.get_Item(k).XYZPoint;
                            if (!pts.Any(x => x.DistanceTo(p) < tolFt))
                                pts.Add(p);
                        }

                        // Вычисляем, коллинеарны ли стены (одинаковый “модуль” направления)
                        XYZ dirA = wallA.line.Direction.Normalize();
                        XYZ dirB = wallB.line.Direction.Normalize();
                        double absDot = Math.Abs(dirA.DotProduct(dirB));
                        bool isColinear = Math.Abs(absDot - 1.0) < colinearTolerance;

                        // Пробегаем по всем точкам пересечения
                        foreach (var pt in pts)
                        {
                            bool A0 = wallA.line.GetEndPoint(0).DistanceTo(pt) < tolFt;
                            bool A1 = wallA.line.GetEndPoint(1).DistanceTo(pt) < tolFt;
                            bool B0 = wallB.line.GetEndPoint(0).DistanceTo(pt) < tolFt;
                            bool B1 = wallB.line.GetEndPoint(1).DistanceTo(pt) < tolFt;

                            // Угловые соседи — только если соединение НЕ коллинеарное
                            if (!isColinear)
                            {
                                if (A0 && wallA.LeftNeighbor == null) wallA.LeftNeighbor = wallB;
                                if (A1 && wallA.RightNeighbor == null) wallA.RightNeighbor = wallB;
                                if (B0 && wallB.LeftNeighbor == null) wallB.LeftNeighbor = wallA;
                                if (B1 && wallB.RightNeighbor == null) wallB.RightNeighbor = wallA;
                            }
                            // Коллинеарные (продольные) пойдут в ConnectedWalls,
                            // но не станут Left/RightNeighbor
                        }

                        // Всегда добавляем связь в общий список независимо от типа
                        if (!wallA.ConnectedWalls.Contains(wallB))
                            wallA.ConnectedWalls.Add(wallB);
                        if (!wallB.ConnectedWalls.Contains(wallA))
                            wallB.ConnectedWalls.Add(wallA);
                    }
                }
            }

            // 3. Назначаем приоритеты и вычисляем угловые параметры
            foreach (var w in walls)
            {
                if (w.LeftNeighbor != null)
                {
                    w.LeftPriority = w.Id.Value < w.LeftNeighbor.Id.Value;
                    w.LeftNeighborAngleIsPerpendicular =
                        Math.Abs(w.line.Direction.Normalize()
                                 .DotProduct(w.LeftNeighbor.line.Direction.Normalize())) < 0.15;
                }
                if (w.RightNeighbor != null)
                {
                    w.RightPriority = w.Id.Value < w.RightNeighbor.Id.Value;
                    w.RightNeighborAngleIsPerpendicular =
                        Math.Abs(w.line.Direction.Normalize()
                                 .DotProduct(w.RightNeighbor.line.Direction.Normalize())) < 0.15;
                }
            }

            // 4. Вычисляем RowOffset (позицию в группе соединённых стен)
            foreach (var w in walls)
            {
                var group = new List<WallInfo>(w.ConnectedWalls) { w };
                group = group.Distinct()
                             .OrderBy(x => x.Id.Value)
                             .ToList();
                w.RowOffset = group.IndexOf(w);
            }
        }

    }
}
