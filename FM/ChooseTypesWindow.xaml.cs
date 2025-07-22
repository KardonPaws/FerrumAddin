using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace FerrumAddinDev.FM
{
    public partial class ChooseTypesWindow : Window, INotifyPropertyChanged
    {
        // Словарь: ключ = Name MenuItem, значение = DataView для этого item.Path
        public Dictionary<string, DataView> Types { get; set; }

        public List<FerrumAddinDev.MenuItem> MenuItems { get; }

        private FerrumAddinDev.MenuItem _selectedMenuItem;
        public FerrumAddinDev.MenuItem SelectedMenuItem
        {
            get => _selectedMenuItem;
            set
            {
                if (_selectedMenuItem != value)
                {
                    _selectedMenuItem = value;
                    OnPropertyChanged(nameof(SelectedMenuItem));
                    if (Types.TryGetValue(_selectedMenuItem.Name, out var dv))
                        CurrentView = dv;
                }
            }
        }

        private DataView _currentView;
        public DataView CurrentView
        {
            get => _currentView;
            set { _currentView = value; OnPropertyChanged(nameof(CurrentView)); }
        }

        public ChooseTypesWindow(List<FerrumAddinDev.MenuItem> menuItems)
        {
            InitializeComponent();
            MenuItems = menuItems;
            Types = LoadCsvViews(menuItems);
            DataContext = this;
        }

        private Dictionary<string, DataView> LoadCsvViews(List<FerrumAddinDev.MenuItem> menuItems)
        {
            var dict = new Dictionary<string, DataView>();

            foreach (var item in menuItems)
            {
                if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path.Remove(item.Path.Length - 3, 3) + "txt"))
                    continue;

                var dt = new DataTable();
                DataRow dr;

                // Добавляем две первые колонки: Выбор и Тип
                dt.Columns.Add("Выбор", typeof(bool));
                // 29.06.25 - смена имени столбца для удаления конфликта с параметром
                dt.Columns.Add("Имя типаᅟ", typeof(string));

                bool firstRow = true;
                foreach (var line in File.ReadLines(item.Path.Remove(item.Path.Length - 3, 3) + "txt", Encoding.GetEncoding("windows-1251")))
                {
                    // 29.06.25 - смена разделителя
                    var cols = line.Split(',', ';');
                    if (firstRow)
                    {
                        bool firstCol = true;
                        foreach (var col in cols)
                        {
                            if (firstCol)
                            {
                                firstCol = false;
                                continue;
                            }
                            var cleaned = col.Split('#')[0];
                            dt.Columns.Add(cleaned);
                        }
                        firstRow = false;
                        continue;
                    }

                    dr = dt.NewRow();
                    // Заполняем поле "Тип" и остальные
                    for (int i = 0; i < cols.Length - 1; i++)
                    {
                        dr[i + 1] = cols[i];
                    }
                    dr[0] = false;
                    dt.Rows.Add(dr);
                }

                dict[item.Name] = new DataView(dt);
            }

            return dict;
        }

        private void FamiliesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedMenuItem != null && Types.TryGetValue(SelectedMenuItem.Name, out var view))
                CurrentView = view;
        }
        public static Dictionary<string, List<string>> selectedTypes;
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Собираем все выбранные элементы из всех DataView
            selectedTypes = new Dictionary<string, List<string>>();
            foreach (var kvp in Types)
            {
                var familyName = kvp.Key;
                var view = kvp.Value;
                foreach (DataRowView drv in view)
                {
                    if (drv.Row.Field<bool>("Выбор"))
                    {
                        string type = drv.Row.Field<string>("Тип");
                        if (selectedTypes.Keys.Contains(familyName))
                        {
                            selectedTypes[familyName].Add(type);
                        }
                        else
                        {
                            selectedTypes.Add(familyName, new List<string>() { type });
                        }
                    }
                }
            }
           
            DialogResult = true;
            this.Close();
        }
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}