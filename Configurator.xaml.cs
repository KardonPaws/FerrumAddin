using Microsoft.Win32;
using System;
using System.Collections.Generic;
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
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

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
                XElement frmTabPath = root.Element("TabPath");
                path.Text = frmTabPath.Attribute("Path").Value;


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
            App.FamilyFolder = pathText;
            if (pathText != null)
                RecreateXmlFile(App.FamilyFolder);
            CreateCheckboxesFromXml();
            SaveCheckboxesToXml();
            App.dockableWindow.Newpath();
            this.Close();
        }

        private void RecreateXmlFile(string folderPath)
        {
            string tabPath = App.TabPath;
            if (folderPath != App.TabPath)
            {
                XElement root = new XElement("Settings");

                foreach (var dir in System.IO.Directory.GetDirectories(folderPath))
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
                    }

                    root.Add(tabElement);
                }

                root.Save(tabPath);
            }
        }
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
        public static string pathText;
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                pathText = fbd.SelectedPath;
            }
        }
    }
}
