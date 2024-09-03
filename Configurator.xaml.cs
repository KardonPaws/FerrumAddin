using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FerrumAddin
{
    /// <summary>
    /// Логика взаимодействия для Window1.xaml
    /// </summary>
    public partial class Configurator : Window
    {
        public Configurator()
        {
            InitializeComponent();
            CreateCheckboxesFromXml();
            LoadToggleButtonState();
        }
        private void LoadToggleButtonState()
        {
            try
            {
                string xmlFilePath = App.xmlFilePath;
                XElement root = XElement.Load(xmlFilePath);

                // Предполагая, что в XML файле есть элемент <frmManger> с атрибутом IsChecked
                XElement frmMangerElement = root.Element("frmManager");
                if (frmMangerElement != null && bool.TryParse(frmMangerElement.Attribute("IsChecked")?.Value, out bool isChecked))
                {
                    frmManger.IsChecked = isChecked;
                }


            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке состояния ToggleButton: {ex.Message}");
            }
        }

        private void SaveToggleButtonState(XElement root)
        {

            XElement frmMangerElement = root.Element("frmManager");
            if (frmMangerElement == null)
            {
                frmMangerElement = new XElement("frmManager");
                root.Add(frmMangerElement);
            }
            frmMangerElement.SetAttributeValue("IsChecked", frmManger.IsChecked);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            string xmlFilePath = App.xmlFilePath;

            XElement root;
            if (System.IO.File.Exists(xmlFilePath))
            {
                root = XElement.Load(xmlFilePath);
            }
            else
            {
                root = new XElement("Settings");
            }
            SaveToggleButtonState(root);
            root.Save(xmlFilePath);
            App.ButtonConf(root);
            RecreateXmlFile();
            CreateCheckboxesFromXml();
            SaveCheckboxesToXml();
            App.dockableWindow.Newpath();
            this.Close();
        }

        private void RecreateXmlFile()
        {
            string tabPath = App.TabPath;
                XElement root = new XElement("Settings");
            var pathsAndSections = new Dictionary<string, string>();

            if (pathFam != null) pathsAndSections.Add(pathFam, "Семейства");
            if (pathWalls != null) pathsAndSections.Add(pathWalls, "Стены");
            if (pathFloor != null) pathsAndSections.Add(pathFloor, "Перекрытия" );
            if (pathCeil != null) pathsAndSections.Add(pathCeil, "Потолки");
            if (pathWind != null) pathsAndSections.Add(pathWind, "Витражи");
            if (pathRoof != null) pathsAndSections.Add(pathRoof, "Крыши");
            if (pathFence != null) pathsAndSections.Add(pathFence, "Ограждения");
            if (pathRamp != null) pathsAndSections.Add(pathRamp, "Пандусы");

            var nameAndCat = new Dictionary<string, BuiltInCategory>
{
    { "Стены", BuiltInCategory.OST_Walls },
    { "Перекрытия", BuiltInCategory.OST_Floors },
    { "Потолки", BuiltInCategory.OST_Ceilings },
    { "Витражи", BuiltInCategory.OST_Walls },
    { "Крыши" , BuiltInCategory.OST_Roofs},
    { "Ограждения" , BuiltInCategory.OST_StairsRailing},
    { "Пандусы", BuiltInCategory.OST_Ramps }
};
            foreach (var folderPath in pathsAndSections.Keys)
            { 
                if (folderPath == null || folderPath == "")
                {
                    continue;
                }
            
                foreach (var dir in System.IO.Directory.GetDirectories(folderPath))
                {
                    XElement tabElement = new XElement("TabItem");
                    tabElement.Add(new XElement("Header", System.IO.Path.GetFileName(dir)));
                    tabElement.Add(new XElement("Visibility", true));

                    foreach (var categoryDir in System.IO.Directory.GetDirectories(dir))
                    {
                        if (pathsAndSections[folderPath] == "Семейства")
                        {
                            foreach (var file in System.IO.Directory.GetFiles(categoryDir, "*.rfa"))
                            {
                                string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(file);
                                string imagePath = System.IO.Path.Combine(categoryDir, fileNameWithoutExtension + ".png");


                                XElement menuItemElement = new XElement("MenuItem");
                                menuItemElement.Add(new XElement("Name", fileNameWithoutExtension));
                                menuItemElement.Add(new XElement("Category", System.IO.Path.GetFileName(categoryDir)));
                                menuItemElement.Add(new XElement("Path", file));
                                menuItemElement.Add(new XElement("ImagePath", imagePath));

                                tabElement.Add(menuItemElement);

                            }
                        }
                        else
                        {
                            foreach (var file in System.IO.Directory.GetFiles(categoryDir, "*.rvt"))
                            {
                                UIDocument uidoc = App.uiapp.OpenAndActivateDocument(file);
                                doc = uidoc.Document;
                                List<Element> elements = new List<Element>();
                                if (pathsAndSections[folderPath] == "Витражи")
                                {     
                                    elements = new FilteredElementCollector(doc).OfCategory(nameAndCat[pathsAndSections[folderPath]]).WhereElementIsElementType().ToElements().Where(x =>( x as WallType).Kind == WallKind.Curtain).ToList();
                                }
                                else if (pathsAndSections[folderPath] == "Стены")
                                {
                                    elements = new FilteredElementCollector(doc).OfCategory(nameAndCat[pathsAndSections[folderPath]]).WhereElementIsElementType().ToElements().Where(x => (x as WallType).Kind == WallKind.Basic).ToList();
                                }
                                else
                                {
                                    elements = new FilteredElementCollector(doc).OfCategory(nameAndCat[pathsAndSections[folderPath]]).WhereElementIsElementType().ToElements().ToList();

                                }
                                foreach (Element element in elements)
                                {
                                    XElement menuItemElement = new XElement("MenuItem");
                                    menuItemElement.Add(new XElement("Name", element.Name));
                                    menuItemElement.Add(new XElement("Category", pathsAndSections[folderPath]));
                                    menuItemElement.Add(new XElement("Path", file));
                                    menuItemElement.Add(new XElement("ImagePath", ""));
                                    tabElement.Add(menuItemElement);
                                }
                                ConfiguratorShow.CloseEv.Raise();
                            }
                        }

                        root.Add(tabElement);
                    }
                }
            }
            root.Save(tabPath);
        }
        public static Document doc;
        private void SaveCheckboxesToXml()
        {
            string filePath = App.TabPath;
            if (!System.IO.File.Exists(filePath))
                return;

            var xdoc = XDocument.Load(filePath);
            var children = FamilyManager.Children;
            List<string> values = new List<string>();
            foreach (var child in children)
            {
                if (child is CheckBox check)
                {
                    values.Add(check.IsChecked.ToString());
                }
            }
            int i = 0;
            foreach (var tabItem in xdoc.Descendants("TabItem"))
            {
                tabItem.Element("Visibility").SetValue(values[i]);
                i++;
            }
            xdoc.Save(filePath);
        }
        private void CreateCheckboxesFromXml()
        {
            string filePath = App.TabPath;
            if (!System.IO.File.Exists(filePath))
                return;

            var xdoc = XDocument.Load(filePath);
            int i = 0;
            foreach (var tabItem in xdoc.Descendants("TabItem"))
            {
                var header = tabItem.Element("Header")?.Value;
                var checkBox = new CheckBox
                {
                    Name = "a" + i.ToString(),
                    Content = header,
                    Margin = new Thickness(5),
                    IsChecked = Convert.ToBoolean(tabItem.Element("Visibility")?.Value)
                };
                i++;
                FamilyManager.Children.Add(checkBox);
            }
        }
        public static string pathFam;
        public static string pathWalls;
        public static string pathFloor;
        public static string pathCeil;
        public static string pathWind;
        public static string pathRoof;
        public static string pathFence;
        public static string pathRamp;

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string selectedPath = fbd.SelectedPath;

                // Определяем, какая кнопка была нажата, и записываем путь в соответствующую переменную
                if ((sender as Button).Name == "_path")
                {
                    pathFam = selectedPath;
                    path.Text = selectedPath; // Обновляем текстовый блок для отображения выбранного пути
                }
                else if ((sender as Button).Name == "_pathWalls")
                {
                    pathWalls = selectedPath;
                    pathWallsText.Text = selectedPath; // Обновляем текстовый блок
                }
                else if ((sender as Button).Name == "_pathFloor")
                {
                    pathFloor = selectedPath;
                    pathFloorText.Text = selectedPath; // Обновляем текстовый блок
                }
                else if ((sender as Button).Name == "_pathCeil")
                {
                    pathCeil = selectedPath;
                    pathCeilText.Text = selectedPath; // Обновляем текстовый блок
                }
                else if ((sender as Button).Name == "_pathWind")
                {
                    pathWind = selectedPath;
                    pathWindText.Text = selectedPath; // Обновляем текстовый блок
                }
                else if ((sender as Button).Name == "_pathRoof")
                {
                    pathRoof = selectedPath;
                    pathRoofText.Text = selectedPath; // Обновляем текстовый блок
                }
                else if ((sender as Button).Name == "_PathFence")
                {
                    pathFence = selectedPath;
                    pathFenceText.Text = selectedPath; // Обновляем текстовый блок
                }
                else if ((sender as Button).Name == "_pathRamp")
                {
                    pathRamp = selectedPath;
                    pathRampText.Text = selectedPath; // Обновляем текстовый блок
                }
            }
        }

    }
}
