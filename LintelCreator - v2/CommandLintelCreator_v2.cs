using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FerrumAddinDev.FM;
using FerrumAddinDev.LintelCreator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

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
                // 24.05.25 - убраны двери/окна с хостом витража
                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().Where(x=> (x as FamilyInstance).SuperComponent == null).Where(x=>((x as FamilyInstance).Host as Wall).WallType.Kind != WallKind.Curtain));

                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType().Where(x => (x as FamilyInstance).SuperComponent == null).Where(x => ((x as FamilyInstance).Host as Wall).WallType.Kind != WallKind.Curtain));

                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
                    .Where(x =>x is Wall && (x as Wall).WallType != null && (x as Wall).WallType.Kind == WallKind.Curtain).ToList());
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

                windowsAndDoorsList.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType()
                    .Where(x => x is Wall && (x as Wall).WallType != null && (x as Wall).WallType.Kind == WallKind.Curtain).Where(f =>
                    {
                        var code = doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип_Число").AsDouble();
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

            // Подготавливаем список плит перекрытия для вычисления опор
            var floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .ToList()
                .Where(f =>
                {
                    var code = doc.GetElement(f.GetTypeId()).LookupParameter("ZH_Код_Тип_Число").AsDouble();
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

                
                var orient = el is FamilyInstance fi ? fi.FacingOrientation : (((el as Wall).Location as LocationCurve).Curve as Line).Direction.CrossProduct(XYZ.BasisZ);
                var center = el is FamilyInstance? (el.Location as LocationPoint).Point + (max.Z-min.Z) * XYZ.BasisZ : new XYZ((min.X + max.X) / 2, (min.Y + max.Y) / 2, max.Z);
                var leftPt = el is FamilyInstance fi1 ? center - ((fi1.Host as Wall).Width/2 - 1e-6) * orient : center - Math.Abs(orient.DotProduct(center - min)) * orient;
                var rightPt = el is FamilyInstance fi2 ? center + ((fi2.Host as Wall).Width / 2 + 1e-6) * orient : center + Math.Abs(orient.DotProduct(max - center)) * orient;
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
                    var opts = new Options { ComputeReferences = false };
                    var geom = fl.get_Geometry(opts);
                    var solid = geom
                        .OfType<GeometryInstance>()
                        .SelectMany(gi => gi.GetInstanceGeometry().OfType<Solid>())
                        .OrderByDescending(s => s.Volume)
                        .FirstOrDefault();
                    if (solid == null) continue;

                    var fbb = solid.GetBoundingBox();
                    var idx = solid.ComputeCentroid();
                    fbb.Min += idx;
                    fbb.Max += idx;

                    if (leftPt.X > fbb.Min.X && leftPt.X < fbb.Max.X
                         && leftPt.Y > fbb.Min.Y && leftPt.Y < fbb.Max.Y)
                    {
                        double dz = fbb.Min.Z - leftPt.Z;
                        if (dz >= 0 && dz <= threshold) leftSup = true;
                    }

                    if (rightPt.X > fbb.Min.X && rightPt.X < fbb.Max.X
                     && rightPt.Y > fbb.Min.Y && rightPt.Y < fbb.Max.Y)
                    {
                        double dz = fbb.Min.Z - rightPt.Z;
                        if (dz >= 0 && dz <= threshold) rightSup = true;
                    }
                    if (leftSup && rightSup) break;
                }

                int supType = leftSup && rightSup ? 2 : (leftSup || rightSup ? 1 : 0);
                XYZ dir = XYZ.Zero;
                if (supType == 1)
                {
                    var supportPt = leftSup ? leftPt : rightPt;
                    dir = (supportPt - center).Normalize();
                }
                supportInfo[el] = (supType, dir);
            }

            // Группировка по имени, типу, ширине и SupportType для окон/дверей
            var windowGroups = windowsAndDoors
                .GroupBy(el => new
                {
                    el.Symbol.FamilyName,
                    el.Symbol.Name,
                    Width = el.LookupParameter("ADSK_Размер_Ширина").AsValueString(),
                    supportInfo[el].SupportType
                })
                .Select(g => new ParentElement
                {
                    Name = g.Key.FamilyName,
                    TypeName = g.Key.Name,
                    Width = g.Key.Width,
                    SupportType = g.Key.SupportType,
                    Walls = g
                        .Where(el => el.Host is Wall)
                        .GroupBy(el => (el.Host as Wall)?.GetTypeId())
                        .Where(wg => wg.Key != null)
                        .ToDictionary(
                            wg => doc.GetElement(wg.Key) as WallType,
                            wg => wg.Cast<Element>().ToList()
                        ),
                    SupportDirection = g.ToDictionary(
                        el => (Element)el,
                        el => supportInfo[el].Direction
                    )
                });

            // Группировка витражей по имени, ширине и SupportType
            var curtainGroups = curtains
                .GroupBy(el => new
                {
                    el.Name,
                    Width = Math.Round(el.LookupParameter("Длина").AsDouble() * 304.8).ToString(),
                    supportInfo[el].SupportType
                })
                .Select(g => new ParentElement
                {
                    Name = g.Key.Name,
                    TypeName = "Витраж",
                    Width = g.Key.Width,
                    SupportType = g.Key.SupportType,
                    Walls = g
                        .GroupBy(el => el.WallType.Id)
                        .ToDictionary(
                            wg => doc.GetElement(wg.Key) as WallType,
                            wg => wg.Cast<Element>().ToList()
                        ),
                    SupportDirection = g.ToDictionary(
                        el => (Element)el,
                        el => supportInfo[el].Direction
                    )
                });

            // Объединяем все группы и возвращаем итоговый список
            var groupedElements = windowGroups.Concat(curtainGroups);
                
            openingsWithoutLintel = new List<ParentElement>();
            openingsWithLintel = new List<ParentElement>();

            foreach (var parent in groupedElements)
            {
                // создаём «пустые» оболочки, копируя метаданные, но не копируя Walls
                var noLintelParent = new ParentElement { Name = parent.Name, 
                    TypeName = parent.TypeName, 
                    Width = parent.Width, 
                    Walls = new Dictionary<WallType, List<Element>>(), 
                    ChildElements = parent.ChildElements, 
                    SupportDirection = parent.SupportDirection, 
                    SupportType = parent.SupportType };
                var withLintelParent = new ParentElement
                {
                    Name = parent.Name,
                    TypeName = parent.TypeName,
                    Width = parent.Width,
                    Walls = new Dictionary<WallType, List<Element>>(),
                    ChildElements = parent.ChildElements,
                    SupportDirection = parent.SupportDirection,
                    SupportType = parent.SupportType
                };

                foreach (var kv in parent.Walls)
                {
                    var wallType = kv.Key;
                    var elems = kv.Value;

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

        private static bool ElementHasLintel(Element element, Document doc)
        {
            var bb = element.get_BoundingBox(null);
            if (bb == null) return false;

            XYZ min = bb.Min;
            XYZ max = bb.Max;

            // создаём область немного ниже макс.Z и немного выше на 100 мм
            double mm = 1.0 / 304.8;
            XYZ searchMin = new XYZ(min.X - 100 * mm, min.Y - 100 * mm, max.Z - 500 * mm);
            XYZ searchMax = new XYZ(max.X + 100 * mm, max.Y + 100 * mm, max.Z + 100 * mm);

            var outline = new Outline(searchMin, searchMax);
            var filter = new BoundingBoxIntersectsFilter(outline);

            var found = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WherePasses(filter)
                .ToElements();

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


            using (Transaction trans = new Transaction(doc, "Создание разрезов для перемычек"))
            {
                trans.Start();

                try
                {
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

                    // Шаблон для разрезов
                    ViewFamilyType sectionViewType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault<ViewFamilyType>(x =>
                      ViewFamily.Section == x.ViewFamily && x.Name == "Номер вида");

                    if (sectionViewType == null)
                    {
                        MessageBox.Show("Не найден разрез 'Номер вида'.", "Ошибка");
                        trans.RollBack();
                        return;
                    }

                    ViewSection viewSection = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .OfType<ViewSection>()
                    .FirstOrDefault(vt => vt.Name == "4_К_Пр");

                    if (viewSection == null)
                    {
                        MessageBox.Show("Не найден шаблон разреза '4_К_Пр'.", "Ошибка");
                        trans.RollBack();
                        return;
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
                        XYZ center = (firstElement.get_BoundingBox(null).Max + (firstElement.get_BoundingBox(null).Min + XYZ.BasisZ*2000/304.8))/2;

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
                        double elementDepth = firstElement.get_BoundingBox(null).Max.Z - firstElement.get_BoundingBox(null).Min.Z - 1900/304.8;
                        
                        BoundingBoxXYZ boundingBox = new BoundingBoxXYZ();
                        boundingBox.Transform = t;

                        // Настройка границ BoundingBox с учетом отступов
                        if (Math.Abs(rotationAngle) < 1e-6 || Math.Abs(rotationAngle - Math.PI) < 1e-6)
                        {
                            boundingBox.Min = new XYZ(-elementHeight / 2 - offsetX, -elementDepth/2 - offsetZ, 0); // Отступы по краям
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
                            var framingElements_ = new FilteredElementCollector(doc, view.Id)
                            .OfCategory(BuiltInCategory.OST_StructuralFraming)
                            .WhereElementIsNotElementType()
                            .Cast<FamilyInstance>()
                            .Where(f => (doc.GetElement(f.Symbol.Id)).LookupParameter("Ключевая пометка").AsString() == "ПР")
                            .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                            .ToList().FirstOrDefault();

                            string positionName_ = framingElements_.LookupParameter("ADSK_Позиция").AsString();
                            bool lower0_ = framingElements_.LookupParameter("ZH_Этаж_Числовой").AsInteger() < 0;

                            if (framingElements_ != null)
                                if (positionName == positionName_)
                                {
                                    doc.Delete(section.Id);
                                    continue;
                                }
                                else
                                {
                                    if (lower0_)
                                        view.Name = positionName_ + " ниже 0.000_";
                                    else
                                        view.Name = positionName_ + " выше 0.000_";
                                }
                        }
                        if (lower0)
                                section.Name = positionName + " ниже 0.000";
                            else
                                section.Name = positionName + " выше 0.000";

                            
                        
                        section.LookupParameter("Шаблон вида").Set(viewSection.Id);
                        section.LookupParameter("Масштаб вида").Set(20);
                    }
                    var views = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Views).Where(x=> x.Name.Contains("Пр") && x.Name.Contains("0.000_")).ToList();
                    foreach (var view in views)
                    {
                        view.Name = view.Name.Replace("_", "");
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
                    var lintelInstances = new FilteredElementCollector(doc, doc.ActiveView.Id)
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
                            (boundingBox.Max.Y + boundingBox.Min.Y)/2,
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
                                       .ToList().Select(x =>x.Elevation).ToList();

            using (Transaction trans = new Transaction(doc, "Добавление перемычек"))
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

                    // Проверяем, активен ли выбранный тип
                    if (!selectedType.IsActive)
                    {
                        selectedType.Activate();
                        doc.Regenerate();
                    }

                    // Получение выбранного типа стены из радиокнопки
                    var selectedWallType = mainViewModel.SelectedWallTypeName;
                    if (selectedWallType == null || !selectedParentElement.Walls.Keys.Select(x=>x.Name).Contains(selectedWallType))
                    {
                        MessageBox.Show("Пожалуйста, выберите тип стены через радиобокс.", "Ошибка");
                        trans.RollBack();
                        return;
                    }

                    // Получаем элементы, связанные с выбранной стеной
                    foreach (var wallElements in selectedParentElement.Walls)
                    {
                        if (wallElements.Key.Name != selectedWallType)
                            continue;
                        List<Element> wallElement = wallElements.Value;  
                        foreach (var element in wallElement)
                        {
                            FamilyInstance newLintel = null;

                            // Получаем уровен 
                            Level level = doc.GetElement(element.LevelId) as Level;

                            // Рассчитываем BoundingBox текущего элемента
                            BoundingBoxXYZ bb = element.get_BoundingBox(null);
                            if (bb == null) continue;

                            XYZ minPoint = bb.Min;
                            XYZ maxPoint = bb.Max;

                            // Увеличиваем BoundingBox вверх для поиска перемычки
                            XYZ searchMinPoint = new XYZ(minPoint.X - 100 / 304.8, minPoint.Y - 100 / 304.8, maxPoint.Z - 500/304.8);
                            XYZ searchMaxPoint = new XYZ(maxPoint.X + 100 / 304.8, maxPoint.Y + 100 / 304.8, maxPoint.Z + 100/304.8); // 100 мм вверх

                            Outline searchOutline = new Outline(searchMinPoint, searchMaxPoint);
                            BoundingBoxIntersectsFilter searchFilter = new BoundingBoxIntersectsFilter(searchOutline);

                            // Поиск существующих перемычек
                            List<Element> lintelCollector = new FilteredElementCollector(doc)
                                .OfClass(typeof(FamilyInstance))
                                .WherePasses(searchFilter).ToList();
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
                                    output += "У элемента " + element.Id + " уже создана перемычка, создание пропущено\n";
                                    continue;
                                }
                            }
                                
                            
                            
                            foreach (ElementId id in listToDel.Distinct())
                            {
                                if (doc.GetElement(id) != null)
                                doc.Delete(id);
                            }

                            double height = 0;
                            XYZ locationPoint = null;
                            XYZ translation = null;
                            if (element is Wall)
                            {
                                var bounding = element.get_BoundingBox(doc.ActiveView);
                                height = bounding.Max.Z - bounding.Min.Z ;
                                Line c = (element.Location as LocationCurve).Curve as Line;
                                locationPoint = (c.GetEndPoint(0) + c.GetEndPoint(1))/2 - level.Elevation * XYZ.BasisZ + height * XYZ.BasisZ;
                            }
                            else
                            { 
                                height = element.LookupParameter("ADSK_Размер_Высота").AsDouble();
                                locationPoint = (element.Location as LocationPoint).Point - level.Elevation * XYZ.BasisZ + height * XYZ.BasisZ;

                            }

                            // Создаем экземпляр перемычки
                            newLintel = doc.Create.NewFamilyInstance(locationPoint, selectedType, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural) as FamilyInstance;
                            XYZ baseOrientation;
                            if (selectedParentElement.SupportType == 1
                                && selectedParentElement.SupportDirection.TryGetValue(element, out XYZ supportDir))
                            {
                                // при одиночной опоре – используем именно её направление
                                baseOrientation = supportDir;
                            }
                            else if (element is Wall w)
                            {
                                baseOrientation = w.Orientation;
                            }
                            else // FamilyInstance
                            {
                                baseOrientation = ((FamilyInstance)element).FacingOrientation;
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
                            if (vm.openingsWithoutLintel.Contains(selectedParentElement))
                                vm.openingsWithoutLintel.Remove(selectedParentElement);
                            if (!vm.openingsWithLintel.Contains(selectedParentElement))
                                vm.openingsWithLintel.Add(selectedParentElement);
                        }
                        break;
                    }
                    trans.Commit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show( ex.Message, "Ошибка");
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
