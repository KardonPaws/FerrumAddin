using Autodesk.Internal.InfoCenter;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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
using static Autodesk.Revit.DB.SpecTypeId;
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
        private MenuItem lastClickedMenuItem;

        private void ElementClick(object sender, RoutedEventArgs e)
        {
            if ((Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift)) && lastClickedMenuItem != null)
            {
                var selectedTabItem = Tabs.SelectedItem as TabItemViewModel;
                if (selectedTabItem != null)
                {
                    // Get the clicked element.
                    var clickedButton = sender as Button;
                    var clickedMenuItem = clickedButton?.DataContext as MenuItem;
                    if (clickedMenuItem != null && lastClickedMenuItem != null && clickedMenuItem != lastClickedMenuItem)
                    {
                        // Find the indices of the last clicked and the current clicked MenuItem.
                        int lastClickedIndex = selectedTabItem.MenuItems.IndexOf(lastClickedMenuItem);
                        int clickedIndex = selectedTabItem.MenuItems.IndexOf(clickedMenuItem);

                        if (lastClickedIndex != -1 && clickedIndex != -1)
                        {
                            // Find the range between the last clicked and current clicked index.
                            int start = Math.Min(lastClickedIndex, clickedIndex);
                            int end = Math.Max(lastClickedIndex, clickedIndex);

                            // Select all MenuItems in the range.
                            for (int i = start; i <= end; i++)
                            {
                                selectedTabItem.MenuItems[i].IsSelected = lastClickedMenuItem.IsSelected;
                            }
                        }
                    }
                }
            }
            else
            {
                var frameworkElement = sender as FrameworkElement;
                var menuItem = frameworkElement?.DataContext as MenuItem;
                if (menuItem != null)
                {
                    menuItem.IsSelected = !menuItem.IsSelected;
                    UpdateIsSelectedStates();
                }
            }
            var button = sender as Button;
            lastClickedMenuItem = button?.DataContext as MenuItem;
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
        IList<Element> GetAllModelElements(Document doc)
        {
            Dictionary<string, Element> uniqueElements = new Dictionary<string, Element>();

            // Собираем все элементы модели
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (Element e in collector)
            {
                if (e.Category != null && e.Category.HasMaterialQuantities)
                {
                    string uniqueKey = null;

                    // Проверяем, является ли элемент экземпляром семейства
                    if (e is FamilyInstance familyInstance)
                    {
                        // Пользовательское семейство: уникальность по имени семейства
                        Family family = familyInstance.Symbol?.Family;
                        if (family != null)
                        {
                            uniqueKey = family.Name;
                        }
                    }
                    else
                    {
                        // Системное семейство: уникальность по имени типа
                        uniqueKey = e.Name;
                    }

                    // Если уникальный ключ определен и элемента с таким ключом еще нет, добавляем в список
                    if (!string.IsNullOrEmpty(uniqueKey) && !uniqueElements.ContainsKey(uniqueKey))
                    {
                        uniqueElements.Add(uniqueKey, e);
                    }
                }
            }

            return uniqueElements.Values.ToList();
        }

        private void CheckFamilyVersions(object sender, RoutedEventArgs e)
        {
            App.AllowLoad = true;
            doc = App.uiapp.ActiveUIDocument.Document;
            ObservableCollection<MenuItem> outdatedItems = new ObservableCollection<MenuItem>();
            ObservableCollection<MenuItem> newerItems = new ObservableCollection<MenuItem>();

            List<Element> elements = (List<Element>)GetAllModelElements(doc);

            foreach (var element in elements)
            {
                if (element is FamilyInstance familyInstance)
                {
                    // Работа с обычными семействами
                    Family family = familyInstance.Symbol?.Family;
                    if (family == null) continue;

                    var MenuItems = mvm.TabItems.Where(x => x.Header == (Tabs.SelectedItem as TabItemViewModel).Header).Select(x => x.OriginalMenuItems).First();
                    var matchingMenuItem = MenuItems.Where(mi => mi.Name == family.Name).FirstOrDefault();

                    if (matchingMenuItem != null)
                    {
                        string projectVersion = "";
                        Document loadedFamily = App.uiapp.Application.OpenDocumentFile(matchingMenuItem.Path);
                        if (loadedFamily == null) continue;

                        try
                        {
                            projectVersion = GetFamilyVersionFromProject(family);
                        }
                        catch
                        {
                            string ver2 = GetFamilyVersionFromLoadedFamily(loadedFamily);
                            if (string.IsNullOrEmpty(ver2))
                            {
                                continue;
                            }
                            else
                            {
                                newerItems.Add(new MenuItem()
                                {
                                    Path = matchingMenuItem.Path,
                                    Category = matchingMenuItem.Category,
                                    Name = matchingMenuItem.Name,
                                    ImagePath = matchingMenuItem.ImagePath,
                                    IsVisible = true,
                                    IsSelected = false
                                });
                            }
                        }

                        string loadedFamilyVersion = GetFamilyVersionFromLoadedFamily(loadedFamily);
                        loadedFamily.Close(false);
                        CompareVersions(projectVersion, loadedFamilyVersion, matchingMenuItem, outdatedItems, newerItems);
                    }
                }
                else
                {
                    // Работа с системными семействами
                    var nameAndCat = new Dictionary<string, BuiltInCategory>
            {
                { "Стены", BuiltInCategory.OST_Walls },
                { "Перекрытия", BuiltInCategory.OST_Floors },
                { "Потолки", BuiltInCategory.OST_Ceilings },
                { "Витражи", BuiltInCategory.OST_Walls },
                { "Крыши", BuiltInCategory.OST_Roofs },
                { "Ограждения", BuiltInCategory.OST_StairsRailing },
                { "Пандусы", BuiltInCategory.OST_Ramps }
            };

                    foreach (var pair in nameAndCat)
                    {
                        if (element.Category.Id.Value == (int)pair.Value)
                        {
                            string projectVersion = doc.GetElement(element.GetTypeId()).LookupParameter("ZH_Версия_Семейства")?.AsString();



                            var MenuItems = mvm.TabItems.Where(x => x.Header == (Tabs.SelectedItem as TabItemViewModel).Header).Select(x => x.OriginalMenuItems).First();
                            var matchingMenuItem = MenuItems.Where(mi => mi.Name == element.Name).FirstOrDefault();

                            if (matchingMenuItem != null)
                            {
                                Document loadedFamily = App.uiapp.Application.OpenDocumentFile(matchingMenuItem.Path);
                                if (loadedFamily == null) continue;

                                var loadedElement = new FilteredElementCollector(loadedFamily)
                                    .OfCategory(pair.Value)
                                    .WhereElementIsElementType()
                                    .FirstOrDefault(el => el.Name == element.Name);

                                if (loadedElement != null)
                                {
                                    string loadedFamilyVersion = loadedElement.LookupParameter("ZH_Версия_Семейства")?.AsString();
                                    loadedFamily.Close(false);
                                    if (string.IsNullOrEmpty(projectVersion) && !string.IsNullOrEmpty(loadedFamilyVersion))
                                    {
                                        newerItems.Add(new MenuItem()
                                        {
                                            Path = matchingMenuItem.Path,
                                            Category = matchingMenuItem.Category,
                                            Name = matchingMenuItem.Name,
                                            ImagePath = matchingMenuItem.ImagePath,
                                            IsVisible = true,
                                            IsSelected = false
                                        });
                                        continue;
                                    }   
                                    if (!string.IsNullOrEmpty(loadedFamilyVersion))
                                    {
                                        CompareVersions(projectVersion, loadedFamilyVersion, matchingMenuItem, outdatedItems, newerItems);
                                    }
                                }
                                else
                                {
                                    loadedFamily.Close(false);
                                }
                            }
                        }
                    }
                }
            }
            if (newerItems.Count > 0 || outdatedItems.Count > 0)
            {
                AddOutdatedTab(outdatedItems, newerItems);
            }
            App.AllowLoad = false;
        }

        private void CompareVersions(
    string projectVersion,
    string loadedFamilyVersion,
    MenuItem matchingMenuItem,
    ObservableCollection<MenuItem> outdatedItems,
    ObservableCollection<MenuItem> newerItems)
        {
            try
            {
                projectVersion = Regex.Replace(projectVersion, @"[^\d\.]", "");
                loadedFamilyVersion = Regex.Replace(loadedFamilyVersion, @"[^\d\.]", "");
                // Разделение версии на основные и дополнительные номера
                var projectParts = projectVersion.Split('.');
                var loadedParts = loadedFamilyVersion.Split('.');


                        int projectMajor = int.Parse(projectParts[0]);

                        int projectMinor = int.Parse(projectParts[1]);

                        int loadedMajor = int.Parse(loadedParts[0]);

                        int loadedMinor = int.Parse(loadedParts[1]);


                // Сравнение версии
                if (projectMajor > loadedMajor || (projectMajor == loadedMajor && projectMinor > loadedMinor))
                {
                    // Проектная версия новее
                    outdatedItems.Add(new MenuItem
                    {
                        Path = matchingMenuItem.Path,
                        Category = matchingMenuItem.Category,
                        Name = matchingMenuItem.Name,
                        ImagePath = matchingMenuItem.ImagePath,
                        IsVisible = true,
                        IsSelected = false
                    });
                }
                else if (projectMajor < loadedMajor || (projectMajor == loadedMajor && projectMinor < loadedMinor))
                {
                    // Загруженная версия новее
                    newerItems.Add(new MenuItem
                    {
                        Path = matchingMenuItem.Path,
                        Category = matchingMenuItem.Category,
                        Name = matchingMenuItem.Name,
                        ImagePath = matchingMenuItem.ImagePath,
                        IsVisible = true,
                        IsSelected = false
                    });
                }
            }
            catch (Exception ex)
            {
                // Логирование или обработка ошибок
                Debug.WriteLine($"Ошибка при сравнении версий: {ex.Message}");
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
                else
                {

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

        private void AddOutdatedTab(ObservableCollection<MenuItem> outdatedItems, ObservableCollection<MenuItem> newerItems)
        {
            if (outdatedItems.Count > 0)
            {
                var outdatedTab = new TabItemViewModel
                {
                    Header = "Устаревшее",
                    MenuItems = outdatedItems,
                    OriginalMenuItems = outdatedItems.ToList()
                };
                if (FamilyManagerWindow.mvm.TabItems.Any(x=>x.Header == "Устаревшее"))
                {
                    TabItemViewModel viewModel = FamilyManagerWindow.mvm.TabItems.Where(x => x.Header == "Устаревшее").FirstOrDefault();
                    FamilyManagerWindow.mvm.TabItems.Remove(viewModel);
                }
                FamilyManagerWindow.mvm.TabItems.Insert(0, outdatedTab);

            }
            if (newerItems.Count > 0)
            {
                var newerTab = new TabItemViewModel
                {
                    Header = "Новее",
                    MenuItems = newerItems,
                    OriginalMenuItems = newerItems.ToList()
                };
                if (FamilyManagerWindow.mvm.TabItems.Any(x => x.Header == "Новее"))
                {
                    TabItemViewModel viewModel = FamilyManagerWindow.mvm.TabItems.Where(x => x.Header == "Новее").FirstOrDefault();
                    FamilyManagerWindow.mvm.TabItems.Remove(viewModel);
                }
                FamilyManagerWindow.mvm.TabItems.Insert(0, newerTab);

            }


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
            {
                TaskDialog.Show("Ошибка", "Не найден файл менеджера семейств");
                return; 
            }

            string tabPath = App.TabPath;
            XElement root = XElement.Load(tabPath);

            // Получение pathFam из первого элемента MenuItem.Path на две папки выше
            string firstMenuItemPath = root.Descendants("MenuItem").FirstOrDefault()?.Element("Path")?.Value;
            if (firstMenuItemPath == null)
            {
                throw new InvalidOperationException("Не удалось найти первый элемент MenuItem.Path");
            }
            string pathFam = System.IO.Path.GetFullPath(System.IO.Path.Combine(firstMenuItemPath, "..", "..", ".."));

            foreach (var dir in System.IO.Directory.GetDirectories(pathFam))
            {
                string tabHeader = System.IO.Path.GetFileName(dir);
                XElement existingTabElement = root.Elements("TabItem").FirstOrDefault(tab => tab.Element("Header")?.Value == tabHeader);

                XElement tabElement = existingTabElement ?? new XElement("TabItem",
                    new XElement("Header", tabHeader),
                    new XElement("Visibility", true));

                foreach (var categoryDir in System.IO.Directory.GetDirectories(dir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(categoryDir, "*.rfa"))
                    {
                        string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(file);
                        string imagePath = System.IO.Path.Combine(categoryDir, fileNameWithoutExtension + ".png");

                        if (tabElement.Descendants("MenuItem").Any(menu => menu.Element("Path")?.Value == file))
                        {
                            continue; // Уже существует
                        }

                        XElement menuItemElement = new XElement("MenuItem",
                            new XElement("Name", fileNameWithoutExtension),
                            new XElement("Category", System.IO.Path.GetFileName(categoryDir)),
                            new XElement("Path", file),
                            new XElement("ImagePath", imagePath));
                        tabElement.Add(menuItemElement);
                    }
                    var menuItemsToRemove = tabElement.Descendants("MenuItem").Where(x=>x.Element("Path").Value.Contains(".rfa"))
                .Where(menu => !File.Exists(menu.Element("Path")?.Value))
                .ToList();

                    foreach (var menuItem in menuItemsToRemove)
                    {
                        menuItem.Remove();
                    }
                }
                var sortedMenuItems = tabElement.Elements("MenuItem")
                .OrderBy(menu => menu.Element("Category")?.Value)
                .ThenBy(menu => menu.Element("Name")?.Value)
                .ToList();
                string tabName = tabElement.Element("Header").Value;
                string visibility = tabElement.Element("Visibility").Value;
                tabElement.ReplaceNodes(new XElement[]
                          {
                            new XElement("Header", tabName),
                            new XElement("Visibility", visibility)
                          }.Concat(sortedMenuItems));
                if (existingTabElement == null)
                {
                    root.Add(tabElement);
                }
            }

            root.Save(tabPath);


            var xdoc = XDocument.Load(filePath);

            foreach (var tabItemElement in root.Descendants("TabItem"))
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
