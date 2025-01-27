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
            SaveSettings(); // Сохраняем настройки фильтров категорий при закрытии окна
            MenuItems.Clear();
            RevitFamilies.Clear();
        }

        private void LoadTabItemsFromXml(string filePath)
        {
            if (!File.Exists(filePath))
                return;
            MenuItems.Clear();
            List<string> categoryNames = new List<string>
            {
                "Перекрытия",                 
                "Стены",                      
                "Потолки",                  
                "Окна",                     
                "Двери",                   
                "Крыши",                    
                "Мебель",                 
                "Сантехнические приборы",   
                "Электрические приборы",     
                "Ограждения",              
                "Выступающие профили",     
                "Ребра плит",               
                "Обобщенные модели",     
                "Колонны",            
                "Каркас несущий",           
                "Пандус",                     
                "Лестница",                   
                "Типовые аннотации",          
                "Несущие колонны",            
                "Несущая арматура",           
                "Фундамент несущих конструкций", 
                "Трубы",                      
                "Материалы изоляции труб",    
                "Соединительные детали трубопроводов", 
                "Арматура трубопроводов",     
                "Оборудование",               
                "Арматура воздуховодов",      
                "Воздуховоды",                
                "Воздухораспределители",      
                "Материалы изоляции воздуховодов", 
                "Соединительные детали воздуховодов" 
            };


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
            RevitFamilies.Clear();
            List<BuiltInCategory> categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Floors,                // Перекрытия
                BuiltInCategory.OST_Walls,                 // Стены
                BuiltInCategory.OST_Ceilings,              // Потолки
                BuiltInCategory.OST_Windows,               // Окна
                BuiltInCategory.OST_Doors,                 // Двери
                BuiltInCategory.OST_Roofs,                 // Крыши
                BuiltInCategory.OST_Furniture,             // Мебель
                BuiltInCategory.OST_PlumbingFixtures,      // Сантехнические приборы
                BuiltInCategory.OST_ElectricalFixtures,    // Электрические приборы
                BuiltInCategory.OST_Railings,              // Ограждения
                BuiltInCategory.OST_Cornices,              // Выступающие профили
                BuiltInCategory.OST_StructuralFraming,     // Ребра плит и каркас несущий
                BuiltInCategory.OST_GenericModel,          // Обобщенные модели
                BuiltInCategory.OST_Columns,               // Колонны
                BuiltInCategory.OST_Ramps,                 // Пандус
                BuiltInCategory.OST_Stairs,                // Лестница
                BuiltInCategory.OST_GenericAnnotation,     // Типовые аннотации
                BuiltInCategory.OST_StructuralColumns,     // Несущие колонны
                BuiltInCategory.OST_Rebar,                 // Несущая арматура
                BuiltInCategory.OST_StructuralFoundation,  // Фундамент несущих конструкций
                BuiltInCategory.OST_PipeCurves,            // Трубы
                BuiltInCategory.OST_PipeInsulations,       // Материалы изоляции труб
                BuiltInCategory.OST_PipeFitting,           // Соединительные детали трубопроводов
                BuiltInCategory.OST_PipeAccessory,         // Арматура трубопроводов
                BuiltInCategory.OST_MechanicalEquipment,   // Оборудование
                BuiltInCategory.OST_DuctAccessory,         // Арматура воздуховодов
                BuiltInCategory.OST_DuctCurves,            // Воздуховоды
                BuiltInCategory.OST_DuctTerminal,          // Воздухораспределители
                BuiltInCategory.OST_DuctInsulations,       // Материалы изоляции воздуховодов
                BuiltInCategory.OST_DuctFitting            // Соединительные детали воздуховодов
            };

            // Создаем список для хранения данных
            List<(Category category, string familyName, string typeName)> elementData = new List<(Category, string, string)>();

            foreach (Element elem in collector)
            {
                // Получаем тип элемента
                ElementId typeId = elem.GetTypeId();
                ElementType elementType = doc.GetElement(typeId) as ElementType;

                if (elementType != null && categories.Contains(elem.Category.BuiltInCategory))
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
            LoadSettings(); // Загружаем настройки фильтров категорий

            _cancellationTokenSource1?.Cancel();
            _cancellationTokenSource1 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredMenuItemsAsync(_cancellationTokenSource1.Token).ConfigureAwait(false);
            
            _cancellationTokenSource2?.Cancel();
            _cancellationTokenSource2 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredRevitFamiliesAsync(_cancellationTokenSource2.Token).ConfigureAwait(false);
        }

        private void SaveSettings()
        {
            var settings = new CategoryFilterSettings
            {
                MenuCategoryFilters = MenuCategoryFilters.ToList(),
                FamilyCategoryFilters = FamilyCategoryFilters.ToList(),
                Height = this.Height,
                Width = this.Width,
                WindowState = this.WindowState,
                Top = this.Top,
                Left = this.Left
            };

            var json = JsonSerializer.Serialize(settings);
            File.WriteAllText(SettingsFilePath, json);
        }

        private void LoadSettings()
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
                this.WindowState = WindowState.Maximized;          
            }
            else
            { 
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<CategoryFilterSettings>(json);
                var allCategoriesMenu = MenuItems.Select(m => m.Category).Distinct().ToList();
                var allCategoriesRevit = RevitFamilies.Select(m => m.Category).Distinct().ToList();

                if (settings != null)
                {
                    MenuCategoryFilters.Clear();
                    foreach (var category in allCategoriesMenu)
                    {
                        MenuCategoryFilters.Add(new CategoryFilter() { CategoryName = category, IsChecked = true });
                    }
                    foreach (var filter in settings.MenuCategoryFilters)
                    {
                        if (allCategoriesMenu.Contains(filter.CategoryName))
                            MenuCategoryFilters.Where(x=> x.CategoryName == filter.CategoryName).First().IsChecked = filter.IsChecked;
                    }
                    FamilyCategoryFilters.Clear();
                    foreach (var category in allCategoriesRevit)
                    {
                        FamilyCategoryFilters.Add(new CategoryFilter() { CategoryName = category, IsChecked = true });
                    }
                    foreach (var filter in settings.FamilyCategoryFilters)
                    {
                        if (allCategoriesRevit.Contains(filter.CategoryName))
                            FamilyCategoryFilters.Where(x => x.CategoryName == filter.CategoryName).First().IsChecked = filter.IsChecked;
                    }
                    this.Height = settings.Height;
                    this.Width = settings.Width;
                    this.WindowState = settings.WindowState;
                    this.Top = settings.Top;
                    this.Left = settings.Left;
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
            SaveSettings();
            ComparisonWindowShow.changeTypesEv.Raise();
            this.Close();
        }

       
    }

    public class CategoryFilterSettings
    {
        public List<CategoryFilter> MenuCategoryFilters { get; set; }
        public List<CategoryFilter> FamilyCategoryFilters { get; set; }
        public double Height { get; set; }
        public double Width { get; set; }
        public WindowState WindowState { get; set; }
        public double Top { get; set; }
        public double Left { get; set; }
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
