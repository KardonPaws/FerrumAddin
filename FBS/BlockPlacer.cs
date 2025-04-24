using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FerrumAddin.FBS
{
    public static class BlockPlacer
    {
        public static void PlaceVariant(LayoutVariant variant, Document doc)
        {
            // Найти тип ViewFamilyType для Section
            ViewFamilyType sectionType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .First(vf => vf.ViewFamily == ViewFamily.Section);

            // Получить все оси (Grid) в модели
            List<Grid> allGrids = new FilteredElementCollector(doc)
                .OfClass(typeof(Grid))
                .Cast<Grid>()
                .ToList();

            // Список уже существующих имён разрезов, чтобы не дублировать
            HashSet<string> existingNames = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSection))
                .Cast<ViewSection>()
                .Select(vs => vs.Name)
                .ToHashSet();

            using (Transaction tx = new Transaction(doc, "Размещение блоков ФБС"))
            {
                tx.Start();

                // 1) Создать разрезы по каждо́й стене, в которой есть блоки
                CreateSectionViewsForVariant(variant, doc, sectionType, allGrids, existingNames);

                // 2) Активировать семейства и размещать блоки
                Dictionary<string, FamilySymbol> symbolCache = new Dictionary<string, FamilySymbol>();
                foreach (BlockPlacement block in variant.Blocks)
                {
                    string familyName;
                    
                    if (block.IsGapFill)
                    {
                        familyName = "Кирпичная заделка (керамический кирпич)";
                    }
                    else
                    {
                        int heightDecimeters = 6;
                        if ((block.Row == 1 && block.Wall.first300) || (block.Row == block.Wall.coordZList.Count() && block.Wall.last300))
                            heightDecimeters = 3;
                        int lengthDecimeters = (int)Math.Round(block.Length / 100.0);
                        int thicknessDecimeters = (int)Math.Round(block.Wall.Thickness / 100.0);
                        familyName = $"ФБС{lengthDecimeters}.{thicknessDecimeters}.{heightDecimeters}";
                    }

                    if (!symbolCache.TryGetValue(familyName, out FamilySymbol symbol))
                    {
                        symbol = new FilteredElementCollector(doc)
                                    .OfClass(typeof(FamilySymbol))
                                    .FirstOrDefault(e => e.Name == familyName) as FamilySymbol;
                        if (symbol == null) continue;
                        if (!symbol.IsActive) symbol.Activate();
                        symbolCache[familyName] = symbol;
                    }

                    double centerDistMm = (block.Start + block.End) / 2.0;
                    double centerDistFt = centerDistMm / 304.8;
                    XYZ wallStart = block.Wall.StartPoint;
                    XYZ wallDir = block.Wall.Direction;
                    XYZ pt = wallStart + wallDir * centerDistFt;

                    double firstRowZ = block.Wall.first300 && block.Row != 1 ? -300 / 304.8 : 0;

                    double zOff = (block.Row - 1) * (600 / 304.8);
                    pt = new XYZ(pt.X, pt.Y, block.Wall.BaseElevation + zOff + firstRowZ);

                    FamilyInstance inst = doc.Create.NewFamilyInstance(pt, symbol, StructuralType.NonStructural);                      
                    block.PlacedElementId = inst.Id;

                    if (block.IsGapFill)
                    {
                        double lenFt = block.Length / 304.8;
                        double thkFt = block.Wall.Thickness / 304.8;
                        double heightFt = 600 / 304.8;
                        if ((block.Row == 1 && block.Wall.first300) || (block.Row == block.Wall.coordZList.Count() && block.Wall.last300))
                            heightFt = 300 / 304.8;
                        inst.LookupParameter("Б")?.Set(lenFt);
                        inst.LookupParameter("А")?.Set(thkFt);
                        inst.LookupParameter("С")?.Set(heightFt);
                        inst.LookupParameter("ADSK_Группирование").Set("ФБСм");

                        XYZ xAxis = XYZ.BasisY;
                        double dot = wallDir.Normalize().DotProduct(xAxis);
                        double ang = Math.Acos(Math.Max(-1, Math.Min(1, dot)));
                        if (xAxis.CrossProduct(wallDir.Normalize()).Z < 0) ang = -ang;
                        Line axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                        ElementTransformUtils.RotateElement(doc, inst.Id, axis, ang);
                    }
                    else
                    {
                        XYZ xAxis = XYZ.BasisX;
                        double dot = wallDir.Normalize().DotProduct(xAxis);
                        double ang = Math.Acos(Math.Max(-1, Math.Min(1, dot)));
                        if (xAxis.CrossProduct(wallDir.Normalize()).Z < 0) ang = -ang;
                        Line axis = Line.CreateBound(pt, pt + XYZ.BasisZ);
                        ElementTransformUtils.RotateElement(doc, inst.Id, axis, ang);
                        inst.LookupParameter("ADSK_Группирование").Set("ФБС");
                    }
                }

                tx.Commit();
            }

            variant.IsPlaced = true;
        }

        // В CreateSectionViewsForVariant передаём wallsInVariant в GenerateSectionName
        private static void CreateSectionViewsForVariant(
    LayoutVariant variant,
    Document doc,
    ViewFamilyType sectionType,
    List<Grid> allGrids,
    HashSet<string> existingNames)
        {
            var wallsInVariant = variant.Blocks.Select(b => b.Wall).Distinct();
            foreach (var wall in wallsInVariant)
            {
                string name = GenerateSectionName(wall, wallsInVariant.ToList(), allGrids, existingNames);
                existingNames.Add(name);

                BoundingBoxXYZ box = GetSectionBox(wall);
                // box теперь валидный
                ViewSection vs = ViewSection.CreateSection(doc, sectionType.Id, box);
                vs.Name = name;
            }
        }

        private static BoundingBoxXYZ GetSectionBox(WallInfo wall)
        {
            // Концы стены в футах
            XYZ p0 = wall.line.GetEndPoint(0);
            XYZ p1 = wall.line.GetEndPoint(1);
            // Центр
            XYZ midXY = (p0 + p1) / 2.0;
            // Высоты
            double topZ = (wall.Height / 304.8);

            // Запас в футах
            double halfLength = (p1 - p0).GetLength() / 2.0 + 0.5;
            double halfDepth = 0.7;

            XYZ upDirection = XYZ.BasisZ;
            XYZ crossDirection = wall.Direction.CrossProduct(upDirection);

            Transform t = Transform.Identity;
            t.Origin = midXY;
            t.BasisX = wall.Direction;
            t.BasisY = upDirection;
            t.BasisZ = crossDirection;

            XYZ min = null;
            XYZ max = null; 

            if (wall.Direction.Y == 0)
            {
                min = new XYZ(-halfLength-0.5, - 1.0, 0);
                max = new XYZ(halfLength, topZ + 1.0, halfDepth);
            }
            else
            {
                min = new XYZ(-halfLength-0.5, - 1.0, 0);
                max = new XYZ(halfLength, topZ + 1.0, halfDepth);
            }

            var box = new BoundingBoxXYZ
            {
                Min = min,
                Max = max,
                Transform = t
            };
            return box;
        }
        private static Line GetHorizontalLine(Line orig)
        {
            XYZ a = orig.GetEndPoint(0);
            XYZ b = orig.GetEndPoint(1);
            return Line.CreateBound(
                new XYZ(a.X, a.Y, 0),
                new XYZ(b.X, b.Y, 0)
            );
        }

        private static string GenerateSectionName(
            WallInfo wall,
            IEnumerable<WallInfo> wallsInVariant,
            List<Grid> grids,
            HashSet<string> existingNames)
        {
            double tol_ft = 5.0 / 304.8;
            Line wLine = wall.line;
            XYZ wDir = (wLine.GetEndPoint(1) - wLine.GetEndPoint(0)).Normalize();

            // все оси параллельные стене
            var parallel = grids.Where(g =>
            {
                var gl = (g.Curve as Line);
                var gd = (gl.GetEndPoint(1) - gl.GetEndPoint(0)).Normalize();
                return Math.Abs(Math.Abs(gd.DotProduct(wDir)) - 1) < 1e-3;
            }).ToList();

            // Проверяем, лежит ли стена на какой‑то оси
            var onAxis = parallel.Where(g =>
            {
                var gl = GetHorizontalLine(g.Curve as Line);
                var p0 = wLine.GetEndPoint(0);
                var p1 = wLine.GetEndPoint(1);
                Line wallLine = GetHorizontalLine(Line.CreateBound(p0, p1));
                SetComparisonResult r1 = gl.Intersect(wallLine);
                SetComparisonResult r2 = wallLine.Intersect(gl);
                return r1 == SetComparisonResult.Superset || r2 == SetComparisonResult.Superset || r1 == SetComparisonResult.Equal || r2 == SetComparisonResult.Equal;
            }).ToList();

            if (onAxis.Count == 1)
            {
                var g = onAxis[0];
                // Считаем, сколько стен этого варианта лежит на той же оси
                int count = wallsInVariant.Count(w =>
                {
                    var gl = GetHorizontalLine(g.Curve as Line);
                    var p0 = w.EndPoint;
                    var p1 = w.StartPoint;
                    Line wallLine = GetHorizontalLine(Line.CreateBound(p0, p1));
                    SetComparisonResult r1 = gl.Intersect(wallLine);
                    SetComparisonResult r2 = wallLine.Intersect(gl);
                    return r1 == SetComparisonResult.Superset || r2 == SetComparisonResult.Superset || r1 == SetComparisonResult.Equal || r2 == SetComparisonResult.Equal;
                });

                if (count == 1)
                {
                    return $"Развертка по оси {g.Name}";
                }

                // Ищем пересекающую перпендикулярную ось
                var perp = grids.Where(h =>
                {
                    var hl = (h.Curve as Line);
                    var hd = (hl.GetEndPoint(1) - hl.GetEndPoint(0)).Normalize();
                    return Math.Abs(wDir.DotProduct(hd)) < 1e-3;
                });
                var cross = perp.FirstOrDefault(h =>
                {
                    h.Curve.Intersect(g.Curve, out _);
                    return true;
                });
                if (cross != null)
                {
                    return $"Развертка по оси {g.Name}-{cross.Name}";
                }                
            }

            // Если стена не на оси – две ближайшие параллельные
            var mid = (wLine.GetEndPoint(0) + wLine.GetEndPoint(1)) / 2.0;
            var nearest2 = parallel
                .Select(g => {
                    var gl = (g.Curve as Line);
                    var pr = gl.Project(mid).XYZPoint;
                    return new { Grid = g, Dist = pr.DistanceTo(mid) };
                })
                .OrderBy(x => x.Dist)
                .Take(2)
                .Select(x => x.Grid)
                .ToList();

            if (nearest2.Count == 2)
            {
                string baseName = $"Развертка по оси {nearest2[0].Name}-{nearest2[1].Name}";
                if (!existingNames.Contains(baseName))
                    return baseName;
                int idx = 1;
                string nm;
                do
                {
                    nm = $"{baseName}_{idx++}";
                } while (existingNames.Contains(nm));
                return nm;
            }

            // Фолбэк
            int k = 1;
            string fb;
            do
            {
                fb = $"Развертка_{k++}";
            } while (existingNames.Contains(fb));
            return fb;
        }

    }
}
