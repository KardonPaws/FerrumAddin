using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FerrumAddin
{
    /// <summary>
    /// Логика взаимодействия для Page1.xaml
    /// </summary>
    public partial class Viewer : Page, IDockablePaneProvider
    {
        // fields
        public ExternalCommandData eData = null;
        public Document doc = null;
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


        private void MenuItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                // Find the parent StackPanel that has the DataContext of MenuItem
                
            }
        }

        // constructor
        public Viewer()
        {
            InitializeComponent();
            MainViewModel mvm = new MainViewModel();
            Tabs.ItemsSource = mvm.TabItems;

        }

        private void Button_Click(object sender, RoutedEventArgs e)
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

            }
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
            TabItems = new ObservableCollection<TabItemViewModel>
            {
                new TabItemViewModel { Header = "Tab 1", MenuItems = new ObservableCollection<MenuItem>
                    {
                        new MenuItem { Name = "Tab 1 Item 1", Category = "Category 1", ImagePath = "path/to/image1.png" },
                        new MenuItem { Name = "Tab 1 Item 2", Category = "Category 1", ImagePath = "path/to/image2.png" }
                    }
                },
                new TabItemViewModel { Header = "Tab 2", MenuItems = new ObservableCollection<MenuItem>
                    {
                        new MenuItem { Name = "Tab 2 Item 1", Category = "Category 2", ImagePath = "path/to/image3.png" },
                        new MenuItem { Name = "Tab 2 Item 2", Category = "Category 2", ImagePath = "path/to/image4.png" }
                    }
                },
                new TabItemViewModel { Header = "Tab 3", MenuItems = new ObservableCollection<MenuItem>
                    {
                        new MenuItem { Name = "Tab 3 Item 1", Category = "Category 3", ImagePath = "path/to/image5.png" },
                        new MenuItem { Name = "Tab 3 Item 2", Category = "Category 3", ImagePath = "path/to/image6.png" }
                    }
                }
            };
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
