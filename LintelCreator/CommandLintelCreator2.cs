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
                .Where(f => (doc.GetElement(f.Symbol.Id)).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Перемычки составные")
                        .ToList();

                    // Группировка элементов по типу
                    var groupedElements = framingElements.GroupBy(el => el.Symbol.Id);

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
                .Where(f => (doc.GetElement(f.Symbol.Id)).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Перемычки составные")
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
                                        .Where(f => (doc.GetElement(f.Symbol.Id)).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Перемычки составные")
                        .Where(el => el.LookupParameter("ADSK_Позиция")?.AsString() != null)
                        .ToList();

                    // Группировка перемычек по параметру ADSK_Позиция
                    var groupedElements = framingElements.GroupBy(el => el.LookupParameter("ADSK_Позиция").AsString());

                    // Шаблон для разрезов
                    ViewFamilyType sectionViewType = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault<ViewFamilyType>(x =>
                      ViewFamily.Section == x.ViewFamily);

                    ViewSection viewSection = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSection))
                    .OfType<ViewSection>()
                    .FirstOrDefault(vt => vt.Name == "4_К_Пр");

                    if (sectionViewType == null)
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

                        // Определение центра перемычки
                        XYZ center = (firstElement.Location as LocationPoint).Point;

                        // Определение размера разреза
                        BoundingBoxXYZ boundingBox = firstElement.get_BoundingBox(null);
                        double width = boundingBox.Max.X - boundingBox.Min.X;
                        double height = boundingBox.Max.Z - boundingBox.Min.Z;

                        // Создание разреза
                        ViewSection section = ViewSection.CreateSection(doc, sectionViewType.Id, boundingBox);
                        if (section == null)
                            continue;

                        // Настройка размеров разреза, неправильно строит, изменить
                        BoundingBoxXYZ sectionBox = section.get_BoundingBox(null);
                        sectionBox.Min = new XYZ(-width / 2, -height / 2, 0);
                        sectionBox.Max = new XYZ(width / 2, height / 2, 0);
                        section.CropBox = (sectionBox);

                        // Установка имени разреза
                        string positionName = firstElement.LookupParameter("ADSK_Позиция").AsString();
                        section.Name = positionName;
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
                                        .Where(f => (doc.GetElement(f.Symbol.Id)).get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).AsString() == "Перемычки составные")
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
                        .OfType<FamilySymbol>().FirstOrDefault(tag => tag.FamilyName == "Марка несущего каркаса");
                        //.FirstOrDefault(tag => tag.FamilyName == "ADSK_Марка_Балка" && tag.Name == "Экземпляр_ADSK_Позиция");

                    if (tagType == null)
                    {
                        TaskDialog.Show("Ошибка", "Не найден тип марки 'Экземпляр_ADSK_Позиция' для семейства 'ADSK_Марка_Балка'.");
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
                            (boundingBox.Min.Y + boundingBox.Max.Y) / 2,
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
