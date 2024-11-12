using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FerrumAddin.FM
{
    /// <summary>
    /// Логика взаимодействия для ComparisonWindow.xaml
    /// </summary>
    public partial class ComparisonWindow : Window
    {
        public ObservableCollection<MenuItem> MenuItems { get; set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> RevitFamilies { get; set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> SelectedFamilies { get; set; } = new ObservableCollection<MenuItem>();
        public ObservableCollection<MenuItem> SelectedMenuItems { get; set; } = new ObservableCollection<MenuItem>();

        public ComparisonWindow()
        {
            InitializeComponent();
            DataContext = this;
            foreach (TabItemViewModel tab in FamilyManagerWindow.mvm.TabItems)
            {
                foreach (FerrumAddin.MenuItem item in tab.MenuItems)
                {
                    MenuItems.Add(new MenuItem() { Category = item.Category, Name = item.Name, ImagePath = item.ImagePath });
                }
            }
        }

        private void MenuItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MenuItemsList.SelectedItem is MenuItem selectedItem)
            {
                SelectedMenuItems.Add(selectedItem);
            }
        }

        private void FamiliesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FamiliesList.SelectedItem is MenuItem selectedItem)
            {
                SelectedFamilies.Add(selectedItem);
            }
        }

        private void SelectedFamiliesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedFamiliesList.SelectedItem is MenuItem selectedItem)
            {
                SelectedFamilies.Remove(selectedItem);
            }
        }

        private void SelectedMenuItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SelectedMenuItemsList.SelectedItem is MenuItem selectedItem)
            {
                SelectedMenuItems.Remove(selectedItem);
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
                else if (sender == FamiliesList && SelectedFamilies.Contains(droppedData))
                {
                    SelectedFamilies.Remove(droppedData);
                }
                else if (sender == MenuItemsList && SelectedMenuItems.Contains(droppedData))
                {
                    SelectedMenuItems.Remove(droppedData);
                }
            }
        }

        private void ListView_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private static T GetVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            if (parentObject is T parent) return parent;
            else return GetVisualParent<T>(parentObject);
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
    }

    public class MenuItem
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string ImagePath { get; set; }
    }
}

