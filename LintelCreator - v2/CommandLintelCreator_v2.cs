using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FerrumAddinDev.FM;
using FerrumAddinDev.LintelCreator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;
using View = Autodesk.Revit.DB.View;

namespace FerrumAddinDev.LintelCreator_v2
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class CommandLintelCreator_v2 : IExternalCommand
    {
        public static ExternalEvent lintelCreateEvent;
        public static ExternalEvent lintelNumerateEvent;
        public static ExternalEvent nestedElementsNumberingEvent;
        public static ExternalEvent createSectionsEvent;
        public static ExternalEvent tagLintelsEvent;
        public static ExternalEvent placeSectionsEvent;
        public static Queue<LintelRequest> PendingRequests = new Queue<LintelRequest>();

        public static Document doc;
        public static Selection sel;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            doc = commandData.Application.ActiveUIDocument.Document;
            sel = commandData.Application.ActiveUIDocument.Selection;

            lintelCreateEvent = ExternalEvent.Create(new LintelCreate());
            lintelNumerateEvent = ExternalEvent.Create(new LintelNumerate());
            nestedElementsNumberingEvent = ExternalEvent.Create(new NestedElementsNumbering());
            createSectionsEvent = ExternalEvent.Create(new CreateSectionsForLintels());
            tagLintelsEvent = ExternalEvent.Create(new TagLintels());
            placeSectionsEvent = ExternalEvent.Create(new PlaceSections());


            List<ElementId> windowsAndDoorsSelectionIds = sel.GetElementIds().ToList();
            List<Element> windowsAndDoorsList = new List<Element>();
            windowsAndDoorsList = GetWindowsAndDoorsFromCurrentSelection(doc, windowsAndDoorsSelectionIds);

            if (windowsAndDoorsList.Count == 0)
            {
                //26.01.26 - исправлена ширина проемов
                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().Where(x => (x as FamilyInstance).SuperComponent == null).Where(x => ((x as FamilyInstance).Host as Wall) != null).Where(x => ((x as FamilyInstance).Host as Wall)?.WallType.Kind != WallKind.Curtain));

                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType().Where(x => (x as FamilyInstance).SuperComponent == null).Where(x => ((x as FamilyInstance).Host as Wall) != null).Where(x => ((x as FamilyInstance).Host as Wall)?.WallType.Kind != WallKind.Curtain));
                // 29.06.25 - исключен тип 211.002
                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
                    .Where(x => x is Wall && (x as Wall).WallType != null && (x as Wall).WallType.Kind == WallKind.Curtain).Where(f => !f.Name.Contains("Лоджий")).Where(f =>
                    {
                        double code;
                        try
                        {
                            code = doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип_Число").AsDouble();
                            if (code == 0)
                            {
                                code = Convert.ToDouble(doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип").AsValueString());

                            }
                        }
                        catch
                        {
                            code = Convert.ToDouble(doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип").AsValueString());

                        }
                        return code != 211.002;
                    }).ToList());
            }
            GroupWindowsAndDoors(windowsAndDoorsList, doc, out var openingsWithoutLintel, out var openingsWithLintel);
            List<Family> lintelFamilysList = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory.Id.Value.Equals((int)BuiltInCategory.OST_StructuralFraming))
                .Where(f => f.GetFamilySymbolIds() != null)
                .Where(f => f.GetFamilySymbolIds().Count != 0)
                .Where(f => (doc.GetElement(f.GetFamilySymbolIds().First()) as FamilySymbol).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Перемычки составные")
                .OrderBy(f => f.Name, new AlphanumComparatorFastString())
                .ToList();

            if (lintelFamilysList.Count == 0)
            {
                message = "В проекте не найдены семейства перемычек! Загрузите семейства с наличием ''Перемычки составные'' в парметре типа ''Модель''.";
                return Result.Cancelled;
            }

            LintelCreatorForm_v2 form = new LintelCreatorForm_v2(doc, sel, openingsWithoutLintel, openingsWithLintel, lintelFamilysList);
            form.Show();

            return Result.Succeeded;
        }
        public static List<List<ParentElement>> RefreshWindow()
        {
            List<ElementId> windowsAndDoorsSelectionIds = sel.GetElementIds().ToList();
            List<Element> windowsAndDoorsList = new List<Element>();
            windowsAndDoorsList = GetWindowsAndDoorsFromCurrentSelection(doc, windowsAndDoorsSelectionIds);

            if (windowsAndDoorsList.Count == 0)
            {

                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().Where(x => (x as FamilyInstance).SuperComponent == null));

                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType().Where(x => (x as FamilyInstance).SuperComponent == null));

                // 29.06.25 - исключен тип 211.002
                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
                    .Where(x => x is Wall && (x as Wall).WallType != null && (x as Wall).WallType.Kind == WallKind.Curtain).Where(f =>
                    {
                        double code;
                        try
                        {
                            code = doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип_Число").AsDouble();
                            if (code == 0)
                            {
                                code = Convert.ToDouble(doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип").AsValueString());

                            }
                        }
                        catch
                        {
                            code = Convert.ToDouble(doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип").AsValueString());

                        }
                        return code != 211.002;
                    }).ToList());
            }
            GroupWindowsAndDoors(windowsAndDoorsList, doc, out var openingsWithoutLintel, out var openingsWithLintel);
            return new List<List<ParentElement>> { openingsWithoutLintel, openingsWithLintel };
        }
        private static void GroupWindowsAndDoors(List<Element> windowsAndDoorsList, Document doc, out List<ParentElement> openingsWithoutLintel, out List<ParentElement> openingsWithLintel)
        {
            // Разделяем на экземпляры семейств и витражи
            var windowsAndDoors = windowsAndDoorsList.OfType<FamilyInstance>().ToList();
            var curtains = windowsAndDoorsList.Except(windowsAndDoors).OfType<Wall>().ToList();
            // Получение всех стен для поиска хостов витражей
            // 12.09.25 - правильное размещение для витражей не по середине стены
            List<Element> allWalls = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .Where(x => x is Wall w && (doc.GetElement(x.GetTypeId()) as WallType).Kind != WallKind.Curtain)
                .ToList();

            // Подготавливаем список плит перекрытия для вычисления опор
            // 30.06.26 - перемычки в модели
            var floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToList()
                .Where(f =>
                {
                    // 14.08.25 - изменен поиск параметров, если есть параметр экз
                    double? code;
                    try
                    {
                        code = (doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип_Число")?.AsDouble());
                        if (code == 0)
                        {
                            code = Convert.ToDouble(doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип").AsValueString());

                        }
                    }
                    catch
                    {
                        code = f.LookupParameter("ZH_Код_Тип_Число")?.AsDouble();
                        if (code == 0)
                        {
                            code = Convert.ToDouble(f.LookupParameter("ZH_Код_Тип").AsValueString());

                        }
                    }
                    return (311 <= code && code < 312) || (317 <= code && code < 318);
                })
                .ToList();

            // Вычисляем SupportType и SupportDirection для каждого элемента (окон/дверей и витражей)
            var supportInfo = new Dictionary<Element, (int SupportType, XYZ Direction)>();
            foreach (var el in windowsAndDoors.Cast<Element>().Concat(curtains))
            {
                var bb = el.get_BoundingBox(null);
                if (bb == null)
                {
                    supportInfo[el] = (0, XYZ.Zero);
                    continue;
                }
                var min = bb.Min;
                var max = bb.Max;
                XYZ wallCenter;

                // 29.06.25 - у некоторых семейств нет точки вставки, в try
                var center = new XYZ(0, 0, 0);
                var orient = el is FamilyInstance fi ? fi.FacingOrientation : (((el as Wall).Location as LocationCurve).Curve as Line).Direction.CrossProduct(XYZ.BasisZ);
                try
                {
                    center = el is FamilyInstance ? (el.Location as LocationPoint).Point + (max.Z - min.Z) * XYZ.BasisZ : new XYZ((min.X + max.X) / 2, (min.Y + max.Y) / 2, max.Z);
                }
                catch
                {
                    continue;
                }
                // 24.09.25 - изменения в перемычках
                var leftPtC = el is FamilyInstance fi1 ? center - ((fi1.Host as Wall).Width / 2) * orient : center - (el as Wall).Width / 2 * orient;
                var rightPtC = el is FamilyInstance fi2 ? center + ((fi2.Host as Wall).Width / 2) * orient : center + (el as Wall).Width / 2 * orient;
                double threshold = 0;
                if (el is FamilyInstance inst)
                {
                    threshold = Convert.ToDouble(inst.LookupParameter("ADSK_Размер_Ширина")?.AsValueString() ?? "0") / 304.8;
                }
                else if (el is Wall wall)
                {
                    threshold = wall.LookupParameter("Длина").AsDouble();
                }

                bool leftSup = false, rightSup = false;
                foreach (var fl in floors)
                {
                    var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                    var geom = fl.get_Geometry(opts);
                    Solid solid = (Solid)geom
                        .OfType<GeometryInstance>()
                        .SelectMany(gi => gi.GetInstanceGeometry())
                        .OrderByDescending(s => (s as Solid).Volume)
                        .FirstOrDefault();
                    if (solid == null)
                    {
                        solid = (Solid)geom
                        .OfType<Solid>()
                        .OrderByDescending(s => (s as Solid).Volume)
                        .FirstOrDefault();
                    }
                    // 29.12.25 - не у всех семейств плит есть геометрия, проверка на объем солида
                    if (solid == null || solid.Volume == 0)
                        continue;

                    var fbb = solid.GetBoundingBox();
                    // 10.08.25 - изменения в перемычках
                    // 12.09.25 - правильное размещение для витражей не по середине стены
                    var idx = solid.ComputeCentroid();
                    fbb.Min += idx;
                    fbb.Max += idx;

                    // 10.08.25 - изменения в перемычках
                    var leftPtM = leftPtC - threshold / 2 * orient.CrossProduct(XYZ.BasisZ);
                    var leftPtP = leftPtC + threshold / 2 * orient.CrossProduct(XYZ.BasisZ);
                    var rightPtM = rightPtC - threshold / 2 * orient.CrossProduct(XYZ.BasisZ);
                    var rightPtP = rightPtC + threshold / 2 * orient.CrossProduct(XYZ.BasisZ);
                    // 24.09.25 - изменения в перемычках
                    if ((leftPtC.X > fbb.Min.X + 5e-6 && leftPtC.X < fbb.Max.X - 5e-6
                         && leftPtC.Y > fbb.Min.Y + 5e-6 && leftPtC.Y < fbb.Max.Y - 5e-6) ||
                         (leftPtM.X > fbb.Min.X + 5e-6 && leftPtM.X < fbb.Max.X - 5e-6
                         && leftPtM.Y > fbb.Min.Y + 5e-6 && leftPtM.Y < fbb.Max.Y - 5e-6) ||
                         (leftPtP.X > fbb.Min.X + 5e-6 && leftPtP.X < fbb.Max.X - 5e-6
                         && leftPtP.Y > fbb.Min.Y + 5e-6 && leftPtP.Y < fbb.Max.Y - 5e-6))
                    {
                        double dz = fbb.Min.Z + 0.0001 - leftPtC.Z;
                        if (dz >= 1e-6 && dz <= threshold) leftSup = true;
                    }

                    if ((rightPtC.X > fbb.Min.X + 5e-6 && rightPtC.X < fbb.Max.X - 5e-6
                     && rightPtC.Y > fbb.Min.Y + 5e-6 && rightPtC.Y < fbb.Max.Y - 5e-6) ||
                     (rightPtM.X > fbb.Min.X + 5e-6 && rightPtM.X < fbb.Max.X - 5e-6
                     && rightPtM.Y > fbb.Min.Y + 5e-6 && rightPtM.Y < fbb.Max.Y - 5e-6) ||
                     (rightPtP.X > fbb.Min.X + 5e-6 && rightPtP.X < fbb.Max.X - 5e-6
                     && rightPtP.Y > fbb.Min.Y + 5e-6 && rightPtP.Y < fbb.Max.Y - 5e-6))
                    {
                        double dz = fbb.Min.Z + 0.0001 - rightPtC.Z;
                        if (dz >= 1e-6 && dz <= threshold) rightSup = true;
                    }
                    if (leftSup && rightSup) break;
                }

                int supType = leftSup && rightSup ? 2 : (leftSup || rightSup ? 1 : 0);
                XYZ dir = XYZ.Zero;
                if (supType == 1)
                {
                    // 10.08.25 - изменения в перемычках
                    var supportPt = leftSup ? leftPtC : rightPtC;
                    dir = (supportPt - center).Normalize();
                }
                supportInfo[el] = (supType, dir);
            }

            //    // Группировка по имени, типу, ширине и SupportType для окон/дверей
            //    var windowGroups = windowsAndDoors
            //        .GroupBy(el => new
            //        {
            //            el.Symbol.FamilyName,
            //            el.Symbol.Name,
            //            Width = el.LookupParameter("ADSK_Размер_Ширина").AsValueString(),
            //            supportInfo[el].SupportType
            //        })
            //        .Select(g => new ParentElement
            //        {
            //            Name = g.Key.FamilyName,
            //            TypeName = g.Key.Name,
            //            Width = g.Key.Width,
            //            SupportType = g.Key.SupportType,
            //            Walls = g
            //                .Where(el => el.Host is Wall && (el.Host as Wall).WallType.Kind != WallKind.Curtain && !(el.Host as Wall).WallType.Name.Contains("_пгп_") && !(el.Host as Wall).WallType.Name.Contains("_ЛДЖ_"))
            //                .GroupBy(el => (el.Host as Wall)?.GetTypeId())
            //                .Where(wg => wg.Key != null)
            //                .ToDictionary(
            //                    wg => doc.GetElement(wg.Key) as WallType,
            //                    wg => wg.Cast<Element>().ToList()
            //                ),
            //            SupportDirection = g.ToDictionary(
            //                el => (Element)el,
            //                el => supportInfo[el].Direction
            //            )
            //        });

            //    List<Element> walls = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().Where(x => x is Wall && (doc.GetElement(x.GetTypeId()) as WallType).Kind != WallKind.Curtain).ToList();
            //    // Группировка витражей по имени, ширине и SupportType
            //    var curtainGroups = curtains
            //        .GroupBy(el => new
            //        {
            //            el.Name,
            //            Width = Math.Round(el.LookupParameter("Длина").AsDouble() * 304.8).ToString(),
            //            supportInfo[el].SupportType
            //        })
            //        .Select(g => new ParentElement
            //        {
            //            Name = g.Key.Name,
            //            TypeName = "Витраж",
            //            Width = g.Key.Width,
            //            SupportType = g.Key.SupportType,
            //            Walls = g
            //    .Select(el => new
            //    {
            //        Element = el,
            //        HostWall = walls
            //            .Cast<Wall>()
            //            .FirstOrDefault(wall =>
            //                wall.FindInserts(false, false, true, false)
            //                    .Any(insertId => insertId == el.Id)
            //            )
            //    })
            //    .Where(x => x.HostWall != null)
            //    .GroupBy(x => x.HostWall.GetTypeId())
            //    .Where(wg => wg.Key != null)
            //    .ToDictionary(
            //        wg => doc.GetElement(wg.Key) as WallType,
            //        wg => wg.Select(x => (Element)x.Element).ToList()
            //    ),
            //            // Словарь SupportDirection остаётся без изменений
            //            SupportDirection = g.ToDictionary(
            //    el => (Element)el,
            //    el => supportInfo[el].Direction
            //)
            //        });

            // Объединяем все группы и возвращаем итоговый список
            //var groupedElements = windowGroups.Concat(curtainGroups);

            //28.07.25 - объединение проемов в перемычках
            const double proximityThreshold = 380.0 / 304.8; // в футах

            // Общий список всех элементов с координатами
            var allOpenings = windowsAndDoors.Cast<Element>().Concat(curtains.Cast<Element>())
                .Select(el =>
                {
                    string name = el is FamilyInstance fi ? fi.Symbol.FamilyName : el.Name;
                    string typeName = el is FamilyInstance fi2 ? fi2.Symbol.Name : "Витраж";
                    // 26.01.26 - исправлена ширина проемов
                    string widthStr = el is FamilyInstance fi3 ?
                        fi3.LookupParameter("ADSK_Размер_Ширина")?.AsValueString() ?? "0" :
                        Math.Round((el.LookupParameter("Длина")?.AsDouble() ?? 0) * 304.8).ToString();

                    XYZ center;
                    double width;
                    XYZ dir;
                    // Для объединения по вертикали используем границы bounding box по Z
                    double zMin;
                    double zMax;

                    var bb0 = el.get_BoundingBox(null);
                    zMin = bb0 != null ? bb0.Min.Z : 0;
                    zMax = bb0 != null ? bb0.Max.Z : 0;

                    if (el is FamilyInstance inst)
                    {
                        center = (inst.Location as LocationPoint)?.Point ?? XYZ.Zero;
                        width = inst.LookupParameter("ADSK_Размер_Ширина")?.AsDouble() ?? 0;
                        dir = inst.HandOrientation;
                    }
                    else if (el is Wall wall)
                    {
                        // 19.09.25 - обновление определения центра
                        var hostWall = allWalls.Cast<Wall>()
                             .FirstOrDefault(w => w.FindInserts(false, false, true, false)
                                 .Any(id => id == el.Id));
                        // 22.10.25 - Изменения в перемычках (нет хоста)
                        if (hostWall == null)
                            return null;
                        var curve = (el.Location as LocationCurve)?.Curve as Line;
                        var p1 = curve.GetEndPoint(0);
                        var p2 = curve.GetEndPoint(1);
                        var wcenter = (p1 + p2) / 2;
                        var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                        var geom = hostWall.get_Geometry(opts);
                        Solid solid = (Solid)geom
                        .OrderByDescending(s => (s as Solid).Volume)
                        .FirstOrDefault();
                        var p = solid.ComputeCentroid();
                        p = p - p.Z * XYZ.BasisZ + curve.GetEndPoint(0).Z * XYZ.BasisZ;
                        center = Line.CreateUnbound(p - 100 * curve.Direction, curve.Direction).Project(wcenter).XYZPoint;
                        dir = curve.Direction;
                        width = curve.Length;
                        // Для витражей точка центра считается на высоте базовой линии,
                        // поэтому zMin/zMax берём из BoundingBox выше.
                    }
                    else return null;

                    return new
                    {
                        Element = el,
                        Name = name,
                        TypeName = typeName,
                        WidthStr = widthStr,
                        Width = width,
                        Center = center,
                        Direction = dir,
                        ZMin = zMin,
                        ZMax = zMax,
                        SupportType = supportInfo.TryGetValue(el, out var val) ? val.SupportType : 0,
                        SupDirection = supportInfo.TryGetValue(el, out var val2) ? val2.Direction : XYZ.Zero
                    };
                })
                .Where(x => x != null)
                .ToList();

            //23.01.26 - объединение проемов в перемычках вверх-вниз

            int n = allOpenings.Count;
            var dsu = new int[n];
            for (int i = 0; i < n; i++) dsu[i] = i;

            Func<int, int> find = null;
            find = (a) => dsu[a] == a ? a : (dsu[a] = find(dsu[a]));
            Action<int, int> unite = (a, b) =>
            {
                int ra = find(a);
                int rb = find(b);
                if (ra != rb) dsu[rb] = ra;
            };

            // Вспомогательные функции
            Func<double, double, double, double, bool> overlaps = (aMin, aMax, bMin, bMax) =>
            {
                double minA = Math.Min(aMin, aMax);
                double maxA = Math.Max(aMin, aMax);
                double minB = Math.Min(bMin, bMax);
                double maxB = Math.Max(bMin, bMax);
                return !(maxA < minB - 1e-6 || maxB < minA - 1e-6);
            };
            Func<double, double, double, double, double> gap = (aMin, aMax, bMin, bMax) =>
            {
                double minA = Math.Min(aMin, aMax);
                double maxA = Math.Max(aMin, aMax);
                double minB = Math.Min(bMin, bMax);
                double maxB = Math.Max(bMin, bMax);
                if (overlaps(minA, maxA, minB, maxB)) return 0.0;
                return Math.Max(0.0, Math.Max(minB - maxA, minA - maxB));
            };

            for (int i = 0; i < n; i++)
            {
                var a = allOpenings[i];

                //26.01.26 - исправлена ширина проемов
                // Направление проёма (U)
                var da = new XYZ(a.Direction.X, a.Direction.Y, 0);
                if (da.GetLength() < 1e-9) continue;
                da = da.Normalize();

                // Перпендикуляр в плане (W) — отличает разные стены/полосы
                var na = da.CrossProduct(XYZ.BasisZ); // (da.X, da.Y, 0) x (0,0,1) => (da.Y, -da.X, 0)
                if (na.GetLength() < 1e-9) continue;
                na = na.Normalize();

                // Концы проёма по ширине (вдоль U)
                var aP1 = a.Center - da * a.Width / 2;
                var aP2 = a.Center + da * a.Width / 2;

                // Диапазон по U
                double aUMin = Math.Min(aP1.DotProduct(da), aP2.DotProduct(da));
                double aUMax = Math.Max(aP1.DotProduct(da), aP2.DotProduct(da));

                // Диапазон по W (перпендикулярно стене в плане)
                double aW1 = aP1.DotProduct(na);
                double aW2 = aP2.DotProduct(na);
                double aWMin = Math.Min(aW1, aW2);
                double aWMax = Math.Max(aW1, aW2);

                // Диапазон по Z
                double aVMin = a.ZMin;
                double aVMax = a.ZMax;

                for (int j = i + 1; j < n; j++)
                {
                    var b = allOpenings[j];

                    var db = new XYZ(b.Direction.X, b.Direction.Y, 0);
                    if (db.GetLength() < 1e-9) continue;
                    db = db.Normalize();

                    // Объединяем только одинаковую ориентацию (параллельны в плане)
                    if (Math.Abs(da.DotProduct(db)) < 0.999) continue;

                    // Считаем b тоже в координатах (da, na), чтобы сравнивать в одном базисе
                    var bP1 = b.Center - db * b.Width / 2;
                    var bP2 = b.Center + db * b.Width / 2;

                    // U для b — именно по da, а не по db (важно при противоположном направлении)
                    double bUMin = Math.Min(bP1.DotProduct(da), bP2.DotProduct(da));
                    double bUMax = Math.Max(bP1.DotProduct(da), bP2.DotProduct(da));

                    // W для b — по na (т.е. “в какую стену попали”)
                    double bW1 = bP1.DotProduct(na);
                    double bW2 = bP2.DotProduct(na);
                    double bWMin = Math.Min(bW1, bW2);
                    double bWMax = Math.Max(bW1, bW2);

                    double bVMin = b.ZMin;
                    double bVMax = b.ZMax;

                    bool overlapU = overlaps(aUMin, aUMax, bUMin, bUMax);
                    bool overlapW = overlaps(aWMin, aWMax, bWMin, bWMax);
                    bool overlapV = overlaps(aVMin, aVMax, bVMin, bVMax);

                    double gapU = gap(aUMin, aUMax, bUMin, bUMax);
                    double gapW = gap(aWMin, aWMax, bWMin, bWMax);
                    double gapV = gap(aVMin, aVMax, bVMin, bVMax);

                    // Теперь объединяем только если близко/пересекается сразу по ТРЁМ осям:
                    // U (вдоль), W (между стенами/полосами), Z (по высоте).
                    bool nearU = overlapU || gapU <= proximityThreshold;
                    bool nearW = overlapW || gapW <= proximityThreshold;
                    bool nearV = overlapV || gapV <= proximityThreshold;

                    if (nearU && nearW && nearV)
                    {
                        unite(i, j);
                    }
                }
            }

            // Собираем компоненты связности в кластеры
            var clusters = new List<List<Element>>();
            var byRoot = new Dictionary<int, List<Element>>();
            for (int i = 0; i < n; i++)
            {
                int r = find(i);
                if (!byRoot.ContainsKey(r)) byRoot[r] = new List<Element>();
                byRoot[r].Add(allOpenings[i].Element);
            }
            clusters = byRoot.Values.ToList();

            // Группировка кластеров в ParentElement
            var groupedElements = new List<ParentElement>();
            var Clusters = new List<List<Element>>();

            foreach (var group in clusters)
            {
                var g = group.Distinct();
                Clusters.Add(g.ToList());
            }
            //26.01.26 - исправлена ширина проемов
            foreach (var group in Clusters)
            {
                var meta = group.Select(el =>
                {
                    string name = el is FamilyInstance fi ? fi.Symbol.FamilyName : el.Name;
                    string typeName = el is FamilyInstance fi2 ? fi2.Symbol.Name : "Витраж";
                    string widthStr = el is FamilyInstance fi3 ?
                        fi3.LookupParameter("ADSK_Размер_Ширина")?.AsValueString() ?? "0" :
                        Math.Round((el.LookupParameter("Длина")?.AsDouble() ?? 0) * 304.8).ToString();

                    var dir = supportInfo.TryGetValue(el, out var val) ? val.Direction : XYZ.Zero;
                    int supportType = supportInfo.TryGetValue(el, out var val2) ? val2.SupportType : 0;

                    return new { el, name, typeName, widthStr, supportType, dir };
                }).ToList();

                // Границы (ширина кластера считается по проекции в плане, а не по 3D-диагонали)
                // Общая ось кластера (U) в плане
                XYZ uDir = XYZ.BasisX;
                {
                    var firstFi = group.OfType<FamilyInstance>().FirstOrDefault();
                    if (firstFi != null)
                    {
                        uDir = new XYZ(firstFi.HandOrientation.X, firstFi.HandOrientation.Y, 0);
                    }
                    else
                    {
                        var firstWall = group.OfType<Wall>().FirstOrDefault();
                        if (firstWall != null)
                        {
                            var ln = ((firstWall.Location as LocationCurve)?.Curve as Line);
                            if (ln != null) uDir = new XYZ(ln.Direction.X, ln.Direction.Y, 0);
                        }
                    }

                    if (uDir.GetLength() > 1e-9) uDir = uDir.Normalize();
                }

                double uMin = double.PositiveInfinity;
                double uMax = double.NegativeInfinity;
                XYZ pMin = XYZ.Zero;
                XYZ pMax = XYZ.Zero;

                foreach (var el in group)
                {
                    XYZ center = XYZ.Zero;
                    XYZ dir = XYZ.Zero;
                    double width = 0.0;

                    // 16.12.25 - у некоторых семейств нет точки вставки, определение по BoundingBox
                    if (el is FamilyInstance fi)
                    {
                        try
                        {
                            center = (el.Location as LocationPoint).Point;
                        }
                        catch
                        {
                            var bb = el.get_BoundingBox(null);
                            center = (bb.Max + bb.Min) / 2;
                        }

                        width = el.LookupParameter("ADSK_Размер_Ширина")?.AsDouble() ?? 0.0;
                        dir = new XYZ(fi.HandOrientation.X, fi.HandOrientation.Y, 0);
                    }
                    else if (el is Wall w)
                    {
                        var curve = (el.Location as LocationCurve)?.Curve as Line;
                        if (curve == null) continue;

                        // Ширина витража берём по геометрической длине, без округлений
                        width = curve.Length;
                        dir = new XYZ(curve.Direction.X, curve.Direction.Y, 0);

                        // 12.09.25 - правильное размещение для витражей не по середине стены
                        var hostWall = allWalls.Cast<Wall>()
                            .FirstOrDefault(ww => ww.FindInserts(false, false, true, false)
                                .Any(id => id == el.Id));

                        if (hostWall != null)
                        {
                            var p1 = curve.GetEndPoint(0);
                            var p2 = curve.GetEndPoint(1);
                            var wcenter = (p1 + p2) / 2;

                            var opts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
                            var geom = hostWall.get_Geometry(opts);

                            Solid solid = geom
                                .OfType<Solid>()
                                .OrderByDescending(s => s.Volume)
                                .FirstOrDefault();

                            if (solid != null)
                            {
                                var p = solid.ComputeCentroid();
                                p = p - p.Z * XYZ.BasisZ + curve.GetEndPoint(0).Z * XYZ.BasisZ;
                                center = Line.CreateUnbound(p - 100 * curve.Direction, curve.Direction).Project(wcenter).XYZPoint;
                            }
                            else
                            {
                                center = wcenter;
                            }
                        }
                        else
                        {
                            // запасной вариант
                            var p1 = curve.GetEndPoint(0);
                            var p2 = curve.GetEndPoint(1);
                            center = (p1 + p2) / 2;
                        }
                    }
                    else
                    {
                        continue;
                    }

                    if (dir.GetLength() < 1e-9 || width <= 1e-9) continue;
                    dir = dir.Normalize();

                    // Концы проёма/витража по ширине (вдоль dir)
                    var e1 = center - dir * (width / 2.0);
                    var e2 = center + dir * (width / 2.0);

                    // Проекция на ось кластера (uDir в плане)
                    double u1 = e1.DotProduct(uDir);
                    double u2 = e2.DotProduct(uDir);

                    double lo = Math.Min(u1, u2);
                    double hi = Math.Max(u1, u2);

                    if (lo < uMin) { uMin = lo; pMin = (u1 <= u2) ? e1 : e2; }
                    if (hi > uMax) { uMax = hi; pMax = (u1 >= u2) ? e1 : e2; }
                }

                bool uValid = !(double.IsInfinity(uMin) || double.IsNaN(uMin) || double.IsInfinity(uMax) || double.IsNaN(uMax));
                double mergedWidth = uValid ? (uMax - uMin) : 0.0;
                List<XYZ> farPoints = new List<XYZ>() { pMin, pMax };

                var distinctNames = meta.Select(x => x.name).Distinct().OrderBy(s => s).ToList();

                var mergedName = string.Join(" + ", distinctNames);

                var mergedTypeNames = meta.Select(x => x.typeName).Distinct().OrderBy(s => s).ToList();
                var mergedTypeName = string.Join(" + ", mergedTypeNames);

                var mergedSupportType = meta.First().supportType;
                var mergedSupportDirection = meta.ToDictionary(x => x.el, x => x.dir);

                // Словарь стен по типам
                var wallsDict = new Dictionary<WallType, List<ElementsForLintel>>();
                WallType wallType = null;
                foreach (var el in group)
                {
                    Wall hostWall = null;
                    if (el is FamilyInstance fi && fi.Host is Wall wallHost)
                    {
                        hostWall = wallHost;
                    }
                    else if (el is Wall curWall)
                    {
                        hostWall = allWalls.Cast<Wall>()
                            .FirstOrDefault(w => w.FindInserts(false, false, true, false)
                                .Any(id => id == el.Id));
                    }

                    if (hostWall == null) continue;

                    var wtId = hostWall.GetTypeId();
                    if (wtId == null) continue;

                    wallType = doc.GetElement(wtId) as WallType;
                    if (wallType == null) continue;
                    break;
                }
                if (wallsDict.Keys.All(x => x.Id != wallType.Id))
                {
                    wallsDict.Add(wallType, new List<ElementsForLintel>() { new ElementsForLintel() { Elements = group, ElementsId = group.Select(g => g.Id).ToList(), Location = (farPoints[0] + farPoints[1]) / 2 } });
                }
                else
                {
                    wallsDict.Where(x => x.Key.Id == wallType.Id).First().Value.Add(new ElementsForLintel() { Elements = group, ElementsId = group.Select(g => g.Id).ToList(), Location = (farPoints[0] + farPoints[1]) / 2 });
                }
                var pEl = new ParentElement
                {
                    Name = mergedName,
                    TypeName = mergedTypeName,
                    Width = Math.Round(mergedWidth * 304.8).ToString(),
                    SupportType = mergedSupportType,
                    SupportDirection = mergedSupportDirection,
                    Walls = wallsDict,
                };
                var existing = groupedElements.Where(x => x.Name == pEl.Name && x.TypeName == pEl.TypeName && x.Width == pEl.Width && x.SupportType == pEl.SupportType).FirstOrDefault();
                if (existing != null)
                {
                    int ind = groupedElements.IndexOf(existing);
                    foreach (var key in pEl.Walls)
                    {
                        foreach (var KeyToAdd in pEl.Walls.Keys)
                        {
                            if (groupedElements[ind].Walls.Any(x => x.Key.Id == KeyToAdd.Id))
                            {
                                groupedElements[ind].Walls.Where(x => x.Key.Id == KeyToAdd.Id).First().Value.AddRange(pEl.Walls.Where(x => x.Key.Id == KeyToAdd.Id).First().Value);
                            }
                            else
                            {
                                groupedElements[ind].Walls.Add(wallType, pEl.Walls.Where(x => x.Key.Id == KeyToAdd.Id).First().Value);
                            }
                        }
                        foreach (var KeyToAdd in pEl.SupportDirection.Keys)
                        {
                            if (groupedElements[ind].SupportDirection.Any(x => x.Key.Id == KeyToAdd.Id))
                            {

                            }
                            else
                            {
                                groupedElements[ind].SupportDirection.Add(pEl.SupportDirection.Where(x => x.Key.Id == KeyToAdd.Id).First().Key, pEl.SupportDirection.Where(x => x.Key.Id == KeyToAdd.Id).First().Value);
                            }
                        }
                        groupedElements[ind].SupportDirection.Concat(pEl.SupportDirection);
                    }
                }
                else
                {
                    groupedElements.Add(pEl);
                }
            }

            openingsWithoutLintel = new List<ParentElement>();
            openingsWithLintel = new List<ParentElement>();

            foreach (var parent in groupedElements)
            {
                // создаём «пустые» оболочки, копируя метаданные, но не копируя Walls
                //28.07.25 - объединение проемов в перемычках
                var noLintelParent = new ParentElement
                {
                    Name = parent.Name,
                    TypeName = parent.TypeName,
                    Width = parent.Width,
                    Walls = new Dictionary<WallType, List<ElementsForLintel>>(),
                    ChildElements = parent.ChildElements,
                    SupportDirection = parent.SupportDirection,
                    SupportType = parent.SupportType
                };
                var withLintelParent = new ParentElement
                {
                    Name = parent.Name,
                    TypeName = parent.TypeName,
                    Width = parent.Width,
                    Walls = new Dictionary<WallType, List<ElementsForLintel>>(),
                    ChildElements = parent.ChildElements,
                    SupportDirection = parent.SupportDirection,
                    SupportType = parent.SupportType
                };

                foreach (var kv in parent.Walls)
                {
                    var wallType = kv.Key;
                    var elems = kv.Value;
                    if (wallType.Name.ToLower().Contains("_пгп_"))
                    {
                        continue;
                    }
                    if (wallType.Name.Contains("ПРГ"))
                    {
                        withLintelParent.SupportType = 0;
                        noLintelParent.SupportType = 0;
                    }
                    // разделить элементы на два списка по наличию перемычки
                    var without = elems.Where(el => !ElementHasLintel(el, doc)).ToList();
                    var with = elems.Where(el => ElementHasLintel(el, doc)).ToList();

                    if (without.Any())
                        noLintelParent.Walls.Add(wallType, without);
                    if (with.Any())
                        withLintelParent.Walls.Add(wallType, with);
                }

                // Добавляем непустые группы в итоговые списки
                if (noLintelParent.Walls.Any())
                    openingsWithoutLintel.Add(noLintelParent);
                if (withLintelParent.Walls.Any())
                    openingsWithLintel.Add(withLintelParent);
            }

            openingsWithoutLintel = openingsWithoutLintel
                .OrderBy(p => p.Name)
                .ThenBy(p => p.TypeName)
                .ThenBy(p => p.Width)
                .ThenBy(p => p.SupportType)
                .ToList();
            openingsWithLintel = openingsWithLintel
                .OrderBy(p => p.Name)
                .ThenBy(p => p.TypeName)
                .ThenBy(p => p.Width)
                .ThenBy(p => p.SupportType)
                .ToList();
        }

        private static bool ElementHasLintel(ElementsForLintel customEl, Document doc)
        {
            Element element = customEl.Elements.FirstOrDefault();
            // 16.12.25 - у некоторых семейств нет точки вставки, определение по BoundingBox

            XYZ center = new XYZ(0, 0, 0);
            double height = 0;

            try
            {
                center = element is FamilyInstance ? (element.Location as LocationPoint).Point :
                    (((element.Location as LocationCurve).Curve as Line).GetEndPoint(0) + ((element.Location as LocationCurve).Curve as Line).GetEndPoint(1)) / 2;
                height = element is FamilyInstance ? element.LookupParameter("ADSK_Размер_Высота").AsDouble() :
                element.LookupParameter("Неприсоединенная высота").AsDouble();
            }
            catch
            {
                FamilyInstance fi = element as FamilyInstance;
                var bb = element.get_BoundingBox(null);
                center = ((bb.Max + bb.Min) / 2).DotProduct(fi.HandOrientation) * fi.HandOrientation +
                bb.Min.DotProduct(XYZ.BasisZ) * XYZ.BasisZ +
                ((bb.Max + bb.Min) / 2).DotProduct(fi.FacingOrientation) * fi.FacingOrientation;
                height = (bb.Max - bb.Max).DotProduct(XYZ.BasisZ);
            }

            // создаём область немного ниже макс.Z и немного выше на 100 мм
            double mm = 1.0 / 304.8;
            XYZ searchMin = new XYZ(center.X - 25 * mm, center.Y - 25 * mm, center.Z + height - 50 * mm);
            XYZ searchMax = new XYZ(center.X + 25 * mm, center.Y + 25 * mm, center.Z + height + 10 * mm);

            var outline = new Outline(searchMin, searchMax);
            var filter = new BoundingBoxIntersectsFilter(outline);

            var found = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WherePasses(filter)
                .Where(x => (x as FamilyInstance).Symbol.FamilyName.Contains("_Перемычки"));

            // параметр "ADSK_Группирование" == "ПР" обозначает уже созданную перемычку
            return found
                .OfType<Element>()
                .Any(e => e.LookupParameter("ADSK_Группирование")?.AsString() == "ПР");
        }

        // Вспомогательный компаратор для удаления дубликатов по ElementId
        public class ElementIdEqualityComparer : IEqualityComparer<Element>
        {
            public static readonly ElementIdEqualityComparer Instance = new ElementIdEqualityComparer();
            public bool Equals(Element x, Element y) => x.Id == y.Id;
            public int GetHashCode(Element obj) => obj.Id.GetHashCode();
        }

        private static List<Element> GetWindowsAndDoorsFromCurrentSelection(Document doc, List<ElementId> selIds)
        {
            List<Element> tempLintelsList = new List<Element>();
            foreach (ElementId lintelId in selIds)
            {
                Element el = doc.GetElement(lintelId);
                if (el is FamilyInstance
                    && null != el.Category
                    && el.Category.Id.Value.Equals((int)BuiltInCategory.OST_Windows)
                    || el.Category.Id.Value.Equals((int)BuiltInCategory.OST_Doors))
                {
                    tempLintelsList.Add(el);
                }
                if (doc.GetElement(lintelId) is Wall
                    && (el as Wall).WallType.Kind == WallKind.Curtain)
                {
                    tempLintelsList.Add(el);
                }
            }
            return tempLintelsList;
        }
    }

    public class PlaceSections : IExternalEventHandler
    {
        public void Execute(UIApplication uiApp)
        {
            Document doc = uiApp.ActiveUIDocument.Document;

            ViewSheet activeSheet = doc.ActiveView as ViewSheet;
            if (activeSheet == null)
            {
                MessageBox.Show("Активный вид не является листом.", "Ошибка");
                return;
            }

            using (Transaction trans = new Transaction(doc, "Размещение разрезов"))
            {
                trans.Start();

                // Получение всех ScheduleSheetInstance на активном листе
                var scheduleInstances = new FilteredElementCollector(doc, activeSheet.Id)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .ToList();

                // Группировка ScheduleSheetInstance по имени ведомости
                var scheduleGroups = scheduleInstances
                    .GroupBy(s => doc.GetElement(s.ScheduleId).Name)
                    .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SegmentIndex).ToList());

                // Получение всех разрезов из документа
                var sections = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .Cast<ViewSection>()
                    .ToList();

                // Фильтрация разрезов по именам ("выше 0" или "ниже 0")
                var sectionsAbove = sections.Where(s => s.Name.Contains("выше 0")).ToList();
                var sectionsBelow = sections.Where(s => s.Name.Contains("ниже 0")).ToList();

                // Размещение разрезов на листе
                placeSections(doc, sectionsAbove, scheduleGroups, "Ведомость_Пр_выше 0,00");
                placeSections(doc, sectionsBelow, scheduleGroups, "Ведомость_Пр_ниже 0,00");

                trans.Commit();
            }
        }

        private void placeSections(Document doc, List<ViewSection> sections,
        Dictionary<string, List<ScheduleSheetInstance>> scheduleGroups, string scheduleName)
        {
            if (!scheduleGroups.ContainsKey(scheduleName)) return;
            ElementId elId = new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Where(x => (x as ElementType).FamilyName == "Видовой экран")
                .Where(x => x.Name == "Без названия")
                .First().Id;

            var scheduleInstances = scheduleGroups[scheduleName];
            int sectionIndex = 0;

            // Использовать только первую ScheduleSheetInstance для размещения
            if (scheduleInstances.Count > 0)
            {
                var scheduleInstance = scheduleInstances.First();
                XYZ basePoint = scheduleInstance.Point;
                double yOffset = 0;

                foreach (var section in sections)
                {
                    // Разместить разрез на листе
                    Viewport view = Viewport.Create(doc, doc.ActiveView.Id, section.Id, new XYZ(basePoint.X + 0.16, basePoint.Y - 0.15 - yOffset, basePoint.Z));
                    view.ChangeTypeId(elId);
                    yOffset += 0.166; // Смещение для следующего разреза
                }
            }
        }

        public string GetName()
        {
            return "Размещение разрезов";
        }
    }

    public class LintelNumerate : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            bool check = LintelCreatorForm_v2.check;
            using (Transaction trans = new Transaction(doc, "Нумерация элементов"))
            {
                trans.Start();

                try
                {
                    // Сбор всех элементов категории OST_StructuralFraming
                    var framingElements = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(f => (doc.GetElement(f.Symbol.Id)).LookupParameter("Ключевая пометка").AsString() == "ПР")
                    .OrderBy(f => f.Symbol.Name) // Сортировка элементов по имени символа
                    .ToList();

                    // Группировка элементов по символу
                    var groupedElements = framingElements.GroupBy(el => el.Symbol.Id)
                                                         .OrderBy(group => doc.GetElement(group.Key).Name);

                    if (check)
                    {
                        int positionCounter1 = 1;
                        int positionCounter2 = 1;
                        foreach (var group in groupedElements)
                        {
                            foreach (var element in group)
                            {
                                if (element.LookupParameter("ZH_Этаж_Числовой").AsInteger() > 0)
                                {
                                    string positionValue = $"Пр-{positionCounter1}";


                                    // Назначение значения параметру ADSK_Позиция
                                    var positionParam = element.LookupParameter("ADSK_Позиция");
                                    if (positionParam != null && positionParam.IsReadOnly == false)
                                    {
                                        positionParam.Set(positionValue);
                                    }


                                    positionCounter1++;
                                }
                                else
                                {
                                    string positionValue = $"Пр-{positionCounter2}";


                                    // Назначение значения параметру ADSK_Позиция
                                    var positionParam = element.LookupParameter("ADSK_Позиция");
                                    if (positionParam != null && positionParam.IsReadOnly == false)
                                    {
                                        positionParam.Set(positionValue);
                                    }


                                    positionCounter2++;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Нумерация групп
                        int positionCounter = 1;
                        foreach (var group in groupedElements)
                        {
                            string positionValue = $"Пр-{positionCounter}";

                            foreach (var element in group)
                            {
                                // Назначение значения параметру ADSK_Позиция
                                var positionParam = element.LookupParameter("ADSK_Позиция");
                                if (positionParam != null && positionParam.IsReadOnly == false)
                                {
                                    positionParam.Set(positionValue);
                                }
                            }

                            positionCounter++;
                        }
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }

        public string GetName()
        {
            return "Нумерация перемычек";
        }
    }

    public class NestedElementsNumbering : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;

            using (Transaction trans = new Transaction(doc, "Нумерация вложенных элементов"))
            {
                trans.Start();

                try
                {
                    // Сбор всех элементов категории OST_StructuralFraming
                    var framingElements = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                .Where(f => (doc.GetElement(f.Symbol.Id)).LookupParameter("Ключевая пометка").AsString() == "ПР")
                        .ToList();
                    Dictionary<string, int> dict = new Dictionary<string, int>();
                    int nestedCounter = 1;
                    Dictionary<string, List<Element>> nestedNames = new Dictionary<string, List<Element>>();
                    foreach (var element in framingElements)
                    {
                        if (element.SuperComponent == null)
                        {
                            var subElements = element.GetSubComponentIds();
                            if (subElements.Count == 0)
                            {
                                // no nested families
                                continue;
                            }
                            else
                            {
                                // has nested families
                                foreach (var aSubElemId in subElements)
                                {
                                    var nestedElement = doc.GetElement(aSubElemId);
                                    if (nestedElement is FamilyInstance)
                                    {
                                        if (nestedNames.Keys.Contains(nestedElement.Name))
                                        {
                                            nestedNames[nestedElement.Name].Add(nestedElement);
                                        }
                                        else
                                        {
                                            nestedNames.Add(nestedElement.Name, new List<Element> { nestedElement });
                                        }
                                        //var positionParam = nestedElement.LookupParameter("ADSK_Позиция");
                                        //if (positionParam != null && positionParam.IsReadOnly == false)
                                        //{
                                        //    if (dict.Keys.Contains(nestedElement.Name))
                                        //    {
                                        //        positionParam.Set(dict[nestedElement.Name].ToString());
                                        //    }
                                        //    else
                                        //    {
                                        //        positionParam.Set(nestedCounter.ToString());
                                        //        dict.Add(nestedElement.Name, nestedCounter);
                                        //        nestedCounter++;
                                        //    }
                                        //}
                                        //nestedCounter++;
                                    }
                                }
                            }
                        }
                    }
                    nestedNames = nestedNames.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);
                    foreach (var nestedElement in nestedNames.Values)
                    {
                        foreach (var el in nestedElement)
                        {
                            var positionParam = el.LookupParameter("ADSK_Позиция");
                            if (positionParam != null && positionParam.IsReadOnly == false)
                            {
                                positionParam.Set(nestedCounter.ToString());
                            }
                        }
                        nestedCounter++;
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }

        public string GetName()
        {
            return "Нумерация вложенных элементов перемычек";
        }
    }

    public class CreateSectionsForLintels : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;

            // Получение всех перемычек
            var framingElements = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
        .Where(f => (doc.GetElement(f.Symbol.Id)).LookupParameter("Ключевая пометка").AsString() == "ПР")
                .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                .ToList();

            // Группировка перемычек по параметру ADSK_Позиция
            var groupedElements = framingElements.OrderBy(el => el.LookupParameter("ADSK_Позиция").AsString()).GroupBy(el => el.LookupParameter("ADSK_Позиция").AsString());
            // 03.02.26 - изменен фильтр для перемычек + создание базовых разрезов и шаблонов
            using (Transaction tr = new Transaction(doc, "Назначение линии видимости"))
            {
                tr.Start();

                foreach (var element in groupedElements)
                {
                    var d = element.First().LookupParameter("Видимость.Глубина").AsDouble();
                    if (d < 2000/304.8)
                        element.First().LookupParameter("Видимость.Глубина").Set(2000 / 304.8);
                }
                tr.Commit();
            }

            using (Transaction trans = new Transaction(doc, "Создание разрезов для перемычек"))
            {
                trans.Start();

                try
                {
                    // Шаблон для разрезов
                    ViewFamilyType sectionViewType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault<ViewFamilyType>(x =>
                      ViewFamily.Section == x.ViewFamily && x.Name == "Номер вида");

                    if (sectionViewType == null)
                    {
                        // Берём любой существующий тип разреза как базу
                        var baseSectionType = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.Section);

                        if (baseSectionType == null)
                        {
                            MessageBox.Show("Не найден ни один тип разреза.", "Ошибка");
                            trans.RollBack();
                            return;
                        }

                        // Дублируем и переименовываем
                        ElementType newTypeId = baseSectionType.Duplicate("Номер вида");
                        sectionViewType = doc.GetElement(newTypeId.Id) as ViewFamilyType;
                    }

                    ViewSection viewSection = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .OfType<ViewSection>()
                    .FirstOrDefault(vt => vt.Name == "4_К_Пр");

                    if (viewSection == null)
                    {
                        // Попробуем продублировать любой другой шаблон-разрез (самый безопасный способ)
                        var anySectionTemplate = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewSection))
                            .Cast<ViewSection>()
                            .FirstOrDefault(v => v.IsTemplate);

                        if (anySectionTemplate != null)
                        {
                            ElementId dupId = ElementTransformUtils.CopyElement(doc, anySectionTemplate.Id, XYZ.Zero).FirstOrDefault();
                            viewSection = doc.GetElement(dupId) as ViewSection;
                            viewSection.Name = "4_К_Пр";
                        }
                        else
                        {
                            MessageBox.Show("Не найден ни один шаблон разреза.", "Ошибка");
                            trans.RollBack();
                            return;
                        }
                    }



                        // Создание разрезов для каждой уникальной группы
                        foreach (var group in groupedElements)
                    {
                        var firstElement = group.FirstOrDefault();
                        if (firstElement == null) continue;



                        // Определение размера разреза
                        LocationPoint locationPoint = firstElement.Location as LocationPoint;
                        double rotationAngle = locationPoint.Rotation;
                        XYZ direction;

                        if (Math.Abs(rotationAngle) < 1e-6 || Math.Abs(rotationAngle - Math.PI) < 1e-6)
                        {
                            direction = XYZ.BasisX; // Без поворота или 180 градусов
                        }
                        else if (Math.Abs(rotationAngle - Math.PI / 2) < 1e-6 || Math.Abs(rotationAngle - 3 * Math.PI / 2) < 1e-6)
                        {
                            direction = XYZ.BasisY; // 90 или 270 градусов
                        }
                        else
                        {
                            // Случай произвольного угла
                            direction = new XYZ(Math.Cos(rotationAngle), Math.Sin(rotationAngle), 0).Normalize();
                        }

                        // Определение направления "вверх" для разреза
                        XYZ upDirection = XYZ.BasisZ;
                        XYZ crossDirection = direction.CrossProduct(upDirection).Negate();

                        // Определение центра перемычки
                        XYZ center = (firstElement.get_BoundingBox(null).Max + (firstElement.get_BoundingBox(null).Min + XYZ.BasisZ * 2000 / 304.8)) / 2;

                        Transform t = Transform.Identity;
                        t.Origin = center;
                        t.BasisX = crossDirection;
                        t.BasisY = upDirection;
                        t.BasisZ = direction;

                        // Размеры разреза с учетом отступов в футах
                        double offsetX = 100 / 304.8; // 100 мм по X (влево и вправо)
                        double offsetZ = 200 / 304.8; // 200 мм по Z (вверх и вниз)

                        // Размеры элемента                       
                        double elementWidth = firstElement.get_BoundingBox(null).Max.X - firstElement.get_BoundingBox(null).Min.X;
                        double elementHeight = firstElement.get_BoundingBox(null).Max.Y - firstElement.get_BoundingBox(null).Min.Y;
                        double elementDepth = firstElement.get_BoundingBox(null).Max.Z - firstElement.get_BoundingBox(null).Min.Z - 1900 / 304.8;

                        BoundingBoxXYZ boundingBox = new BoundingBoxXYZ();
                        boundingBox.Transform = t;

                        // Настройка границ BoundingBox с учетом отступов
                        if (Math.Abs(rotationAngle) < 1e-6 || Math.Abs(rotationAngle - Math.PI) < 1e-6)
                        {
                            boundingBox.Min = new XYZ(-elementHeight / 2 - offsetX, -elementDepth / 2 - offsetZ, 0); // Отступы по краям
                            boundingBox.Max = new XYZ(elementHeight / 2 + offsetX, elementDepth / 2 + offsetZ, offsetZ);   // Отступы по краям
                        }
                        else if (Math.Abs(rotationAngle - Math.PI / 2) < 1e-6 || Math.Abs(rotationAngle - 3 * Math.PI / 2) < 1e-6)
                        {
                            boundingBox.Min = new XYZ(-elementWidth / 2 - offsetX, -elementDepth / 2 - offsetZ, 0); // Отступы по краям
                            boundingBox.Max = new XYZ(elementWidth / 2 + offsetX, elementDepth / 2 + offsetZ, offsetZ);   // Отступы по краям
                        }

                        // Создание разреза
                        ViewSection section = ViewSection.CreateSection(doc, sectionViewType.Id, boundingBox);
                        if (section == null)
                            continue;

                        // Установка имени разреза
                        string positionName = firstElement.LookupParameter("ADSK_Позиция").AsString();
                        bool lower0 = firstElement.LookupParameter("ZH_Этаж_Числовой").AsInteger() < 0;

                        var view = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views).Where(x => x.Name.Contains(positionName)).FirstOrDefault();
                        if (view != null)
                        {
                            // 29.01.26 - уникальное имя для разреза
                            var framingElements_ = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(f => (doc.GetElement(f.Symbol.Id)).LookupParameter("Ключевая пометка").AsString() == "ПР")
                            .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                            .ToList().FirstOrDefault();
                            if (framingElements_ != null)
                            {
                                string positionName_ = framingElements_.LookupParameter("ADSK_Позиция").AsString();
                                bool lower0_ = framingElements_.LookupParameter("ZH_Этаж_Числовой").AsInteger() < 0;

                                if (positionName == positionName_)
                                {
                                    doc.Delete(section.Id);
                                    continue;
                                }
                                else
                                {
                                    if (lower0_)
                                        view.Name = MakeUniqueViewName(doc, positionName_ + " ниже 0.000_");
                                    else
                                        view.Name = MakeUniqueViewName(doc, positionName_ + " выше 0.000_");
                                }
                            }
                            else
                            {
                                doc.Delete(view.Id);
                            }
                        }
                        if (lower0)
                            section.Name = positionName + " ниже 0.000";
                        else
                            section.Name = positionName + " выше 0.000";



                        section.LookupParameter("Шаблон вида").Set(viewSection.Id);
                        section.LookupParameter("Масштаб вида").Set(20);
                    }
                    var views = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views).Where(x => x.Name.Contains("Пр") && x.Name.Contains("0.000_")).ToList();
                    foreach (var view in views)
                    {
                        view.Name = MakeUniqueViewName(doc, view.Name.Replace("_", ""));
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }
        // 29.01.26 - уникальное имя для разреза
        private static string MakeUniqueViewName(Document doc, string desiredName)
        {
            // Собираем все имена видов (без шаблонов)
            var names = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .Select(v => v.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(desiredName))
                return desiredName;

            int i = 1;
            while (true)
            {
                string candidate = $"{desiredName} ({i})";
                if (!names.Contains(candidate))
                    return candidate;
                i++;
            }
        }


        public string GetName()
        {
            return "Создание разрезов для уникальных перемычек";
        }
    }

    public class TagLintels : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            UIDocument uidoc = app.ActiveUIDocument;
            if (doc.ActiveView.ViewType != ViewType.FloorPlan)
            {
                MessageBox.Show("Перейдите на план этажа для создания разрезов", "Ошибка");
                return;
            }

            using (Transaction trans = new Transaction(doc, "Маркировка перемычек"))
            {
                trans.Start();

                try
                {
                    // Сбор всех перемычек
                    // 30.06.26 - перемычки в модели
                    var lintelInstances = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_StructuralFraming)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                .Where(f => (doc.GetElement(f.Symbol.Id)).LookupParameter("Ключевая пометка").AsString() == "ПР")
                        .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                        .ToList();

                    if (lintelInstances.Count == 0)
                    {
                        MessageBox.Show("Не найдено ни одной перемычки для маркировки.", "Ошибка");
                        trans.RollBack();
                        return;
                    }

                    // Поиск типа марки
                    var tagType = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .OfType<FamilySymbol>().FirstOrDefault(tag => tag.FamilyName == "ADSK_Марка_Балка" && tag.Name == "Экземпляр_ADSK_Позиция");

                    var tagType2 = new FilteredElementCollector(doc)
                        .OfClass(typeof(SpotDimensionType))
                        .OfType<SpotDimensionType>().FirstOrDefault(tag => tag.FamilyName == "Высотные отметки" && tag.Name == "ADSK_Проектная_без всего");

                    var tagType2Vert = new FilteredElementCollector(doc)
                        .OfClass(typeof(SpotDimensionType))
                        .OfType<SpotDimensionType>().FirstOrDefault(tag => tag.FamilyName == "Высотные отметки" && tag.Name == "ADSK_Проектная_без всего_(В)");


                    if (tagType == null)
                    {
                        MessageBox.Show("Не найден тип марки 'Экземпляр_ADSK_Позиция' для семейства 'ADSK_Марка_Балка'.", "Ошибка");
                        trans.RollBack();
                        return;
                    }
                    if (tagType2 == null)
                    {
                        MessageBox.Show("Не найден тип марки 'ADSK_Проектная_без всего' для семейства 'Высотные отметки'.", "Ошибка");
                        trans.RollBack();
                        return;
                    }

                    if (tagType2Vert == null)
                    {
                        MessageBox.Show("Не найден тип марки 'ADSK_Проектная_без всего_(В)' для семейства 'Высотные отметки'.", "Ошибка");
                        trans.RollBack();
                        return;
                    }

                    // Активируем тип марки, если не активирован
                    if (!tagType.IsActive)
                    {
                        tagType.Activate();
                        doc.Regenerate();
                    }

                    // Маркировка всех перемычек
                    foreach (var lintel in lintelInstances)
                    {
                        // Получение центра перемычки
                        BoundingBoxXYZ boundingBox = lintel.get_BoundingBox(null);
                        if (boundingBox == null) continue;

                        //Изменить логику простановки (сейчас поверх перемычки)
                        XYZ centerTop = new XYZ(
                            (boundingBox.Min.X + boundingBox.Max.X) / 2,
                            (boundingBox.Max.Y + 495 / 304.8),
                            boundingBox.Max.Z
                        );
                        XYZ centerLeft = new XYZ(
                            boundingBox.Min.X - 315 / 304.8,
                            (boundingBox.Max.Y + boundingBox.Min.Y) / 2,
                            boundingBox.Max.Z
                        );

                        // Создание марки
                        IndependentTag newTag = null;
                        if ((lintel as FamilyInstance).HandOrientation.X == 1)
                        {
                            newTag = IndependentTag.Create(
                            doc,
                            tagType.Id,
                            doc.ActiveView.Id,
                            new Reference(lintel),
                            false,
                            TagOrientation.Horizontal,
                            centerTop
                        );
                        }
                        else
                        {
                            newTag = IndependentTag.Create(
                            doc,
                            tagType.Id,
                            doc.ActiveView.Id,
                            new Reference(lintel),
                            false,
                            TagOrientation.Vertical,
                            centerLeft
                        );
                        }

                        if (newTag == null)
                        {
                            MessageBox.Show("Не удалось создать марку для перемычки.", "Ошибка");
                            continue;
                        }

                        centerTop = new XYZ(
                            (boundingBox.Min.X + boundingBox.Max.X) / 2,
                            (boundingBox.Max.Y + 150 / 304.8),
                            boundingBox.Max.Z
                        );
                        centerLeft = new XYZ(
                            boundingBox.Min.X - 150 / 304.8,
                            (boundingBox.Max.Y + boundingBox.Min.Y) / 2,
                            boundingBox.Max.Z
                        );

                        // Создание высотной отметки
                        Reference ref_ = null;
                        ref_ = lintel.GetReferences(FamilyInstanceReferenceType.Bottom).First();
                        SpotDimension newTag2 = null;
                        if ((lintel as FamilyInstance).HandOrientation.X == 1)
                        {
                            newTag2 = doc.Create.NewSpotElevation(
                            doc.ActiveView,
                            ref_,
                            (lintel.Location as LocationPoint).Point,
                            ((lintel.Location as LocationPoint).Point + centerTop) / 2,
                            centerTop,
                            new XYZ(0, 0, 0),
                            false
                        );
                            if (newTag2 == null)
                            {
                                MessageBox.Show("Не удалось создать высотную отметку для перемычки.", "Ошибка");
                                continue;
                            }

                            newTag2.SpotDimensionType = tagType2;
                            (newTag2 as Dimension).TextPosition = (boundingBox.Max + boundingBox.Min) / 2 + 1.15 * XYZ.BasisY;
                        }
                        else
                        {
                            newTag2 = doc.Create.NewSpotElevation(
                            doc.ActiveView,
                            ref_,
                            (lintel.Location as LocationPoint).Point,
                            ((lintel.Location as LocationPoint).Point + centerLeft) / 2,
                            centerLeft,
                            new XYZ(0, 0, 0),
                            false
                        );
                            if (newTag2 == null)
                            {
                                MessageBox.Show("Не удалось создать высотную отметку для перемычки.", "Ошибка");
                                continue;
                            }

                            newTag2.SpotDimensionType = tagType2Vert;
                            (newTag2 as Dimension).TextPosition = (boundingBox.Max + boundingBox.Min) / 2 - 0.8 * XYZ.BasisX;
                        }


                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }
        }

        public string GetName()
        {
            return "Маркировка перемычек";
        }
    }

    public class LintelRequest
    {
        public ParentElement ParentElement { get; set; }
        public WallType WallType { get; set; }
        public FamilySymbol LintelType { get; set; }
    }

    // 20.06.25 - изменение ошибок транзакций
    public class LintelCreate : IExternalEventHandler
    {
        string output = "";
        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            UIDocument uidoc = app.ActiveUIDocument;

            if (CommandLintelCreator_v2.PendingRequests.Count == 0)
                return;

            var req = CommandLintelCreator_v2.PendingRequests.Dequeue();

            // подставляем его в ViewModel
            var vm = LintelCreatorForm_v2.MainViewModel;
            vm.SelectedParentElement = req.ParentElement;
            vm.SelectedWallTypeName = req.WallType.Name;
            vm.SelectedFamily = new FamilyWrapper(req.LintelType.Family, doc);
            vm.SelectedType = req.LintelType;

            FilteredElementCollector levelCollector = new FilteredElementCollector(doc);
            var levels = levelCollector.OfClass(typeof(Level))
                                       .Cast<Level>()
                                       .Where(l => l.Elevation >= 0)
                                       .OrderBy(l => l.Elevation)
                                       .ToList().Select(x => x.Elevation).ToList();

            using (TransactionGroup trans = new TransactionGroup(doc, "Добавление перемычек"))
            {
                trans.Start();

                try
                {
                    // Получение модели данных из окна
                    var mainViewModel = LintelCreatorForm_v2.MainViewModel;
                    if (mainViewModel == null)
                    {
                        MessageBox.Show("Не удалось получить данные из окна.", "Ошибка");
                        trans.RollBack();
                        return;
                    }
                    // Получение выбранного семейства, типа и элемента
                    var selectedFamily = mainViewModel.SelectedFamily;
                    var selectedType = mainViewModel.SelectedType;
                    var selectedParentElement = mainViewModel.SelectedParentElement;

                    if (selectedFamily == null || selectedType == null || selectedParentElement == null)
                    {
                        MessageBox.Show("Пожалуйста, выберите семейство, тип перемычки и элемент.", "Ошибка");
                        trans.RollBack();
                        return;
                    }



                    // Получение выбранного типа стены из радиокнопки
                    var selectedWallType = mainViewModel.SelectedWallTypeName;
                    if (selectedWallType == null || !selectedParentElement.Walls.Keys.Select(x => x.Name).Contains(selectedWallType))
                    {
                        MessageBox.Show("Пожалуйста, выберите тип стены через радиобокс.", "Ошибка");
                        trans.RollBack();
                        return;
                    }
                    var sel = selectedParentElement.Walls;
                    // Получаем элементы, связанные с выбранной стеной
                    // 20.06.25 - изменения в созданных элементах в окне
                    foreach (var wallElements in sel)
                    {
                        if (wallElements.Key.Name != selectedWallType)
                            continue;
                        //28.07.25 - объединение проемов в перемычках
                        List<ElementsForLintel> wallElement = wallElements.Value;
                        List<ElementsForLintel> lintelCreated = new List<ElementsForLintel>();
                        foreach (var element in wallElement)
                        {
                            using (Transaction tr = new Transaction(doc, "Добавление перемычек"))
                            {
                                tr.Start();

                                // Проверяем, активен ли выбранный тип
                                if (!selectedType.IsActive)
                                {
                                    selectedType.Activate();
                                    doc.Regenerate();
                                }

                                FamilyInstance newLintel = null;

                                // Рассчитываем BoundingBox текущего элемента
                                //28.07.25 - объединение проемов в перемычках
                                BoundingBoxXYZ bb = element.Elements.FirstOrDefault().get_BoundingBox(null);
                                if (bb == null) continue;
                                // 24.09.25 - изменения в перемычках
                                XYZ center = (bb.Max + bb.Min) / 2;
                                XYZ maxPoint = bb.Max;

                                // Увеличиваем BoundingBox вверх для поиска перемычки
                                XYZ searchMinPoint = new XYZ(center.X - 100 / 304.8, center.Y - 100 / 304.8, maxPoint.Z - 50 / 304.8);
                                XYZ searchMaxPoint = new XYZ(center.X + 100 / 304.8, center.Y + 100 / 304.8, maxPoint.Z + 10 / 304.8); // 100 мм вверх

                                Outline searchOutline = new Outline(searchMinPoint, searchMaxPoint);
                                BoundingBoxIntersectsFilter searchFilter = new BoundingBoxIntersectsFilter(searchOutline);

                                // Поиск существующих перемычек
                                List<Element> lintelCollector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WherePasses(searchFilter)
                .Where(x => (x as FamilyInstance).Symbol.FamilyName.Contains("_Перемычки")).ToList();

                                List<ElementId> listToDel = new List<ElementId>();
                                // Если есть существующая перемычка, пропускаем создание новой
                                if (lintelCollector.Count() > 0 && lintelCollector.Any(x => x.LookupParameter("ADSK_Группирование")?.AsString() == "ПР"))
                                {
                                    if (LintelCreatorForm_v2.recreate)
                                    {
                                        foreach (Element e in lintelCollector)
                                        {
                                            string par = e.LookupParameter("ADSK_Группирование")?.AsString();
                                            if (par == "ПР")
                                                listToDel.Add(e.Id);
                                        }
                                    }
                                    else
                                    {
                                        //28.07.25 - объединение проемов в перемычках
                                        output += "У элемента " + string.Join("+", element.Elements.Select(x => x.Id)) + " уже создана перемычка, создание пропущено\n";
                                        continue;
                                    }
                                }



                                foreach (ElementId id in listToDel.Distinct())
                                {
                                    if (doc.GetElement(id) != null)
                                        doc.Delete(id);
                                }

                                double height = 0;
                                double topZ = double.MinValue;
                                double locZ = double.MinValue;
                                double BestZ = double.MinValue;
                                XYZ locationPoint = null;
                                XYZ translation = null;


                                // Получаем уровен 
                                //23.01.26 - объединение проемов в перемычках вверх-вниз
                                Level level = null;

                                foreach (var opEl in element.Elements)
                                {
                                    Level l_ = null;
                                    if (opEl is Wall)
                                    {
                                        height = opEl.LookupParameter("Неприсоединенная высота").AsDouble();
                                        l_ = doc.GetElement(opEl.LevelId) as Level;
                                        locZ = ((opEl.Location as LocationCurve).Curve as Line).GetEndPoint(0).Z - l_.ProjectElevation + height + element.Elements.FirstOrDefault().LookupParameter("Смещение снизу").AsDouble();
                                    }
                                    else
                                    {
                                        height = opEl.LookupParameter("ADSK_Размер_Высота").AsDouble();
                                        l_ = doc.GetElement(opEl.LevelId) as Level;
                                        locZ = (opEl.Location as LocationPoint).Point.Z - l_.ProjectElevation + height;
                                    }
                                    if (locZ + l_.ProjectElevation > BestZ)
                                    { topZ = locZ; level = l_; BestZ = locZ + l_.ProjectElevation; }
                                }

                                // Вставляем перемычку строго по верхней отметке проёма
                                locationPoint = new XYZ(element.Location.X, element.Location.Y, topZ);

                                // Создаем экземпляр перемычки
                                newLintel = doc.Create.NewFamilyInstance(locationPoint, selectedType, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural) as FamilyInstance;

                                if (newLintel == null)
                                {
                                    tr.RollBack();
                                    continue;
                                }

                                XYZ baseOrientation;
                                //28.07.25 - объединение проемов в перемычках
                                if (selectedParentElement.SupportType == 1
                                    && selectedParentElement.SupportDirection.TryGetValue(element.Elements.FirstOrDefault(), out XYZ supportDir))
                                {
                                    // при одиночной опоре – используем именно её направление
                                    baseOrientation = supportDir;
                                }
                                else if (element.Elements.FirstOrDefault() is Wall w)
                                {
                                    baseOrientation = w.Orientation;
                                }
                                else // FamilyInstance
                                {
                                    baseOrientation = ((FamilyInstance)element.Elements.FirstOrDefault()).FacingOrientation;
                                }

                                // смещение перемычки на половину ширины
                                translation = baseOrientation * wallElements.Key.Width / 2;

                                // поворот перемычки так, чтобы newLintel.FacingOrientation == baseOrientation
                                if (!baseOrientation.IsAlmostEqualTo(newLintel.FacingOrientation))
                                {
                                    // ось вращения – вертикальная через точку размещения
                                    var locPt = (LocationPoint)newLintel.Location;
                                    Line rotateAxis = Line.CreateBound(
                                        locPt.Point,
                                        locPt.Point + XYZ.BasisZ);

                                    // считаем углы относительно глобальной X
                                    double u1 = baseOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                                    double u2 = newLintel.FacingOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                                    double rotateAngle = u2 - u1;

                                    ElementTransformUtils.RotateElement(
                                        doc,
                                        newLintel.Id,
                                        rotateAxis,
                                        rotateAngle);
                                }

                                // перемещаем перемычку
                                ElementTransformUtils.MoveElement(doc, newLintel.Id, translation);
                                newLintel.LookupParameter("ADSK_Группирование").Set("ПР");
                                int intLev = level.Elevation >= 0 ? levels.IndexOf(level.Elevation) + 1 : -1;
                                newLintel.LookupParameter("ZH_Этаж_Числовой").SetValueString(intLev.ToString());
                                newLintel.LookupParameter("Видимость.Глубина").SetValueString("2000");

                                // Переделать удаление списков - тут удалять из списка Walls сам элемент, после группы проверять колиество элементов и удалять если нет
                                // 20.06.25 - изменения в созданных элементах в окне
                                lintelCreated.Add(element);

                                tr.Commit();
                            }
                        }

                        // найдём реальный ключ WallType в ParentElement по имени
                        var wallTypeKey = selectedParentElement.Walls.Keys
                            .First(x => x.Name == selectedWallType);

                        foreach (var element in lintelCreated)
                        {
                            selectedParentElement.Walls[wallTypeKey].Remove(element);
                        }

                        // Обновляем список без перемычек
                        if (vm.openingsWithoutLintel.Contains(selectedParentElement))
                        {
                            var noLintelParent = vm.openingsWithoutLintel
                                [vm.openingsWithoutLintel.IndexOf(selectedParentElement)];

                            // если для этого типа стены больше не осталось элементов — убираем ключ
                            if (noLintelParent.Walls.TryGetValue(wallTypeKey, out var remaining)
                                && remaining.Count == 0)
                            {
                                noLintelParent.Walls.Remove(wallTypeKey);
                            }
                            // если словарь словарей пуст — убираем весь ParentElement
                            if (noLintelParent.Walls.Count == 0)
                            {
                                vm.openingsWithoutLintel.Remove(noLintelParent);
                            }
                        }

                        // Переносим все обработанные элементы в openingsWithLintel
                        var withParent = vm.openingsWithLintel.FirstOrDefault(p =>
                            p.Name == selectedParentElement.Name
                            && p.TypeName == selectedParentElement.TypeName
                            && p.Width == selectedParentElement.Width
                            && p.SupportType == selectedParentElement.SupportType);

                        if (withParent == null)
                        {
                            // если ещё не было такой группы — создаём
                            withParent = new ParentElement
                            {
                                Name = selectedParentElement.Name,
                                TypeName = selectedParentElement.TypeName,
                                Width = selectedParentElement.Width,
                                SupportType = selectedParentElement.SupportType,
                                SupportDirection = selectedParentElement.SupportDirection,
                                ChildElements = selectedParentElement.ChildElements,
                                //28.07.25 - объединение проемов в перемычках
                                Walls = new Dictionary<WallType, List<ElementsForLintel>>()
                            };
                            vm.openingsWithLintel.Add(withParent);
                        }

                        // добавляем к существующему или новому ParentElement
                        if (withParent.Walls.ContainsKey(wallTypeKey))
                            withParent.Walls[wallTypeKey].AddRange(lintelCreated);
                        else
                            //28.07.25 - объединение проемов в перемычках
                            withParent.Walls[wallTypeKey] = new List<ElementsForLintel>(lintelCreated);


                        break;
                    }
                    trans.Assimilate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Ошибка");
                    trans.RollBack();
                }
            }

            if (CommandLintelCreator_v2.PendingRequests.Count > 0)
                CommandLintelCreator_v2.lintelCreateEvent.Raise();
            else
            {
                if (output == "")
                    output = "Выполнено без ошибок";
                MessageBox.Show(output, "Выполнено");
                output = "";
            }

        }


        public string GetName()
        {
            return "xxx";
        }
    }
}
