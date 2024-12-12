using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
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
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            Selection sel = commandData.Application.ActiveUIDocument.Selection;

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

            LintelCreatorForm2 form = new LintelCreatorForm2(doc, list);
            form.Show();
           
            return Result.Succeeded;
        }

        private List<ParentElement> GroupWindowsAndDoors(List<FamilyInstance> windowsAndDoorsList, Document doc)
        {
            // Группируем окна и двери по семействам
            var groupedElements = windowsAndDoorsList
                .GroupBy(el => el.Symbol.FamilyName) // Группировка по имени семейства
                .Select(group => new ParentElement
                {
                    Name = group.Key, // Имя семейства
                    TypeName = group.First().Symbol.Name, // Имя типа семейства
                    Width = group.First().get_Parameter(BuiltInParameter.WINDOW_WIDTH)?.AsValueString()
                            ?? group.First().get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsValueString()
                            ?? "Не указано", // Ширина окна/двери, если доступно
                    ChildElements = new ObservableCollection<ChildElement>(
                        group.Select(el =>
                        {
                            // Получаем информацию о стене-хозяине
                            string wallType = "Неизвестный тип стены";
                            if (el.Host is Wall hostWall)
                            {
                                // Получаем имя типа стены
                                var wallTypeElement = doc.GetElement(hostWall.GetTypeId()) as ElementType;
                                if (wallTypeElement != null)
                                {
                                    wallType = wallTypeElement.Name;
                                }
                            }

                            return new ChildElement
                            {
                                WallType = wallType
                            };
                        }).ToList()
                    ),
                    Walls = group
                        .Where(el => el.Host is Wall) // Только элементы, у которых есть хозяин-стена
                        .GroupBy(el =>
                        {
                            // Используем существующие типы стен
                            var hostWall = el.Host as Wall;
                            if (hostWall == null)
                                return null;

                            return doc.GetElement(hostWall.GetTypeId()) as WallType;
                        })
                        .Where(wallGroup => wallGroup.Key != null)
                        .ToDictionary(
                            wallGroup => wallGroup.Key, // Тип стены
                            wallGroup => wallGroup.ToList<Element>()
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
}
