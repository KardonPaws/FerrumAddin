using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
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

namespace FerrumAddin.FBS
{
    /// <summary>
    /// Логика взаимодействия для LayoutWindow.xaml
    /// </summary>
    public partial class LayoutWindow : Window
    {
        private UIApplication _uiApp;
        // External event and handler fields
        public ExternalEvent SelectWallsEvent { get; set; }
        public SelectWallsHandler SelectHandler { get; set; }
        public ExternalEvent PlaceLayoutEvent { get; set; }
        public PlaceLayoutHandler PlaceHandler { get; set; }
        public ExternalEvent ShowIssuesEvent { get; set; }
        public ShowIssuesHandler ShowIssuesHandler { get; set; }
        // Data
        private List<WallInfo> _selectedWalls;
        private List<LayoutVariant> _variants;

        public LayoutWindow(UIApplication uiApp)
        {
            InitializeComponent();
            _uiApp = uiApp;
            // Initialize external events and handlers
            //SelectHandler = new SelectWallsHandler(this);
            //SelectWallsEvent = ExternalEvent.Create(SelectHandler);
            //PlaceHandler = new PlaceLayoutHandler(this);
            //PlaceLayoutEvent = ExternalEvent.Create(PlaceHandler);
            //ShowIssuesHandler = new ShowIssuesHandler();
            //ShowIssuesEvent = ExternalEvent.Create(ShowIssuesHandler);
            //// Initially disable generate and place until walls selected
            //GenerateButton.IsEnabled = false;
            //PlaceButton.IsEnabled = false;
            //ShowIssuesButton.IsEnabled = false;
        }

        public void OnWallsSelected(List<WallInfo> walls)
        {
            // Callback from SelectWallsHandler when walls are picked
            _selectedWalls = walls;
            if (_selectedWalls == null || _selectedWalls.Count == 0)
            {
                SelectedWallsLabel.Text = "No walls selected";
                GenerateButton.IsEnabled = false;
            }
            else
            {
                SelectedWallsLabel.Text = $"Selected {_selectedWalls.Count} walls";
                GenerateButton.IsEnabled = true;
            }
        }

        private void SelectWalls_Click(object sender, RoutedEventArgs e)
        {
            // Trigger external event to select walls in Revit
            SelectWallsEvent.Raise();
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedWalls == null || _selectedWalls.Count == 0) return;
            // Parse user inputs for counts
            int variantCount, bestCount;
            if (!int.TryParse(VariantsCountBox.Text, out variantCount)) variantCount = 10;
            if (!int.TryParse(BestCountBox.Text, out bestCount)) bestCount = 3;
            // Generate variants using the LayoutGenerator
            _variants = LayoutGenerator.GenerateVariants(_selectedWalls, variantCount, bestCount);
            // Bind results to DataGrid
            VariantsGrid.ItemsSource = new ObservableCollection<LayoutVariant>(_variants);
            // Enable placement after generation
            if (_variants.Count > 0)
            {
                PlaceButton.IsEnabled = true;
            }
        }

        private void PlaceSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_variants == null) return;
            if (VariantsGrid.SelectedItem is LayoutVariant variant)
            {
                // Set the variant to place and trigger placement external event
                PlaceHandler.VariantToPlace = variant;
                PlaceLayoutEvent.Raise();
                ShowIssuesButton.IsEnabled = true;
            }
        }

        private void ShowIssues_Click(object sender, RoutedEventArgs e)
        {
            if (VariantsGrid.SelectedItem is LayoutVariant variant)
            {
                if (!variant.IsPlaced)
                {
                    MessageBox.Show("Please place this variant in the model before highlighting issues.", "Notice");
                    return;
                }
                // Trigger external event to highlight issues
                ShowIssuesHandler.VariantToShow = variant;
                ShowIssuesEvent.Raise();
            }
        }
    }
}