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
using Point = System.Windows.Point;
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
            FilterItems(SearchText);
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
            int index = Tabs.SelectedIndex;
            if (SearchText == "" || SearchText == String.Empty)
            {
                Tabs.ItemsSource = mvm.TabItems;
                Tabs.SelectedIndex = index;
            }
            else
            {
                filteredTabItems = new ObservableCollection<TabItemViewModel>();

                foreach (var tab in mvm.TabItems)
                {
                    var filteredMenuItems = new ObservableCollection<MenuItem>();

                    foreach (var item in tab.MenuItems)
                    {
                        if (item.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            item.Category.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            filteredMenuItems.Add(item);
                        }
                    }

                    if (filteredMenuItems.Count > 0)
                    {
                        filteredTabItems.Add(new TabItemViewModel
                        {
                            Header = tab.Header,
                            MenuItems = filteredMenuItems
                        });
                    }
                }

                Tabs.ItemsSource = filteredTabItems;
                if (filteredTabItems.Count < index || index == -1)
                {
                    Tabs.SelectedIndex = 0;
                }
                else
                {
                    Tabs.SelectedIndex = index;
                }
            }
            
        }

        public string SearchText;
        // constructor
        public FamilyManagerWindow()
        {
            InitializeComponent();
            mvm = new MainViewModel();
            Tabs.ItemsSource = mvm.TabItems;

        }
        public static MainViewModel mvm;
        private void ElementClick(object sender, RoutedEventArgs e)
        {
            var frameworkElement = sender as FrameworkElement;

            var menuItem = frameworkElement?.DataContext as MenuItem;
            if (menuItem != null)
            {
                menuItem.IsSelected = !menuItem.IsSelected;              
                if (menuItem.IsSelected)
                {
                    (frameworkElement as Button).Background = Brushes.LightBlue;
                }
                else
                {
                    (frameworkElement as Button).Background = Brushes.White;
                }
                UpdateIsSelectedStates();
            }
        }

        private void LoadFamilies(object sender, RoutedEventArgs e)
        {
            App.LoadEvent.Raise();
            tc = Tabs;
            
        }
        static TabControl tc;
        public static void Reload()
        {

            foreach (TabItemViewModel tab in mvm.TabItems)
            {
                foreach (MenuItem menuItem in tab.MenuItems.Where(x => x.IsSelected))
                {
                    menuItem.IsSelected = false;
                }
            }
            int index = tc.SelectedIndex;
            tc.SelectedIndex = -1;
            tc.SelectedIndex = index;
            
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Button btnSender = (Button)sender;
            Point ptLowerLeft = new Point(0, btnSender.Height);
            ptLowerLeft = btnSender.PointToScreen(ptLowerLeft);
            ctMenuStrip.StaysOpen = true;
            ctMenuStrip.IsOpen = true;
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

    public class MenuItem
    {
        private bool _isSelected;
        public string Name { get; set; }
        public string Category { get; set; }
        public string ImagePath { get; set; }
        public string Path { get; set; }
        public bool IsSelected
        {
            get => _isSelected; 
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
