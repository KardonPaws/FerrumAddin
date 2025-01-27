using Autodesk.Revit.DB;
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

namespace FerrumAddin.LintelCreator
{
    /// <summary>
    /// Логика взаимодействия для LintelCreatorForm2.xaml
    /// </summary>
    public partial class LintelCreatorForm2 : Window
    {
        List<ParentElement> ElementList;
        public static MainViewModel MainViewModel;

        public LintelCreatorForm2(Document doc, List<ParentElement> list, List<Family> families)
        {
            InitializeComponent();

            var familyWrappers = families.Select(family => new FamilyWrapper(family, doc)).ToList();
            familyWrappers.OrderBy(x => x.FamilyName);

            // Устанавливаем DataContext для привязки данных
            DataContext = new MainViewModel
            {
                FilteredFamilies = new ObservableCollection<FamilyWrapper>(familyWrappers),
                ElementList = new ObservableCollection<ParentElement>(list.Where(x=>x.Walls.Count()>0))
            };
            MainViewModel = DataContext as MainViewModel;
        }

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
            if (sender is RadioButton radioButton && radioButton.DataContext is KeyValuePair<WallType, List<Element>> childElement)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    // Найти ParentElement, содержащий этот ChildElement
                    var parentElement = viewModel.ElementList.FirstOrDefault(pe =>
                        pe.Walls.Any(wall => wall.Key == childElement.Key && wall.Value.Contains(childElement.Value.FirstOrDefault())));

                    if (parentElement != null)
                    {
                        viewModel.SelectedParentElement = parentElement;
                        viewModel.SelectedWallTypeName = (sender as RadioButton).Content.ToString();
                        viewModel.FilterFamiliesAndTypes();
                    }
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator2.lintelCreateEvent.Raise();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator2.lintelNumerateEvent.Raise();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator2.nestedElementsNumberingEvent.Raise();
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator2.createSectionsEvent.Raise();
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator2.tagLintelsEvent.Raise();
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

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            CommandLintelCreator2.placeSectionsEvent.Raise();
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
        public ObservableCollection<ParentElement> ElementList { get; set; }

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
                    return SelectedParentElement.Walls.Keys.FirstOrDefault(); // Берем первую стену
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

        public bool IsNoSupportChecked { get; set; }
        public bool IsOneSideSupportChecked { get; set; }
        public bool IsTwoSidesSupportChecked { get; set; }

        public bool HasSupportPads { get; set; }

        public bool IsMetalChecked { get; set; }
        public bool IsReinforcedConcreteChecked { get; set; }
        

        public void FilterFamiliesAndTypes()
        {
            if (FilteredFamilies == null || FilteredFamilies.Count == 0)
                return;

            foreach (var family in FilteredFamilies)
            {
                family.RestoreOriginalTypes();

                var filteredTypes = family.OriginalTypes
                            .Where(type =>
                    {
                        var parts = type.Name.Split('_');
                        if (parts.Length < 4) return false;

                        // 1. Проверка высоты кирпича
                        if (IsBrick65Checked && parts[0] != "65") return false;
                        if (IsBrick85Checked && parts[0] != "88") return false;

                        // 2. Проверка толщины стены
                        var wallThickness = Math.Round((double)(SelectedWallType?.Width * 304.8));
                        if (Convert.ToDouble(parts[1]) != wallThickness) return false;

                        // 3. Размер пролёта
                        if (double.TryParse(parts[2], out double spanWidth))
                        {
                            if (spanWidth < SelectedOpeningWidth) return false;
                        }

                        // 4. Опирание
                        bool hasOneSupport = false;
                        bool hasTwoSupports = false;
                        var support = parts[3];
                        if (parts.Count() == 4)
                        {
                            hasOneSupport = support.Any(char.IsUpper);
                        }
                        else
                        {
                            hasOneSupport = (support.Count(char.IsUpper) > 0 && parts[parts.Count() - 1].Count(char.IsUpper) == 0) ||
                            (support.Count(char.IsUpper) == 0 && parts[parts.Count() - 1].Count(char.IsUpper) > 0);
                            hasTwoSupports = support.Any(char.IsUpper) && parts[parts.Count() - 1].Any(char.IsUpper);
                        }

                        if (IsNoSupportChecked && (hasOneSupport || hasTwoSupports)) return false;
                        if (IsOneSideSupportChecked && !hasOneSupport) return false;
                        if (IsTwoSidesSupportChecked && !hasTwoSupports) return false;

                        // 5. Наличие опорных подушек
                        if (HasSupportPads)
                        {
                            if (type.LookupParameter("ОП-1-Л.Видимость")?.AsInteger() != 1 &&
                                type.LookupParameter("ОП-1-П.Видимость")?.AsInteger() != 1)
                            {
                                return false;
                            }
                        }

                        // 6. Материал перемычки
                        if (IsMetalChecked && !(type.Name.Contains("У") || type.Name.Contains("А") || type.Name.Contains("Шв"))) return false;
                        if (IsReinforcedConcreteChecked && (type.Name.Contains("У") || type.Name.Contains("А") || type.Name.Contains("Шв"))) return false;

                        return true;
                    })
                    .ToList();

                // Обновляем доступные типы
                family.Types = new ObservableCollection<FamilySymbol>(filteredTypes);
                OnPropertyChanged(nameof(family.Types));

            }

            OnPropertyChanged(nameof(FilteredFamilies));
            OnPropertyChanged(nameof(SelectedFamilyTypes));

        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
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


    public class ParentElement
    {
        public string Name { get; set; } // Имя семейства
        public string TypeName { get; set; } // Имя типа
        public string Width { get; set; } // Ширина проема
        public ObservableCollection<ChildElement> ChildElements { get; set; } // Список дочерних элементов
        public Dictionary<WallType, List<Element>> Walls { get; set; } // Словарь для определения в какой стене находятся отверстия

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
