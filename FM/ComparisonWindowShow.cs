using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using FerrumAddin.FM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FerrumAddin.FM
{

    [Transaction(TransactionMode.Manual)]
    public class ComparisonWindowShow : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                changeTypesEv = ExternalEvent.Create(new ChangeTypes());
                ComparisonWindow cw = new ComparisonWindow(commandData);
                cw.Show();
            }
            catch (Exception ex)
            {
                // show error info dialog
                TaskDialog.Show("Info Message", ex.Message);
            }
            // return result
            return Result.Succeeded;
        }
        public static ExternalEvent changeTypesEv;
    }
    public class ChangeTypes : IExternalEventHandler
    {

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            List<MenuItem> SelectedMenuItems = ComparisonWindow.selMen;
            List<MenuItem> SelectedFamilies = ComparisonWindow.selFam;
            string output = "";
            App.AllowLoad = true;
            if (SelectedFamilies.Count() != SelectedMenuItems.Count())
            {
                TaskDialog.Show("Внимание", "Количество выбранных элементов не совпадает");
            }
            else
            {
                var sortedMenuItems = SelectedMenuItems.Select((item, index) => new { Item = item, Index = index })
                                           .OrderBy(x => x.Item.Path)
                                           .ToList();
                List<MenuItem> SelectedMenuItems1 = sortedMenuItems.Select(x => x.Item).ToList();
                List<MenuItem> SelectedFamilies1 = sortedMenuItems.Select(x => SelectedFamilies[x.Index]).ToList();
                using (Transaction trans = new Transaction(doc, "Сопоставление семейств"))
                {

                    trans.Start();
                    FailureHandlingOptions failureOptions = trans.GetFailureHandlingOptions();
                    failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                    trans.SetFailureHandlingOptions(failureOptions);
                    failureOptions.SetClearAfterRollback(true); // Опционально
                    Document tempDoc = null;
                    for (int i = 0; i < SelectedFamilies.Count && i < SelectedMenuItems.Count; i++)
                    {
                        var selectedFamily = SelectedFamilies1[i];
                        var menuItem = SelectedMenuItems1[i];

                        if (Path.GetExtension(menuItem.Path).ToLower() == ".rvt")
                        {
                            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(menuItem.Path);
                            OpenOptions openOptions = new OpenOptions();
                            if (tempDoc == null)
                                tempDoc = doc.Application.OpenDocumentFile(modelPath, openOptions);

                            ElementId sourceFamily = new FilteredElementCollector(tempDoc)
                        .OfCategory(selectedFamily.RevitCategory)
                        .WhereElementIsElementType()
                        .Where(x => x.Name == menuItem.Name)
                        .Select(x => x.Id).First();
                            if (sourceFamily != null)
                            {

                                CopyPasteOptions options = new CopyPasteOptions();
                                options.SetDuplicateTypeNamesHandler(new MyCopyHandler());
                                ICollection<ElementId> copiedElements = ElementTransformUtils.CopyElements(
                                    tempDoc,
                                    new List<ElementId> { sourceFamily },
                                    doc,
                                    Transform.Identity,
                                    options
                                );
                                if (i + 1 == SelectedMenuItems1.Count() || SelectedMenuItems1[i + 1].Path != SelectedMenuItems1[i].Path)
                                    tempDoc.Close(false);

                                ElementId copiedTypeId = copiedElements.First();
                                ElementType copiedType = doc.GetElement(copiedTypeId) as ElementType;

                                // Ищем существующий тип с таким же именем в целевом документе
                                ElementType existingType = FindTypeByNameAndClass(doc, selectedFamily.Name, copiedType.GetType());
                                ElementType existingOriginalType = FindTypeByNameAndClass(doc, menuItem.Name, copiedType.GetType());

                                if (existingType != null)
                                {
                                    // Заменяем все элементы, использующие старый тип, на новый тип
                                    ReplaceElementsType(doc, existingType.Id, copiedType.Id);
                                }
                                if (existingOriginalType != null)
                                {
                                    ReplaceElementsType(doc, existingOriginalType.Id, copiedType.Id);
                                    string exName = doc.GetElement(existingOriginalType.Id).Name;
                                    //doc.Delete(existingOriginalType.Id);
                                    doc.GetElement(copiedTypeId).Name = exName;
                                }

                            }
                            else
                            {
                                output += ("Не найден тип " + menuItem.Name + "\n");
                            }

                        }
                        else if (Path.GetExtension(menuItem.Path).ToLower() == ".rfa")
                        {
                            Family family;
                            MyFamilyLoadOptions loadOptions = new MyFamilyLoadOptions();
                            if (doc.LoadFamily(menuItem.Path, loadOptions, out family))
                            {
                                var familySymbol = family.GetFamilySymbolIds().Select(id => doc.GetElement(id) as FamilySymbol).FirstOrDefault();
                                if (familySymbol != null && !familySymbol.IsActive)
                                {
                                    familySymbol.Activate();
                                    doc.Regenerate();
                                }
                                List<FamilyInstance> instances = new List<FamilyInstance>();

                                foreach (var instance in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
                                {
                                    if (instance.Symbol.Name == selectedFamily.Name || instance.Symbol.Name == menuItem.Name)
                                    {
                                        instances.Add(instance);
                                    }
                                }
                                foreach (FamilyInstance instance in instances)
                                {
                                    var parameters = instance.Parameters.Cast<Parameter>()
                                    .Where(p => !p.IsReadOnly)
                                    .ToDictionary(p => p.Definition.Name, p => new { p.StorageType, Value = GetParameterValue(p) });

                                    instance.Symbol = familySymbol;

                                    foreach (var param in parameters)
                                    {
                                        if (param.Key != "Семейство и типоразмер" && param.Key != "Семейство" && param.Key != "Код типа" && param.Key != "Тип")
                                        {
                                            var newParam = instance.LookupParameter(param.Key);
                                            if (newParam != null && newParam.StorageType == param.Value.StorageType)
                                            {
                                                SetParameterValue(newParam, param.Value.Value);
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Family fam = new FilteredElementCollector(doc).OfClass(typeof(Family)).Where(x => x.Name == menuItem.Name).Cast<Family>().FirstOrDefault();
                                if (fam == null)
                                {
                                    output += ("Не найден тип " + menuItem.Name + "\n");
                                    break;
                                }
                                var type = fam.GetFamilySymbolIds().Select(id => doc.GetElement(id) as FamilySymbol).FirstOrDefault();
                                if (type != null && !type.IsActive)
                                {
                                    type.Activate();
                                    doc.Regenerate();
                                }
                                List<FamilyInstance> instances = new List<FamilyInstance>();

                                foreach (var instance in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
                                {
                                    if (instance.Symbol.Name == selectedFamily.Name || instance.Symbol.Name == menuItem.Name)
                                    {

                                        instances.Add(instance);
                                    }
                                }
                                foreach (FamilyInstance instance in instances)
                                {
                                    var parameters = instance.Parameters.Cast<Parameter>()
                                    .Where(p => !p.IsReadOnly)
                                    .ToDictionary(p => p.Definition.Name, p => new { p.StorageType, Value = GetParameterValue(p) });

                                    instance.Symbol = type;

                                    foreach (var param in parameters)
                                    {
                                        if (param.Key != "Семейство и типоразмер" && param.Key != "Семейство" && param.Key != "Код типа" && param.Key != "Тип")
                                        {
                                            var newParam = instance.LookupParameter(param.Key);
                                            if (newParam != null && newParam.StorageType == param.Value.StorageType)
                                            {
                                                SetParameterValue(newParam, param.Value.Value);
                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }

                    //App.application.DialogBoxShowing += new EventHandler<DialogBoxShowingEventArgs>(App.a_DialogBoxShowing);

                    trans.Commit();

                }
            }
            if (output == "")
            {
                output = "Выполнено";
            }
            App.AllowLoad = false;
            //App.application.DialogBoxShowing -= new EventHandler<DialogBoxShowingEventArgs>(App.a_DialogBoxShowing);
            TaskDialog.Show("Отчет", output);
        }

        private object GetParameterValue(Parameter param)
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString();
                case StorageType.Double:
                    return param.AsDouble();
                case StorageType.Integer:
                    return param.AsInteger();
                case StorageType.ElementId:
                    return param.AsElementId();
                default:
                    return null;
            }
        }

        // Метод для установки значения параметра
        private void SetParameterValue(Parameter param, object value)
        {
            if (!param.IsReadOnly)
                switch (param.StorageType)
                {
                    case StorageType.String:
                        param.Set(value as string);
                        break;
                    case StorageType.Double:
                        param.Set((double)value);
                        break;
                    case StorageType.Integer:
                        param.Set((int)value);
                        break;
                    case StorageType.ElementId:
                        param.Set((ElementId)value);
                        break;
                }
        }

        // Метод для поиска типа по имени и классу в целевом документе
        private ElementType FindTypeByNameAndClass(Document doc, string typeName, Type typeClass)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeClass)
                .Cast<ElementType>()
                .FirstOrDefault(e => e.Name.Equals(typeName, StringComparison.InvariantCultureIgnoreCase));
        }

        // Метод для замены типа у всех элементов
        private void ReplaceElementsType(Document doc, ElementId oldTypeId, ElementId newTypeId)
        {
            // Находим все элементы, использующие старый тип
            List<Element> collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() == oldTypeId).ToList();

            foreach (Element elem in collector)
            {
                var parameters = elem.Parameters.Cast<Parameter>()
                                    .Where(p => !p.IsReadOnly)
                                    .ToDictionary(p => p.Definition.Name, p => new { p.StorageType, Value = GetParameterValue(p) });
                // Устанавливаем новый тип
                elem.ChangeTypeId(newTypeId);

                foreach (var param in parameters)
                {
                    if (param.Key != "Семейство и типоразмер" && param.Key != "Семейство" && param.Key != "Код типа" && param.Key != "Тип")
                    {
                        var newParam = elem.LookupParameter(param.Key);
                        if (newParam != null && newParam.StorageType == param.Value.StorageType)
                        {
                            SetParameterValue(newParam, param.Value.Value);
                        }
                    }
                }

            }
        }

        public string GetName()
        {
            return "xxx";
        }
    }
}
