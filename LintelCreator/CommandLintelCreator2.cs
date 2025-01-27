using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FerrumAddin.FM;
using FerrumAddin.LintelCreator;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FerrumAddin
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class CommandLintelCreator2 : IExternalCommand
    {
        public static ExternalEvent lintelCreateEvent;
        public static ExternalEvent lintelNumerateEvent;
        public static ExternalEvent nestedElementsNumberingEvent;
        public static ExternalEvent createSectionsEvent;
        public static ExternalEvent tagLintelsEvent;
        public static ExternalEvent placeSectionsEvent;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;

            lintelCreateEvent = ExternalEvent.Create(new LintelCreate());
            lintelNumerateEvent = ExternalEvent.Create(new LintelNumerate());
            nestedElementsNumberingEvent = ExternalEvent.Create(new NestedElementsNumbering());
            createSectionsEvent = ExternalEvent.Create(new CreateSectionsForLintels());
            tagLintelsEvent = ExternalEvent.Create(new TagLintels());
            placeSectionsEvent = ExternalEvent.Create(new PlaceSections());


            List<ElementId> windowsAndDoorsSelectionIds = sel.GetElementIds().ToList();
            List<FamilyInstance> windowsAndDoorsList = new List<FamilyInstance>();
            windowsAndDoorsList = GetWindowsAndDoorsFromCurrentSelection(doc, windowsAndDoorsSelectionIds);

            if (windowsAndDoorsList.Count == 0)
            {

                FilteredElementCollector filter = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType();

                foreach (Element el in filter)
                {
                    windowsAndDoorsList.Add(el as FamilyInstance);
                }

                filter = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Windows).WhereElementIsNotElementType();

                foreach (Element el in filter)
                {
                    windowsAndDoorsList.Add(el as FamilyInstance);
                }
            }
            List<ParentElement> list = GroupWindowsAndDoors(windowsAndDoorsList, doc);
            List<Family> lintelFamilysList = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory.Id.IntegerValue.Equals((int)BuiltInCategory.OST_StructuralFraming))
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

            LintelCreatorForm2 form = new LintelCreatorForm2(doc, list, lintelFamilysList);
            form.Show();
           
            return Result.Succeeded;
        }
        private List<ParentElement> GroupWindowsAndDoors(List<FamilyInstance> windowsAndDoorsList, Document doc)
        {
            var groupedElements = windowsAndDoorsList
                .GroupBy(el => new
                {
                    el.Symbol.FamilyName, // Имя семейства
                    el.Symbol.Name,       // Имя типа
                    Width = el.LookupParameter("ADSK_Размер_Ширина")?.AsValueString()
                })
                .Select(group => new ParentElement
                {
                    Name = group.Key.FamilyName,
                    TypeName = group.Key.Name,
                    Width = group.First().LookupParameter("ADSK_Размер_Ширина")?.AsValueString(),
                    Walls = group
                        .Where(el => el.Host is Wall)
                        .GroupBy(el =>
                        {
                            var wall = el.Host as Wall;
                            return wall?.GetTypeId();
                        })
                        .Where(wallGroup => wallGroup.Key != null)
                        .ToDictionary(
                            wallGroup => doc.GetElement(wallGroup.Key) as WallType,
                            wallGroup => wallGroup
                                .Cast<Element>()
                                .ToList()
                        )
                })
                .ToList();

            return groupedElements;
        }




        private static List<FamilyInstance> GetWindowsAndDoorsFromCurrentSelection(Document doc, List<ElementId> selIds)
        {
            List<FamilyInstance> tempLintelsList = new List<FamilyInstance>();
            foreach (ElementId lintelId in selIds)
            {
                if (doc.GetElement(lintelId) is FamilyInstance
                    && null != doc.GetElement(lintelId).Category
                    && (doc.GetElement(lintelId).Category.Id.IntegerValue.Equals((int)BuiltInCategory.OST_Windows)
                    || doc.GetElement(lintelId).Category.Id.IntegerValue.Equals((int)BuiltInCategory.OST_Doors)))
                {
                    tempLintelsList.Add(doc.GetElement(lintelId) as FamilyInstance);
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
                TaskDialog.Show("Ошибка", "Активный вид не является листом.");
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
            bool check = LintelCreatorForm2.check;
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
                    TaskDialog.Show("Ошибка", ex.Message);
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
                    TaskDialog.Show("Ошибка", ex.Message);
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
                        TaskDialog.Show("Ошибка", "Не найден разрез 'Номер вида'.");
                        trans.RollBack();
                        return;
                    }

                    ViewSection viewSection = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .OfType<ViewSection>()
                    .FirstOrDefault(vt => vt.Name == "4_К_Пр");

                    if (viewSection == null)
                    {
                        TaskDialog.Show("Ошибка", "Не найден шаблон разреза '4_К_Пр'.");
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
                    TaskDialog.Show("Ошибка", ex.Message);
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
                TaskDialog.Show("Ошибка", "Перейдите на план этажа для создания разрезов");
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
                        TaskDialog.Show("Ошибка", "Не найдено ни одной перемычки для маркировки.");
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
                        TaskDialog.Show("Ошибка", "Не найден тип марки 'Экземпляр_ADSK_Позиция' для семейства 'ADSK_Марка_Балка'.");
                        trans.RollBack();
                        return;
                    }
                    if (tagType2 == null)
                    {
                        TaskDialog.Show("Ошибка", "Не найден тип марки 'ADSK_Проектная_без всего' для семейства 'Высотные отметки'.");
                        trans.RollBack();
                        return;
                    }

                    if (tagType2Vert == null)
                    {
                        TaskDialog.Show("Ошибка", "Не найден тип марки 'ADSK_Проектная_без всего_(В)' для семейства 'Высотные отметки'.");
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
                            TaskDialog.Show("Ошибка", "Не удалось создать марку для перемычки.");
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
                                TaskDialog.Show("Ошибка", "Не удалось создать высотную отметку для перемычки.");
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
                                TaskDialog.Show("Ошибка", "Не удалось создать высотную отметку для перемычки.");
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
                    TaskDialog.Show("Ошибка", ex.Message);
                    trans.RollBack();
                }
            }
        }

        public string GetName()
        {
            return "Маркировка перемычек";
        }
    }

    public class LintelCreate : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            string output = "";
            Document doc = app.ActiveUIDocument.Document;
            UIDocument uidoc = app.ActiveUIDocument;

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
                    var mainViewModel = LintelCreatorForm2.MainViewModel;
                    if (mainViewModel == null)
                    {
                        TaskDialog.Show("Ошибка", "Не удалось получить данные из окна.");
                        trans.RollBack();
                        return;
                    }

                    // Получение выбранного семейства, типа и элемента
                    var selectedFamily = mainViewModel.SelectedFamily;
                    var selectedType = mainViewModel.SelectedType;
                    var selectedParentElement = mainViewModel.SelectedParentElement;

                    if (selectedFamily == null || selectedType == null || selectedParentElement == null)
                    {
                        TaskDialog.Show("Ошибка", "Пожалуйста, выберите семейство, тип перемычки и элемент.");
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
                        TaskDialog.Show("Ошибка", "Пожалуйста, выберите тип стены через радиобокс.");
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

                            // Получаем уровень текущего элемента
                            Level level = doc.GetElement(element.LevelId) as Level;

                            // Рассчитываем BoundingBox текущего элемента
                            BoundingBoxXYZ bb = element.get_BoundingBox(null);
                            if (bb == null) continue;

                            XYZ minPoint = bb.Min;
                            XYZ maxPoint = bb.Max;

                            // Увеличиваем BoundingBox вверх для поиска перемычки
                            XYZ searchMinPoint = new XYZ(minPoint.X, minPoint.Y, maxPoint.Z);
                            XYZ searchMaxPoint = new XYZ(maxPoint.X, maxPoint.Y, maxPoint.Z + 100/304.8); // 100 мм вверх

                            Outline searchOutline = new Outline(searchMinPoint, searchMaxPoint);
                            BoundingBoxIntersectsFilter searchFilter = new BoundingBoxIntersectsFilter(searchOutline);

                            // Поиск существующих перемычек
                            FilteredElementCollector lintelCollector = new FilteredElementCollector(doc)
                                .OfClass(typeof(FamilyInstance))
                                .WherePasses(searchFilter);
                            List<ElementId> listToDel = new List<ElementId>();
                            // Если есть существующая перемычка, пропускаем создание новой
                            if (lintelCollector.Any(x=>x.LookupParameter("ADSK_Группирование")?.AsString() == "ПР"))
                            {
                                if (LintelCreatorForm2.recreate)
                                {
                                    foreach (Element e in lintelCollector.Where(x=> x.LookupParameter("ADSK_Группирование")?.AsString() == "ПР"))
                                    {
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
                            double height = element.LookupParameter("ADSK_Размер_Высота").AsDouble();
                            XYZ locationPoint = (element.Location as LocationPoint).Point - level.Elevation * XYZ.BasisZ + height * XYZ.BasisZ;

                            // Создаем экземпляр перемычки
                            newLintel = doc.Create.NewFamilyInstance(locationPoint, selectedType, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural) as FamilyInstance;
                            XYZ translation = new XYZ(0, wallElements.Key.Width/2, 0);

                            // Проверяем ориентацию и выполняем поворот, если необходимо
                            if (!(element as FamilyInstance).FacingOrientation.IsAlmostEqualTo(newLintel.FacingOrientation))
                            {
                                Line rotateAxis = Line.CreateBound((newLintel.Location as LocationPoint).Point, (newLintel.Location as LocationPoint).Point + 1 * XYZ.BasisZ);
                                double u1 = (element as FamilyInstance).FacingOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                                double u2 = newLintel.FacingOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                                double rotateAngle = (u2 - u1);

                                ElementTransformUtils.RotateElement(doc, newLintel.Id, rotateAxis, rotateAngle);

                                translation = new XYZ(wallElements.Key.Width/2, 0, 0);
                            }
                            ElementTransformUtils.MoveElement(doc, newLintel.Id, translation);
                            newLintel.LookupParameter("ADSK_Группирование").Set("ПР");
                            int intLev = level.Elevation >= 0 ? levels.IndexOf(level.Elevation) + 1 : -1;
                            newLintel.LookupParameter("ZH_Этаж_Числовой").SetValueString(intLev.ToString());
                            newLintel.LookupParameter("Видимость.Глубина").SetValueString("2000");
                        }
                        break;
                    }

                    trans.Commit();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Ошибка", ex.Message);
                    trans.RollBack();
                }
            }
            TaskDialog.Show("Отчет", output);
        }


        public string GetName()
        {
            return "xxx";
        }
    }
}
