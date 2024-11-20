using Autodesk.Revit.Creation;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
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
            .ThenBy(data => data.typeName) // Затем по типу
            .ToList();

            // Вывод данных в окно
            string result = "Список элементов (сортировано по категории, семейству и типу):\n";
            foreach (var data in sortedData)
            {
                RevitFamilies.Add(new MenuItem()
                {
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedFamilies.Count() != SelectedMenuItems.Count())
            {
                TaskDialog.Show("Внимание", "Количество выбранных элементов не совпадает");
            }
            else
            {
                using (Transaction trans = new Transaction(doc, "Сопоставление семейств"))
                {
                    trans.Start();
                    FailureHandlingOptions failureOptions = trans.GetFailureHandlingOptions();
                    failureOptions.SetFailuresPreprocessor(new MyFailuresPreprocessor());
                    failureOptions.SetClearAfterRollback(true); // Опционально

                    for (int i = 0; i < SelectedFamilies.Count && i < SelectedMenuItems.Count; i++)
                    {
                        var selectedFamily = SelectedFamilies[i];
                        var menuItem = SelectedMenuItems[i];

                        if (Path.GetExtension(menuItem.Path).ToLower() == ".rvt")
                        {
                            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(menuItem.Path);
                            OpenOptions openOptions = new OpenOptions();
                            using (Document tempDoc = doc.Application.OpenDocumentFile(modelPath, openOptions))
                            {
                                ElementId sourceFamily = new FilteredElementCollector(tempDoc)
                            .OfCategory(selectedFamily.RevitCategory)
                            .WhereElementIsElementType()
                            .Where(x => x.Name == menuItem.Name)
                            .Select(x => x.Id).First();
                                if (sourceFamily != null)
                                {
                                    CopyPasteOptions options = new CopyPasteOptions();
                                    options.SetDuplicateTypeNamesHandler(new MyCopyHandler());
                                    ICollection<ElementId> copiedElements = ElementTransformUtils.CopyElements(
                                        tempDoc,
                                        new List<ElementId> { sourceFamily },
                                        doc,
                                        Transform.Identity,
                                        options
                                    );

                                    tempDoc.Close(false);

                                    ElementId copiedTypeId = copiedElements.First();
                                    ElementType copiedType = doc.GetElement(copiedTypeId) as ElementType;

                                    // Ищем существующий тип с таким же именем в целевом документе
                                    ElementType existingType = FindTypeByNameAndClass(doc, selectedFamily.Name, copiedType.GetType());

                                    if (existingType != null && existingType.Id != copiedType.Id)
                                    {
                                        // Заменяем все элементы, использующие старый тип, на новый тип
                                        ReplaceElementsType(doc, existingType.Id, copiedType.Id);

                                    }
                                }
                            }
                        }
                        else if (Path.GetExtension(menuItem.Path).ToLower() == ".rfa")
                        {
                            Family family;
                            if (doc.LoadFamily(menuItem.Path, out family))
                            {
                                foreach (var instance in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>())
                                {
                                    if (instance.Symbol.Name == selectedFamily.Name)
                                    {
                                        var familySymbol = family.GetFamilySymbolIds().Select(id => doc.GetElement(id) as FamilySymbol).FirstOrDefault();
                                        if (familySymbol != null && !familySymbol.IsActive)
                                        {
                                            familySymbol.Activate();
                                            doc.Regenerate();
                                        }

                                        instance.Symbol = familySymbol;
                                    }
                                }
                            }
                        }
                    }

                    trans.Commit();
                }
            }
            this.Close();
        }

        // Метод для поиска типа по имени и классу в целевом документе
        private ElementType FindTypeByNameAndClass(Document doc, string typeName, Type typeClass)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeClass)
                .Cast<ElementType>()
                .FirstOrDefault(e => e.Name.Equals(typeName, StringComparison.InvariantCultureIgnoreCase));
        }

        // Метод для замены типа у всех элементов
        private void ReplaceElementsType(Document doc, ElementId oldTypeId, ElementId newTypeId)
        {
            // Находим все элементы, использующие старый тип
            List<Element> collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() == oldTypeId).ToList();

            foreach (Element elem in collector)
            {
                // Устанавливаем новый тип
                elem.ChangeTypeId(newTypeId);
            }
        }
    }

    public class MenuItem
    {
        public string Name { get; set; }
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
