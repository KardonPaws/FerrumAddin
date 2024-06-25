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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;

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
            LoadToggleButtonState();
        }
        private void LoadToggleButtonState()
        {
            try
            {
                // Замените путь к вашему XML файлу
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
            XElement frmTabPath = root.Element("TabPath");
            frmTabPath.SetAttributeValue("Path", path.Text);
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
            App.TabPath = path.Text;
            App.dockableWindow.Newpath();
            this.Close();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Файлы XML (*.xml)|*.xml";
            if ((bool)ofd.ShowDialog())
            {
                
                path.Text = ofd.FileName;
            }
            
        }
    }
}
