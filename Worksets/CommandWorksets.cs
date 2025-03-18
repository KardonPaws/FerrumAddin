#region usings
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB; 
using Autodesk.Revit.UI;
using System.IO;
using System.Xml;
#endregion

namespace FerrumAddin
{

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    class CommandWorksets : IExternalCommand
    {
        public static bool byModel;

        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {

            Document doc = commandData.Application.ActiveUIDocument.Document;

            if(!doc.IsWorkshared)
            {
                message = "Файл не является файлом совместной работы";
                Debug.WriteLine("File os not workshared document");
                return Result.Failed; ;
            }
            FerrumAddin.Worksets.Workset workset = new Worksets.Workset();
            workset.ShowDialog();
            //считываю список рабочих наборов


            System.Windows.Forms.OpenFileDialog dialog = new System.Windows.Forms.OpenFileDialog();
            dialog.InitialDirectory = "B:\\!Automation\\Velcon_Plugin_CS\\WorkSets\\";
            dialog.Multiselect = false;
            dialog.Filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*";
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;
            string xmlFilePath = dialog.FileName;
            Debug.WriteLine("Xml path: " + xmlFilePath);

            InfosStorage storage = new InfosStorage();
            System.Xml.Serialization.XmlSerializer serializer =
                new System.Xml.Serialization.XmlSerializer(typeof(InfosStorage));
            using (StreamReader r = new StreamReader(xmlFilePath))
            {
                storage = (InfosStorage)serializer.Deserialize(r);
            }
            
            if(storage == null)
            {
                string errormsg = "Unable to deserialize: " + xmlFilePath.Replace("\\", " \\");
                Debug.WriteLine(errormsg);
                throw new Exception(errormsg);
            }
            Debug.WriteLine("Deserialize success");

            using (Transaction t = new Transaction(doc))
            {
                t.Start("Создание рабочих наборов");

                Debug.WriteLine("Start worksets by category");
                foreach (WorksetByCategory wb in storage.worksetsByCategory)
                {
                    Debug.WriteLine("Current workset: " + wb.WorksetName);
                    Workset wset = wb.GetWorkset(doc);
                    List<BuiltInCategory> cats = wb.revitCategories;
                    if (cats == null) continue;
                    if (cats.Count == 0) continue;

                    foreach (BuiltInCategory bic in cats)
                    {
                        List<Element> elems = new List<Element>();
                        if (byModel == false)
                        {
                            elems = new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType()
                            .ToElements()
                            .ToList();
                        }
                        else
                        {
                            elems = new FilteredElementCollector(doc)
                            .OfCategory(bic)
                            .WhereElementIsNotElementType()
                            .ToElements()
                            .ToList();
                        }
                        foreach (Element elem in elems)
                        {
                            WorksetBy.SetWorkset(elem, wset);
                        }
                    }
                }

                Debug.WriteLine("Start worksets by family names");
                List<FamilyInstance> famIns = new List<FamilyInstance>();
                if (byModel == false)
                {
                    famIns = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .ToList();
                }
                else
                {
                    famIns = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>()
                        .ToList();
                }
                Debug.WriteLine("Family instances found: " + famIns.Count);
                foreach (WorksetByFamily wb in storage.worksetsByFamily)
                {
                    Debug.WriteLine("Current workset:" + wb.WorksetName);
                    Workset wset = wb.GetWorkset(doc);

                    List<string> families = wb.FamilyNames;
                    if (families == null) continue;
                    if (families.Count == 0) continue;

                    foreach (string familyName in families)
                    {
                        List<FamilyInstance> curFamIns = famIns
                            .Where(f => f.Symbol.FamilyName.ToLower().Contains(familyName))
                            .ToList();
                        
                        foreach (FamilyInstance fi in curFamIns)
                        {
                            WorksetBy.SetWorkset(fi, wset);
                        }
                    }
                }

                Debug.WriteLine("Start worksets by type names");
                List<Element> allElems = new List<Element>();
                if (byModel == false)
                {
                    allElems = new FilteredElementCollector(doc, doc.ActiveView.Id)
                            .WhereElementIsNotElementType()
                            .Cast<Element>()
                            .ToList();
                }
                else
                {
                    allElems = new FilteredElementCollector(doc)
                            .WhereElementIsNotElementType()
                            .Cast<Element>()
                            .ToList();
                }
                Debug.WriteLine("Elements found: " + allElems.Count);

                foreach (WorksetByType wb in storage.worksetsByType)
                {
                    Debug.WriteLine("Current workset:" + wb.WorksetName);
                    Workset wset = wb.GetWorkset(doc);
                    List<string> typeNames = wb.TypeNames;
                    if (typeNames == null) continue;
                    if (typeNames.Count == 0) continue;

                    foreach(string typeName in typeNames)
                    {
                        foreach(Element elem in allElems)
                        {
                            ElementId typeId = elem.GetTypeId();
                            if (typeId == null || typeId == ElementId.InvalidElementId) continue;
                            ElementType elemType = doc.GetElement(typeId) as ElementType;
                            if (elemType == null) continue;
                            Debug.WriteLine("Element id: " + elem.Id.IntegerValue + ", TypeName: " + elemType.Name);

                            if (elemType.Name.ToLower().Contains(typeName))
                            {
                                WorksetBy.SetWorkset(elem, wset);
                            }
                        }
                    }
                }
                
                if(storage.worksetByParameter != null)
                {
                    Debug.WriteLine("Start worksets by parameters");
                    foreach (WorksetByParameter wb in storage.worksetByParameter)
                    {
                        string wsetParamValue = wb.WorksetName;
                        string paramName = wb.ParameterNames[0];
                        foreach (Element elem in allElems)
                        {
                            Parameter p = elem.LookupParameter(paramName);
                            if (p == null) continue;
                            if (!p.HasValue) continue;
                            if (p.StorageType == StorageType.String)
                                for (int i = 1; i < wb.ParameterNames.Count; i++)
                                {
                                    try
                                    {

                                        string s = p.AsValueString();
                                        if (s != null && s != "")
                                        {
                                            if (p.AsValueString().ToLower().Contains(wb.ParameterNames[i].ToLower()))
                                            {
                                                Workset wsetByparamval = WorksetBy.GetOrCreateWorkset(doc, wsetParamValue);
                                                WorksetBy.SetWorkset(elem, wsetByparamval);
                                            }
                                        }
                                        else if (p.AsString().ToLower().Contains(wb.ParameterNames[i].ToLower()))
                                        {
                                            Workset wsetByparamval = WorksetBy.GetOrCreateWorkset(doc, wsetParamValue);
                                            WorksetBy.SetWorkset(elem, wsetByparamval);
                                        }
                                    }
                                    catch
                                    {
                                        if (p.AsString().ToLower().Contains(wb.ParameterNames[i].ToLower()))
                                        {
                                            Workset wsetByparamval = WorksetBy.GetOrCreateWorkset(doc, wsetParamValue);

                                        }
                                    }
                                }
                        }
                    }
                }



                if (storage.worksetByLink != null)
                {
                    WorksetByLink wsetbylink = storage.worksetByLink;
                    Debug.WriteLine("Worksets for link files");
                    List<RevitLinkInstance> links = new List<RevitLinkInstance>();
                    if (byModel == false)
                    {
                        links = new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .ToList();
                    }
                    else
                    {
                        links = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .ToList();
                    }
                    Debug.WriteLine("Links found: " + links.Count);

                    foreach (RevitLinkInstance rli in links)
                    {
                        Debug.WriteLine("Current link: " + rli.Name);
                        RevitLinkType linkFileType = doc.GetElement(rli.GetTypeId()) as RevitLinkType;
                        if (linkFileType == null)
                        {
                            Debug.WriteLine("LinkType is invalid");
                            continue;
                        }
                        if (linkFileType.IsNestedLink)
                        {
                            Debug.WriteLine("It is nested link");
                            continue;
                        }

                        char separator = wsetbylink.separator[0];
                        string linkWorksetName1 = linkFileType.Name.Split(separator)[wsetbylink.partNumberAfterSeparator];
                        string linkWorksetName2 = linkWorksetName1
                            .Substring(wsetbylink.ignoreFirstCharsAfterSeparation, linkWorksetName1.Length - wsetbylink.ignoreLastCharsAfterSeparation);
                        string linkWorksetName = wsetbylink.prefixForLinkWorksets + linkWorksetName2;
                        Debug.WriteLine("Workset name: " + linkWorksetName);

                        Workset linkWorkset = WorksetBy.GetOrCreateWorkset(doc, linkWorksetName);
                        WorksetBy.SetWorkset(rli, linkWorkset);
                        WorksetBy.SetWorkset(linkFileType, linkWorkset);
                    }
                }
                /*
                List<CADLinkType> cadLinks = new FilteredElementCollector(doc)
                        .OfClass(typeof(CADLinkType)).Cast<CADLinkType>()
                        .ToList();
                Workset wsetCad = WorksetBy.GetOrCreateWorkset(doc, "#DWG");
                foreach (CADLinkType linkType in cadLinks)
                {
                    WorksetBy.SetWorkset(linkType, wsetCad);
                }

                ///внесение элементов в рабочий набор Задания
                Workset wset_ = WorksetBy.GetOrCreateWorkset(doc, "Задания");
                List<FamilyInstance> curFamIns_ = famIns
                            .Where(f => f.Symbol.FamilyName.ToLower().Contains("задание"))
                            .ToList();

                List<RevitLinkInstance> rvtLinks = new FilteredElementCollector(doc)
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .ToList();
                List<RevitLinkInstance> rvtLink = rvtLinks.Where(f => f.Name.ToLower().Contains("задание")).ToList();
                List<CADLinkType> cadLink = cadLinks.Where(f => f.Name.ToLower().Contains("задание")).ToList();
                foreach (FamilyInstance fi in curFamIns_)
                {
                    WorksetBy.SetWorkset(fi, wset_);
                }
                foreach (Element rvt in rvtLink)
                {
                    WorksetBy.SetWorkset(rvt, wset_);
                }
                foreach (Element cad in cadLink)
                {
                    WorksetBy.SetWorkset(cad, wset_);
                }*/

                t.Commit();
            }

            List<string> emptyWorksetsNames = WorksetTool.GetEmptyWorksets(doc);
            if(emptyWorksetsNames.Count > 0)
            {
                string msg = "Обнаружены пустые рабочие наборы! Их можно удалить вручную:\n";
                foreach(string s in emptyWorksetsNames)
                {
                    msg += s + "\n";
                }
                Debug.WriteLine("Empty worksets found: " + msg);
                TaskDialog.Show("Отчёт", msg);
            }
            Debug.WriteLine("Finished");
            return Result.Succeeded;
        }
    }
}
