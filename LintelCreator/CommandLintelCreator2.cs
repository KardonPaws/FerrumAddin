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
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;

            lintelCreateEvent = ExternalEvent.Create(new LintelCreate());

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
