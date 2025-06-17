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
        public WindowGrillageCreator_v2(List<Element> elements, List<Element> elements2, List<Element> elements3)
        {
            InitializeComponent();
            List<string> rebars = new List<string>();
            foreach (Element element in elements)
            {
                rebars.Add(element.Name);
            }
            rebars = rebars.OrderBy(x=>x, new NaturalStringComparer()).ToList();
            List<string> rebars2 = new List<string>();
            foreach (Element element in elements2)
            {
                rebars2.Add(element.Name);
            }
            rebars2 = rebars2.OrderBy(x => x, new NaturalStringComparer()).ToList();
            List<string> rebars3 = new List<string>();
            foreach (Element element in elements3)
            {
                rebars3.Add(element.Name);
            }
            rebars3 = rebars3.OrderBy(x => x, new NaturalStringComparer()).ToList();
            comboBottom.ItemsSource = rebars;
            comboTop.ItemsSource = rebars;
            comboVert.ItemsSource = rebars;
            comboHorizont.ItemsSource = rebars2;
            comboCorner.ItemsSource = rebars3;
            LoadSettings();
        }

        private string _settingsFilePath = App.xmlFilePath + "GrillageCreator.xml";

        private void LoadSettings()
        {
            if (string.IsNullOrEmpty(_settingsFilePath)) return;

            var settings = GrillageSettings.LoadSettings(_settingsFilePath);
            if (settings == null) return;

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
            boxTopBottom.Text = settings.TopBottomOffset == 0 ? "50" : settings.TopBottomOffset.ToString();
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
                TopBottomOffset = int.TryParse(boxTopBottom.Text, out var tb) ? tb : 50
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
        public static int topBottomOffset;
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
                string.IsNullOrEmpty(boxVertical.Text) ||
                string.IsNullOrEmpty(boxLeftRight.Text) ||
                string.IsNullOrEmpty(boxTopBottom.Text) ||
                string.IsNullOrEmpty(boxHorizontal.Text) ||
                comboTop.SelectedItem == null ||
                comboBottom.SelectedItem == null ||
                comboVert.SelectedItem == null ||
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
                topBottomOffset = int.Parse(boxTopBottom.Text);
                horizontCount = int.Parse(boxHorizontal.Text);

                topDiameter = comboTop.SelectedItem.ToString();
                bottomDiameter = comboBottom.SelectedItem.ToString();
                vertDiameter = comboVert.SelectedItem.ToString();
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

            // Отписываемся, чтобы перестать синхронно менять значения
            comboVert.SelectionChanged -= ComboVert_SelectionChanged;
            comboHorizont.SelectionChanged -= ComboHorizont_SelectionChanged;
            boxVertical.TextChanged -= BoxVertical_TextChanged;
            boxHorizontal.TextChanged -= BoxHorizontal_TextChanged;
        }

        private void RadioButton_Welded_Unchecked(object sender, RoutedEventArgs e)
        {
            isKnittedMode = true;

            comboVert.SelectedItem = comboHorizont.SelectedItem;
            boxVertical.Text = boxHorizontal.Text;

            // Подписываемся на события, чтобы синхронизировать «вверх/вниз»
            comboVert.SelectionChanged += ComboVert_SelectionChanged;
            comboHorizont.SelectionChanged += ComboHorizont_SelectionChanged;
            boxVertical.TextChanged += BoxVertical_TextChanged;
            boxHorizontal.TextChanged += BoxHorizontal_TextChanged;
        }

        // Срабатывает, когда включается «Вязанные каркасы»
        private void RadioButton_Knitted_Checked(object sender, RoutedEventArgs e)
        {
            isKnittedMode = true;

            comboVert.SelectedItem = comboHorizont.SelectedItem;
            boxVertical.Text = boxHorizontal.Text;

            // Подписываемся на события, чтобы синхронизировать «вверх/вниз»
            comboVert.SelectionChanged += ComboVert_SelectionChanged;
            comboHorizont.SelectionChanged += ComboHorizont_SelectionChanged;
            boxVertical.TextChanged += BoxVertical_TextChanged;
            boxHorizontal.TextChanged += BoxHorizontal_TextChanged;
        }

        // Срабатывает, когда выключается «Вязанные каркасы»
        private void RadioButton_Knitted_Unchecked(object sender, RoutedEventArgs e)
        {
            isKnittedMode = false;

            // Отписываемся, чтобы перестать синхронно менять значения
            comboVert.SelectionChanged -= ComboVert_SelectionChanged;
            comboHorizont.SelectionChanged -= ComboHorizont_SelectionChanged;
            boxVertical.TextChanged -= BoxVertical_TextChanged;
            boxHorizontal.TextChanged -= BoxHorizontal_TextChanged;
        }

        // Если пользователь поменял выбор в comboVert, то, будучи в «вязаном» режиме,
        // сразу выставляем тот же SelectedItem у comboHorizont
        private void ComboVert_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isKnittedMode) return;

            var newValue = comboVert.SelectedItem;
            if (newValue != null && !newValue.Equals(comboHorizont.SelectedItem))
                comboHorizont.SelectedItem = newValue;
        }

        // Аналогично, если пользователь поменял выбор в comboHorizont
        private void ComboHorizont_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isKnittedMode) return;

            var newValue = comboHorizont.SelectedItem;
            if (newValue != null && !newValue.Equals(comboVert.SelectedItem))
                comboVert.SelectedItem = newValue;
        }

        // При изменении текста в boxVertical – копируем его в boxHorizontal,
        // но с флагом isUpdatingBoxes, чтобы не зациклиться
        private void BoxVertical_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isKnittedMode || isUpdatingBoxes) return;

            try
            {
                isUpdatingBoxes = true;
                if (boxHorizontal == null)
                {
                    return;
                }
                string newText = boxVertical.Text;
                if (boxHorizontal.Text != newText)
                    boxHorizontal.Text = newText;
            }
            finally
            {
                isUpdatingBoxes = false;
            }
        }

        // При изменении текста в boxHorizontal – копируем его в boxVertical
        private void BoxHorizontal_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isKnittedMode || isUpdatingBoxes) return;

            try
            {
                isUpdatingBoxes = true;
                if (boxVertical == null)
                {
                    return;
                }
                string newText = boxHorizontal.Text;
                if (boxVertical.Text != newText)
                    boxVertical.Text = newText;
            }
            finally
            {
                isUpdatingBoxes = false;
            }
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
        public int TopBottomOffset { get; set; }

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
