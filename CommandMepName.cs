#region Namespaces
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Application = Autodesk.Revit.ApplicationServices.Application;
#endregion

namespace FerrumAddin
{
    [Transaction(TransactionMode.Manual)]
    public class CommandMepName: IExternalCommand
    {
        public Result Execute(
          ExternalCommandData commandData,
          ref string message,
          ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            using (Transaction tx = new Transaction(doc))
            {
                tx.Start("Спецификация - Трубы/Воздуховоды");

                List<Element> pipes = (List<Element>)new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType().ToElements();
                foreach (Element pipe in pipes)
                {
                    pipe.LookupParameter("ADSK_Количество").Set(Convert.ToDouble(pipe.LookupParameter("Длина").AsValueString()) / 1000);
                    string dn = "DN " + pipe.LookupParameter("Диаметр").AsValueString();
                    Element pipe_type = doc.GetElement(pipe.GetTypeId());
                    string mark = pipe_type.LookupParameter("ADSK_Марка").AsString();
                    var regex = new Regex(@"\d+");
                    string out_d = regex.Match(pipe.LookupParameter("Внешний диаметр").AsValueString()).Value;
                    string in_d = regex.Match(pipe.LookupParameter("Внутренний диаметр").AsValueString()).Value;
                    
                    double th = (Convert.ToDouble(out_d) - Convert.ToDouble(in_d))/2;
                    out_d = "d" + out_d + "x" + th;
                    string op = pipe_type.LookupParameter("ADSK_Наименование краткое").AsString();
                    if (mark != null && (mark.Contains("ГОСТ 6942-98") || mark.Contains("ГОСТ 3262")))
                    {
                        op = op + " " + dn;
                    }
                    else
                    {
                        op = op + " " + out_d;
                    }
                    pipe.LookupParameter("ADSK_Наименование").Set(op);

                }
                List<Element> ducts = (List<Element>)new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType().ToElements();
                foreach (Element duct in ducts)
                {
                    duct.LookupParameter("ADSK_Количество").Set(Convert.ToDouble(duct.LookupParameter("Длина").AsValueString()) / 1000);
                    Element duct_type = doc.GetElement(duct.GetTypeId());
                    bool circle = true;
                    try
                    {
                        double o = (duct as MEPCurve).Diameter;
                    }
                    catch
                    {
                        circle = false;
                    }
                    string op = duct_type.LookupParameter("Комментарии к типоразмеру").AsString();
                    bool ognez = false;
                    Element ins = null;
                    try
                    {
                        ins = doc.GetElement(((List<ElementId>)InsulationLiningBase.GetInsulationIds(doc, duct.Id))[0]);
                    }
                    catch { }
                    //Element lin = doc.GetElement(((List<ElementId>)InsulationLiningBase.GetLiningIds(doc, duct.Id))[0]);
                    string mat = duct_type.LookupParameter("ADSK_Материал обозначение").AsString();
                    string b = "";
                    if ((duct_type.LookupParameter("Огнезащита") != null && duct_type.LookupParameter("Огнезащита").AsInteger() == 1) || (ins != null && ins.Name.ToLower().Contains("огнезащ")))
                        ognez = true;
                    if (!circle)
                    {
                        int h = Convert.ToInt32(duct.LookupParameter("Высота").AsValueString());
                        int v = Convert.ToInt32(duct.LookupParameter("Ширина").AsValueString());
                        if (h > v)
                        {
                            op += " " + v + "x" + h + ", b=";
                        }
                        else
                        {
                            op += " " + h + "x" + v + ", b=";
                        }
                        int max = h > v ? h : v;
                        if (mat == "Оцинковка")
                        {
                            if (ognez)
                            {
                                if (max <= 1200)
                                    b = "0.8";
                                else if (max <= 2000)
                                    b = "0.9";
                                else if (max <= 2400)
                                    b = "1.2";
                            }
                            else
                            {
                                if (max <= 250)
                                    b = "0.5";
                                else if (max <= 1000)
                                    b = "0.7";
                                else if (max <= 2000)
                                    b = "0.9";
                                else
                                    b = "1.2";
                            }
                        }
                        else
                        {

                            if (max <= 600)
                                b = "1.5";
                            else if (max <= 1000)
                                b = "1.7";
                            else if (max <= 1500)
                                b = "1.9";
                            else if (max <= 1800)
                                b = "2.0";
                            else if (max <= 2000)
                                b = "2.4";

                        }
                        op += b;
                        duct.LookupParameter("ADSK_Толщина стенки").SetValueString(b);
                        if (ognez)
                        {
                            op += ", класс герметичности - \"B\"";
                        }
                    }                
                    else
                    {
                        int d = Convert.ToInt32(duct.LookupParameter("Диаметр").AsValueString());
                        op += " ⌀" + d + ", b=";
                        if (mat == "Оцинковка")
                        {
                            if (ognez)
                            {
                                if (d <= 900)
                                    b = "0.8";
                                else if (d <= 1250)
                                    b = "1.0";
                                else if (d <= 1600)
                                    b = "1.2";
                                else if (d <= 2000)
                                    b = "1.4";
                            }
                            else
                            {
                                if (d <= 200)
                                    b = "0.5";
                                else if (d <= 450)
                                    b = "0.6";
                                else if (d <= 800)
                                    b = "0.7";
                                else if (d <= 1250)
                                    b = "1.0";
                                else if (d <= 1600)
                                    b = "1.2";
                                else if (d <= 2000)
                                    b = "1.4";
                            }
                        }
                        else
                        {
                            if (d <= 1200)
                                b = "1.5";
                            else if (d <= 1800)
                                b = "1.8";
                            else if (d <= 2000)
                                b = "2.0";
                        }
                        op += b;
                        duct.LookupParameter("ADSK_Толщина стенки").SetValueString(b);
                        if (ognez)
                        {
                            op += ", класс герметичности \"B\"";
                        }
                    }

                    duct.LookupParameter("ADSK_Наименование").Set(op);
                }
                tx.Commit();        
            }
            TaskDialog.Show("Отчет о выполнении", "Выполнено");
            return Result.Succeeded;
        }

    }
}
