using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FerrumAddin.GrillageCreator
{
    /// <summary>
    /// Логика взаимодействия для WindowGrillageCreator.xaml
    /// </summary>
    public partial class WindowGrillageCreator : Window
    {
        public WindowGrillageCreator(List<Element> elements)
        {
            InitializeComponent();
            List<string> rebars = new List<string>();
            foreach (Element element in elements)
            {
                rebars.Add(element.Name);
            }
            comboBottom.ItemsSource = rebars;
            comboTop.ItemsSource = rebars;
            comboVert.ItemsSource = rebars;
            comboHorizont.ItemsSource = rebars;
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton btn = (sender as RadioButton);
            try
            {
                if (btn.Name == "radioBtnNumber")
                {
                    if ((bool)btn.IsChecked)
                    {
                        textHorizont.Text = "Количество\n;горизонтальных каркасов";
                        //textVertical.Text = "Количество &#x0a;вертикальных каркасов";
                    }
                    else
                    {
                        textHorizont.Text = "Шаг\n;горизонтальных каркасов";
                        //textVertical.Text = "Шаг &#x0a;вертикальных каркасов";
                    }
                }
                else
                {
                    if ((bool)btn.IsChecked)
                    {
                        textHorizont.Text = "Шаг\nгоризонтальных каркасов";
                        //textVertical.Text = "Шаг &#x0a;вертикальных каркасов";
                    }
                    else
                    {
                        textHorizont.Text = "Количество\nгоризонтальных каркасов";
                        //textVertical.Text = "Количество &#x0a;вертикальных каркасов";
                    }
                }
            }
            catch
            {

            }
        }

        private static readonly Regex _regex = new Regex("[^0-9.-]+"); //regex that matches disallowed text
        private static bool IsTextAllowed(string text)
        {
            return !_regex.IsMatch(text);
        }

        private void previewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsTextAllowed(e.Text);
        }

        public static int horizontalCount;
        public static int verticalCount;
        public static int leftRightOffset;
        public static int topBottomOffset;
        public static int horizontCount;

        public static bool isNumber;
        public static string topDiameter;
        public static string bottomDiameter;
        public static string vertDiameter;
        public static string horizontDiameter;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, что все обязательные поля заполнены
            if (string.IsNullOrEmpty(boxHorizont.Text) ||
                string.IsNullOrEmpty(boxVertical.Text) ||
                string.IsNullOrEmpty(boxLeftRight.Text) ||
                string.IsNullOrEmpty(boxTopBottom.Text) ||
                string.IsNullOrEmpty(boxHorizontal.Text) ||
                comboTop.SelectedItem == null ||
                comboBottom.SelectedItem == null ||
                comboVert.SelectedItem == null ||
                comboHorizont.SelectedItem == null)
            {
                MessageBox.Show("Пожалуйста, заполните все параметры перед выполнением команды.",
                               "Не все параметры заполнены",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
                return;
            }

            try
            {
                isNumber = true;
                horizontalCount = int.Parse(boxHorizont.Text);
                verticalCount = int.Parse(boxVertical.Text);
                leftRightOffset = int.Parse(boxLeftRight.Text);
                topBottomOffset = int.Parse(boxTopBottom.Text);
                horizontCount = int.Parse(boxHorizontal.Text);

                topDiameter = comboTop.SelectedItem.ToString();
                bottomDiameter = comboBottom.SelectedItem.ToString();
                vertDiameter = comboVert.SelectedItem.ToString();
                horizontDiameter = comboHorizont.SelectedItem.ToString();

                CommandGrillageCreator.createGrillage.Raise();
            }
            catch (FormatException)
            {
                MessageBox.Show("Пожалуйста, введите корректные числовые значения во все поля.",
                               "Ошибка ввода",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка: {ex.Message}",
                               "Ошибка",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
            }
        }
    }
}
