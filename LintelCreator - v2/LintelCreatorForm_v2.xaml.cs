using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.ExceptionServices;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace FerrumAddinDev.LintelCreator_v2
{
    /// <summary>
    /// Логика взаимодействия для LintelCreatorForm2.xaml
    /// </summary>
    public partial class LintelCreatorForm_v2 : Window
    {
        List<ParentElement> ElementList;
        public static MainViewModel MainViewModel;
        // 19.01.26 - ошибки в перемычках
        private List<ElementId> _errorLintelIds = new List<ElementId>();
        private int _currentErrorIndex = -1;
        public Document doc_;

        public LintelCreatorForm_v2(Document doc, Selection sel, List<ParentElement> openingsWithoutLintel, List<ParentElement> openingsWithLintel, List<Family> families)
        {
            InitializeComponent();

            var familyWrappers = families.Select(family => new FamilyWrapper(family, doc)).ToList();
            familyWrappers.OrderBy(x => x.FamilyName);
            // 19.01.26 - ошибки в перемычках
            doc_ = doc;

            // Устанавливаем DataContext для привязки данных
            DataContext = new MainViewModel
            {
                FilteredFamilies = new ObservableCollection<FamilyWrapper>(familyWrappers),
                openingsWithoutLintel = new ObservableCollection<ParentElement>(openingsWithoutLintel.Where(x=>x.Walls.Count()>0)),
                openingsWithLintel = new ObservableCollection<ParentElement>(openingsWithLintel.Where(x => x.Walls.Count() > 0))

            };
            MainViewModel = DataContext as MainViewModel;
            selection = sel;
            autoMode = false;
            recreate = false;

            UpdateErrorsUi();

        }
        public static Selection selection;

        // Обработка прокрутки ListBox
        //private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        //{
        //    if (sender is ListBox listBox)
        //    {
        //        // Передаем прокрутку родительскому контейнеру
        //        var eventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        //        {
        //            RoutedEvent = UIElement.MouseWheelEvent,
        //            Source = sender
        //        };
        //        listBox.RaiseEvent(eventArgs);
        //        e.Handled = true;
        //    }
        //}


        // Обработка изменения выбранного элемента TreeView
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is ParentElement selectedElement)
            {
                var viewModel = DataContext as MainViewModel;
                if (viewModel != null)
                {
                    viewModel.SelectedParentElement = selectedElement;
                }
            }
        }

        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.FilterFamiliesAndTypes();
            }
        }

        private void RadioButton_Checked_1(object sender, RoutedEventArgs e)
        {
            //28.07.25 - объединение проемов в перемычках
            if (sender is RadioButton radioButton && radioButton.DataContext is KeyValuePair<WallType, List<ElementsForLintel>> childElement)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    if (radioButton != null)
                    {
                        // Находим родительский элемент HierarchicalDataTemplate
                        DependencyObject parent = VisualTreeHelper.GetParent(radioButton);
                        again:
                        while (parent != null && !(parent is TreeViewItem))
                        {
                            parent = VisualTreeHelper.GetParent(parent);
                        }
                        if (((parent as TreeViewItem).DataContext as ParentElement) == null)
                        {
                            parent = VisualTreeHelper.GetParent(parent);
                            goto again;
                        }

                        if (parent is TreeViewItem treeViewItem)
                        {
                            // Получаем DataContext родительского элемента
                            var parentElement = treeViewItem.DataContext as ParentElement;

                            if (parentElement != null)
                            {
                                viewModel.SelectedParentElement = parentElement;
                                viewModel.SelectedWallTypeName = (sender as RadioButton).Content.ToString();
                                viewModel.FilterFamiliesAndTypes();
                                //28.07.25 - объединение проемов в перемычках
                                var list = parentElement.Walls[childElement.Key];
                                List<ElementId> elements = new List<ElementId>();
                                foreach (var element in list) 
                                {
                                    elements.AddRange(element.ElementsId);
                                }
                                selection.SetElementIds(elements);
                            }
                        }
                    }
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var vm = MainViewModel;
            // Ручной режим: создаём только для выбранного проёма
            if (!autoMode)
            {
                if (vm.SelectedWallType != null)
                {
                    if (vm.SelectedType == null)
                    {
                        vm.SelectedType = vm.SelectedFamily?.Types.FirstOrDefault();
                    }
                    CommandLintelCreator_v2.PendingRequests.Enqueue(new LintelRequest
                    {
                        ParentElement = vm.SelectedParentElement,
                        WallType = vm.SelectedWallType,
                        LintelType = vm.SelectedType
                    });
                    if (CommandLintelCreator_v2.PendingRequests.Any())
                        CommandLintelCreator_v2.lintelCreateEvent.Raise();
                }
                else
                {
                    MessageBox.Show( "Выберите проем и тип стены для простановки перемычек", "Ошибка");
                }
                return;
            }

            // Автоматический режим: проходим по всем ParentElement
            List<ParentElement> listToWork = vm.openingsWithoutLintel.ToList();
            if (recreate)
            {
                listToWork.AddRange(vm.openingsWithLintel);
            }    
            foreach (var parent in listToWork)
            {
                // Выбираем текущий элемент
                vm.SelectedFamily = vm.SelectedFamily == null? vm.FilteredFamilies.FirstOrDefault() : vm.SelectedFamily;
                vm.SelectedParentElement = parent;
                // Берём первую стену из словаря Walls
                foreach (var wallType in parent.Walls.Keys)
                {
                    vm.SelectedWallTypeName = wallType.Name;
                    // Обновляем список типов семейств под этот проём
                    vm.FilterFamiliesAndTypes();
                    // Выбираем первое семейство и первый тип перемычки
                    vm.SelectedType = vm.SelectedFamily?.Types.FirstOrDefault();
                    if (vm.SelectedType == null) continue;
                    CommandLintelCreator_v2.PendingRequests.Enqueue(new LintelRequest
                    {
                        ParentElement = parent,
                        WallType = wallType,
                        LintelType = vm.SelectedFamily?.Types.FirstOrDefault()
                    });
                }

            }
            // Запускаем ExternalEvent для создания перемычки
            if (CommandLintelCreator_v2.PendingRequests.Any())
                CommandLintelCreator_v2.lintelCreateEvent.Raise();
        }

        // 16.12.25 - у некоторых семейств нет точки вставки, определение по BoundingBox
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator_v2.lintelNumerateEvent.Raise();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator_v2.nestedElementsNumberingEvent.Raise();
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator_v2.createSectionsEvent.Raise();
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator_v2.tagLintelsEvent.Raise();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (check)
            {
                check = false;
            }
            else
            {
                check = true;
            }
        }

        public static bool check = false;

        private void CheckBox_Checked_1(object sender, RoutedEventArgs e)
        {
            if (recreate)
            {
                recreate = false;
            }
            else
            {
                recreate = true;
            }
        }
        public static bool recreate = false;
        private void CheckBox_Checked_2(object sender, RoutedEventArgs e)
        {
            if (autoMode)
            {
                autoMode = false;
            }
            else
            {
                autoMode = true;
            }
        }
        public static bool autoMode = false;
        // 16.12.25 - у некоторых семейств нет точки вставки, определение по BoundingBox

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator_v2.placeSectionsEvent.Raise();
        }

        private void CheckBox_Checked_3(object sender, RoutedEventArgs e)
        {
            MainViewModel.FilterFamiliesAndTypes();
        }

        private void Button_Click_6(object sender, RoutedEventArgs e)
        {
            List<List<ParentElement>> result = CommandLintelCreator_v2.RefreshWindow();

            // 2. разбираем результат
            var withoutLintel = result[0];
            var withLintel = result[1];



            var vm = MainViewModel;
            vm.openingsWithoutLintel.Clear();
            foreach (var e0 in withoutLintel)
                vm.openingsWithoutLintel.Add(e0);

            vm.openingsWithLintel.Clear();
            foreach (var e1 in withLintel)
                vm.openingsWithLintel.Add(e1);

            // 4. уведомляем интерфейс
            vm.OnPropertyChanged(nameof(vm.openingsWithoutLintel));
            vm.OnPropertyChanged(nameof(vm.openingsWithLintel));
        }
        // 19.01.26 - ошибки в перемычках
        private void Button_FindErrors_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _errorLintelIds = FindErrorLintels(doc_);

                // По требованию: при первом поиске в Revit выбираем ВСЕ ошибочные перемычки
                if (_errorLintelIds.Count > 0)
                {
                    selection.SetElementIds(_errorLintelIds);
                    _currentErrorIndex = 0;
                }
                else
                {
                    _currentErrorIndex = -1;
                }

                UpdateErrorsUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка");
            }
        }

        private void Button_PrevError_Click(object sender, RoutedEventArgs e)
        {
            if (_errorLintelIds == null || _errorLintelIds.Count == 0) return;
            if (_currentErrorIndex <= 0) return;

            _currentErrorIndex--;
            SelectCurrentErrorSingle();
            UpdateErrorsUi();
        }

        private void Button_NextError_Click(object sender, RoutedEventArgs e)
        {
            if (_errorLintelIds == null || _errorLintelIds.Count == 0) return;
            if (_currentErrorIndex >= _errorLintelIds.Count - 1) return;

            _currentErrorIndex++;
            SelectCurrentErrorSingle();
            UpdateErrorsUi();
        }

        private void SelectCurrentErrorSingle()
        {
            if (_currentErrorIndex < 0 || _currentErrorIndex >= _errorLintelIds.Count) return;
            selection.SetElementIds(new List<ElementId> { _errorLintelIds[_currentErrorIndex] });
        }

        private void UpdateErrorsUi()
        {
            int count = _errorLintelIds?.Count ?? 0;
            if (TxtErrorsCount != null) TxtErrorsCount.Text = count.ToString();

            if (TxtErrorsProgress != null)
            {
                TxtErrorsProgress.Text = (_currentErrorIndex >= 0 && count > 0)
                    ? $"{_currentErrorIndex + 1} / {count}"
                    : "-";
            }

            bool has = count > 0;
            if (BtnPrevError != null) BtnPrevError.IsEnabled = has && _currentErrorIndex > 0;
            if (BtnNextError != null) BtnNextError.IsEnabled = has && _currentErrorIndex >= 0 && _currentErrorIndex < count - 1;
        }

        private static List<ElementId> FindErrorLintels(Document doc)
        {
            // Ошибочные перемычки ищем по имени типа: "Ошибка" (возможен невидимый символ в начале)
            var found = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Where(fi => fi?.Symbol != null)
                .Where(fi => fi.Symbol.FamilyName != null && fi.Symbol.FamilyName.Contains("_Перемычки"))
                .Where(fi => IsErrorTypeName(fi.Symbol.Name))
                .Select(fi => fi.Id)
                .ToList();

            return found;
        }

        /// <summary>
        /// Проверка имени типа на "Ошибка". Допускается невидимый символ в начале/внутри.
        /// </summary>
        public static bool IsErrorTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return false;
            var cleaned = new string(typeName
                .Where(c => !char.IsControl(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)
                .ToArray())
                .Trim();

            return string.Equals(cleaned, "Ошибка", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<FamilyWrapper> _filteredFamilies;
        private FamilyWrapper _selectedFamily;
        private FamilySymbol _selectedType;
        private ParentElement _selectedParentElement;
        private string _selectedWallTypeName;

        // Список элементов из TreeView
        public ObservableCollection<ParentElement> openingsWithoutLintel { get; set; }
        public ObservableCollection<ParentElement> openingsWithLintel { get; set; }

        // Список отфильтрованных семейств
        public ObservableCollection<FamilyWrapper> FilteredFamilies
        {
            get => _filteredFamilies;
            set
            {
                _filteredFamilies = value;
                OnPropertyChanged(nameof(FilteredFamilies));
            }
        }

        // Выбранное семейство
        public FamilyWrapper SelectedFamily
        {
            get => _selectedFamily;
            set
            {
                _selectedFamily = value;
                OnPropertyChanged(nameof(SelectedFamily));
                OnPropertyChanged(nameof(SelectedFamilyTypes)); // Обновление типов
            }
        }

        // Типы, доступные для выбранного семейства
        public ObservableCollection<FamilySymbol> SelectedFamilyTypes
        {
            get => _selectedFamily?.Types ?? new ObservableCollection<FamilySymbol>();
        }

        // Выбранный тип
        public FamilySymbol SelectedType
        {
            get => _selectedType;
            set
            {
                _selectedType = value;
                OnPropertyChanged(nameof(SelectedType));
            }
        }

        // Выбранный элемент из TreeView
        public ParentElement SelectedParentElement
        {
            get => _selectedParentElement;
            set
            {
                _selectedParentElement = value;
                OnPropertyChanged(nameof(SelectedParentElement));
                OnPropertyChanged(nameof(SelectedWallType));
                OnPropertyChanged(nameof(SelectedOpeningWidth));
                FilterFamiliesAndTypes(); // Перезапуск фильтрации при изменении выбранного ParentElement
            }
        }

        // Тип стены
        public WallType SelectedWallType
        {
            get
            {
                if (SelectedParentElement?.Walls != null && SelectedParentElement.Walls.Any())
                {
                    return SelectedParentElement.Walls.Keys.Where(x => x.Name == SelectedWallTypeName).FirstOrDefault();
                }
                return null;
            }
        }

        public string SelectedWallTypeName { 
            get => _selectedWallTypeName;

            set
            {
                _selectedWallTypeName = value;
            }
        }

        // Ширина проема
        public double SelectedOpeningWidth
        {
            get
            {
                if (double.TryParse(SelectedParentElement?.Width, out double width))
                {
                    return width;
                }
                return 0;
            }
        }

        // Прочие свойства и фильтры
        public bool IsBrick65Checked { get; set; }
        public bool IsBrick85Checked { get; set; }
        public bool IsPartitionChecked { get; set; }

        public bool HasSupportPads { get; set; }

        public bool IsMetalChecked { get; set; }
        public bool IsReinforcedConcreteChecked { get; set; }
        public bool AllSupports { get; set; }

        // 19.01.26 - ошибки в перемычках
        public static bool IsErrorTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return false;
            var cleaned = new string(typeName
                .Where(c => !char.IsControl(c) && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format)
                .ToArray())
                .Trim();

            return string.Equals(cleaned, "Ошибка", StringComparison.OrdinalIgnoreCase);
        }

        public void FilterFamiliesAndTypes()
        {
            if (SelectedFamily == null)
                return;
            if (FilteredFamilies == null || FilteredFamilies.Count == 0)
                return;

            var family = SelectedFamily;
            family.RestoreOriginalTypes();
            // 03.02.26 - изменен фильтр для перемычек + создание базовых разрезов и шаблонов

            int GetSupportCategory(string[] nameParts)
            {
                if (nameParts == null || nameParts.Length < 4) return 0;
                string s1 = nameParts[3] ?? string.Empty;
                bool left = s1.Any(char.IsUpper);

                if (nameParts.Length == 4)
                    return left ? 1 : 0;

                string s2 = nameParts[nameParts.Length - 1] ?? string.Empty;
                bool right = s2.Any(char.IsUpper);

                if (left && right) return 2;
                if (left || right) return 1;
                return 0;
            }

            var candidates = family.OriginalTypes
                .Select(type =>
                {
                    var parts = type.Name.Split('_');
                    return new
                    {
                        Type = type,
                        Parts = parts,
                        SupportCategory = GetSupportCategory(parts)
                    };
                })
                .ToList();

            var baseFiltered = candidates
                .Where(x =>
                {
                    var type = x.Type;

                    if (IsErrorTypeName(type.Name))
                        return true;

                    var parts = x.Parts;
                    if (parts.Length < 4) return false;

                    // 1. Проверка высоты кирпича
                    if (IsBrick65Checked && parts[0] != "65") return false;
                    if (IsBrick85Checked && parts[0] != "88") return false;

                    // 2. Проверка толщины стены
                    try
                    {
                        var wallThickness = Math.Round((double)(SelectedWallType?.Width * 304.8));
                        if (wallThickness == 400)
                            wallThickness = 380;
                        if (wallThickness == 500)
                            wallThickness = 510;
                        if (wallThickness == 600)
                            wallThickness = 640;
                        if (Convert.ToDouble(parts[1]) != wallThickness) return false;
                    }
                    catch
                    {
                        return true;
                    }

                    // 3. Размер пролёта
                    if (double.TryParse(parts[2], out double spanWidth))
                    {
                        if (spanWidth < SelectedOpeningWidth) return false;
                    }

                    // 4. Опирание
                    // Здесь НЕ фильтруем жёстко, потому что нужен приоритетный подбор с fallback:
                    // - Проём без опирания (0): сначала 0, если нет — 1, если нет — 2
                    // - Проём с 1 опиранием (1): сначала 1, если нет — 2
                    // - Проём с 2 опираниями (2): только 2
                    // Фильтрацию по этим правилам применяем после базовых условий.

                    // 5. Наличие опорных подушек
                    if (HasSupportPads)
                    {
                        if (type.LookupParameter("ОП-1-Л.Видимость")?.AsInteger() != 1 &&
                            type.LookupParameter("ОП-1-П.Видимость")?.AsInteger() != 1)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (type.LookupParameter("ОП-1-Л.Видимость")?.AsInteger() == 1 &&
                            type.LookupParameter("ОП-1-П.Видимость")?.AsInteger() == 1)
                        {
                            return false;
                        }
                    }

                    // 6. Материал перемычки
                    // 29.06.25 - исправлен фильтр
                    if (IsMetalChecked && (!type.Name.Contains("у") && !type.Name.Contains("У") && !type.Name.Contains("А") && !type.Name.Contains("Шв"))) return false;
                    if (IsReinforcedConcreteChecked && (type.Name.Contains("у") || type.Name.Contains("У") || type.Name.Contains("А") || type.Name.Contains("Шв"))) return false;

                    return true;
                })
                .ToList();

            // Применяем правило опирания (если AllSupports=false). Иначе просто сортируем.
            List<FamilySymbol> filteredTypes;
            if (!AllSupports && SelectedParentElement != null)
            {
                int parentSupport = SelectedParentElement.SupportType;

                // Определяем доступные категории после базовой фильтрации
                bool has0 = baseFiltered.Any(t => t.SupportCategory == 0);
                bool has1 = baseFiltered.Any(t => t.SupportCategory == 1);
                bool has2 = baseFiltered.Any(t => t.SupportCategory == 2);

                int chosenCategory;
                if (parentSupport == 2)
                {
                    chosenCategory = 2;
                }
                else if (parentSupport == 1)
                {
                    // сначала 1 опирание, если нет — 2
                    chosenCategory = has1 ? 1 : (has2 ? 2 : 1);
                }
                else
                {
                    // parentSupport == 0: сначала 0, если нет — 1, если нет — 2
                    if (has0) chosenCategory = 0;
                    else if (has1) chosenCategory = 1;
                    else chosenCategory = 2;
                }

                filteredTypes = baseFiltered
                    .Where(t => t.SupportCategory == chosenCategory)
                    .Select(t => t.Type)
                    .ToList();
            }
            else
            {
                // AllSupports=true: показываем всё
                int parentSupport = SelectedParentElement?.SupportType ?? 0;
                // Для типа 0: 0->1->2, для 1: 1->2->0, для 2: 2->1->0
                int SupportSortKey(int cat)
                {
                    if (parentSupport == 2) return cat == 2 ? 0 : (cat == 1 ? 1 : 2);
                    if (parentSupport == 1) return cat == 1 ? 0 : (cat == 2 ? 1 : 2);
                    return cat; // 0,1,2
                }

                filteredTypes = baseFiltered
                    .OrderBy(t => SupportSortKey(t.SupportCategory))
                    .Select(t => t.Type)
                    .ToList();
            }

            // Обновляем доступные типы
            SelectedFamily.Types = new ObservableCollection<FamilySymbol>(filteredTypes);
            OnPropertyChanged(nameof(family.Types));



            OnPropertyChanged(nameof(FilteredFamilies));
            OnPropertyChanged(nameof(SelectedFamilyTypes));

        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FamilyWrapper : INotifyPropertyChanged
    {
        public string FamilyName { get; set; }

        private ObservableCollection<FamilySymbol> _types;
        public ObservableCollection<FamilySymbol> Types
        {
            get => _types;
            set
            {
                _types = value;
                OnPropertyChanged(nameof(Types));
            }
        }

        public ObservableCollection<FamilySymbol> OriginalTypes { get; set; }

        public FamilyWrapper(Family family, Document doc)
        {
            FamilyName = family.Name;
            var familySymbols = family.GetFamilySymbolIds()
                                      .Select(id => doc.GetElement(id) as FamilySymbol)
                                      .Where(symbol => symbol != null)
                                      .OrderBy(symbol => symbol.Name);

            Types = new ObservableCollection<FamilySymbol>(familySymbols);
            OriginalTypes = new ObservableCollection<FamilySymbol>(familySymbols);
        }

        public void RestoreOriginalTypes()
        {
            Types = new ObservableCollection<FamilySymbol>(OriginalTypes);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    //28.07.25 - объединение проемов в перемычках

    public class ElementsForLintel
    {
        public List<Element> Elements { get; set; }
        public List<ElementId> ElementsId { get; set; }
        public XYZ Location;
    }
    public class ParentElement
    {
        public string Name { get; set; } // Имя семейства
        public string TypeName { get; set; } // Имя типа
        public string Width { get; set; } // Ширина проема
        public ObservableCollection<ChildElement> ChildElements { get; set; } // Список дочерних элементов
        //28.07.25 - объединение проемов в перемычках
        public Dictionary<WallType, List<ElementsForLintel>> Walls { get; set; } // Словарь для определения в какой стене находятся отверстия
        public int SupportType { get; set; } // опирание, 0 - без опирания, 1 - опирание с 1 стороны, 2 - опирание с 2 сторон
        public Dictionary<Element, XYZ> SupportDirection { get; set; } // При опирании == 1 указывает направление пересекаемого перекрытия

        public ParentElement()
        {
            ChildElements = new ObservableCollection<ChildElement>();
        }
    }

    public class ChildElement
    {
        public string WallType { get; set; } // Тип стены + толщина
    }
}
