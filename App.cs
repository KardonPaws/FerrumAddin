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
using Autodesk.Revit.UI.Events;
using System.Threading.Tasks;
using System.Windows.Controls;
using Autodesk.Windows;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using System.Xml.Linq;

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


        public static BitmapImage Convert(System.Drawing.Image img)
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
            
            xmlFilePath = a.ControlledApplication.CurrentUserAddinsLocation + "\\Settings.xml";
            XElement root;
            if (System.IO.File.Exists(xmlFilePath))
            {
                root = XElement.Load(xmlFilePath);
            }
            else
            {
                root = new XElement("Settings");
            }


            XElement frmMangerElement = root.Element("frmManger");
            if (frmMangerElement == null)
            {
                frmMangerElement = new XElement("frmManger");
                root.Add(frmMangerElement);
            }
            XElement frmTabPath = root.Element("TabPath");
            if (frmTabPath == null)
            {
                frmTabPath = new XElement("TabPath");
                frmTabPath.SetAttributeValue("Path", a.ControlledApplication.CurrentUserAddinsLocation + "\\TabItems.xml");
                root.Add(frmTabPath);
            }
            TabPath = frmTabPath.Attribute("Path").Value;
            root.Save(xmlFilePath);

            string tabName = "Железно";

            a.CreateRibbonTab(tabName);
            RibbonPanel panelFerrum = a.CreateRibbonPanel(tabName, "Железно");
            PushButtonData conf = new PushButtonData("frmConfig", "Конфигуратор", Assembly.GetExecutingAssembly().Location, "FerrumAddin.ConfiguratorShow");
            conf.Image = Convert(Properties.Resources.ferrum);
            conf.LargeImage = Convert(Properties.Resources.ferrum);
            ComboBoxData comboBoxData = new ComboBoxData("ChangeRazd");
            List<RibbonItem> items = panelFerrum.AddStackedItems(conf, comboBoxData).ToList();
            AW.RibbonItem ri = GetButton(tabName, "Железно", comboBoxData.Name);
            ri.Width = 110;
            ComboBox cb = (items[1] as ComboBox);
            cb.AddItems(new List<ComboBoxMemberData>() { new ComboBoxMemberData("Common", "Общие"),
                                                                             new ComboBoxMemberData("Views", "Виды"),
                                                                             new ComboBoxMemberData("AR", "АР"),
                                                                             new ComboBoxMemberData("MEP", "MEP"),
                                                                             new ComboBoxMemberData("Control", "Управление")});

            cb.CurrentChanged += Cb_CurrentChanged;


            PushButtonData FamilyManager = new PushButtonData("frmManager", "Менеджер семейств", Assembly.GetExecutingAssembly().Location, "FerrumAddin.FamilyManagerShow");
            FamilyManager.Image = Convert(Properties.Resources.FamilyManager);
            FamilyManager.LargeImage = Convert(Properties.Resources.FamilyManager);

            panelFerrum.AddItem(FamilyManager);

            FamilyManagerWindow dock = new FamilyManagerWindow();
            dockableWindow = dock;

            DockablePaneId id = new DockablePaneId(new Guid("{68D44FAC-CF09-46B2-9544-D5A3F809373C}"));
            try
            {
                a.RegisterDockablePane(id, "Менеджер семейств Железно",
                        dockableWindow as IDockablePaneProvider);
                a.ControlledApplication.FamilyLoadingIntoDocument += ControlledApplication_FamilyLoadingIntoDocument;
                a.ControlledApplication.DocumentOpened += ControlledApplication_DocumentOpened;
                a.ViewActivated += A_ViewActivated;
                LoadEvent = ExternalEvent.Create(new LoadEvent());
            }
            catch (Exception ex)
            {

            }
            AllowLoad = false;

            ButtonConf(root);

            return Result.Succeeded;
        }
        public static string xmlFilePath;
        public static string TabPath;
        public static Dictionary<string, bool> GetElementStates(XElement root)
        {
            var elementStates = new Dictionary<string, bool>();

            foreach (var element in root.Elements())
            {
                if (bool.TryParse(element.Attribute("IsChecked")?.Value, out bool isChecked))
                {
                    elementStates[element.Name.LocalName] = isChecked;
                }
            }

            return elementStates;
        }
        public static void ButtonConf(XElement root)
        {
            Dictionary<string,bool> names = GetElementStates(root);
            Autodesk.Windows.RibbonControl ribbon = Autodesk.Windows.ComponentManager.Ribbon;
            foreach (Autodesk.Windows.RibbonTab tab in ribbon.Tabs)
            {
                if (tab.Title.Contains("Железно"))
                {
                    foreach (Autodesk.Windows.RibbonPanel panel in tab.Panels)
                    {

                        RibbonItemCollection collctn = panel.Source.Items;
                        foreach (Autodesk.Windows.RibbonItem ri in collctn)
                        {
                            string name = ri.Id.Split('%').Last();
                            if (names.Keys.ToList().Contains(name))
                            {
                                ri.IsVisible = names[name];
                                ri.ShowText = names[name];
                                ri.ShowImage = names[name];
                            }
                        }
                        
                    }
                }
            }
        }

        private void A_ViewActivated(object sender, ViewActivatedEventArgs e)
        {
            Document d = e.Document;
            dockableWindow.CustomInitiator(d);
        }

        private void ControlledApplication_DocumentOpened(object sender, Autodesk.Revit.DB.Events.DocumentOpenedEventArgs e)
        {
            Document d = e.Document;
            dockableWindow.CustomInitiator(d);
        }

        public static ExternalEvent LoadEvent;
        public static bool AllowLoad;
        private void ControlledApplication_FamilyLoadingIntoDocument(object sender, Autodesk.Revit.DB.Events.FamilyLoadingIntoDocumentEventArgs e)
        {
            if (AllowLoad)
            {
              
            }
            else
            {
                e.Cancel();
                TaskDialog.Show("Запрет загрузки", "Загрузите семейство из менеджера семейств");
            }
        }

        public static FamilyManagerWindow dockableWindow = null;
        ExternalCommandData edata = null;

        

        private void Cb_CurrentChanged(object sender, Autodesk.Revit.UI.Events.ComboBoxCurrentChangedEventArgs e)
        {
            string vkl = e.NewValue.ItemText;
        }

        public Result OnShutdown(UIControlledApplication a)
        {
            return Result.Succeeded;
        }
    }

    public class LoadEvent : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {

            List<MenuItem> list = new List<MenuItem>();
            foreach (TabItemViewModel tab in FamilyManagerWindow.mvm.TabItems)
            {
                list.AddRange(tab.MenuItems.Where(x => x.IsSelected).ToList());
            }
            App.AllowLoad = true;
            using (Transaction tx = new Transaction(FamilyManagerWindow.doc))
            {
                tx.Start("Загрузка семейств");
                foreach (MenuItem tab in list)
                {
                    FamilyManagerWindow.doc.LoadFamily(tab.Path);
                }
                tx.Commit();
            }
            App.AllowLoad = false;
            FamilyManagerWindow.Reload();
        }


        public string GetName()
        {
            return "xxx";
        }
    }

    public class CommandAvailability : IExternalCommandAvailability
    {
        // interface member method
        public bool IsCommandAvailable(UIApplication app, CategorySet cate)
        {
            // zero doc state
            if (app.ActiveUIDocument == null)
            {
                // disable register btn
                return true;
            }
            // enable register btn
            return false;
        }
    }
}
