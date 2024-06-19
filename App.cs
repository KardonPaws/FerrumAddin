#region Namespaces
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AW = Autodesk.Windows;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Collections.Generic;
using RibbonPanel = Autodesk.Revit.UI.RibbonPanel;
using RibbonItem = Autodesk.Revit.UI.RibbonItem;
using ComboBox = Autodesk.Revit.UI.ComboBox;

#endregion

namespace FerrumAddin
{
    public class App : IExternalApplication
    {
        public AW.RibbonItem GetButton(string tabName, string panelName, string itemName)
        {
            AW.RibbonControl ribbon = AW.ComponentManager.Ribbon;
            foreach (AW.RibbonTab tab in ribbon.Tabs)
            {
                if (tab.Name == tabName)
                {
                    foreach (AW.RibbonPanel panel in tab.Panels)
                    {
                        if (panel.Source.Title == panelName)
                        {
                            return panel.FindItem("CustomCtrl_%CustomCtrl_%"
                              + tabName + "%" + panelName + "%" + itemName,
                              true) as AW.RibbonItem;
                        }
                    }
                }
            }
            return null;
        }


        public static BitmapImage Convert(Image img)
        {
            using (var memory = new MemoryStream())
            {
                img.Save(memory, ImageFormat.Png);
                memory.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                return bitmapImage;

            }
        }
        public Result OnStartup(UIControlledApplication a)
        {
            string tabName = "Железно";

            a.CreateRibbonTab(tabName);
            RibbonPanel panelFerrum = a.CreateRibbonPanel(tabName, "Железно");
            PushButtonData iss = new PushButtonData("Конфигуратор", "Конфигуратор", Assembly.GetExecutingAssembly().Location, "velcon_Addin.Issues");
            iss.Image = Convert(Properties.Resources.Image1);
            iss.LargeImage = Convert(Properties.Resources.Image1);
            ComboBoxData comboBoxData = new ComboBoxData("ChangeRazd");
            List<RibbonItem> items = panelFerrum.AddStackedItems(iss, comboBoxData).ToList();
            AW.RibbonItem ri = GetButton(tabName, "Железно", comboBoxData.Name);
            ri.Width = 110;
            ComboBox cb = (items[1] as ComboBox);
            cb.AddItems(new List<ComboBoxMemberData>() { new ComboBoxMemberData("Common", "Общие"),
                                                                             new ComboBoxMemberData("Views", "Виды"),
                                                                             new ComboBoxMemberData("AR", "АР"),
                                                                             new ComboBoxMemberData("MEP", "MEP"),
                                                                             new ComboBoxMemberData("Control", "Управление")});

            cb.CurrentChanged += Cb_CurrentChanged;
            return Result.Succeeded;
        }

        private void Cb_CurrentChanged(object sender, Autodesk.Revit.UI.Events.ComboBoxCurrentChangedEventArgs e)
        {
            string vkl = e.NewValue.ItemText;
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
    }
}
