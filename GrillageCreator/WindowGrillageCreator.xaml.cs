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
        public WindowGrillageCreator()
        {
            InitializeComponent();
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
            if ((bool)radioBtnNumber.IsChecked)
            {
                isNumber = true;
            }
            else
            {
                isNumber = false;
            }
            // Собираем значения из текстовых полей
            horizontalCount = int.Parse(boxHorizont.Text);
            verticalCount = int.Parse(boxVertical.Text);
            leftRightOffset = int.Parse(boxLeftRight.Text);
            topBottomOffset = int.Parse(boxTopBottom.Text);
            horizontCount = int.Parse(boxHorizontal.Text);

            // Собираем значения из комбобоксов
            //topDiameter = comboTop.SelectedItem.ToString();
            //bottomDiameter = comboBottom.SelectedItem.ToString();
            //vertDiameter = comboVert.SelectedItem.ToString();
            //horizontDiameter = comboHorizont.SelectedItem.ToString();
            CommandGrillageCreator.createGrillage.Raise();
        }
    }
}
