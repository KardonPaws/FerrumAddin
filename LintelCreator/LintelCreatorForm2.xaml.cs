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
    /// Логика взаимодействия для LintelCreatorWPF.xaml
    /// </summary>
    public partial class LintelCreatorForm2 : Window
    {
        List<ParentElement> ElementList;
        public LintelCreatorForm2(Document doc, List<ParentElement> list)
        {
            InitializeComponent();
            ElementList = list;
            firstColumnt.ItemsSource = ElementList;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        // Прочие свойства и фильтры
        public bool IsBrick65Checked { get; set; }
        public bool IsBrick85Checked { get; set; }
        public bool IsPartitionChecked { get; set; }

        // Тип перемычки
        public bool IsLoadBearingChecked { get; set; }
        public bool IsNoSupportChecked { get; set; }
        public bool IsOneSideSupportChecked { get; set; }
        public bool IsTwoSidesSupportChecked { get; set; }

        // Опорные подушки
        public bool HasSupportPads { get; set; }

        // Материал перемычки
        public bool IsMetalChecked { get; set; }
        public bool IsReinforcedConcreteChecked { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public MainViewModel()
        {
            // Заполнение данных и логика фильтрации
        }
    }
    // Родительский элемент (например, окно или дверь)
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

    // Дочерний элемент (например, тип стены)
    public class ChildElement
    {
        public string WallType { get; set; } // Тип стены + толщина
    }


}
