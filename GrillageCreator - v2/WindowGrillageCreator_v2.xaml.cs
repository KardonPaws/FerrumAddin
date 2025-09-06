using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
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
using System.Xml.Serialization;

namespace FerrumAddinDev.GrillageCreator_v2
{
    /// <summary>
    /// Логика взаимодействия для WindowGrillageCreator.xaml
    /// </summary>
    public partial class WindowGrillageCreator_v2 : Window
    {
        List<string> rebars = new List<string>();
        List<string> rebarsHorizontal = new List<string>();
        List<string> rebarsCorner = new List<string>();
        List<string> rebarsKnitted = new List<string>();

        public WindowGrillageCreator_v2(List<Element> elements, List<Element> elements2, List<Element> elements3, List<Element> elements4)
        {
            InitializeComponent();
            foreach (Element element in elements)
            {
                rebars.Add(element.Name);
            }
            rebars = rebars.OrderBy(x=>x, new NaturalStringComparer()).ToList();
            foreach (Element element in elements2)
            {
                rebarsHorizontal.Add(element.Name);
            }
            rebarsHorizontal = rebarsHorizontal.OrderBy(x => x, new NaturalStringComparer()).ToList();
            foreach (Element element in elements3)
            {
                rebarsCorner.Add(element.Name);
            }
            rebarsCorner = rebarsCorner.OrderBy(x => x, new NaturalStringComparer()).ToList();
            foreach (Element element in elements4)
            {
                rebarsKnitted.Add(element.Name);
            }
            rebarsKnitted = rebarsKnitted.OrderBy(x => x, new NaturalStringComparer()).ToList();
            comboBottom.ItemsSource = rebars;
            comboTop.ItemsSource = rebars;
            comboVert.ItemsSource = rebars;
            comboHorizont.ItemsSource = rebarsHorizontal;
            comboCorner.ItemsSource = rebarsCorner;
            LoadSettings();
        }

        private string _settingsFilePath = App.xmlFilePath + "GrillageCreator.xml";

        private void LoadSettings()
        {
            if (string.IsNullOrEmpty(_settingsFilePath)) return;

            var settings = GrillageSettings.LoadSettings(_settingsFilePath);
            if (settings == null) return;
            // 06.09.25 - сохранение режима работы
            if (settings.KnittedMode == true)
            {
                radioBtnKnitted.IsChecked = true;
                radioBtnWelded.IsChecked = false;
            }
            else
            {
                radioBtnKnitted.IsChecked = false;
                radioBtnWelded.IsChecked = true;
            }

            // Загружаем значения в UI элементы с проверкой наличия в ItemsSource
            SetComboBoxValueIfExists(comboTop, settings.TopDiameter);
            SetComboBoxValueIfExists(comboBottom, settings.BottomDiameter);
            SetComboBoxValueIfExists(comboVert, settings.VertDiameter);
            SetComboBoxValueIfExists(comboHorizont, settings.HorizontDiameter);
            SetComboBoxValueIfExists(comboCorner, settings.CornerDiameter);

            // Загружаем числовые значения
            boxHorizont.Text = settings.HorizontCount == 0 ? "2" : settings.HorizontCount.ToString();
            boxVertical.Text = settings.VerticalStep == 0 ? "200" : settings.VerticalStep.ToString() ;
            boxHorizontal.Text = settings.HorizontalStep == 0 ? "200" : settings.HorizontalStep.ToString();
            boxLeftRight.Text = settings.LeftRightOffset == 0 ? "50" : settings.LeftRightOffset.ToString();
            // 24.07.25 - отдельный отступ сверху
            boxBottom.Text = settings.BottomOffset == 0 ? "50" : settings.BottomOffset.ToString();
            boxTop.Text = settings.TopOffset == 0 ? "50" : settings.TopOffset.ToString();
        }
        private void SaveSettings()
        {
            if (string.IsNullOrEmpty(_settingsFilePath)) return;

            var settings = new GrillageSettings
            {
                TopDiameter = comboTop.SelectedValue?.ToString(),
                BottomDiameter = comboBottom.SelectedValue?.ToString(),
                VertDiameter = comboVert.SelectedValue?.ToString(),
                HorizontDiameter = comboHorizont.SelectedValue?.ToString(),
                CornerDiameter = comboCorner.SelectedValue?.ToString(),
                HorizontCount = int.TryParse(boxHorizont.Text, out var hc) ? hc : 2,
                VerticalStep = int.TryParse(boxVertical.Text, out var vs) ? vs : 200,
                HorizontalStep = int.TryParse(boxHorizontal.Text, out var hs) ? hs : 200,
                LeftRightOffset = int.TryParse(boxLeftRight.Text, out var lr) ? lr : 50,
                // 24.07.25 - отдельный отступ сверху
                BottomOffset = int.TryParse(boxBottom.Text, out var tb) ? tb : 50,
                TopOffset = int.TryParse(boxTop.Text, out var to) ? to : 50,
                // 06.09.25 - сохранение режима работы
                KnittedMode = (bool)radioBtnKnitted.IsChecked
            };

            GrillageSettings.SaveSettings(settings, _settingsFilePath);
        }

        private void SetComboBoxValueIfExists(ComboBox comboBox, string value)
        {
            if (string.IsNullOrEmpty(value) || comboBox.ItemsSource == null)
            {
                comboBox.SelectedIndex = -1;
                return;
            }

            // Ищем элемент в ItemsSource
            foreach (var item in comboBox.ItemsSource)
            {
                // Предполагаем, что элементы - это объекты Element или строки
                string itemValue = item is string element ? element : item.ToString();

                if (itemValue == value)
                {
                    comboBox.SelectedValue = item;
                    return;
                }
            }

            // Если элемент не найден
            comboBox.SelectedIndex = -1;
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
        public static int bottomOffset;
        public static int topOffset;
        public static int horizontCount;

        public static bool isNumber;
        public static string topDiameter;
        public static string bottomDiameter;
        public static string vertDiameter;
        public static string horizontDiameter;
        public static string cornerDiameter;
        

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, что все обязательные поля заполнены
            if (string.IsNullOrEmpty(boxHorizont.Text) ||
                (!isKnittedMode && string.IsNullOrEmpty(boxVertical.Text)) ||
                string.IsNullOrEmpty(boxLeftRight.Text) ||
                string.IsNullOrEmpty(boxBottom.Text) ||
                string.IsNullOrEmpty(boxTop.Text) ||
                string.IsNullOrEmpty(boxHorizontal.Text) ||
                comboTop.SelectedItem == null ||
                comboBottom.SelectedItem == null ||
                (!isKnittedMode && comboVert.SelectedItem == null) ||
                comboHorizont.SelectedItem == null ||
                comboCorner.SelectedItem == null)
            {
                MessageBox.Show("Пожалуйста, заполните все параметры перед выполнением команды.",
                               "Не все параметры заполнены",
                               MessageBoxButton.OK,
                               MessageBoxImage.Warning);
                return;
            }

            try
            {
                horizontalCount = int.Parse(boxHorizont.Text);
                verticalCount = int.Parse(boxVertical.Text);
                leftRightOffset = int.Parse(boxLeftRight.Text);
                bottomOffset = int.Parse(boxBottom.Text);
                topOffset = int.Parse(boxTop.Text);
                horizontCount = int.Parse(boxHorizontal.Text);

                topDiameter = comboTop.SelectedItem.ToString();
                bottomDiameter = comboBottom.SelectedItem.ToString();
                vertDiameter = comboVert.SelectedItem?.ToString();
                horizontDiameter = comboHorizont.SelectedItem.ToString();
                cornerDiameter = comboCorner.SelectedItem.ToString();

                if (horizontalCount >= 2)
                {
                    SaveSettings();
                    CommandGrillageCreator_v2.createGrillage.Raise();
                }
                else
                    MessageBox.Show("Пожалуйста, введите число каркасов >1.",
                               "Ошибка ввода",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);
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

        public static bool isKnittedMode = false;  
        private bool isUpdatingBoxes = false;

        private void RadioButton_Welded_Checked(object sender, RoutedEventArgs e)
        {
            isKnittedMode = false;

            comboBottom.ItemsSource = rebars;
            comboTop.ItemsSource = rebars;
            comboHorizont.ItemsSource = rebarsHorizontal;
            comboCorner.ItemsSource = rebarsCorner;

            comboVert.Visibility = System.Windows.Visibility.Visible;
            boxVertical.Visibility = System.Windows.Visibility.Visible;
        }

        private void RadioButton_Welded_Unchecked(object sender, RoutedEventArgs e)
        {
            isKnittedMode = true;

            comboBottom.ItemsSource = rebarsKnitted;
            comboTop.ItemsSource = rebarsKnitted;
            comboHorizont.ItemsSource = rebarsCorner;
            comboCorner.ItemsSource = rebarsCorner;

            comboVert.Visibility = System.Windows.Visibility.Hidden;
            boxVertical.Visibility = System.Windows.Visibility.Hidden;
        }

        // Срабатывает, когда включается «Вязанные каркасы»
        private void RadioButton_Knitted_Checked(object sender, RoutedEventArgs e)
        {
            isKnittedMode = true;

            comboBottom.ItemsSource = rebarsKnitted;
            comboTop.ItemsSource = rebarsKnitted;
            comboHorizont.ItemsSource = rebarsCorner;
            comboCorner.ItemsSource = rebarsCorner;

            comboVert.Visibility = System.Windows.Visibility.Hidden;
            boxVertical.Visibility = System.Windows.Visibility.Hidden;
        }

        // Срабатывает, когда выключается «Вязанные каркасы»
        private void RadioButton_Knitted_Unchecked(object sender, RoutedEventArgs e)
        {
            isKnittedMode = false;

            comboBottom.ItemsSource = rebars;
            comboTop.ItemsSource = rebars;
            comboHorizont.ItemsSource = rebarsHorizontal;
            comboCorner.ItemsSource = rebarsCorner;

            comboVert.Visibility = System.Windows.Visibility.Visible;
            boxVertical.Visibility = System.Windows.Visibility.Visible;
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();
        }
    }

    public class GrillageSettings
    {
        public string TopDiameter { get; set; }
        public string BottomDiameter { get; set; }
        public string VertDiameter { get; set; }
        public string HorizontDiameter { get; set; }
        public string CornerDiameter { get; set; }
        public int HorizontCount { get; set; }
        public int VerticalStep { get; set; }
        public int HorizontalStep { get; set; }
        public int LeftRightOffset { get; set; }
        public int TopOffset { get; set; }
        public int BottomOffset { get; set; }
        // 06.09.25 - сохранение режима работы
        public bool KnittedMode { get; set; }


        public static void SaveSettings(GrillageSettings settings, string filePath)
        {
            var serializer = new XmlSerializer(typeof(GrillageSettings));
            using (var writer = new StreamWriter(filePath))
            {
                serializer.Serialize(writer, settings);
            }
        }

        public static GrillageSettings LoadSettings(string filePath)
        {
            if (!File.Exists(filePath)) return new GrillageSettings();

            var serializer = new XmlSerializer(typeof(GrillageSettings));
            using (var reader = new StreamReader(filePath))
            {
                return (GrillageSettings)serializer.Deserialize(reader);
            }
        }
    }

    public class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    // Сравниваем числовые части
                    double numX = 0, numY = 0;
                    while (i < x.Length && char.IsDigit(x[i]))
                    {
                        numX = numX * 10 + (x[i] - '0');
                        i++;
                    }
                    while (j < y.Length && char.IsDigit(y[j]))
                    {
                        numY = numY * 10 + (y[j] - '0');
                        j++;
                    }
                    if (numX != numY)
                        return numX.CompareTo(numY);
                }
                else
                {
                    // Сравниваем символы
                    if (x[i] != y[j])
                        return x[i].CompareTo(y[j]);
                    i++;
                    j++;
                }
            }
            return x.Length.CompareTo(y.Length);
        }
    }
}
