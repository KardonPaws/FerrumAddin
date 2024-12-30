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

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;

            lintelCreateEvent = ExternalEvent.Create(new LintelCreate());
            lintelNumerateEvent = ExternalEvent.Create(new LintelNumerate());
            nestedElementsNumberingEvent = ExternalEvent.Create(new NestedElementsNumbering());
            createSectionsEvent = ExternalEvent.Create(new CreateSectionsForLintels());
            tagLintelsEvent = ExternalEvent.Create(new TagLintels());


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
                        .ToList();

                    // Группировка элементов по типу
                    var groupedElements = framingElements.GroupBy(el => el.Symbol.Id);

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
                                int nestedCounter = 1;
                                foreach (var aSubElemId in subElements)
                                {
                                    var nestedElement = doc.GetElement(aSubElemId);
                                    if (nestedElement is FamilyInstance)
                                    {
                                        var positionParam = nestedElement.LookupParameter("ADSK_Позиция");
                                        if (positionParam != null && positionParam.IsReadOnly == false)
                                        {
                                            positionParam.Set(nestedCounter.ToString());
                                        }
                                        nestedCounter++;
                                    }
                                }
                            }
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
                    var groupedElements = framingElements.GroupBy(el => el.LookupParameter("ADSK_Позиция").AsString());

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
                        XYZ center = (firstElement.get_BoundingBox(null).Max + firstElement.get_BoundingBox(null).Min)/2;

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
                        double elementDepth = firstElement.get_BoundingBox(null).Max.Z - firstElement.get_BoundingBox(null).Min.Z;
                        
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
                        if (lower0)
                            section.Name = positionName + " ниже 0.000";
                        else
                            section.Name = positionName + " выше 0.000";
                        section.LookupParameter("Шаблон вида").Set(viewSection.Id);
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
                            (boundingBox.Max.Y + 495/304.8),
                            boundingBox.Max.Z
                        );

                        // Создание марки
                        IndependentTag newTag = IndependentTag.Create(
                            doc,
                            tagType.Id,
                            doc.ActiveView.Id,
                            new Reference(lintel),
                            false,
                            TagOrientation.Horizontal,
                            centerTop
                        );

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

                        // Создание марки
                        //SpotDimension newTag2 = doc.Create.NewSpotElevation(
                        //    doc.ActiveView,
                        //    lintel.Location,
                            
                        //);

                        //if (newTag2 == null)
                        //{
                        //    TaskDialog.Show("Ошибка", "Не удалось создать высотную отметку для перемычки.");
                        //    continue;
                        //}

                        //newTag2.SpotDimensionType = tagType2;
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
            Document doc = app.ActiveUIDocument.Document;
            UIDocument uidoc = app.ActiveUIDocument;

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

                            // Рассчитываем координаты для размещения перемычки
                            BoundingBoxXYZ bb = element.get_BoundingBox(null);
                            double height = bb.Max.Z - bb.Min.Z;
                            XYZ locationPoint = (element.Location as LocationPoint).Point - level.Elevation * XYZ.BasisZ + height * XYZ.BasisZ;

                            // Создаем экземпляр перемычки
                            newLintel = doc.Create.NewFamilyInstance(locationPoint, selectedType, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural) as FamilyInstance;

                            // Проверяем ориентацию и выполняем поворот, если необходимо
                            if (!(element as FamilyInstance).FacingOrientation.IsAlmostEqualTo(newLintel.FacingOrientation))
                            {
                                Line rotateAxis = Line.CreateBound((newLintel.Location as LocationPoint).Point, (newLintel.Location as LocationPoint).Point + 1 * XYZ.BasisZ);
                                double u1 = (element as FamilyInstance).FacingOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                                double u2 = newLintel.FacingOrientation.AngleOnPlaneTo(XYZ.BasisX, XYZ.BasisZ);
                                double rotateAngle = (u2 - u1);

                                ElementTransformUtils.RotateElement(doc, newLintel.Id, rotateAxis, rotateAngle);
                            }
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
        }


        public string GetName()
        {
            return "xxx";
        }
    }
}
