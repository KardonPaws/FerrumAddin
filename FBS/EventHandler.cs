using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FerrumAddinDev.FBS
{
    // Selection filter to allow only wall elements
    public class WallSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element elem) => elem is Wall;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }
    //02.12.25 - измененный профиль стены + вынос на листы
    public class SelectWallsHandler
    {
        private FBSLayoutCommand _window;

        // Вспомогательный класс сегмента профиля снизу
        private class BottomProfileSegment
        {
            public double StartFt;    // расстояние вдоль стены от начала (ft)
            public double EndFt;      // расстояние вдоль стены до конца (ft)
            public double BaseZFt;    // отметка низа сегмента (ft)
        }

        public void SelectWalls(UIApplication app, FBSLayoutCommand win)
        {
            _window = win;
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.Element,
                    new WallSelectionFilter(),
                    "Выберете стены");

                if (refs == null) return;

                List<WallInfo> wallInfos = new List<WallInfo>();

                foreach (Reference r in refs)
                {
                    Wall wall = doc.GetElement(r) as Wall;
                    if (wall == null) continue;

                    LocationCurve locCurve = wall.Location as LocationCurve;
                    if (locCurve == null) continue;

                    XYZ start = locCurve.Curve.GetEndPoint(0);
                    XYZ end = locCurve.Curve.GetEndPoint(1);
                    Line wallLine = locCurve.Curve as Line;
                    XYZ wallDir = (end - start).Normalize();

                    double lengthFt = locCurve.Curve.Length;      // ft
                    double lengthMm = lengthFt * 304.8;           // мм
                    double thicknessMm = wall.Width * 304.8;      // мм

                    // Высота и базовый уровень
                    double baseElevFt = wall.get_BoundingBox(null).Min.Z;
                    double heightMm;
                    Parameter heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                    if (heightParam != null && heightParam.HasValue)
                    {
                        heightMm = heightParam.AsDouble() * 304.8;
                    }
                    else
                    {
                        heightMm = (wall.get_BoundingBox(null).Max.Z - wall.get_BoundingBox(null).Min.Z) * 304.8;
                    }

                    // ОТВЕРСТИЯ
                    List<OpeningInfo> openingsAbsolute = new List<OpeningInfo>();

                    IEnumerable<FamilyInstance> hostedInserts =
                        new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .OfType<FamilyInstance>()
                            .Where(fi =>
                                fi.Category.Id.Value == (int)BuiltInCategory.OST_Doors ||
                                fi.Category.Id.Value == (int)BuiltInCategory.OST_Windows)
                            .Where(fi => fi.Host != null)
                            .Where(fi => fi.Host.Id == wall.Id);

                    foreach (FamilyInstance fi in hostedInserts)
                    {
                        // Положение вставки
                        XYZ insPoint;
                        BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                        if (fi.Location is LocationPoint lp)
                        {
                            insPoint = lp.Point;
                        }
                        else
                        {
                            insPoint = (bb.Min + bb.Max) / 2;
                        }

                        // 18.11.25 - изменения в ФБС при отступах != 300 и 600
                        double startzFt = insPoint.Z;

                        XYZ vec = insPoint - start;
                        double distAlongWallFt = vec.DotProduct(wallDir);
                        double openingCenterMm = distAlongWallFt * 304.8;

                        // Ширина и высота отверстия
                        double openWidthFt = 0;
                        double openHeightFt = 0;

                        Parameter widthParam =
                            fi.get_Parameter(BuiltInParameter.WINDOW_WIDTH) ??
                            fi.get_Parameter(BuiltInParameter.DOOR_WIDTH);

                        Parameter heightParam_ =
                            fi.get_Parameter(BuiltInParameter.WINDOW_HEIGHT) ??
                            fi.get_Parameter(BuiltInParameter.DOOR_HEIGHT) ??
                            fi.get_Parameter(BuiltInParameter.GENERIC_HEIGHT);

                        if (widthParam != null && widthParam.HasValue)
                        {
                            openWidthFt = widthParam.AsDouble();
                        }
                        else
                        {
                            // 05.08.25 - добавлен параметр если отсутсвует встроенный параметр
                            widthParam = fi.LookupParameter("ADSK_Размер_Ширина");
                            if (widthParam != null && widthParam.HasValue)
                                openWidthFt = widthParam.AsDouble();
                        }

                        if (heightParam_ != null && heightParam_.HasValue)
                        {
                            openHeightFt = heightParam_.AsDouble();
                        }
                        else
                        {
                            heightParam_ = fi.LookupParameter("ADSK_Размер_Высота");
                            if (heightParam_ != null && heightParam_.HasValue)
                                openHeightFt = heightParam_.AsDouble();
                        }

                        double endzFt = startzFt + openHeightFt;

                        double openWidthMm = openWidthFt * 304.8;
                        double openStartMm = openingCenterMm - openWidthMm / 2.0;
                        double openEndMm = openingCenterMm + openWidthMm / 2.0;

                        openingsAbsolute.Add(new OpeningInfo
                        {
                            Start = openStartMm,
                            End = openEndMm,
                            StartZ = startzFt * 304.8,
                            EndZ = endzFt * 304.8
                        });
                    }

                    openingsAbsolute = openingsAbsolute
                        .OrderBy(op => op.Start)
                        .ToList();

                    //   проверка: изменён ли profile стены
                    ElementId sketchId = wall.SketchId;   // у стены с изменённым профилем есть Sketch
                    bool hasModifiedProfile = sketchId != ElementId.InvalidElementId;

                    if (!hasModifiedProfile)
                    {
                        WallInfo info = new WallInfo
                        {
                            Id = wall.Id,
                            StartPoint = start,
                            EndPoint = end,
                            Direction = wallDir,
                            Length = lengthMm,
                            Thickness = thicknessMm,
                            Height = Math.Round(heightMm),
                            BaseElevation = baseElevFt,          // как было: Min.Z из bb
                            Openings = openingsAbsolute,         // как было: от начала стены, абсолютные Z
                            line = wallLine,
                            // 04.08.25 - базовый уровень в перемычках
                            baseLevel = wall.LookupParameter("Зависимость снизу").AsElementId()
                        };

                        wallInfos.Add(info);
                        continue; // ВАЖНО: к следующей стене, без разбиения по профилю
                    }

                    //    нижний профиль и разбиение только если
                    //      профиль действительно изменён
                    const double mmPerFt = 304.8;
                    double wallLengthMm = lengthFt * mmPerFt;

                    List<BottomProfileSegment> bottomSegments =
                        GetBottomProfileSegments(wall, start, wallDir, lengthFt);

                    // Теоретически при изменённом профиле сегменты должны быть,
                    // но на всякий случай fallback — один сегмент по всей длине:
                    if (bottomSegments == null || bottomSegments.Count == 0)
                    {
                        bottomSegments = new List<BottomProfileSegment>
                    {
                        new BottomProfileSegment
                        {
                            StartFt = 0.0,
                            EndFt = lengthFt,
                            BaseZFt = baseElevFt
                        }
                    };
                    }

                    // 24.11.25 – лестничный профиль: собираем интервалы по возрастающим отметкам
                    double wallTopFt = baseElevFt + heightMm / mmPerFt;
                    List<double> baseLevels = bottomSegments
                        .Select(s => s.BaseZFt)
                        .Distinct()
                        .OrderBy(z => z)
                        .ToList();

                    for (int i = 0; i < baseLevels.Count; i++)
                    {
                        double level = baseLevels[i];
                        double nextLevel = i == baseLevels.Count - 1
                            ? wallTopFt
                            : baseLevels[i + 1];

                        // объединяем все отрезки с отметкой ниже или равной текущей
                        List<(double start, double end)> rawIntervals = bottomSegments
                            .Where(s => s.BaseZFt <= level)
                            .Select(s => (
                                Math.Max(0.0, Math.Min(lengthFt, s.StartFt)),
                                Math.Max(0.0, Math.Min(lengthFt, s.EndFt))
                            ))
                            .Where(se => se.Item2 - se.Item1 > 1e-6)
                            .ToList();

                        if (rawIntervals.Count == 0)
                            continue;

                        // слияние перекрывающихся интервалов
                        List<(double start, double end)> merged = new List<(double start, double end)>();
                        foreach (var interval in rawIntervals.OrderBy(s => s.start))
                        {
                            if (merged.Count == 0 || interval.start - merged.Last().end > 1e-6)
                            {
                                merged.Add(interval);
                            }
                            else
                            {
                                var last = merged.Last();
                                merged[merged.Count - 1] = (last.start, Math.Max(last.end, interval.end));
                            }
                        }

                        double segHeightMm = (nextLevel - level) * mmPerFt;

                        foreach (var interval in merged)
                        {
                            double segStartFt = interval.start;
                            double segEndFt = interval.end;

                            double segLengthMm = Math.Round((segEndFt - segStartFt) * mmPerFt);

                            // отверстия теперь общие для всех сегментов
                            List<OpeningInfo> openingsForInfo =
                                openingsAbsolute.Count > 0 ? openingsAbsolute : null;

                            // Точки начала/конца сегмента на линии стены
                            XYZ segStartPoint = start + wallDir * segStartFt;
                            XYZ segEndPoint = start + wallDir * segEndFt;

                            WallInfo infoSeg = new WallInfo
                            {
                                Id = wall.Id,
                                StartPoint = segStartPoint,
                                EndPoint = segEndPoint,
                                Direction = wallDir,
                                Length = segLengthMm,
                                Thickness = thicknessMm,
                                Height = Math.Round(segHeightMm),
                                BaseElevation = level,    // отметка по профилю снизу
                                Openings = openingsForInfo,    // общие отверстия стены или null
                                line = Line.CreateBound(segStartPoint, segEndPoint),
                                baseLevel = wall.LookupParameter("Зависимость снизу").AsElementId()
                            };

                            wallInfos.Add(infoSeg);
                        }
                    }
                }

                _window._selectedWalls = wallInfos;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // отмена
            }
        }

        public string GetName() => "SelectWallsHandler";

        //   Получение сегментов нижнего профиля стены
        private List<BottomProfileSegment> GetBottomProfileSegments(
            Wall wall,
            XYZ wallStart,
            XYZ wallDir,
            double wallLengthFt)
        {
            List<BottomProfileSegment> result = new List<BottomProfileSegment>();
            const double tol = 1e-6;

            Document doc = wall.Document;

            // Профиль есть только у стены с изменённым Profile
            if (wall.SketchId == ElementId.InvalidElementId)
                return result;

            Sketch sketch = doc.GetElement(wall.SketchId) as Sketch;
            if (sketch == null)
                return result;
            var bb = wall.get_BoundingBox(null);
            // sketch.Profile – набор замкнутых контуров профиля
            CurveArrArray loops = sketch.Profile;
            foreach (CurveArray loop in loops)
            {
                foreach (Curve c in loop)
                {             
                    // Берём только отрезки (линии); если нужны дуги – логику можно расширить
                    Line line = c as Line;
                    if (line == null) continue;
                    if (bb.Max.Z - line.GetEndPoint(0).Z < tol)
                        continue;

                    XYZ p0 = line.GetEndPoint(0);
                    XYZ p1 = line.GetEndPoint(1);

                    // Интересуют только горизонтальные ребра (нижний контур)
                    if (Math.Abs(p0.Z - p1.Z) > tol) continue;

                    // Ребро должно идти вдоль стены
                    XYZ dir = (p1 - p0).Normalize();
                    double dot = Math.Abs(dir.DotProduct(wallDir));
                    if (dot < 0.99) continue; // почти параллельно стене

                    // Проекция на ось стены – расстояние вдоль стены в футах
                    double s0 = (p0 - wallStart).DotProduct(wallDir);
                    double s1 = (p1 - wallStart).DotProduct(wallDir);

                    if (s1 < s0)
                    {
                        double tmp = s0;
                        s0 = s1;
                        s1 = tmp;
                    }

                    // Отрезок должен пересекаться с длиной стены
                    if (s1 < -tol || s0 > wallLengthFt + tol)
                        continue;

                    s0 = Math.Max(0.0, s0);
                    s1 = Math.Min(wallLengthFt, s1);
                    if (s1 - s0 <= tol)
                        continue;

                    result.Add(new BottomProfileSegment
                    {
                        StartFt = s0,
                        EndFt = s1,
                        BaseZFt = p0.Z  // Z низа сегмента профиля
                    });
                }
            }

            // Сортируем по началу вдоль стены
            result = result
                .OrderBy(s => s.StartFt)
                .ToList();

            return result;
        }

    }

    public class PlaceLayoutHandler : IExternalEventHandler
    {
        private FBSLayoutCommand _window;
        public LayoutVariant VariantToPlace { get; set; }
        public PlaceLayoutHandler(FBSLayoutCommand window)
        {
            _window = window;
        }
        public void Execute(UIApplication app)
        {
            if (VariantToPlace == null) return;
            Document doc = app.ActiveUIDocument.Document;
            BlockPlacer.PlaceVariant(VariantToPlace, doc);
        }
        public string GetName() => "PlaceLayoutHandler";
    }
}