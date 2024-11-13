using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;

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

        public ComparisonWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadData();
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

        private void LoadData()
        {
            LoadTabItemsFromXml(App.TabPath);

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

            _cancellationTokenSource1?.Cancel();
            _cancellationTokenSource1 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredMenuItemsAsync(_cancellationTokenSource1.Token).ConfigureAwait(false);
            
            _cancellationTokenSource2?.Cancel();
            _cancellationTokenSource2 = new CancellationTokenSource();

            // Запуск нового фильтра с токеном отмены
            UpdateFilteredRevitFamiliesAsync(_cancellationTokenSource2.Token).ConfigureAwait(false);
        }

        private async Task UpdateFilteredMenuItemsAsync(CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var selectedCategories = MenuCategoryFilters.Where(c => c.IsChecked).Select(c => c.CategoryName).ToList();
                var filtered = MenuItems.Where(item => (string.IsNullOrEmpty(MenuSearchText) || item.Name.ToLower().Contains(MenuSearchText.ToLower())) &&
                                                       (selectedCategories.Contains(item.Category))).ToList();


                FilteredMenuItems.Clear();
                foreach (var item in filtered)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    FilteredMenuItems.Add(item);
                }
            });
        }

        private async Task UpdateFilteredRevitFamiliesAsync(CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var selectedCategories = FamilyCategoryFilters.Where(c => c.IsChecked).Select(c => c.CategoryName).ToList();
                var filtered = RevitFamilies.Where(item => (string.IsNullOrEmpty(FamilySearchText) || item.Name.ToLower().Contains(FamilySearchText.ToLower())) &&
                                                           (selectedCategories.Contains(item.Category))).ToList();

                FilteredRevitFamilies.Clear();
                foreach (var item in filtered)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                    FilteredRevitFamilies.Add(item);
                }
            });
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

        private void FamiliesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FamiliesList.SelectedItem is MenuItem selectedItem)
            {
                SelectedFamilies.Add(selectedItem);
            }
        }

        private void MenuItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MenuItemsList.SelectedItem is MenuItem selectedItem)
            {
                SelectedMenuItems.Add(selectedItem);
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

        private void ListView_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    public class MenuItem
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string ImagePath { get; set; }
        public string Path { get; set; }
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
