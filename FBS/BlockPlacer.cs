using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FerrumAddinDev.FBS
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

            FamilySymbol tagSym = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_StructuralFramingTags)
            .Cast<FamilySymbol>()
            .FirstOrDefault(fs => fs.Family.Name == "ADSK_Марка_Балка" && fs.Name == "Экземпляр_ADSK_Позиция");
            //02.12.25 - измененный профиль стены + вынос на листы
            ElementId titleBlockTypeId = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>().Where(x => x.FamilyName == "ADSK_ОсновнаяНадпись")
            .Select(s => s.Id)
            .FirstOrDefault();

            View viewTemplate = new FilteredElementCollector(doc).OfClass(typeof(View)).
                    Cast<View>().Where(v => v.IsTemplate && v.Name.Equals("4_К_ФБС_развертки")).FirstOrDefault();

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
                List<Element> blocks = new List<Element>();
                // 1) Создать разрезы по каждо́й стене, в которой есть блоки
                CreateSectionViewsForVariant(variant, doc, sectionType, allGrids, existingNames, viewTemplate);
                //02.12.25 - измененный профиль стены + вынос на листы
                PlaceSectionsOnSheets(variant, doc, titleBlockTypeId);

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
                    // 22.10.25 - Исправления ФБС (нет уровня + имя)
                    double l = doc.GetElement(inst.LevelId) == null ? doc.GetElement(block.Wall.baseLevel).LookupParameter("ZH_Этаж_Числовой").AsDouble() :
                        doc.GetElement(inst.LevelId).LookupParameter("ZH_Этаж_Числовой").AsDouble();
                    inst.LookupParameter("ZH_Этаж_Числовой").Set(l);
                    if (familyName != "Кирпичная заделка (керамический кирпич)")
                        blocks.Add(inst);
                    block.PlacedElementId = inst.Id;
                    //04.08.25 - базовый уровень в перемычках
                    inst.LookupParameter("Базовый уровень").Set(block.Wall.baseLevel);

                    if (block.IsGapFill)
                    {
                        double lenFt = block.Length / 304.8;
                        double thkFt = block.Wall.Thickness / 304.8;
                        double heightFt = 600 / 304.8;
                        if ((block.Row == 1 && block.Wall.first300) || (block.Row == block.Wall.coordZList.Count() && block.Wall.last300))
                            heightFt = 300 / 304.8;
                        inst.LookupParameter("Б")?.Set(thkFt);
                        inst.LookupParameter("А")?.Set(lenFt);
                        inst.LookupParameter("С")?.Set(heightFt);
                        //27.11.25 - Пропуск параметра ADSK_Группирование в заделках при отсутствии
                        try
                        {
                            inst.LookupParameter("ADSK_Группирование").Set("ФБСм");

                            inst.LookupParameter("Вырезы").Set(0);
                        }
                        catch
                        {

                        }
                        XYZ xAxis = XYZ.BasisX;
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
                        //inst.LookupParameter("ADSK_Позиция").Set(Math.Round(block.Length / 100.0).ToString());
                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            tagSym.Id,
                            views[block.Wall.Id.IntegerValue],
                            new Reference(inst),
                            false,
                            TagOrientation.Horizontal,
                            (inst.Location as LocationPoint).Point + 0.2 * XYZ.BasisZ);
                    }
                }
                var listBlocks = blocks.GroupBy(b => b.Name).OrderBy(g =>
                {
                    var m = Regex.Match(g.Key, @"ФБС(\d+)\.(\d+)\.(\d+)");
                    int a = int.Parse(m.Groups[1].Value);
                    int b = int.Parse(m.Groups[2].Value);
                    int c = int.Parse(m.Groups[3].Value);
                    return (a, b, c);
                }).ToList();
                int i = 1;
                foreach (var block in listBlocks)
                {
                    foreach (var b in block)
                    {
                        b.LookupParameter("ADSK_Позиция").Set(i.ToString());
                    }
                    i++;
                }
                tx.Commit();
            }

            variant.IsPlaced = true;
        }

        // 11.12.25 - Заголовки к разрезам ФБС
        /// <summary>
        /// Смещает заголовок вьюпорта над рамкой вида на листе.
        /// Работает для любых размеров вьюпорта. Запас по высоте ~8 мм.
        /// </summary>
        private static void PlaceTitleAbove(Viewport vp)
        {
            if (vp == null) return;

            // Размер рамки вьюпорта на листе
            Outline box = vp.GetBoxOutline();
            double height = box.MaximumPoint.Y - box.MinimumPoint.Y;
            double width = box.MaximumPoint.X - box.MinimumPoint.X;

            // Небольшой запас вверх (в мм → футы)
            double pad = UnitUtils.ConvertToInternalUnits(8, UnitTypeId.Millimeters);

            // Текущий оффсет заголовка; смещаем по Y выше рамки
            XYZ cur = vp.LabelOffset; // координаты листа: +Y вверх
            vp.LabelOffset = new XYZ(cur.X + width / 2, Math.Abs(cur.Y) + height + pad, 0);
        }

        public static Dictionary<int, ElementId> views = new Dictionary<int, ElementId>();
        //02.12.25 - измененный профиль стены + вынос на листы
        private static void PlaceSectionsOnSheets(LayoutVariant variant, Document doc, ElementId titleBlockTypeId)
        {
            ElementType vpTypeTitle = new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Where(x => x.Name.Equals("Заголовок на листе", StringComparison.OrdinalIgnoreCase))
                .Cast<ElementType>()
                .FirstOrDefault(x => x.FamilyName.Equals("Видовой экран"));

            // Отступы от рамки 
            double MARGIN = 0.07;  // поля по периметру
            double GAP = 0.03;     // расстояние между видами по X/Y

            // Рабочая область листа
            double freeLeft = 0, freeRight = 0, freeTop = 0, freeBot = 0;
            double cursorX = 0, cursorY = 0, rowHeight = 0.0;

            ViewSheet currentSheet = InitSheet();

            // Все виды для варианта
            var viewsToPlace = variant.Blocks
                .Select(b => b.Wall)
                .Distinct()
                .Select(w => doc.GetElement(views[w.Id.IntegerValue]) as View)
                .Where(v => v != null)
                .OrderBy(v => v.Name)
                .ToList();

            foreach (var view in viewsToPlace)
            {
            retry:
                // Временный вьюпорт в центре рабочей области
                XYZ tempPoint = new XYZ((freeLeft + freeRight) / 2.0, (freeTop + freeBot) / 2.0, 0);
                if (!Viewport.CanAddViewToSheet(doc, currentSheet.Id, view.Id))
                    continue;

                Viewport vp = Viewport.Create(doc, currentSheet.Id, view.Id, tempPoint);
                doc.Regenerate();

                // Назначаем тип с заголовком
                if (vpTypeTitle != null)
                    vp.ChangeTypeId(vpTypeTitle.Id);

                // Включаем заголовок
                Parameter pShow = vp.get_Parameter(BuiltInParameter.VIEWPORT_ATTR_SHOW_LABEL);
                if (pShow != null)
                    pShow.Set(1);

                // Поднимаем заголовок над рамкой
                PlaceTitleAbove(vp);
                doc.Regenerate();

                // --- объединённый bounding-box "рамка + заголовок" ---
                Outline boxView = vp.GetBoxOutline();
                Outline boxLabel = vp.GetLabelOutline(); // может вернуть null, но обычно нет

                double minX = boxView.MinimumPoint.X;
                double maxX = boxView.MaximumPoint.X;
                double minY = boxView.MinimumPoint.Y;
                double maxY = boxView.MaximumPoint.Y;

                if (boxLabel != null)
                {
                    minX = Math.Min(minX, boxLabel.MinimumPoint.X);
                    maxX = Math.Max(maxX, boxLabel.MaximumPoint.X);
                    minY = Math.Min(minY, boxLabel.MinimumPoint.Y);
                    maxY = Math.Max(maxY, boxLabel.MaximumPoint.Y);
                }

                double width = maxX - minX;      // полная ширина (вид + заголовок)
                double height = maxY - minY;     // полная высота (включая заголовок)

                XYZ center0 = vp.GetBoxCenter();
                double leftOffset = minX - center0.X;
                double topOffset = maxY - center0.Y;

                // Перенос на новую строку, если не влезаем по ширине
                if (cursorX + width > freeRight)
                {
                    cursorX = freeLeft;
                    cursorY -= rowHeight + GAP;
                    rowHeight = 0.0;
                }

                // Если по высоте (с учётом заголовка) не влезаем — новый лист
                if (cursorY - height < freeBot)
                {
                    doc.Delete(vp.Id);
                    currentSheet = InitSheet();
                    goto retry;
                }

                // Центр рамки вида так, чтобы верх объединённой рамки совпал с cursorY,
                // а левая граница с cursorX
                double cx = cursorX - leftOffset;
                double cy = cursorY - topOffset;
                vp.SetBoxCenter(new XYZ(cx, cy, 0));

                // Обновляем курсор и высоту строки (по полной высоте)
                cursorX += width + GAP;
                rowHeight = Math.Max(rowHeight, height);
            }


            // --- локальная функция инициализации листа ---
            ViewSheet InitSheet()
            {
                ViewSheet sheet = ViewSheet.Create(doc, titleBlockTypeId);
                FamilyInstance tbInst = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .FirstOrDefault(fi => fi.OwnerViewId == sheet.Id);

                doc.Regenerate();

                BoundingBoxXYZ tbBb = tbInst.get_BoundingBox(sheet);
                XYZ tbMin = tbBb.Min;
                XYZ tbMax = tbBb.Max;

                freeLeft = tbMin.X + MARGIN;
                freeRight = tbMax.X - MARGIN;
                freeTop = tbMax.Y - MARGIN;
                freeBot = tbMin.Y + MARGIN;

                cursorX = freeLeft;
                cursorY = freeTop;
                rowHeight = 0.0;

                return sheet;
            }
        }


        private static (double width, double height) EstimateViewportSize(View view)
        {
            BoundingBoxXYZ crop = view.CropBox;
            if (crop == null)
                return (1.0, 1.0);

            double width = (crop.Max.X - crop.Min.X) / view.Scale;
            double height = (crop.Max.Y - crop.Min.Y) / view.Scale;

            double reserve = 0.3; // небольшой запас под надписи
            return (width + reserve, height + reserve);
        }

        // В CreateSectionViewsForVariant передаём wallsInVariant в GenerateSectionName
        private static void CreateSectionViewsForVariant(
    LayoutVariant variant,
    Document doc,
    ViewFamilyType sectionType,
    List<Grid> allGrids,
    HashSet<string> existingNames,
    View viewTemplate)
        {
            var wallsInVariant = variant.Blocks.Select(b => b.Wall.Id.Value).Distinct();
            foreach (var wallId in wallsInVariant)
            {
                var wall = doc.GetElement(new ElementId(wallId));
                int i = 1;
                string name = GenerateSectionName(wall, variant.Blocks.Select(b => b.Wall).ToList(), allGrids, existingNames);
                // 08.07.25 - добавление цифр к разрезам тк при одинаковых именах вылетает
                if (!existingNames.Contains(name))
                {
                    existingNames.Add(name);
                }
                else
                {
                // 12.11.25 - Исправления ФБС
                again:
                    if (i != 1)
                    {
                        name = name.Remove(name.Length - 3, 3);
                        name += "(" + i + ")";
                        if (existingNames.Contains(name))
                        {
                            i++;
                            goto again;
                        }
                        existingNames.Add(name);
                        i++;
                    }
                    else
                    {
                        name += " (" + i + ")";
                        if (existingNames.Contains(name))
                        {
                            i++;
                            goto again;
                        }
                        existingNames.Add(name);
                        i++;
                    }
                }

                BoundingBoxXYZ box = GetSectionBox(wall);
                // box теперь валидный
                ViewSection vs = ViewSection.CreateSection(doc, sectionType.Id, box);

                vs.ViewTemplateId = viewTemplate.Id;
                vs.Name = name;
                views.Add(wall.Id.IntegerValue, vs.Id);
            }
        }

        private static BoundingBoxXYZ GetSectionBox(Element wall)
        {
            Line line = (wall.Location as LocationCurve).Curve as Line;
            // Концы стены в футах
            XYZ p0 = line.GetEndPoint(0);
            XYZ p1 = line.GetEndPoint(1);
            // Центр
            XYZ midXY = (p0 + p1) / 2.0;
            // Высоты
            double topZ;
            Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (heightParam != null && heightParam.HasValue)
            {
                topZ = heightParam.AsDouble();
            }
            else
            {
                topZ = (wall.get_BoundingBox(null).Max.Z - wall.get_BoundingBox(null).Min.Z);
            }

            // Запас в футах
            double halfLength = (p1 - p0).GetLength() / 2.0 + 0.5;
            double halfDepth = 0.7;

            XYZ upDirection = XYZ.BasisZ;
            XYZ crossDirection = line.Direction.CrossProduct(upDirection);

            Transform t = Transform.Identity;
            t.Origin = midXY;
            t.BasisX = line.Direction;
            t.BasisY = upDirection;
            t.BasisZ = crossDirection;

            XYZ min = null;
            XYZ max = null; 

            if (line.Direction.Y == 0)
            {
                min = new XYZ(-halfLength-0.5, - 2.0, 0);
                max = new XYZ(halfLength, topZ + 1.0, halfDepth);
            }
            else
            {
                min = new XYZ(-halfLength-0.5, - 2.0, 0);
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
            Element wall,
            IEnumerable<WallInfo> wallsInVariant,
            List<Grid> grids,
            HashSet<string> existingNames)
        {
            double tol_ft = 5.0 / 304.8;
            Line wLine = (wall.Location as LocationCurve).Curve as Line;
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
