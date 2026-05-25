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
        private const double CornerThreshold = 10.0;  // порог (мм) для определения углового пересечения

        public static List<LayoutVariant> GenerateVariants(List<WallInfo> walls, int generateCount, int keepCount)
        {
            if (walls == null || walls.Count == 0)
                return new List<LayoutVariant>();

            SetupWallConnections(walls);
            ComputeCoordZLists(walls);
            LayoutVariant variant = GenerateSingleVariant(walls);
            variant.TotalBlocks = variant.Blocks.Count;
            return new List<LayoutVariant> { variant };
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
                while (diff > 600.0 + eps)
                {
                    diff -= 600.0;
                }

                // 3) diff < 300 — смещаем следующую группу на (300 - diff) вверх
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
                // 4) diff = 300 — отдельный первый 300-мм ряд не нужен
                else if (Math.Abs(diff - 300.0) < eps)
                {
                    foreach (var w in groupWalls)
                    {
                        hasFirst300[w] = false;
                        w.first300 = false;
                    }
                }
                // 5) diff > 300 — смещаем группу так, чтобы остаток стал 300 мм
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

                double heightAfterFirst = Math.Max(0.0, height - (first ? 300.0 : 0.0));
                int fullRows = (int)(heightAfterFirst / 600.0);
                double rem = heightAfterFirst - fullRows * 600.0;

                if (rem >= 300.0 - eps && rem < 600.0 - eps)
                {
                    w.coordZList.Add(baseMm + (first ? 300.0 : 0.0) + fullRows * 600.0);
                    w.last300 = true;
                }
                

                // 6.4) Убираем дубли и сортируем
                w.coordZList = w.coordZList
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();
            }
        }

        private static double GetRowHeight(WallInfo wall, int row)
        {
            return (row == 1 && wall.first300) || (row == wall.coordZList.Count && wall.last300)
                ? 300.0
                : 600.0;
        }

        private static double GetRowBottom(WallInfo wall, int row)
        {
            return wall.coordZList[row - 1];
        }

        private static double GetRowTop(WallInfo wall, int row)
        {
            return GetRowBottom(wall, row) + GetRowHeight(wall, row);
        }

        private static int GetMinBlockLengthForRow(WallInfo wall, int row)
        {
            return (row == 1 && wall.first300) || (row == wall.coordZList.Count && wall.last300)
                ? 1200
                : 900;
        }

        private class RowMember
        {
            public WallInfo Wall { get; set; }
            public int Row { get; set; }
            public double Origin { get; set; }
            public double Left { get; set; }
            public double Right { get; set; }
        }

        private static double GetJointOrigin(WallInfo wall)
        {
            return Math.Round(wall.StartPoint.DotProduct(wall.Direction) * 304.8);
        }

        private static bool IsOpeningEdgeJointInRow(WallInfo wall, int row, double absoluteJoint)
        {
            if (row <= 0 || row > wall.coordZList.Count)
                return false;

            double rowBottom = GetRowBottom(wall, row);
            double rowTop = GetRowTop(wall, row);
            double localJoint = absoluteJoint - GetJointOrigin(wall);

            foreach (var opening in GetOpeningEdgePositions(wall, rowBottom, rowTop))
            {
                if (Math.Abs(localJoint - opening) <= LayoutGenerator.CornerThreshold)
                    return true;
            }

            return false;
        }

        private static bool HasBlockingPreviousJoint(
            WallInfo wall,
            int row,
            double candidateJoint,
            List<double> previousJoints,
            double jointGap)
        {
            foreach (double previousJoint in previousJoints)
            {
                if (Math.Abs(previousJoint - candidateJoint) >= jointGap)
                    continue;

                bool currentIsOpeningEdge = IsOpeningEdgeJointInRow(wall, row, candidateJoint);
                bool previousIsOpeningEdge = IsOpeningEdgeJointInRow(wall, row - 1, previousJoint);
                if (Math.Abs(previousJoint - candidateJoint) <= LayoutGenerator.CornerThreshold &&
                    currentIsOpeningEdge && previousIsOpeningEdge)
                    continue;

                return true;
            }

            return false;
        }

        private static List<double> GetPreviousRowJoints(LayoutVariant variant, WallInfo wall, int row)
        {
            if (row <= 1)
                return new List<double>();

            double currentBottom = GetRowBottom(wall, row);
            double currentTop = GetRowTop(wall, row);
            double previousBottom = GetRowBottom(wall, row - 1);
            double previousTop = GetRowTop(wall, row - 1);
            List<double> joints = new List<double>();

            foreach (WallInfo groupWall in GetRowColinearGroup(wall, currentBottom, currentTop))
            {
                int previousRow = GetOverlappingRow(groupWall, previousBottom, previousTop);
                if (previousRow <= 0 ||
                    !variant.JointsByWall.ContainsKey(groupWall.LayoutKey) ||
                    !variant.JointsByWall[groupWall.LayoutKey].ContainsKey(previousRow))
                    continue;

                joints.AddRange(variant.JointsByWall[groupWall.LayoutKey][previousRow]);
            }

            return joints.Distinct().ToList();
        }

        private static void AddJoint(LayoutVariant variant, WallInfo wall, int row, double localJoint)
        {
            if (!variant.JointsByWall.ContainsKey(wall.LayoutKey))
                variant.JointsByWall[wall.LayoutKey] = new Dictionary<int, List<double>>();
            if (!variant.JointsByWall[wall.LayoutKey].ContainsKey(row))
                variant.JointsByWall[wall.LayoutKey][row] = new List<double>();

            variant.JointsByWall[wall.LayoutKey][row].Add(Math.Round(localJoint + GetJointOrigin(wall)));
        }

        private static List<double> GetOpeningEdgePositions(WallInfo wall, double rowBottom, double rowTop)
        {
            List<double> edges = new List<double>();

            foreach (var opening in wall.Openings)
            {
                if (opening.StartZ >= rowTop || opening.EndZ <= rowBottom)
                    continue;

                edges.Add(opening.Start < 900 ? 0 : opening.Start);
                edges.Add(wall.Length - opening.End < 900 ? wall.Length : opening.End);
            }

            foreach (var conn in wall.Connections.Where(c => c.IsColinear && IsWallEndpoint(wall, c.PositionOnWall, LayoutGenerator.CornerThreshold)))
            {
                foreach (var opening in conn.Neighbor.Openings)
                {
                    if (opening.StartZ >= rowTop || opening.EndZ <= rowBottom)
                        continue;

                    XYZ p0 = conn.Neighbor.StartPoint + conn.Neighbor.Direction.Normalize() * (opening.Start / 304.8);
                    XYZ p1 = conn.Neighbor.StartPoint + conn.Neighbor.Direction.Normalize() * (opening.End / 304.8);

                    double pos0 = PositionOnWall(wall, p0);
                    double pos1 = PositionOnWall(wall, p1);
                    edges.Add(Math.Min(pos0, pos1));
                    edges.Add(Math.Max(pos0, pos1));
                }
            }

            return edges
                .Where(edge => edge >= -LayoutGenerator.CornerThreshold && edge <= wall.Length + LayoutGenerator.CornerThreshold)
                .Select(edge => Math.Max(0.0, Math.Min(wall.Length, edge)))
                .Distinct()
                .ToList();
        }

        private static List<double> GetColinearNeighborOpeningEdgePositions(WallInfo wall, double rowBottom, double rowTop)
        {
            List<double> edges = new List<double>();

            foreach (var conn in wall.Connections.Where(c => c.IsColinear && IsWallEndpoint(wall, c.PositionOnWall, LayoutGenerator.CornerThreshold)))
            {
                foreach (var opening in conn.Neighbor.Openings)
                {
                    if (opening.StartZ >= rowTop || opening.EndZ <= rowBottom)
                        continue;

                    XYZ p0 = conn.Neighbor.StartPoint + conn.Neighbor.Direction.Normalize() * (opening.Start / 304.8);
                    XYZ p1 = conn.Neighbor.StartPoint + conn.Neighbor.Direction.Normalize() * (opening.End / 304.8);

                    double pos0 = PositionOnWall(wall, p0);
                    double pos1 = PositionOnWall(wall, p1);
                    edges.Add(Math.Min(pos0, pos1));
                    edges.Add(Math.Max(pos0, pos1));
                }
            }

            return edges
                .Where(edge => edge >= -LayoutGenerator.CornerThreshold && edge <= wall.Length + LayoutGenerator.CornerThreshold)
                .Select(edge => Math.Max(0.0, Math.Min(wall.Length, edge)))
                .Distinct()
                .ToList();
        }

        private static bool AreSameDirectedAxes(WallInfo first, WallInfo second)
        {
            return first.Direction.Normalize().DotProduct(second.Direction.Normalize()) > 1.0 - 1e-3;
        }

        private static bool AreSameThickness(WallInfo first, WallInfo second)
        {
            return Math.Abs(first.Thickness - second.Thickness) <= 1.0;
        }

        private static int GetMatchingRow(WallInfo wall, double rowBottom, double rowHeight)
        {
            for (int row = 1; row <= wall.coordZList.Count; row++)
            {
                if (Math.Abs(GetRowBottom(wall, row) - rowBottom) <= 1.0 &&
                    Math.Abs(GetRowHeight(wall, row) - rowHeight) <= 1.0)
                    return row;
            }

            return 0;
        }

        private static List<RowMember> GetMergedRowMembers(WallInfo wall, int row)
        {
            double rowBottom = GetRowBottom(wall, row);
            double rowHeight = GetRowHeight(wall, row);
            Dictionary<WallInfo, int> rowsByWall = new Dictionary<WallInfo, int>();
            Queue<WallInfo> queue = new Queue<WallInfo>();

            rowsByWall[wall] = row;
            queue.Enqueue(wall);

            while (queue.Count > 0)
            {
                WallInfo current = queue.Dequeue();
                foreach (var conn in current.Connections)
                {
                    WallInfo neighbor = conn.Neighbor;
                    if (!conn.IsColinear ||
                        rowsByWall.ContainsKey(neighbor) ||
                        !AreSameDirectedAxes(wall, neighbor) ||
                        !AreSameThickness(wall, neighbor))
                        continue;

                    int neighborRow = GetMatchingRow(neighbor, rowBottom, rowHeight);
                    if (neighborRow == 0)
                        continue;

                    rowsByWall[neighbor] = neighborRow;
                    queue.Enqueue(neighbor);
                }
            }

            return rowsByWall
                .Select(pair => new RowMember
                {
                    Wall = pair.Key,
                    Row = pair.Value,
                    Origin = GetJointOrigin(pair.Key)
                })
                .OrderBy(member => member.Origin)
                .ThenBy(member => member.Wall.Id.Value)
                .ToList();
        }

        private static string GetMergedRowKey(List<RowMember> members)
        {
            return string.Join("|", members
                .Select(member => member.Wall.LayoutKey.ToString() + ":" + member.Row.ToString())
                .OrderBy(value => value));
        }

        private static List<(double start, double end)> MergeIntervals(List<(double start, double end)> intervals)
        {
            List<(double start, double end)> merged = new List<(double start, double end)>();
            foreach (var interval in intervals
                .Where(i => i.end > i.start)
                .OrderBy(i => i.start))
            {
                if (merged.Count == 0 || interval.start - merged.Last().end > LayoutGenerator.CornerThreshold)
                {
                    merged.Add(interval);
                    continue;
                }

                var last = merged.Last();
                merged[merged.Count - 1] = (last.start, Math.Max(last.end, interval.end));
            }

            return merged;
        }

        private static List<(double start, double end)> SubtractIntervals(
            List<(double start, double end)> baseIntervals,
            List<(double start, double end)> subtractIntervals)
        {
            List<(double start, double end)> result = new List<(double start, double end)>();
            foreach (var interval in baseIntervals)
            {
                result.AddRange(SubtractIntervals(interval, subtractIntervals));
            }

            return result;
        }

        private static List<double> GetPreviousRowJoints(LayoutVariant variant, List<RowMember> members)
        {
            List<double> joints = new List<double>();
            foreach (var member in members)
            {
                joints.AddRange(GetPreviousRowJoints(variant, member.Wall, member.Row));
            }

            return joints.Distinct().ToList();
        }

        private static bool IsOpeningEdgeJointInMergedRow(List<RowMember> members, double absoluteJoint)
        {
            foreach (var member in members)
            {
                if (IsOpeningEdgeJointInRow(member.Wall, member.Row, absoluteJoint))
                    return true;
            }

            return false;
        }

        private static bool IsOpeningEdgeJointInPreviousMergedRow(List<RowMember> members, double absoluteJoint)
        {
            foreach (var member in members)
            {
                if (member.Row > 1 && IsOpeningEdgeJointInRow(member.Wall, member.Row - 1, absoluteJoint))
                    return true;
            }

            return false;
        }

        private static bool HasBlockingPreviousJoint(
            List<RowMember> members,
            double candidateJoint,
            List<double> previousJoints,
            double jointGap)
        {
            foreach (double previousJoint in previousJoints)
            {
                if (Math.Abs(previousJoint - candidateJoint) >= jointGap)
                    continue;

                bool currentIsOpeningEdge = IsOpeningEdgeJointInMergedRow(members, candidateJoint);
                bool previousIsOpeningEdge = IsOpeningEdgeJointInPreviousMergedRow(members, previousJoint);
                if (Math.Abs(previousJoint - candidateJoint) <= LayoutGenerator.CornerThreshold &&
                    currentIsOpeningEdge && previousIsOpeningEdge)
                    continue;

                return true;
            }

            return false;
        }

        private static void AddAbsoluteJoint(LayoutVariant variant, WallInfo wall, int row, double absoluteJoint)
        {
            AddJoint(variant, wall, row, absoluteJoint - GetJointOrigin(wall));
        }

        private static void AddMergedJoint(LayoutVariant variant, List<RowMember> members, double absoluteJoint)
        {
            var owners = members
                .Where(member =>
                    absoluteJoint >= member.Origin - LayoutGenerator.CornerThreshold &&
                    absoluteJoint <= member.Origin + member.Wall.Length + LayoutGenerator.CornerThreshold)
                .ToList();

            if (owners.Count == 0)
            {
                RowMember nearest = members
                    .OrderBy(member =>
                    {
                        double left = member.Origin;
                        double right = member.Origin + member.Wall.Length;
                        if (absoluteJoint < left)
                            return left - absoluteJoint;
                        if (absoluteJoint > right)
                            return absoluteJoint - right;
                        return 0.0;
                    })
                    .FirstOrDefault();

                if (nearest != null)
                    owners.Add(nearest);
            }

            foreach (var owner in owners)
            {
                AddAbsoluteJoint(variant, owner.Wall, owner.Row, absoluteJoint);
            }
        }

        private static RowMember GetBlockOwner(List<RowMember> members, double absoluteStart, double absoluteEnd)
        {
            double center = (absoluteStart + absoluteEnd) / 2.0;
            RowMember owner = members.FirstOrDefault(member =>
                center >= member.Origin - LayoutGenerator.CornerThreshold &&
                center <= member.Origin + member.Wall.Length + LayoutGenerator.CornerThreshold);
            if (owner != null)
                return owner;

            return members
                .Select(member =>
                {
                    double left = member.Origin;
                    double right = member.Origin + member.Wall.Length;
                    double overlap = Math.Min(absoluteEnd, right) - Math.Max(absoluteStart, left);
                    return new { Member = member, Overlap = overlap };
                })
                .OrderByDescending(item => item.Overlap)
                .First()
                .Member;
        }

        private static void AddMergedBlock(
            LayoutVariant variant,
            List<RowMember> members,
            double absoluteStart,
            double absoluteEnd,
            double length,
            bool isGapFill = false)
        {
            RowMember owner = GetBlockOwner(members, absoluteStart, absoluteEnd);
            double origin = GetJointOrigin(owner.Wall);
            variant.Blocks.Add(new BlockPlacement
            {
                Wall = owner.Wall,
                Row = owner.Row,
                Length = length,
                Start = absoluteStart - origin,
                End = absoluteEnd - origin,
                IsGapFill = isGapFill
            });
        }

        private static void NormalizeMergedJoints(LayoutVariant variant, List<RowMember> members)
        {
            foreach (var member in members)
            {
                if (variant.JointsByWall.ContainsKey(member.Wall.LayoutKey) &&
                    variant.JointsByWall[member.Wall.LayoutKey].ContainsKey(member.Row))
                {
                    variant.JointsByWall[member.Wall.LayoutKey][member.Row] =
                        variant.JointsByWall[member.Wall.LayoutKey][member.Row].Distinct().ToList();
                }
            }
        }

        private static void GenerateMergedRow(LayoutVariant variant, List<RowMember> members)
        {
            if (members == null || members.Count == 0)
                return;

            HashSet<WallInfo> memberWalls = new HashSet<WallInfo>(members.Select(member => member.Wall));
            double rowBottom = GetRowBottom(members[0].Wall, members[0].Row);
            double rowTop = GetRowTop(members[0].Wall, members[0].Row);
            double rowHeight = GetRowHeight(members[0].Wall, members[0].Row);

            List<(double start, double end)> baseIntervals = new List<(double start, double end)>();
            List<(double start, double end)> openings = new List<(double start, double end)>();

            foreach (var member in members)
            {
                double leftLocal = ComputeLeftBoundary(member.Wall, member.Row, memberWalls);
                double rightLocal = member.Wall.Length + ComputeRightBoundary(member.Wall, member.Row, memberWalls);
                member.Left = member.Origin + leftLocal;
                member.Right = member.Origin + rightLocal;

                if (member.Left < member.Right)
                    baseIntervals.Add((member.Left, member.Right));
            }

            baseIntervals = MergeIntervals(baseIntervals);
            if (baseIntervals.Count == 0)
                return;

            foreach (var member in members)
            {
                double leftLocal = member.Left - member.Origin;
                double rightLocal = member.Right - member.Origin;
                foreach (var op in member.Wall.Openings)
                {
                    if (op.End > leftLocal && op.Start < rightLocal && op.StartZ < rowTop && op.EndZ > rowBottom)
                    {
                        double absoluteStart = member.Origin + op.Start;
                        double absoluteEnd = member.Origin + op.End;
                        foreach (var rowInterval in baseIntervals.Where(interval =>
                            absoluteEnd > interval.start &&
                            absoluteStart < interval.end))
                        {
                            double opStart = absoluteStart - rowInterval.start < 900
                                ? rowInterval.start
                                : Math.Max(absoluteStart, rowInterval.start);
                            double opEnd = rowInterval.end - absoluteEnd < 900
                                ? rowInterval.end
                                : Math.Min(absoluteEnd, rowInterval.end);

                            if (opEnd <= opStart)
                                continue;

                            openings.Add((opStart, opEnd));
                            if (opStart > rowInterval.start + 1e-6 && opStart < rowInterval.end - 1e-6)
                                AddMergedJoint(variant, members, opStart);
                            if (opEnd > rowInterval.start + 1e-6 && opEnd < rowInterval.end - 1e-6)
                                AddMergedJoint(variant, members, opEnd);
                        }
                    }
                }

                foreach (var nodeOpening in GetNodeOpenings(member.Wall, member.Row, leftLocal, rightLocal))
                {
                    openings.Add((member.Origin + nodeOpening.start, member.Origin + nodeOpening.end));
                }

                foreach (var neighborOpening in GetColinearNeighborOpenings(member.Wall, leftLocal, rightLocal, rowBottom, rowTop))
                {
                    openings.Add((member.Origin + neighborOpening.start, member.Origin + neighborOpening.end));
                }

                foreach (double openingEdge in GetColinearNeighborOpeningEdgePositions(member.Wall, rowBottom, rowTop))
                {
                    double absoluteEdge = member.Origin + openingEdge;
                    if (absoluteEdge > member.Left + 1e-6 && absoluteEdge < member.Right - 1e-6)
                        AddMergedJoint(variant, members, absoluteEdge);
                }
            }

            openings = MergeIntervals(openings);
            var fillSegments = SubtractIntervals(baseIntervals, openings);
            if (fillSegments.Count == 0)
                return;

            bool startFromLeft = (members[0].Row % 2 == 1);
            bool isThreeHundredRow = rowHeight <= 300.0 + 1.0;

            foreach (var seg in fillSegments)
            {
                double segStart = seg.start;
                double segEnd = seg.end;
                if (segEnd - segStart <= 0)
                    continue;

                double leftCursor = Math.Round(segStart);
                double rightCursor = Math.Round(segEnd);
                bool leftTurn = startFromLeft;
                List<double> segmentJoints = new List<double>();
                double jointGap = 300;

                while (Math.Round(rightCursor - leftCursor) >= 4200)
                {
                    double available = rightCursor - leftCursor;
                    List<int> possibleBlocks = isThreeHundredRow
                        ? new List<int>() { 1200 }.Where(len => len <= available).ToList()
                        : AllowedBlockLengths.Where(len => len <= available).OrderByDescending(len => len).ToList();

                    int chosenBlockLen = -1;
                    double candidateJoint = 0;
                    List<double> prevJoints = GetPreviousRowJoints(variant, members);

                    foreach (int len in possibleBlocks)
                    {
                        candidateJoint = leftTurn ? leftCursor + len : rightCursor - len;
                        if (!HasBlockingPreviousJoint(members, candidateJoint, prevJoints, jointGap))
                        {
                            chosenBlockLen = len;
                            break;
                        }
                    }

                    if (chosenBlockLen == -1)
                    {
                        if (jointGap > 100)
                        {
                            jointGap -= 50;
                            continue;
                        }

                        const double shiftDelta = 50.0;
                        if (possibleBlocks.Count == 0)
                            break;
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
                        AddMergedBlock(variant, members, blockStart, blockEnd, chosenBlockLen);
                        segmentJoints.Add(blockEnd);
                        leftCursor = blockEnd;
                    }
                    else
                    {
                        double blockStart = rightCursor - chosenBlockLen;
                        double blockEnd = rightCursor;
                        AddMergedBlock(variant, members, blockStart, blockEnd, chosenBlockLen);
                        segmentJoints.Add(blockStart);
                        rightCursor = blockStart;
                    }

                    leftTurn = !leftTurn;
                }

                double gap = Math.Round(rightCursor - leftCursor);
                Dictionary<double, List<double>> gaps = GetGapPatterns(isThreeHundredRow);
                jointGap = 300;
                if (gap >= 900)
                {
                    List<double> chosenGaps = new List<double>();
                    var orderedGapKeys = gaps.Keys.OrderByDescending(k => k).ToList();
                    foreach (var key in orderedGapKeys)
                    {
                        int keyIndex = orderedGapKeys.IndexOf(key);
                        if (keyIndex + 1 < orderedGapKeys.Count && gap <= key && orderedGapKeys[keyIndex + 1] <= gap)
                        {
                            chosenGaps = gaps[key].Where(len => len <= gap).ToList();
                            break;
                        }
                    }

                    while (chosenGaps.Count > 0)
                    {
                        int chosenBlockLen = -1;
                        double candidateJoint = 0;
                        List<double> prevJoints = GetPreviousRowJoints(variant, members);
                        foreach (int len in chosenGaps)
                        {
                            candidateJoint = leftTurn ? leftCursor + len : rightCursor - len;
                            if (!HasBlockingPreviousJoint(members, candidateJoint, prevJoints, jointGap))
                            {
                                chosenBlockLen = len;
                                break;
                            }
                        }

                        if (chosenBlockLen == -1)
                        {
                            if (jointGap > 100)
                            {
                                jointGap -= 50;
                                continue;
                            }

                            const double shiftDelta = 50.0;
                            if (leftTurn && leftCursor + shiftDelta + chosenGaps.Min() <= Math.Round(rightCursor))
                                leftCursor += shiftDelta;
                            else if (!leftTurn && rightCursor - shiftDelta - chosenGaps.Min() >= Math.Round(leftCursor))
                                rightCursor -= shiftDelta;
                            else
                                break;
                            continue;
                        }

                        chosenGaps.Remove(chosenBlockLen);
                        if (leftTurn)
                        {
                            double blockStart = leftCursor;
                            double blockEnd = leftCursor + chosenBlockLen;
                            AddMergedBlock(variant, members, blockStart, blockEnd, chosenBlockLen);
                            segmentJoints.Add(blockEnd);
                            leftCursor = blockEnd;
                        }
                        else
                        {
                            double blockStart = rightCursor - chosenBlockLen;
                            double blockEnd = rightCursor;
                            AddMergedBlock(variant, members, blockStart, blockEnd, chosenBlockLen);
                            segmentJoints.Add(blockStart);
                            rightCursor = blockStart;
                        }

                        leftTurn = !leftTurn;
                    }
                }

                gap = rightCursor - leftCursor;
                if (gap > 69)
                {
                    segmentJoints.Add(leftCursor);
                    AddMergedBlock(variant, members, leftCursor, rightCursor, gap, true);
                }

                foreach (double joint in segmentJoints)
                {
                    if (joint > segStart + 1e-6 && joint < segEnd - 1e-6)
                        AddMergedJoint(variant, members, joint);
                }

                NormalizeMergedJoints(variant, members);
            }
        }

        private static LayoutVariant GenerateSingleVariant(List<WallInfo> walls)
        {
            LayoutVariant variant = new LayoutVariant();
            HashSet<string> processedMergedRows = new HashSet<string>();
            foreach (WallInfo wall in walls)
            {
                int maxBaseRows = wall.coordZList.Count();

            for (int row = 1; row <= maxBaseRows; row++)
            {
                    int localRow = row;

                    List<RowMember> mergedMembers = GetMergedRowMembers(wall, localRow);
                    if (mergedMembers.Count > 1)
                    {
                        string mergedRowKey = GetMergedRowKey(mergedMembers);
                        if (processedMergedRows.Add(mergedRowKey))
                            GenerateMergedRow(variant, mergedMembers);
                        continue;
                    }

                    // Базовые границы – физические границы стены
                    double baseLeft = 0;
                    double baseRight = wall.Length;

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
                    //02.12.25 - измененный профиль стены + вынос на листы
                    double rowBottom = GetRowBottom(wall, row);
                    double rowTop = GetRowTop(wall, row);
                    foreach (var op in wall.Openings)
                    {
                        if (op.End > leftBound && op.Start < rightBound && op.StartZ < rowTop && op.EndZ > rowBottom)
                        {
                            double opStart = op.Start < 900 ? leftBound : Math.Max(op.Start, leftBound);
                            double opEnd = rightBound - op.End < 900 ? rightBound : Math.Min(op.End, rightBound);
                            openings.Add((opStart, opEnd));
                        }
                    }
                    // Добавляем проёмы для перпендикулярных соединённых стен
                    openings.AddRange(GetNodeOpenings(wall, localRow, leftBound, rightBound));
                    openings.AddRange(GetColinearNeighborOpenings(wall, leftBound, rightBound, rowBottom, rowTop));

                    var fillSegments = SubtractIntervals((leftBound, rightBound), openings);
                    if (fillSegments.Count == 0)
                        continue;

                    foreach (double openingEdge in GetOpeningEdgePositions(wall, rowBottom, rowTop))
                    {
                        if (openingEdge > leftBound + 1e-6 && openingEdge < rightBound - 1e-6)
                            AddJoint(variant, wall, localRow, openingEdge);
                    }

                    // Чередование направления заполнения – если имеются пересечения, фиксируем: нечётный ряд начинаем слева, четный – справа
                    bool startFromLeft =  (localRow % 2 == 1);
                    foreach (var seg in fillSegments)
                    {
                        double segStart = seg.start, segEnd = seg.end;
                        if (segEnd - segStart <= 0)
                            continue;
                        double leftCursor = Math.Round(segStart);
                        double rightCursor = Math.Round(segEnd);
                        bool leftTurn = startFromLeft; // изменяем по очереди

                        List<double> segmentJoints = new List<double>();
                        double jointGap = 300;

                        while (Math.Round(rightCursor - leftCursor) >= 4200)
                        {
                            double available = rightCursor - leftCursor;
                            List<int> possibleBlocks = ((row == 1 && wall.first300) || (row == wall.coordZList.Count() && wall.last300))
                                ? new List<int>() { 1200 }.Where(len => len <= available).ToList()
                                :
                                AllowedBlockLengths.Where(len => len <= available)
                                                                           .OrderByDescending(len => len)
                                                                           .ToList();
                            int chosenBlockLen = -1;
                            double candidateJoint = 0;
                            List<double> prevJoints = GetPreviousRowJoints(variant, wall, localRow);

                            foreach (int len in possibleBlocks)
                            {
                                candidateJoint = leftTurn ? wall.StartPoint.DotProduct(wall.Direction)*304.8 + leftCursor + len : wall.StartPoint.DotProduct(wall.Direction)*304.8 + rightCursor - len;
                                if (!HasBlockingPreviousJoint(wall, localRow, candidateJoint, prevJoints, jointGap))
                                {
                                    chosenBlockLen = len;
                                    break;
                                }
                            }
                            if (chosenBlockLen == -1)
                            {
                                if (jointGap > 100)
                                {
                                    jointGap -= 50;
                                    continue;
                                }    
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
                        double gap = Math.Round(rightCursor - leftCursor);
                        Dictionary<double, List<double>> gaps = new Dictionary<double, List<double>>()
                        {
                            { 4200, new List<double>(){900, 900, 900, 1200 } },
                            { 3900, new List<double>(){1200, 2400} },
                            { 3600, new List<double>(){900, 2400 } },
                            { 3300, new List<double>(){1200, 900, 900} },
                            { 3000, new List<double>(){900, 900, 900} },
                            { 2700, new List<double>(){2400} },
                            { 2400, new List<double>(){900, 1200} },
                            { 2100, new List<double>(){900, 900} },
                            { 1800, new List<double>(){1200 } },
                            { 1200, new List<double>(){900} },
                            { 900, new List<double>(){0} }
                        };
                        if ((row == 1 && wall.first300) || (row == wall.coordZList.Count() && wall.last300)) //32028638
                        {
                            gaps = new Dictionary<double, List<double>>()
                            {
                                { 4200, new List<double>(){1200, 1200, 1200} },
                                { 3600, new List<double>(){1200, 1200} },
                                { 2400, new List<double>(){1200} },
                                { 0, new List<double>(){0} },
                            };
                        }
                        jointGap = 300;
                        if (gap >= 900)
                        {
                            List<double> chosenGaps = new List<double>();
                            var orderedGapKeys = gaps.Keys.OrderByDescending(k => k).ToList();
                            foreach (var key in orderedGapKeys)
                            {
                                int keyIndex = orderedGapKeys.IndexOf(key);
                                if (keyIndex + 1 < orderedGapKeys.Count && gap <= key && orderedGapKeys[keyIndex + 1] <= gap)
                                {
                                    chosenGaps = gaps[key].Where(len => len <= gap).ToList();
                                    break;
                                }
                            }
                            while (chosenGaps.Count() > 0)
                            {
                                int chosenBlockLen = -1;
                                double candidateJoint = 0;
                                List<double> prevJoints = GetPreviousRowJoints(variant, wall, localRow);
                                foreach (int len in chosenGaps)
                                {
                                    candidateJoint = leftTurn ? wall.StartPoint.DotProduct(wall.Direction) * 304.8 + leftCursor + len : wall.StartPoint.DotProduct(wall.Direction) * 304.8 + rightCursor - len;
                                    if (!HasBlockingPreviousJoint(wall, localRow, candidateJoint, prevJoints, jointGap))
                                    {
                                        chosenBlockLen = len;
                                        break;
                                    }
                                }
                                if (chosenBlockLen == -1)
                                {
                                    if (jointGap > 100)
                                    {
                                        jointGap -= 50;
                                        continue;
                                    }
                                    const double shiftDelta = 50.0;
                                    if (leftTurn && leftCursor + shiftDelta + chosenGaps.Min() <= Math.Round(rightCursor))
                                        leftCursor += shiftDelta;
                                    else if (!leftTurn && rightCursor - shiftDelta - chosenGaps.Min() >= Math.Round(leftCursor))
                                        rightCursor -= shiftDelta;
                                    else
                                        break;
                                    continue;
                                }
                                chosenGaps.Remove(chosenBlockLen);                             
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
                        
                        }
                        // 01.12.25 - возврат заделок
                        gap = rightCursor - leftCursor;
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
                                AddJoint(variant, wall, localRow, j);
                            }
                        }
                        if (variant.JointsByWall.ContainsKey(wall.LayoutKey) && variant.JointsByWall[wall.LayoutKey].ContainsKey(localRow))
                        {
                            variant.JointsByWall[wall.LayoutKey][localRow] = variant.JointsByWall[wall.LayoutKey][localRow].Distinct().ToList();
                        }
                    }
                }
            }
            return variant;
        }

        private static List<(double start, double end)> GetNodeOpenings(WallInfo wall, int localRow, double leftBound, double rightBound)
        {
            double rowBottom = GetRowBottom(wall, localRow);
            double rowTop = GetRowTop(wall, localRow);
            List<(double start, double end)> openings = new List<(double, double)>();
            List<double> processedPositions = new List<double>();
            double tol = LayoutGenerator.CornerThreshold;

            var perpendicularConnections = GetRowColinearGroup(wall, rowBottom, rowTop)
                .SelectMany(groupWall =>
                {
                    int groupRow = GetOverlappingRow(groupWall, rowBottom, rowTop);
                    return groupWall.Connections
                        .Where(conn => conn.IsPerpendicular && ShouldCreatePerpendicularOpening(groupWall, conn))
                        .Select(conn =>
                        {
                            XYZ connectionPoint = groupWall.StartPoint + groupWall.Direction.Normalize() * (conn.PositionOnWall / 304.8);
                            double projectedPosition = PositionOnWall(wall, connectionPoint);
                            int neighborRow = GetOverlappingRow(conn.Neighbor, rowBottom, rowTop);
                            int sharedRowIndex = groupRow > 0
                                ? GetSharedRowIndex(groupWall, groupRow, conn.Neighbor)
                                : 0;

                            return new
                            {
                                SourceWall = groupWall,
                                Connection = conn,
                                PositionOnWall = projectedPosition,
                                NeighborRow = neighborRow,
                                SharedRowIndex = sharedRowIndex,
                            };
                        });
                })
                .Where(candidate =>
                    candidate.PositionOnWall >= leftBound - tol &&
                    candidate.PositionOnWall <= rightBound + tol)
                .OrderBy(candidate => candidate.PositionOnWall)
                .ToList();

            foreach (var conn in perpendicularConnections)
            {
                if (processedPositions.Any(pos => Math.Abs(pos - conn.PositionOnWall) <= tol))
                    continue;

                var activeConnections = perpendicularConnections
                    .Where(candidate =>
                        Math.Abs(candidate.PositionOnWall - conn.PositionOnWall) <= tol &&
                        AreColinearAxes(candidate.Connection.Neighbor, conn.Connection.Neighbor))
                    .Where(candidate =>
                        candidate.NeighborRow > 0 &&
                        candidate.SharedRowIndex > 0 &&
                        CanNeighborPlaceBlockAtConnection(candidate.Connection, candidate.NeighborRow))
                    .ToList();

                processedPositions.Add(conn.PositionOnWall);
                if (activeConnections.Count == 0)
                    continue;

                var priorityConnection = activeConnections
                    .OrderByDescending(candidate => candidate.Connection.Neighbor.Thickness)
                    .ThenBy(candidate => candidate.Connection.Neighbor.Id.Value)
                    .First();

                if (!PerpendicularHasPriorityAtNode(
                    wall,
                    priorityConnection.SharedRowIndex,
                    priorityConnection.Connection.Neighbor,
                    conn.PositionOnWall,
                    rowBottom,
                    rowTop))
                    continue;

                double halfThickness = activeConnections.Max(candidate => candidate.Connection.Neighbor.Thickness) / 2.0;
                double openStart = Math.Max(leftBound, conn.PositionOnWall - halfThickness);
                double openEnd = Math.Min(rightBound, conn.PositionOnWall + halfThickness);
                if (openEnd > openStart)
                    openings.Add((openStart, openEnd));
            }

            return openings;
        }

        private static int GetOverlappingRow(WallInfo wall, double rowBottom, double rowTop)
        {
            const double eps = 1e-6;
            int bestRow = 0;
            double bestOverlap = 0.0;

            for (int row = 1; row <= wall.coordZList.Count; row++)
            {
                double bottom = GetRowBottom(wall, row);
                double top = GetRowTop(wall, row);
                double overlap = Math.Min(rowTop, top) - Math.Max(rowBottom, bottom);
                if (overlap > bestOverlap + eps)
                {
                    bestOverlap = overlap;
                    bestRow = row;
                }
            }

            return bestOverlap > eps ? bestRow : 0;
        }

        private static int GetSharedRowIndex(WallInfo wall, int localRow, WallInfo neighbor)
        {
            if (localRow <= 0 || localRow > wall.coordZList.Count)
                return 0;

            double rowBottom = GetRowBottom(wall, localRow);
            double rowTop = GetRowTop(wall, localRow);
            if (GetOverlappingRow(neighbor, rowBottom, rowTop) == 0)
                return 0;

            int sharedIndex = 0;
            for (int row = 1; row <= wall.coordZList.Count; row++)
            {
                double currentBottom = GetRowBottom(wall, row);
                double currentTop = GetRowTop(wall, row);
                if (GetOverlappingRow(neighbor, currentBottom, currentTop) == 0)
                    continue;

                sharedIndex++;
                if (row == localRow)
                    return sharedIndex;
            }

            return 0;
        }

        private static bool AreColinearAxes(WallInfo first, WallInfo second)
        {
            double absDot = Math.Abs(first.Direction.Normalize().DotProduct(second.Direction.Normalize()));
            return Math.Abs(absDot - 1.0) < 1e-3;
        }

        private static bool PerpendicularHasPriorityAtNode(
            WallInfo wall,
            int sharedRowIndex,
            WallInfo perpendicularWall,
            double positionOnWall,
            double rowBottom,
            double rowTop)
        {
            if (sharedRowIndex <= 0)
                return false;

            XYZ nodePoint = wall.StartPoint + wall.Direction.Normalize() * (positionOnWall / 304.8);
            List<long> colinearWallIds = GetRowColinearGroup(wall, rowBottom, rowTop)
                .Where(groupWall =>
                {
                    double position = PositionOnWall(groupWall, nodePoint);
                    return position >= -LayoutGenerator.CornerThreshold &&
                        position <= groupWall.Length + LayoutGenerator.CornerThreshold;
                })
                .Select(groupWall => groupWall.Id.Value)
                .ToList();

            if (colinearWallIds.Count == 0)
                colinearWallIds.Add(wall.Id.Value);

            long colinearAxisId = colinearWallIds.Min();
            return sharedRowIndex % 2 != 0
                ? perpendicularWall.Id.Value > colinearAxisId
                : perpendicularWall.Id.Value < colinearAxisId;
        }

        private static List<WallInfo> GetRowColinearGroup(WallInfo wall, double rowBottom, double rowTop)
        {
            List<WallInfo> group = new List<WallInfo>();
            Queue<WallInfo> queue = new Queue<WallInfo>();
            group.Add(wall);
            queue.Enqueue(wall);

            while (queue.Count > 0)
            {
                WallInfo current = queue.Dequeue();
                foreach (var conn in current.Connections)
                {
                    WallInfo neighbor = conn.Neighbor;
                    if (!conn.IsColinear ||
                        !AreColinearAxes(wall, neighbor) ||
                        GetOverlappingRow(neighbor, rowBottom, rowTop) == 0 ||
                        group.Contains(neighbor))
                        continue;

                    group.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            return group;
        }

        private static bool OpeningBlocksPosition(WallInfo wall, OpeningInfo opening, int row, double position)
        {
            double rowBottom = wall.coordZList[row - 1];
            double rowTop = rowBottom + GetRowHeight(wall, row);
            if (opening.StartZ >= rowTop || opening.EndZ <= rowBottom)
                return false;

            double start = opening.Start < 900 ? 0 : opening.Start;
            double end = wall.Length - opening.End < 900 ? wall.Length : opening.End;
            return position >= start - LayoutGenerator.CornerThreshold &&
                position <= end + LayoutGenerator.CornerThreshold;
        }

        private static bool CanNeighborPlaceBlockAtConnection(WallConnectionInfo conn, int neighborRow)
        {
            WallInfo neighbor = conn.Neighbor;
            double position = conn.PositionOnNeighbor;

            if (neighborRow <= 0 || neighborRow > neighbor.coordZList.Count)
                return false;

            if (neighbor.Openings.Any(op => OpeningBlocksPosition(neighbor, op, neighborRow, position)))
                return false;

            double leftBound = ComputeEndpointBoundary(neighbor, neighborRow, true, null, false);
            double rightBound = neighbor.Length + ComputeEndpointBoundary(neighbor, neighborRow, false, null, false);
            if (leftBound >= rightBound)
                return false;

            bool atLeft = position <= LayoutGenerator.CornerThreshold;
            bool atRight = neighbor.Length - position <= LayoutGenerator.CornerThreshold;
            if (atLeft && leftBound > position + LayoutGenerator.CornerThreshold)
                return false;
            if (atRight && rightBound < position - LayoutGenerator.CornerThreshold)
                return false;

            double checkPosition = position;
            if (checkPosition < leftBound - LayoutGenerator.CornerThreshold ||
                checkPosition > rightBound + LayoutGenerator.CornerThreshold)
                return false;

            double rowBottom = GetRowBottom(neighbor, neighborRow);
            double rowTop = GetRowTop(neighbor, neighborRow);
            List<(double start, double end)> openings = new List<(double, double)>();
            foreach (var op in neighbor.Openings)
            {
                if (op.End > leftBound && op.Start < rightBound && op.StartZ < rowTop && op.EndZ > rowBottom)
                {
                    double opStart = op.Start < 900 ? leftBound : Math.Max(op.Start, leftBound);
                    double opEnd = rightBound - op.End < 900 ? rightBound : Math.Min(op.End, rightBound);
                    openings.Add((opStart, opEnd));
                }
            }
            openings.AddRange(GetColinearNeighborOpenings(neighbor, leftBound, rightBound, rowBottom, rowTop));

            var fillSegments = SubtractIntervals((leftBound, rightBound), openings);
            return fillSegments.Any(seg => SimulatedBlockCoversPosition(neighbor, neighborRow, seg, checkPosition, false));
        }

        private static bool IsThreeHundredRow(WallInfo wall, int row)
        {
            return (row == 1 && wall.first300) || (row == wall.coordZList.Count && wall.last300);
        }

        private static Dictionary<double, List<double>> GetGapPatterns(bool isThreeHundredRow)
        {
            if (isThreeHundredRow)
            {
                return new Dictionary<double, List<double>>
                {
                    { 4200, new List<double>() { 1200, 1200, 1200 } },
                    { 3600, new List<double>() { 1200, 1200 } },
                    { 2400, new List<double>() { 1200 } },
                    { 0, new List<double>() { 0 } },
                };
            }

            return new Dictionary<double, List<double>>
            {
                { 4200, new List<double>() { 900, 900, 900, 1200 } },
                { 3900, new List<double>() { 1200, 2400 } },
                { 3600, new List<double>() { 900, 2400 } },
                { 3300, new List<double>() { 1200, 900, 900 } },
                { 3000, new List<double>() { 900, 900, 900 } },
                { 2700, new List<double>() { 2400 } },
                { 2400, new List<double>() { 900, 1200 } },
                { 2100, new List<double>() { 900, 900 } },
                { 1800, new List<double>() { 1200 } },
                { 1200, new List<double>() { 900 } },
                { 900, new List<double>() { 0 } }
            };
        }

        private static bool BlockIntervalCovers(double start, double end, double position)
        {
            return position >= start - LayoutGenerator.CornerThreshold &&
                position <= end + LayoutGenerator.CornerThreshold &&
                end - start > LayoutGenerator.CornerThreshold;
        }

        private static bool SimulatedBlockCoversPosition(
            WallInfo wall,
            int row,
            (double start, double end) segment,
            double position,
            bool includeGapFill = true)
        {
            double leftCursor = Math.Round(segment.start);
            double rightCursor = Math.Round(segment.end);
            bool leftTurn = row % 2 == 1;
            bool isThreeHundredRow = IsThreeHundredRow(wall, row);

            while (Math.Round(rightCursor - leftCursor) >= 4200)
            {
                double available = rightCursor - leftCursor;
                List<int> possibleBlocks = isThreeHundredRow
                    ? new List<int>() { 1200 }.Where(candidateLength => candidateLength <= available).ToList()
                    : AllowedBlockLengths.Where(candidateLength => candidateLength <= available).OrderByDescending(candidateLength => candidateLength).ToList();
                if (possibleBlocks.Count == 0)
                    break;

                int selectedLength = possibleBlocks.First();
                double blockStart = leftTurn ? leftCursor : rightCursor - selectedLength;
                double blockEnd = leftTurn ? leftCursor + selectedLength : rightCursor;
                if (BlockIntervalCovers(blockStart, blockEnd, position))
                    return true;

                if (leftTurn)
                    leftCursor = blockEnd;
                else
                    rightCursor = blockStart;
                leftTurn = !leftTurn;
            }

            double gap = Math.Round(rightCursor - leftCursor);
            if (gap >= 900)
            {
                Dictionary<double, List<double>> gaps = GetGapPatterns(isThreeHundredRow);
                List<double> chosenGaps = new List<double>();
                var orderedGapKeys = gaps.Keys.OrderByDescending(k => k).ToList();
                foreach (var key in orderedGapKeys)
                {
                    int keyIndex = orderedGapKeys.IndexOf(key);
                    if (keyIndex + 1 < orderedGapKeys.Count && gap <= key && orderedGapKeys[keyIndex + 1] <= gap)
                    {
                        chosenGaps = gaps[key].Where(len => len <= gap).ToList();
                        break;
                    }
                }

                while (chosenGaps.Count > 0)
                {
                    double len = chosenGaps.First();
                    chosenGaps.Remove(len);
                    if (len <= LayoutGenerator.CornerThreshold)
                    {
                        leftTurn = !leftTurn;
                        continue;
                    }

                    double blockStart = leftTurn ? leftCursor : rightCursor - len;
                    double blockEnd = leftTurn ? leftCursor + len : rightCursor;
                    if (BlockIntervalCovers(blockStart, blockEnd, position))
                        return true;

                    if (leftTurn)
                        leftCursor = blockEnd;
                    else
                        rightCursor = blockStart;
                    leftTurn = !leftTurn;
                }
            }

            double remainingGap = rightCursor - leftCursor;
            if (includeGapFill && remainingGap > 69 && BlockIntervalCovers(leftCursor, rightCursor, position))
                return true;

            return false;
        }

        private static bool IsWallEndpoint(WallInfo wall, double positionMm, double toleranceMm)
        {
            return positionMm <= toleranceMm || wall.Length - positionMm <= toleranceMm;
        }

        private static bool HasColinearConnectionAt(WallInfo wall, double positionMm)
        {
            return wall.Connections.Any(c =>
                c.IsColinear &&
                Math.Abs(c.PositionOnWall - positionMm) <= LayoutGenerator.CornerThreshold);
        }

        private static bool IsNeighborEndpoint(WallConnectionInfo conn)
        {
            return conn.PositionOnNeighbor <= LayoutGenerator.CornerThreshold ||
                conn.Neighbor.Length - conn.PositionOnNeighbor <= LayoutGenerator.CornerThreshold;
        }

        private static bool IsDifferentThicknessColinearConnection(WallInfo wall, WallConnectionInfo conn)
        {
            return conn.IsColinear &&
                AreSameDirectedAxes(wall, conn.Neighbor) &&
                !AreSameThickness(wall, conn.Neighbor);
        }

        private static bool ShouldCreatePerpendicularOpening(WallInfo wall, WallConnectionInfo conn)
        {
            bool currentEndpoint = IsWallEndpoint(wall, conn.PositionOnWall, LayoutGenerator.CornerThreshold);
            if (!currentEndpoint)
                return true;

            // L-угол: оба участника заканчиваются в узле, проем не нужен.
            if (IsNeighborEndpoint(conn) && !HasColinearConnectionAt(wall, conn.PositionOnWall))
                return false;

            // T-узел из трех стен: проходная стена разбита на две сонаправленные части.
            return HasColinearConnectionAt(wall, conn.PositionOnWall);
        }

        private static List<(double start, double end)> GetColinearNeighborOpenings(
            WallInfo wall,
            double leftBound,
            double rightBound,
            double rowBottom,
            double rowTop)
        {
            List<(double start, double end)> openings = new List<(double, double)>();

            foreach (var conn in wall.Connections.Where(c => c.IsColinear && IsWallEndpoint(wall, c.PositionOnWall, LayoutGenerator.CornerThreshold)))
            {
                foreach (var op in conn.Neighbor.Openings)
                {
                    if (op.StartZ >= rowTop || op.EndZ <= rowBottom)
                        continue;

                    XYZ p0 = conn.Neighbor.StartPoint + conn.Neighbor.Direction.Normalize() * (op.Start / 304.8);
                    XYZ p1 = conn.Neighbor.StartPoint + conn.Neighbor.Direction.Normalize() * (op.End / 304.8);

                    double pos0 = PositionOnWall(wall, p0);
                    double pos1 = PositionOnWall(wall, p1);
                    double start = Math.Min(pos0, pos1);
                    double end = Math.Max(pos0, pos1);

                    if (end > leftBound && start < rightBound)
                        openings.Add((Math.Max(start, leftBound), Math.Min(end, rightBound)));
                }
            }

            return openings;
        }

        private static double SignedEndpointShift(WallInfo wall, WallConnectionInfo conn, int actualRow, bool isLeft)
        {
            if (conn.IsColinear)
            {
                if (IsDifferentThicknessColinearConnection(wall, conn))
                {
                    // У разных толщин общий шов смещаем в тело более толстой стены.
                    double overlap = Math.Min(wall.Thickness + 300, conn.Neighbor.Thickness + 300);
                    bool currentIsThinner = wall.Thickness < conn.Neighbor.Thickness;

                    if (currentIsThinner)
                        return isLeft ? -overlap : overlap;

                    return isLeft ? overlap : -overlap;
                }

                double shift = conn.Neighbor.Thickness;
                bool currentIsAfterNeighbor = wall.Id.Value != conn.Neighbor.Id.Value
                    ? wall.Id.Value > conn.Neighbor.Id.Value
                    : wall.StartPoint.DotProduct(wall.Direction) > conn.Neighbor.StartPoint.DotProduct(conn.Neighbor.Direction);

                if (isLeft)
                {
                    return (actualRow % 2 != 0)
                        ? (currentIsAfterNeighbor ? -shift : shift)
                        : (currentIsAfterNeighbor ? shift : -shift);
                }

                return (actualRow % 2 != 0)
                    ? (currentIsAfterNeighbor ? shift : -shift)
                    : (currentIsAfterNeighbor ? -shift : shift);
            }

            double angularShift = conn.Neighbor.Thickness / 2.0;
            bool highPriority = wall.Id.Value > conn.Neighbor.Id.Value;

            if (isLeft)
                return highPriority
                    ? ((actualRow % 2 != 0) ? angularShift : -angularShift)
                    : ((actualRow % 2 != 0) ? -angularShift : angularShift);

            return highPriority
                ? ((actualRow % 2 != 0) ? -angularShift : angularShift)
                : ((actualRow % 2 != 0) ? angularShift : -angularShift);
        }

        private static double ComputeEndpointBoundary(WallInfo wall, int localRow, bool isLeft)
        {
            return ComputeEndpointBoundary(wall, localRow, isLeft, null, true);
        }

        private static double ComputeEndpointBoundary(
            WallInfo wall,
            int localRow,
            bool isLeft,
            HashSet<WallInfo> ignoredColinearNeighbors)
        {
            return ComputeEndpointBoundary(wall, localRow, isLeft, ignoredColinearNeighbors, true);
        }

        private static bool HasPerpendicularBlockAtEndpoint(
            WallInfo wall,
            int localRow,
            List<WallConnectionInfo> endpointConnections,
            bool requirePriority)
        {
            double rowBottom = GetRowBottom(wall, localRow);
            double rowTop = GetRowTop(wall, localRow);

            foreach (var conn in endpointConnections.Where(c => c.IsPerpendicular && ShouldCreatePerpendicularOpening(wall, c)))
            {
                int neighborRow = GetOverlappingRow(conn.Neighbor, rowBottom, rowTop);
                int sharedRowIndex = GetSharedRowIndex(wall, localRow, conn.Neighbor);
                if (neighborRow <= 0 || sharedRowIndex <= 0)
                    continue;

                if (!CanNeighborPlaceBlockAtConnection(conn, neighborRow))
                    continue;

                if (requirePriority &&
                    !PerpendicularHasPriorityAtNode(
                        wall,
                        sharedRowIndex,
                        conn.Neighbor,
                        conn.PositionOnWall,
                        rowBottom,
                        rowTop))
                    continue;

                return true;
            }

            return false;
        }

        private static bool HasActivePerpendicularConnectionAtEndpoint(
            WallInfo wall,
            int localRow,
            List<WallConnectionInfo> endpointConnections)
        {
            return HasPerpendicularBlockAtEndpoint(wall, localRow, endpointConnections, true);
        }

        private static double GetPerpendicularBlockBoundaryShift(
            WallInfo wall,
            int localRow,
            bool isLeft,
            List<WallConnectionInfo> endpointConnections)
        {
            double rowBottom = GetRowBottom(wall, localRow);
            double rowTop = GetRowTop(wall, localRow);
            double result = 0.0;

            foreach (var conn in endpointConnections.Where(c => c.IsPerpendicular && ShouldCreatePerpendicularOpening(wall, c)))
            {
                int neighborRow = GetOverlappingRow(conn.Neighbor, rowBottom, rowTop);
                int sharedRowIndex = GetSharedRowIndex(wall, localRow, conn.Neighbor);
                if (neighborRow <= 0 || sharedRowIndex <= 0)
                    continue;

                if (!CanNeighborPlaceBlockAtConnection(conn, neighborRow))
                    continue;

                double candidate = (isLeft ? 1.0 : -1.0) * conn.Neighbor.Thickness / 2.0;
                if (Math.Abs(candidate) > Math.Abs(result))
                    result = candidate;
            }

            return result;
        }

        private static double ComputeEndpointBoundary(
            WallInfo wall,
            int localRow,
            bool isLeft,
            HashSet<WallInfo> ignoredColinearNeighbors,
            bool respectPerpendicularPriority)
        {
            double endpoint = isLeft ? 0.0 : wall.Length;
            double tol = LayoutGenerator.CornerThreshold;
            double result = 0.0;
            var endpointConnections = wall.Connections
                .Where(c => Math.Abs(c.PositionOnWall - endpoint) <= tol)
                .ToList();

            bool hasColinearAtEndpoint = endpointConnections.Any(c => c.IsColinear);
            double perpendicularBlockBoundaryShift = respectPerpendicularPriority && hasColinearAtEndpoint
                ? GetPerpendicularBlockBoundaryShift(wall, localRow, isLeft, endpointConnections)
                : 0.0;
            result = perpendicularBlockBoundaryShift;
            bool hasPerpendicularBlockAtEndpoint = Math.Abs(perpendicularBlockBoundaryShift) > 0.0;
            bool hasActivePerpendicularAtEndpoint = respectPerpendicularPriority &&
                hasColinearAtEndpoint &&
                HasActivePerpendicularConnectionAtEndpoint(wall, localRow, endpointConnections);

            foreach (var conn in endpointConnections)
            {
                if (conn.IsColinear &&
                    ignoredColinearNeighbors != null &&
                    ignoredColinearNeighbors.Contains(conn.Neighbor))
                    continue;

                if (hasPerpendicularBlockAtEndpoint && IsDifferentThicknessColinearConnection(wall, conn))
                    continue;

                // В ряду с активной перпендикулярной стеной границу задаст ее проем.
                if (hasActivePerpendicularAtEndpoint && conn.IsColinear)
                    continue;

                if (hasColinearAtEndpoint && conn.IsPerpendicular)
                    continue;

                int sharedRowIndex = GetSharedRowIndex(wall, localRow, conn.Neighbor);
                if (sharedRowIndex == 0)
                    continue;

                double candidate = SignedEndpointShift(wall, conn, sharedRowIndex, isLeft);
                if (Math.Abs(candidate) > Math.Abs(result))
                    result = candidate;
            }

            return result;
        }

        private static double ComputeLeftBoundary(WallInfo wall, int localRow)
        {
            return ComputeEndpointBoundary(wall, localRow, true);
        }

        private static double ComputeLeftBoundary(WallInfo wall, int localRow, HashSet<WallInfo> ignoredColinearNeighbors)
        {
            return ComputeEndpointBoundary(wall, localRow, true, ignoredColinearNeighbors);
        }


        private static double ComputeRightBoundary(WallInfo wall, int localRow)
        {
            return ComputeEndpointBoundary(wall, localRow, false);
        }

        private static double ComputeRightBoundary(WallInfo wall, int localRow, HashSet<WallInfo> ignoredColinearNeighbors)
        {
            return ComputeEndpointBoundary(wall, localRow, false, ignoredColinearNeighbors);
        }


        // Метод вычитания интервалов (открытий) из базового интервала.
        private static List<(double start, double end)> SubtractIntervals((double start, double end) baseInterval, List<(double start, double end)> subtractIntervals)
        {
            List<(double start, double end)> segments = new List<(double, double)>();
            double current = baseInterval.start;
            var sorted = subtractIntervals
                .Select(i => (start: Math.Max(i.start, baseInterval.start), end: Math.Min(i.end, baseInterval.end)))
                .Where(i => i.end > i.start)
                .OrderBy(i => i.start)
                .ToList();
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

        private static double PositionOnWall(WallInfo wall, XYZ point)
        {
            return (point - wall.StartPoint).DotProduct(wall.Direction.Normalize()) * 304.8;
        }

        private static void AddConnection(WallInfo wall, WallInfo neighbor, XYZ point, bool isColinear, bool isPerpendicular)
        {
            double positionOnWall = Math.Max(0.0, Math.Min(wall.Length, PositionOnWall(wall, point)));
            double positionOnNeighbor = Math.Max(0.0, Math.Min(neighbor.Length, PositionOnWall(neighbor, point)));

            WallConnectionInfo existing = wall.Connections.FirstOrDefault(c =>
                c.Neighbor == neighbor &&
                Math.Abs(c.PositionOnWall - positionOnWall) < LayoutGenerator.CornerThreshold);
            if (existing != null)
            {
                existing.IsColinear = existing.IsColinear || isColinear;
                existing.IsPerpendicular = existing.IsPerpendicular || isPerpendicular;
                return;
            }

            wall.Connections.Add(new WallConnectionInfo
            {
                Neighbor = neighbor,
                PositionOnWall = positionOnWall,
                PositionOnNeighbor = positionOnNeighbor,
                IsColinear = isColinear,
                IsPerpendicular = isPerpendicular
            });
        }

        private static void AddConnectionPair(WallInfo wallA, WallInfo wallB, XYZ point, bool isColinear, bool isPerpendicular)
        {
            AddConnection(wallA, wallB, point, isColinear, isPerpendicular);
            AddConnection(wallB, wallA, point, isColinear, isPerpendicular);

            if (!wallA.ConnectedWalls.Contains(wallB))
                wallA.ConnectedWalls.Add(wallB);
            if (!wallB.ConnectedWalls.Contains(wallA))
                wallB.ConnectedWalls.Add(wallA);
        }

        private static List<XYZ> GetConnectionPoints(WallInfo wallA, WallInfo wallB, double tolFt)
        {
            List<XYZ> points = new List<XYZ>();
            XYZ dirA = wallA.line.Direction.Normalize();
            XYZ dirB = wallB.line.Direction.Normalize();
            double absDot = Math.Abs(dirA.DotProduct(dirB));
            bool isColinear = Math.Abs(absDot - 1.0) < 1e-3;
            double nodeTolFt = isColinear
                ? (Math.Abs(wallA.Thickness - wallB.Thickness) / 2.0 + LayoutGenerator.CornerThreshold) / 304.8
                : (Math.Max(wallA.Thickness, wallB.Thickness) / 2.0 + LayoutGenerator.CornerThreshold) / 304.8;

            double HorizontalDistance(XYZ first, XYZ second)
            {
                double dx = first.X - second.X;
                double dy = first.Y - second.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            void AddPoint(XYZ point)
            {
                double duplicateTolerance = isColinear ? tolFt : nodeTolFt;
                if (!points.Any(p => HorizontalDistance(p, point) < duplicateTolerance))
                    points.Add(point);
            }

            double CrossXY(XYZ first, XYZ second)
            {
                return first.X * second.Y - first.Y * second.X;
            }

            double DotXY(XYZ first, XYZ second)
            {
                return first.X * second.X + first.Y * second.Y;
            }

            bool TryProjectPointOnSegmentXY(Line line, XYZ point, double tolerance, out XYZ projectedPoint)
            {
                XYZ start = line.GetEndPoint(0);
                XYZ end = line.GetEndPoint(1);
                XYZ direction = new XYZ(end.X - start.X, end.Y - start.Y, 0);
                XYZ toPoint = new XYZ(point.X - start.X, point.Y - start.Y, 0);
                double lengthSquared = DotXY(direction, direction);
                projectedPoint = null;
                if (lengthSquared < 1e-12)
                    return false;

                double t = DotXY(toPoint, direction) / lengthSquared;
                projectedPoint = new XYZ(
                    start.X + direction.X * t,
                    start.Y + direction.Y * t,
                    point.Z);

                double projectionMm = t * Math.Sqrt(lengthSquared) * 304.8;
                double lengthMm = Math.Sqrt(lengthSquared) * 304.8;
                return HorizontalDistance(point, projectedPoint) <= tolerance &&
                    projectionMm >= -LayoutGenerator.CornerThreshold &&
                    projectionMm <= lengthMm + LayoutGenerator.CornerThreshold;
            }

            bool TryProjectPointOnSegmentEndpointXY(Line line, XYZ point, double tolerance, out XYZ projectedPoint)
            {
                XYZ start = line.GetEndPoint(0);
                XYZ end = line.GetEndPoint(1);
                XYZ direction = new XYZ(end.X - start.X, end.Y - start.Y, 0);
                XYZ toPoint = new XYZ(point.X - start.X, point.Y - start.Y, 0);
                double lengthSquared = DotXY(direction, direction);
                projectedPoint = null;
                if (lengthSquared < 1e-12)
                    return false;

                double t = DotXY(toPoint, direction) / lengthSquared;
                projectedPoint = new XYZ(
                    start.X + direction.X * t,
                    start.Y + direction.Y * t,
                    point.Z);

                double lengthMm = Math.Sqrt(lengthSquared) * 304.8;
                double projectionMm = t * lengthMm;
                bool projectsToEndpoint = projectionMm <= LayoutGenerator.CornerThreshold ||
                    lengthMm - projectionMm <= LayoutGenerator.CornerThreshold;

                return projectsToEndpoint &&
                    HorizontalDistance(point, projectedPoint) <= tolerance &&
                    projectionMm >= -LayoutGenerator.CornerThreshold &&
                    projectionMm <= lengthMm + LayoutGenerator.CornerThreshold;
            }

            bool TryIntersectSegmentsXY(Line first, Line second, double tolerance, out XYZ intersection)
            {
                XYZ p = first.GetEndPoint(0);
                XYZ pEnd = first.GetEndPoint(1);
                XYZ q = second.GetEndPoint(0);
                XYZ qEnd = second.GetEndPoint(1);
                XYZ r = new XYZ(pEnd.X - p.X, pEnd.Y - p.Y, 0);
                XYZ s = new XYZ(qEnd.X - q.X, qEnd.Y - q.Y, 0);
                double denominator = CrossXY(r, s);
                intersection = null;

                if (Math.Abs(denominator) < 1e-12)
                    return false;

                XYZ qp = new XYZ(q.X - p.X, q.Y - p.Y, 0);
                double t = CrossXY(qp, s) / denominator;
                double u = CrossXY(qp, r) / denominator;
                double firstLength = Math.Sqrt(DotXY(r, r));
                double secondLength = Math.Sqrt(DotXY(s, s));
                double firstTolerance = firstLength > 1e-9 ? tolerance / firstLength : 0.0;
                double secondTolerance = secondLength > 1e-9 ? tolerance / secondLength : 0.0;

                if (t < -firstTolerance || t > 1.0 + firstTolerance ||
                    u < -secondTolerance || u > 1.0 + secondTolerance)
                    return false;

                intersection = new XYZ(p.X + r.X * t, p.Y + r.Y * t, p.Z);
                return true;
            }

            if (TryIntersectSegmentsXY(wallA.line, wallB.line, nodeTolFt, out XYZ xyIntersection))
                AddPoint(xyIntersection);

            IntersectionResultArray results;
            SetComparisonResult comp = wallA.line.Intersect(wallB.line, out results);
            if (comp != SetComparisonResult.Disjoint && results != null)
            {
                for (int k = 0; k < results.Size; k++)
                {
                    XYZ point = results.get_Item(k).XYZPoint;
                    if (point != null)
                        AddPoint(point);
                }
            }

            XYZ a0 = wallA.line.GetEndPoint(0);
            XYZ a1 = wallA.line.GetEndPoint(1);
            XYZ b0 = wallB.line.GetEndPoint(0);
            XYZ b1 = wallB.line.GetEndPoint(1);

            if (TryProjectPointOnSegmentXY(wallB.line, a0, tolFt, out _)) AddPoint(a0);
            if (TryProjectPointOnSegmentXY(wallB.line, a1, tolFt, out _)) AddPoint(a1);
            if (TryProjectPointOnSegmentXY(wallA.line, b0, tolFt, out _)) AddPoint(b0);
            if (TryProjectPointOnSegmentXY(wallA.line, b1, tolFt, out _)) AddPoint(b1);

            if (isColinear)
            {
                if (TryProjectPointOnSegmentEndpointXY(wallB.line, a0, nodeTolFt, out _)) AddPoint(a0);
                if (TryProjectPointOnSegmentEndpointXY(wallB.line, a1, nodeTolFt, out _)) AddPoint(a1);
                if (TryProjectPointOnSegmentEndpointXY(wallA.line, b0, nodeTolFt, out _)) AddPoint(b0);
                if (TryProjectPointOnSegmentEndpointXY(wallA.line, b1, nodeTolFt, out _)) AddPoint(b1);
            }

            if (!isColinear)
            {
                if (TryProjectPointOnSegmentXY(wallB.line, a0, nodeTolFt, out _)) AddPoint(a0);
                if (TryProjectPointOnSegmentXY(wallB.line, a1, nodeTolFt, out _)) AddPoint(a1);
                if (TryProjectPointOnSegmentXY(wallA.line, b0, nodeTolFt, out _)) AddPoint(b0);
                if (TryProjectPointOnSegmentXY(wallA.line, b1, nodeTolFt, out _)) AddPoint(b1);
            }

            return points;
        }

        private static bool HasColinearConnection(WallInfo wall, WallInfo neighbor)
        {
            return wall.Connections.Any(c => c.Neighbor == neighbor && c.IsColinear);
        }

        private static void AddInferredColinearConnectionsThroughTNodes(List<WallInfo> walls)
        {
            double tol = LayoutGenerator.CornerThreshold;

            for (int i = 0; i < walls.Count; i++)
            {
                WallInfo wallA = walls[i];
                for (int j = i + 1; j < walls.Count; j++)
                {
                    WallInfo wallB = walls[j];
                    if (!AreColinearAxes(wallA, wallB) || HasColinearConnection(wallA, wallB))
                        continue;

                    double sameNodeTolerance = Math.Abs(wallA.Thickness - wallB.Thickness) / 2.0 + tol;
                    var aPerpendicularConnections = wallA.Connections
                        .Where(c => c.IsPerpendicular && IsWallEndpoint(wallA, c.PositionOnWall, tol))
                        .ToList();

                    foreach (var connA in aPerpendicularConnections)
                    {
                        var connB = wallB.Connections.FirstOrDefault(c =>
                            c.IsPerpendicular &&
                            c.Neighbor == connA.Neighbor &&
                            IsWallEndpoint(wallB, c.PositionOnWall, tol) &&
                            Math.Abs(c.PositionOnNeighbor - connA.PositionOnNeighbor) <= sameNodeTolerance);

                        if (connB == null)
                            continue;

                        XYZ nodePoint = connA.Neighbor.StartPoint +
                            connA.Neighbor.Direction.Normalize() *
                            (((connA.PositionOnNeighbor + connB.PositionOnNeighbor) / 2.0) / 304.8);

                        AddConnectionPair(wallA, wallB, nodePoint, true, false);
                        break;
                    }
                }
            }
        }

        // Обновлённый метод установки соединений между стенами
        private static void SetupWallConnections(List<WallInfo> walls)
        {
            // 1. Сброс исходных данных
            foreach (var w in walls)
            {
                if (w.LayoutKey == 0)
                    w.LayoutKey = w.Id.IntegerValue;
                w.LeftNeighbor = null;
                w.RightNeighbor = null;
                w.ConnectedWalls.Clear();
                w.Connections.Clear();
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

                    XYZ dirA = wallA.line.Direction.Normalize();
                    XYZ dirB = wallB.line.Direction.Normalize();
                    double absDot = Math.Abs(dirA.DotProduct(dirB));
                    bool isColinear = Math.Abs(absDot - 1.0) < colinearTolerance;
                    bool isPerpendicular = absDot < 0.15;

                    foreach (var pt in GetConnectionPoints(wallA, wallB, tolFt))
                    {
                        AddConnectionPair(wallA, wallB, pt, isColinear, isPerpendicular);

                        bool A0 = wallA.line.GetEndPoint(0).DistanceTo(pt) < tolFt;
                        bool A1 = wallA.line.GetEndPoint(1).DistanceTo(pt) < tolFt;
                        bool B0 = wallB.line.GetEndPoint(0).DistanceTo(pt) < tolFt;
                        bool B1 = wallB.line.GetEndPoint(1).DistanceTo(pt) < tolFt;

                        if (!isColinear)
                        {
                            if (A0 && wallA.LeftNeighbor == null) wallA.LeftNeighbor = wallB;
                            if (A1 && wallA.RightNeighbor == null) wallA.RightNeighbor = wallB;
                            if (B0 && wallB.LeftNeighbor == null) wallB.LeftNeighbor = wallA;
                            if (B1 && wallB.RightNeighbor == null) wallB.RightNeighbor = wallA;
                        }
                    }
                }
            }

            AddInferredColinearConnectionsThroughTNodes(walls);

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
