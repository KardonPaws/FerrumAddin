using Autodesk.Internal.InfoCenter;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml.Linq;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TabControl = System.Windows.Controls.TabControl;
using TextBox = System.Windows.Controls.TextBox;

namespace FerrumAddin
{
    public partial class FamilyManagerWindow : Page, IDockablePaneProvider
    {
        // fields
        public ExternalCommandData eData = null;
        public static Document doc = null;
        public UIDocument uidoc = null;
        public static ObservableCollection<CategoryFilterItem> CategoryFilters { get; set; } = new ObservableCollection<CategoryFilterItem>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = this as FrameworkElement;
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
        }

        public void CustomInitiator(Document d)
        {
            doc = d;
        }

        public void Newpath()
        {
            mvm = new MainViewModel();
            Tabs.ItemsSource = mvm.TabItems;
        }


        public ObservableCollection<TabItemViewModel> filteredTabItems;

        private void UpdateIsSelectedStates()
        {
            if (filteredTabItems != null)
            {
                if (string.IsNullOrEmpty(SearchText))
                {
                    foreach (var filteredTab in filteredTabItems)
                    {
                        var originalTab = mvm.TabItems.FirstOrDefault(t => t.Header == filteredTab.Header);
                        if (originalTab != null)
                        {
                            foreach (var filteredItem in filteredTab.MenuItems)
                            {
                                var originalItem = originalTab.MenuItems.FirstOrDefault(i => i.Name == filteredItem.Name && i.Category == filteredItem.Category);
                                if (originalItem != null)
                                {
                                    originalItem.IsSelected = filteredItem.IsSelected;
                                }
                            }
                        }
                    }
                }
                else
                {
                    foreach (var originalTab in mvm.TabItems)
                    {
                        var filteredTab = filteredTabItems.FirstOrDefault(t => t.Header == originalTab.Header);
                        if (filteredTab != null)
                        {
                            foreach (var originalItem in originalTab.MenuItems)
                            {
                                var filteredItem = originalTab.MenuItems.FirstOrDefault(i => i.Name == originalItem.Name && i.Category == originalItem.Category);
                                if (filteredItem != null)
                                {
                                    filteredItem.IsSelected = originalItem.IsSelected;
                                }
                            }
                        }
                    }
                }
            }
        }


        public string SearchText;

        public FamilyManagerWindow()
        {
            InitializeComponent();
            mvm = new MainViewModel();
            Tabs.ItemsSource = mvm.TabItems;
            this.DataContext = this;
            Tabs.SelectionChanged += Tabs_SelectionChanged;
        }

        private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SearchText = string.Empty;
            SearchTextBox.Text = string.Empty;
            UpdateCategoryFilters();
        }

        private CancellationTokenSource _cancellationTokenSource;

        // Обновленный метод для запуска фильтрации с учетом отмены предыдущих задач
        private void StartDynamicFiltering()
        {
            // Отмена предыдущей задачи, если она существует
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            ApplyFiltersDynamicAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
        }

        // Метод динамической фильтрации с учетом нового токена и копии оригинальных элементов
        private async Task ApplyFiltersDynamicAsync(CancellationToken cancellationToken)
        {
            var selectedTabItem = Tabs.SelectedItem as TabItemViewModel;
            if (selectedTabItem == null) return;

            // Очистка отображаемого списка и подготовка к новому добавлению
            selectedTabItem.MenuItems.Clear();

            // Получение категорий для фильтрации
            var selectedCategories = CategoryFilters
                .Where(cf => cf.IsChecked)
                .Select(cf => cf.CategoryName)
                .ToHashSet();

            // Копируем OriginalMenuItems для безопасного фильтра
            var itemsToFilter = selectedTabItem.OriginalMenuItems.ToList();

            // Начинаем динамическую фильтрацию
            foreach (var menuItem in itemsToFilter)
            {
                // Проверка на запрос отмены
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Проверка на соответствие условиям фильтра
                bool matchesCategory = selectedCategories.Contains(menuItem.Category);
                bool matchesSearch = string.IsNullOrEmpty(SearchText) ||
                                     menuItem.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     menuItem.Category.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;

                // Добавляем элемент в фильтрованный список, если условия совпадают
                if (matchesCategory && matchesSearch)
                {
                    selectedTabItem.MenuItems.Add(menuItem);

                    // Небольшая задержка для обновления UI
                    await Task.Delay(30, cancellationToken);
                }
            }
        }

        // Обновление вызова при изменении текста в поисковой строке
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchText = (sender as TextBox).Text;
            StartDynamicFiltering();
        }

        // Обновление фильтра при изменении категории
        private void UpdateCategoryFilters()
        {
            CategoryFilters.Clear();
            var selectedTabItem = Tabs.SelectedItem as TabItemViewModel;
            if (selectedTabItem != null)
            {
                var uniqueCategories = selectedTabItem.OriginalMenuItems.Select(mi => mi.Category).Distinct();
                foreach (var category in uniqueCategories)
                {
                    var filterItem = new CategoryFilterItem(StartDynamicFiltering)
                    {
                        CategoryName = category,
                        IsChecked = true
                    };
                    CategoryFilters.Add(filterItem);
                }
            }
            StartDynamicFiltering();
        }



        private void CategoryFilterItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CategoryFilterItem.IsChecked))
            {
                ApplyCategoryFilter();
            }
        }

        private void ApplyCategoryFilter()
        {
            var selectedTabItem = Tabs.SelectedItem as TabItemViewModel;
            if (selectedTabItem != null)
            {
                foreach (var menuItem in selectedTabItem.MenuItems)
                {
                    var categoryFilter = CategoryFilters.FirstOrDefault(cf => cf.CategoryName == menuItem.Category);
                    menuItem.IsVisible = categoryFilter?.IsChecked ?? true;
                }
            }
        }

        private void OptionsButton_MouseEnter(object sender, MouseEventArgs e)
        {
            OptionsPopup.IsOpen = true;
        }

        private void OptionsButton_MouseLeave(object sender, MouseEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!OptionsPopup.IsMouseOver)
                {
                    OptionsPopup.IsOpen = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OptionsPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            OptionsPopup.IsOpen = true;
        }

        private void OptionsPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!OptionsButton.IsMouseOver)
            {
                OptionsPopup.IsOpen = false;
            }
        }

        private static bool isFirstOptionChecked = true;
        public static bool IsFirstOptionChecked()
        {
            return isFirstOptionChecked;
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender == FirstRadioButton)
            {
                isFirstOptionChecked = true;
            }
            else if (sender == SecondRadioButton)
            {
                isFirstOptionChecked = false;
            }
        }

        public static MainViewModel mvm;

        private void ElementClick(object sender, RoutedEventArgs e)
        {
            var frameworkElement = sender as FrameworkElement;
            var menuItem = frameworkElement?.DataContext as MenuItem;
            if (menuItem != null)
            {
                menuItem.IsSelected = !menuItem.IsSelected;
                UpdateIsSelectedStates();
            }
        }

        private async void LoadFamilies(object sender, RoutedEventArgs e)
        {
            doc = App.uiapp.ActiveUIDocument.Document;
            App.LoadEvent.Raise();
            tc = Tabs;
        }

        static TabControl tc;
        static ScrollViewer sv;

        public static void Reload()
        {
            var outdatedTab = FamilyManagerWindow.mvm.TabItems.FirstOrDefault(t => t.Header == "Устаревшее");

            if (outdatedTab != null)
            {
                var selectedItems = outdatedTab.MenuItems.Where(mi => mi.IsSelected).ToList();

                foreach (var selectedItem in selectedItems)
                {
                    outdatedTab.MenuItems.Remove(selectedItem);
                }

                if (outdatedTab.MenuItems.Count == 0)
                {
                    FamilyManagerWindow.mvm.TabItems.Remove(outdatedTab);
                }

                FamilyManagerWindow.tc.ItemsSource = null;
                FamilyManagerWindow.tc.ItemsSource = FamilyManagerWindow.mvm.TabItems;
                tc.SelectedIndex = 0;
            }

            foreach (TabItemViewModel tab in mvm.TabItems)
            {
                foreach (MenuItem menuItem in tab.MenuItems.Where(x => x.IsSelected))
                {
                    menuItem.IsSelected = false;
                }
            }
        }

        public static FrameworkElement FindElementByDataContext(DependencyObject parent, object dataContext)
        {
            if (parent == null) return null;

            if (parent is FrameworkElement frameworkElement && frameworkElement.DataContext == dataContext)
            {
                return frameworkElement;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var result = FindElementByDataContext(child, dataContext);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void CheckFamilyVersions(object sender, RoutedEventArgs e)
        {
            doc = App.uiapp.ActiveUIDocument.Document;
            ObservableCollection<MenuItem> outdatedItems = new ObservableCollection<MenuItem>();

            var collector = new FilteredElementCollector(doc);
            var familyInstances = collector.OfClass(typeof(FamilyInstance)).ToElements();

            foreach (var familyInstance in familyInstances)
            {
                Family family = (familyInstance as FamilyInstance)?.Symbol?.Family;
                if (family == null) continue;

                var matchingMenuItem = FamilyManagerWindow.mvm.TabItems
                    .SelectMany(ti => ti.MenuItems)
                    .FirstOrDefault(mi => mi.Name == family.Name);

                if (matchingMenuItem != null)
                {
                    string projectVersion = GetFamilyVersionFromProject(family);

                    Document loadedFamily = App.uiapp.Application.OpenDocumentFile(matchingMenuItem.Path);
                    if (loadedFamily == null) continue;
                    string loadedFamilyVersion = GetFamilyVersionFromLoadedFamily(loadedFamily);
                    loadedFamily.Close(false);
                    if (!string.Equals(projectVersion, loadedFamilyVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        outdatedItems.Add(matchingMenuItem);
                    }
                }
            }

            if (outdatedItems.Count > 0)
            {
                AddOutdatedTab(outdatedItems);
            }
        }

        private string GetFamilyVersionFromProject(Family family)
        {
            FamilySymbol symbol = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family.Id == family.Id);

            if (symbol != null)
            {
                Parameter versionParam = symbol.LookupParameter("ZH_Версия_Семейства");
                if (versionParam != null)
                {
                    return versionParam.AsString();
                }
            }

            return string.Empty;
        }

        private string GetFamilyVersionFromLoadedFamily(Document familyDoc)
        {
            FamilyManager familyManager = familyDoc.FamilyManager;
            if (familyManager != null)
            {
                FamilyParameter versionParam = familyManager.get_Parameter("ZH_Версия_Семейства");
                if (versionParam != null)
                {
                    return familyManager.CurrentType.AsString(versionParam);
                }
            }

            return string.Empty;
        }

        private void AddOutdatedTab(ObservableCollection<MenuItem> outdatedItems)
        {
            var outdatedTab = new TabItemViewModel
            {
                Header = "Устаревшее",
                MenuItems = outdatedItems
            };

            FamilyManagerWindow.mvm.TabItems.Insert(0, outdatedTab);

            Tabs.ItemsSource = null;
            Tabs.ItemsSource = mvm.TabItems;
            Tabs.SelectedIndex = 0;
        }
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isVisible && isVisible)
                return System.Windows.Visibility.Visible;
            else
                return System.Windows.Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSelected && isSelected)
            {
                return Brushes.LightBlue;
            }
            return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class TabItemViewModel
    {
        public string Header { get; set; }
        public ObservableCollection<MenuItem> MenuItems { get; set; }

        // Оригинальные элементы, используемые для сброса фильтрации
        public List<MenuItem> OriginalMenuItems { get; set; }
    }

    // Инициализируем OriginalMenuItems при загрузке вкладок
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<TabItemViewModel> TabItems { get; set; }

        public MainViewModel()
        {
            TabItems = new ObservableCollection<TabItemViewModel>();
            LoadTabItemsFromXml(App.TabPath);
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
                    var tabItemViewModel = new TabItemViewModel
                    {
                        Header = tabItemElement.Element("Header")?.Value,
                        MenuItems = new ObservableCollection<MenuItem>(),
                        OriginalMenuItems = new List<MenuItem>() // Инициализация оригинальных элементов
                    };

                    foreach (var menuItemElement in tabItemElement.Descendants("MenuItem"))
                    {
                        var menuItem = new MenuItem
                        {
                            Name = menuItemElement.Element("Name")?.Value,
                            Category = menuItemElement.Element("Category")?.Value,
                            ImagePath = menuItemElement.Element("ImagePath")?.Value,
                            Path = menuItemElement.Element("Path")?.Value
                        };

                        tabItemViewModel.MenuItems.Add(menuItem);
                        tabItemViewModel.OriginalMenuItems.Add(menuItem); // Добавляем элемент в OriginalMenuItems
                    }

                    TabItems.Add(tabItemViewModel);
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class MenuItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isVisible = true;
        public string Name { get; set; }
        public string Category { get; set; }
        public string ImagePath { get; set; }
        public string Path { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible != value)
                {
                    _isVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class CategoryFilterItem : INotifyPropertyChanged
    {
        private bool _isChecked;
        private readonly Action _applyFiltersAction;

        public string CategoryName { get; set; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                    _applyFiltersAction?.Invoke();
                }
            }
        }

        public CategoryFilterItem(Action applyFiltersAction)
        {
            _applyFiltersAction = applyFiltersAction;
            _isChecked = true;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
