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
            CreateCheckboxesFromXml();
            SaveCheckboxesToXml();
            App.dockableWindow.Newpath();
            this.Close();
        }

        private void RecreateXmlFile()
        {
            string tabPath = App.TabPath;
                XElement root = new XElement("Settings");
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

            List<Document> documents = new List<Document>();
            foreach (var dir in System.IO.Directory.GetDirectories(pathFam))
            {
                XElement tabElement = new XElement("TabItem");
                tabElement.Add(new XElement("Header", System.IO.Path.GetFileName(dir)));
                tabElement.Add(new XElement("Visibility", true));

                foreach (var categoryDir in System.IO.Directory.GetDirectories(dir))
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
                    foreach (var file in System.IO.Directory.GetFiles(categoryDir, "*.rvt"))
                    {
                        UIDocument uidoc = App.uiapp.OpenAndActivateDocument(file);
                        doc = uidoc.Document;
                        documents.Add(doc);
                        List<Element> elements = new List<Element>();
                        string directory = categoryDir.Split('\\').Last();
                        if (categoryDir.Contains("Витражи"))
                        {
                            elements = (List<Element>)new FilteredElementCollector(doc)
                            .OfCategory(nameAndCat[directory])
                            .WhereElementIsNotElementType()
                            .Cast<Wall>()
                            .Select(x => x.WallType)
                            .Distinct()
                            .Where(x => x.Kind == WallKind.Curtain).Select(x => x as Element).ToList();
                        }
                        else if (categoryDir.Contains("Стены"))
                        {
                            elements = (List<Element>)new FilteredElementCollector(doc)
                                .OfCategory(nameAndCat[directory])
                                .WhereElementIsNotElementType()
                                .Cast<Wall>()
                                .Select(x => x.WallType)
                                .Distinct()
                                .Where(x => x.Kind == WallKind.Basic).Select(x => x as Element).ToList();
                        }
                        else
                        {
                            elements = (List<Element>)new FilteredElementCollector(doc)
                                .OfCategory(nameAndCat[directory])
                                .WhereElementIsNotElementType()
                                .Cast<Element>().Select(x => x.GetTypeId()).Select(x => doc.GetElement(x))
                                .ToList().Select(x => x as Element);
                        }
                        foreach (Element element in elements)
                        {
                            XElement menuItemElement = new XElement("MenuItem");
                            menuItemElement.Add(new XElement("Name", element.Name));
                            menuItemElement.Add(new XElement("Category", directory));
                            menuItemElement.Add(new XElement("Path", file));
                            menuItemElement.Add(new XElement("ImagePath", System.IO.Path.Combine(categoryDir, element.Name + ".png")));
                            tabElement.Add(menuItemElement);
                        }
                        ConfiguratorShow.CloseEv.Raise();
                    }
                }
                root.Add(tabElement);
            }
            root.Save(tabPath);
            foreach(Document doc in documents)
            {
                doc.Close(false);
            }
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
                    RecreateXmlFile();
                    CreateCheckboxesFromXml();
                    SaveCheckboxesToXml();
                    App.dockableWindow.Newpath();
                }
            }
        }

    }
}
