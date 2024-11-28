using Autodesk.Revit.Creation;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Document = Autodesk.Revit.DB.Document;
using Transform = Autodesk.Revit.DB.Transform;

namespace FerrumAddin.FM
{
    public partial class ComparisonWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<MenuItem> MenuItems { get; set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> RevitFamilies { get; set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> SelectedFamilies { get; set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> SelectedMenuItems { get; set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> FilteredMenuItems { get; private set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> FilteredRevitFamilies { get; private set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<CategoryFilter> MenuCategoryFilters { get; set; } = new ObservableCollection<CategoryFilter>();
        public ObservableCollection<CategoryFilter> FamilyCategoryFilters { get; set; } = new ObservableCollection<CategoryFilter>();
        private static readonly string SettingsFilePath = Path.Combine(App.downloadDir, "CategoryFiltersSettings.json");


        private string _menuSearchText;
        public string MenuSearchText
        {
            get => _menuSearchText;
            set
            {
                _menuSearchText = value;
                OnPropertyChanged(nameof(MenuSearchText));
                _cancellationTokenSource1?.Cancel();
                _cancellationTokenSource1 = new CancellationTokenSource();

                // Запуск нового фильтра с токеном отмены
                UpdateFilteredMenuItemsAsync(_cancellationTokenSource1.Token).ConfigureAwait(false);
            }
        }

        private string _familySearchText;
        private CancellationTokenSource _cancellationTokenSource1;
        private CancellationTokenSource _cancellationTokenSource2;


        public string FamilySearchText
        {
            get => _familySearchText;
            set
            {
                _familySearchText = value;
                OnPropertyChanged(nameof(FamilySearchText));
                _cancellationTokenSource2?.Cancel();
                _cancellationTokenSource2 = new CancellationTokenSource();

                // Запуск нового фильтра с токеном отмены
                UpdateFilteredRevitFamiliesAsync(_cancellationTokenSource2.Token).ConfigureAwait(false);
            }
        }

        Document doc;

        public ComparisonWindow(ExternalCommandData commandData)
        {
            InitializeComponent();
            DataContext = this;
            doc = commandData.Application.ActiveUIDocument.Document;
            LoadData();
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveCategoryFilterSettings(); // Сохраняем настройки фильтров категорий при закрытии окна
        }

        private void LoadTabItemsFromXml(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var xdoc = XDocument.Load(filePath);

            foreach (var tabItemElement in xdoc.Descendants("TabItem"))
            {
                if (Convert.ToBoolean(tabItemElement.Element("Visibility")?.Value) == true)
                {

                    foreach (var menuItemElement in tabItemElement.Descendants("MenuItem"))
                    {
                        var menuItem = new MenuItem
                        {
                            Name = menuItemElement.Element("Name")?.Value,
                            Category = menuItemElement.Element("Category")?.Value,
                            ImagePath = menuItemElement.Element("ImagePath")?.Value,
                            Path = menuItemElement.Element("Path")?.Value
                        };

                        MenuItems.Add(menuItem);
                    }
                }
            }
        }

        private void LoadRevitFamilies()
        {
            List<Element> collector = (List<Element>)new FilteredElementCollector(doc)
            .WhereElementIsNotElementType().ToElements()
            .Where(e => e.Category != null && e.Category.HasMaterialQuantities).ToList(); 

            // Создаем список для хранения данных
            List<(Category category, string familyName, string typeName)> elementData = new List<(Category, string, string)>();

            foreach (Element elem in collector)
            {
                // Получаем тип элемента
                ElementId typeId = elem.GetTypeId();
                ElementType elementType = doc.GetElement(typeId) as ElementType;

                if (elementType != null)
                {
                    // Получаем категорию, имя семейства и тип
                    Category category = elem.Category;
                    string familyName = elementType.FamilyName;
                    string typeName = elementType.Name;

                    // Добавляем в список
                    elementData.Add((category, familyName, typeName));
                }
            }

            // Сортируем список по категории, имени семейства и типу
            var sortedData = elementData
            .GroupBy(data => data.typeName) // Группируем по имени типа
            .Select(group => group.First()) // Берем первый элемент из каждой группы
            .OrderBy(data => data.category.Name) // Сортировка по категории
            .ThenBy(data => data.familyName)
            .ThenBy(data => data.typeName) // Затем по типу
            .ToList();

            // Вывод данных в окно
            string result = "Список элементов (сортировано по категории, семейству и типу):\n";
            foreach (var data in sortedData)
            {
                RevitFamilies.Add(new MenuItem()
                {
                    FamilyName = data.familyName,
                    Category = data.category.Name,
                    Name = data.typeName,
                    RevitCategory = data.category.BuiltInCategory
                });
            }
        }



        private void LoadData()
        {
            LoadTabItemsFromXml(App.TabPath);
            LoadRevitFamilies();

            var allCategories = MenuItems.Select(m => m.Category).Distinct().ToList();
            LoadCategoryFilterSettings(); // Загружаем настройки фильтров категорий

            _cancellationTokenSource1?.Cancel();
            _cancellationTokenSource1 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredMenuItemsAsync(_cancellationTokenSource1.Token).ConfigureAwait(false);
            
            _cancellationTokenSource2?.Cancel();
            _cancellationTokenSource2 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredRevitFamiliesAsync(_cancellationTokenSource2.Token).ConfigureAwait(false);
        }

        private void SaveCategoryFilterSettings()
        {
            var settings = new CategoryFilterSettings
            {
                MenuCategoryFilters = MenuCategoryFilters.ToList(),
                FamilyCategoryFilters = FamilyCategoryFilters.ToList()
            };

            var json = JsonSerializer.Serialize(settings);
            File.WriteAllText(SettingsFilePath, json);
        }

        private void LoadCategoryFilterSettings()
        {
            if (!File.Exists(SettingsFilePath))
            {
                var allCategories = MenuItems.Select(m => m.Category).Distinct().ToList();
                foreach (var category in allCategories)
                {
                    MenuCategoryFilters.Add(new CategoryFilter { CategoryName = category, IsChecked = true });
                }

                allCategories = RevitFamilies.Select(f => f.Category).Distinct().ToList();
                foreach (var category in allCategories)
                {
                    FamilyCategoryFilters.Add(new CategoryFilter { CategoryName = category, IsChecked = true });
                }
            }
            else
            { 
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<CategoryFilterSettings>(json);

                if (settings != null)
                {
                    MenuCategoryFilters.Clear();
                    foreach (var filter in settings.MenuCategoryFilters)
                    {
                        MenuCategoryFilters.Add(filter);
                    }

                    FamilyCategoryFilters.Clear();
                    foreach (var filter in settings.FamilyCategoryFilters)
                    {
                        FamilyCategoryFilters.Add(filter);
                    }
                }
            }
        }


        private async Task UpdateFilteredMenuItemsAsync(CancellationToken cancellationToken)
        {
            var selectedCategories = MenuCategoryFilters.Where(c => c.IsChecked).Select(c => c.CategoryName).ToList();

            FilteredMenuItems.Clear();
            foreach (var item in MenuItems)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                if ((string.IsNullOrEmpty(MenuSearchText) || item.Name.IndexOf(MenuSearchText, StringComparison.OrdinalIgnoreCase) >= 0) &&
                selectedCategories.Contains(item.Category))
                {
                    FilteredMenuItems.Add(item);
                    await Task.Delay(30, cancellationToken);

                }
            }
        }

        private async Task UpdateFilteredRevitFamiliesAsync(CancellationToken cancellationToken)
        {
            
                var selectedCategories = FamilyCategoryFilters.Where(c => c.IsChecked).Select(c => c.CategoryName).ToList();

                FilteredRevitFamilies.Clear();
                foreach (var item in RevitFamilies)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                if ((string.IsNullOrEmpty(FamilySearchText) || item.Name.IndexOf(FamilySearchText, StringComparison.OrdinalIgnoreCase) >= 0) &&
                                    selectedCategories.Contains(item.Category))
                {
                    FilteredRevitFamilies.Add(item);
                    await Task.Delay(30, cancellationToken);

                }
            }
        }

        private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ListView listView = sender as ListView;
            if (listView == null) return;

            var item = GetVisualParent<ListViewItem>((DependencyObject)e.OriginalSource);
            if (item == null) return;

            DragDrop.DoDragDrop(listView, item.DataContext, DragDropEffects.Move);
        }

        private void ListView_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(MenuItem)) is MenuItem droppedData)
            {
                if (sender == SelectedFamiliesList && !SelectedFamilies.Contains(droppedData))
                {
                    SelectedFamilies.Add(droppedData);
                }
                else if (sender == SelectedMenuItemsList && !SelectedMenuItems.Contains(droppedData))
                {
                    SelectedMenuItems.Add(droppedData);
                }
            }
        }

        private static T GetVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            if (parentObject is T parent) return parent;
            else return GetVisualParent<T>(parentObject);
        }

        private void MenuCategoryFilter_Changed(object sender, EventArgs e)
        {
            _cancellationTokenSource1?.Cancel();
            _cancellationTokenSource1 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredMenuItemsAsync(_cancellationTokenSource1.Token).ConfigureAwait(false);
        }

        private void FamilyCategoryFilter_Changed(object sender, EventArgs e)
        {
            _cancellationTokenSource2?.Cancel();
            _cancellationTokenSource2 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredRevitFamiliesAsync(_cancellationTokenSource2.Token).ConfigureAwait(false);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RemoveSelectedFamily_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is MenuItem selectedItem)
            {
                SelectedFamilies.Remove(selectedItem);
            }
        }

        private void RemoveSelectedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is MenuItem selectedItem)
            {
                SelectedMenuItems.Remove(selectedItem);
            }
        }


        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if ((sender as CheckBox).Name == "MenuCat")
            {
                _cancellationTokenSource1?.Cancel();
                _cancellationTokenSource1 = new CancellationTokenSource();

                // Запуск нового фильтра с токеном отмены
                UpdateFilteredMenuItemsAsync(_cancellationTokenSource1.Token).ConfigureAwait(false);
            }
            else
            {
                _cancellationTokenSource2?.Cancel();
                _cancellationTokenSource2 = new CancellationTokenSource();

                // Запуск нового фильтра с токеном отмены
                UpdateFilteredRevitFamiliesAsync(_cancellationTokenSource2.Token).ConfigureAwait(false);
            }
        }

        private void MenuItemsList_MouseDoubleClick_1(object sender, MouseButtonEventArgs e)
        {
            if (MenuItemsList.SelectedItem != null)
            {
                SelectedMenuItems.Add((MenuItem)MenuItemsList.SelectedItem);
            }
        }

        private void FamiliesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FamiliesList.SelectedItem != null && !SelectedFamilies.Contains((MenuItem)FamiliesList.SelectedItem))
            {
                SelectedFamilies.Add((MenuItem)FamiliesList.SelectedItem);
            }
        }
        public static List<MenuItem> selFam;
        public static List<MenuItem> selMen;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            selFam = SelectedFamilies.ToList();
            selMen = SelectedMenuItems.ToList();
            SaveCategoryFilterSettings();
            ComparisonWindowShow.changeTypesEv.Raise();
            this.Close();
        }

       
    }

    public class CategoryFilterSettings
    {
        public List<CategoryFilter> MenuCategoryFilters { get; set; }
        public List<CategoryFilter> FamilyCategoryFilters { get; set; }
    }

    public class MenuItem
    {
        public string Name { get; set; }
        public string FamilyName { get; set; }
        public string Category { get; set; }
        public string ImagePath { get; set; }
        public string Path { get; set; }
        public BuiltInCategory RevitCategory { get; set; }
    }

    public class CategoryFilter : INotifyPropertyChanged
    {
        public string CategoryName { get; set; }
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
