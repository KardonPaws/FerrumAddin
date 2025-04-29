using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using FerrumAddinDev.FM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace FerrumAddinDev.FM
{
    [Transaction(TransactionMode.Manual)]
    public class ComparisonWindowShow2 : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                changeTypesEv = ExternalEvent.Create(new ChangeTypes2());
                ComparisonWindow cw = new ComparisonWindow(commandData);
                cw.Show();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Info Message", ex.Message);
            }
            return Result.Succeeded;
        }
        public static ExternalEvent changeTypesEv;
    }

    public class ChangeTypes2 : IExternalEventHandler
    {
        private const string TempFolderPath = "C:\\Temp\\RevitRFA"; // Укажите папку для временных файлов

        public void Execute(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            List<MenuItem> SelectedMenuItems = ComparisonWindow.selMen;
            List<MenuItem> SelectedFamilies = ComparisonWindow.selFam;
            string output = "";
            App.AllowLoad = true;

            if (SelectedFamilies.Count != SelectedMenuItems.Count)
            {
                TaskDialog.Show("Внимание", "Количество выбранных элементов не совпадает");
                return;
            }

            Directory.CreateDirectory(TempFolderPath);

            using (Transaction trans = new Transaction(doc, "Сопоставление семейств"))
            {
                trans.Start();

                FailureHandlingOptions failureOptions = trans.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                trans.SetFailureHandlingOptions(failureOptions);

                for (int i = 0; i < SelectedFamilies.Count; i++)
                {
                    var selectedFamily = SelectedFamilies[i];
                    var menuItem = SelectedMenuItems[i];

                    if (Path.GetExtension(menuItem.Path).ToLower() == ".rfa")
                    {
                        string tempFilePath = Path.Combine(TempFolderPath, selectedFamily.Name + ".rfa");
                        File.Copy(menuItem.Path, tempFilePath, true);

                        Family family;
                        if (doc.LoadFamily(tempFilePath, new MyFamilyLoadOptions(), out family))
                        {
                            FamilySymbol familySymbol = family.GetFamilySymbolIds()
                                .Select(id => doc.GetElement(id) as FamilySymbol)
                                .FirstOrDefault(x => x.Name == selectedFamily.Name);

                            if (familySymbol != null && !familySymbol.IsActive)
                            {
                                familySymbol.Activate();
                                doc.Regenerate();
                            }
                            family.Name = menuItem.FamilyName;
                        }

                        File.Delete(tempFilePath);
                    }
                    else
                    {
                        ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(menuItem.Path);
                        OpenOptions openOptions = new OpenOptions();

                        Document tempDoc = doc.Application.OpenDocumentFile(modelPath, openOptions);

                        ElementId sourceFamilyId = new FilteredElementCollector(tempDoc)
                            .OfCategory(selectedFamily.RevitCategory)
                            .WhereElementIsElementType()
                            .FirstOrDefault(x => x.Name == menuItem.Name)?.Id;

                        if (sourceFamilyId != null)
                        {
                            ElementType sourceFamily = tempDoc.GetElement(sourceFamilyId) as ElementType;
                            ElementType existingType = FindTypeByNameAndClass(doc, selectedFamily.Name, sourceFamily.GetType());

                            if (existingType != null)
                            {
                                if (sourceFamily is WallType)
                                {
                                    ApplyWallStructureParameters(tempDoc, sourceFamily, existingType);
                                }
                                else
                                {
                                    CopyPasteOptions options = new CopyPasteOptions();
                                    options.SetDuplicateTypeNamesHandler(new MyCopyHandler());
                                    ICollection<ElementId> copiedElements = ElementTransformUtils.CopyElements(
      tempDoc,
      new List<ElementId> { sourceFamily.Id },
      doc,
      Transform.Identity,
      options
  );
                                    ReplaceElementsType(doc, existingType.Id, copiedElements.First());
                                }
                            }
                        }

                        tempDoc.Close(false);
                    }
                }

                trans.Commit();
            }

            output = output == "" ? "Выполнено" : output;
            App.AllowLoad = false;
            TaskDialog.Show("Отчет", output);
        }

        private void ApplyWallStructureParameters(Document sourceDoc, ElementType source, ElementType target)
        {
            if (source is WallType sourceWallType && target is WallType targetWallType)
            {
                CompoundStructure sourceStructure = sourceWallType.GetCompoundStructure();
                CompoundStructure targetStructure = targetWallType.GetCompoundStructure();

                if (sourceStructure != null && targetStructure != null)
                {
                    targetStructure = sourceStructure;
                    targetWallType.SetCompoundStructure(targetStructure);
                }

                foreach (Parameter sourceParam in source.Parameters)
                {
                    if (!sourceParam.IsReadOnly)
                    {
                        Parameter targetParam = target.LookupParameter(sourceParam.Definition.Name);
                        if (targetParam != null && targetParam.StorageType == sourceParam.StorageType)
                        {
                            object value = GetParameterValue(sourceParam);
                            SetParameterValue(targetParam, value);
                        }
                    }
                }
            }
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

        private void SetParameterValue(Parameter param, object value)
        {
            if (!param.IsReadOnly)
            {
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
        }

        private ElementType FindTypeByNameAndClass(Document doc, string typeName, Type typeClass)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeClass)
                .Cast<ElementType>()
                .FirstOrDefault(e => e.Name.Equals(typeName, StringComparison.InvariantCultureIgnoreCase));
        }

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
            return "ChangeTypesHandler";
        }
    }
}
