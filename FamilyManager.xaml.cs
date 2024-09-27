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
    /// <summary>
    /// Логика взаимодействия для Page1.xaml
    /// </summary>
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

        // IDockablePaneProvider abstrat method
        public void SetupDockablePane(DockablePaneProviderData data)
        {
            // wpf object with pane's interface
            data.FrameworkElement = this as FrameworkElement;
            // initial state position
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Tabbed,
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };

        }
        public void CustomInitiator(Document d)
        {
            // ExternalCommandData and Doc
            doc = d;

        }
        public void Newpath()
        {
            mvm = new MainViewModel();
            Tabs.ItemsSource = mvm.TabItems;
        }
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchText = (sender as TextBox).Text;
            ApplyFilters();
        }

        public ObservableCollection<TabItemViewModel> filteredTabItems;

        private void UpdateIsSelectedStates()
        {
            if (filteredTabItems != null)
            {
                if (SearchText == "" || SearchText == String.Empty)
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
        private void FilterItems(string searchText)
        {
            SearchText = searchText;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            var selectedTabItem = Tabs.SelectedItem as TabItemViewModel;
            int ind = Tabs.SelectedIndex;
            if (selectedTabItem != null)
            {
                var selectedCategories = CategoryFilters
                    .Where(cf => cf.IsChecked)
                    .Select(cf => cf.CategoryName)
                    .ToHashSet();

                foreach (var menuItem in selectedTabItem.MenuItems)
                {
                    bool matchesCategory = selectedCategories.Contains(menuItem.Category);
                    bool matchesSearch = string.IsNullOrEmpty(SearchText) ||
                                         menuItem.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         menuItem.Category.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;

                    menuItem.IsVisible = matchesCategory && matchesSearch;
                }
            }
            if (ind != -1)
            {
                Tabs.SelectionChanged -= Tabs_SelectionChanged;
                mvm.TabItems[mvm.TabItems.IndexOf(selectedTabItem)] = selectedTabItem;
                Tabs.ItemsSource = mvm.TabItems;
                Tabs.SelectedIndex = ind;
                Tabs.SelectionChanged += Tabs_SelectionChanged;
            }
        }


        public string SearchText;
        // constructor
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
            UpdateCategoryFilters();
        }

        private void UpdateCategoryFilters()
        {
            // Clear the current category filters
            CategoryFilters.Clear();

            // Get the selected TabItemViewModel
            var selectedTabItem = Tabs.SelectedItem as TabItemViewModel;
            if (selectedTabItem != null)
            {
                // Get unique categories from MenuItems
                var uniqueCategories = selectedTabItem.MenuItems.Select(mi => mi.Category).Distinct();

                // For each category, create a CategoryFilterItem
                foreach (var category in uniqueCategories)
                {
                    var filterItem = new CategoryFilterItem(ApplyFilters)
                    {
                        CategoryName = category,
                        IsChecked = true // By default, all categories are visible
                    };
                    CategoryFilters.Add(filterItem);
                }
            }

            // Apply the filters initially
            ApplyFilters();
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
            // Get the selected TabItemViewModel
            var selectedTabItem = Tabs.SelectedItem as TabItemViewModel;
            if (selectedTabItem != null)
            {
                // For each MenuItem, set its IsVisible based on the category filter
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
            // Откладываем закрытие, чтобы пользователь мог переместить курсор на Popup
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
            // Закрываем Popup, если курсор не находится на кнопке
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
                //if (menuItem.IsSelected)
                //{
                //    (frameworkElement as Button).Background = Brushes.LightBlue;
                //}
                //else
                //{
                //    (frameworkElement as Button).Background = Brushes.White;
                //}
                UpdateIsSelectedStates();
            }
        }

        private void LoadFamilies(object sender, RoutedEventArgs e)
        {
            App.LoadEvent.Raise();
            tc = Tabs;            
        }

        static TabControl tc;
        static ScrollViewer sv;
        public static void Reload()
        {

            foreach (TabItemViewModel tab in mvm.TabItems)
            {
                foreach (MenuItem menuItem in tab.MenuItems.Where(x => x.IsSelected))
                {
                    menuItem.IsSelected = false;
                }
            }
            //int index = tc.SelectedIndex;
            //var selectedCategories = CategoryFilters.Where(cf => cf.IsChecked);
            //tc.SelectedIndex = -1;
            //tc.SelectedIndex = index;
            //CategoryFilters = (ObservableCollection<CategoryFilterItem>)selectedCategories;

        }
        public static FrameworkElement FindElementByDataContext(DependencyObject parent, object dataContext)
        {
            if (parent == null) return null;

            // Проверяем, является ли текущий элемент FrameworkElement и совпадает ли его DataContext
            if (parent is FrameworkElement frameworkElement && frameworkElement.DataContext == dataContext)
            {
                return frameworkElement;
            }

            // Проходим по дочерним элементам текущего объекта
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var result = FindElementByDataContext(child, dataContext);
                if (result != null)
                {
                    return result; // Если нашли элемент с нужным DataContext, возвращаем его
                }
            }

            return null; // Если не нашли, возвращаем null
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
                        MenuItems = new ObservableCollection<MenuItem>()
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

    public class TabItemViewModel
    {
        public string Header { get; set; }
        public ObservableCollection<MenuItem> MenuItems { get; set; }
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
            _isChecked = true; // Default to checked
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

}
