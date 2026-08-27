using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using RevitExternalEvent = Autodesk.Revit.UI.ExternalEvent;
using RevitExternalEventRequest = Autodesk.Revit.UI.ExternalEventRequest;

namespace FerrumAddinDev.LintelCreator_v3
{
    public partial class LintelCreatorForm_v3 : Window
    {
        private readonly OpeningSelectionHandlerV3 _selectionHandler;
        private readonly RevitExternalEvent _selectionEvent;
        private readonly OpeningReloadHandlerV3 _reloadHandler;
        private readonly RevitExternalEvent _reloadEvent;
        private readonly LintelPlacementHandlerV3 _placementHandler;
        private readonly RevitExternalEvent _placementEvent;
        private readonly LintelTypeReplacementHandlerV3 _typeReplacementHandler;
        private readonly RevitExternalEvent _typeReplacementEvent;
        private readonly RevitExternalEvent _lintelNumerateEvent;
        private readonly RevitExternalEvent _nestedElementsNumberingEvent;
        private readonly RevitExternalEvent _setLintelBaseTypeEvent;
        private readonly RevitExternalEvent _createSectionsEvent;
        private readonly RevitExternalEvent _tagLintelsEvent;
        private readonly RevitExternalEvent _placeSectionsEvent;
        private bool _isClosing;
        private double _smoothScrollTarget = double.NaN;
        private DateTime _smoothScrollUntilUtc = DateTime.MinValue;
        private double _existingLintelsSmoothScrollTarget = double.NaN;
        private DateTime _existingLintelsSmoothScrollUntilUtc = DateTime.MinValue;
        private double _variantsSmoothScrollTarget = double.NaN;
        private DateTime _variantsSmoothScrollUntilUtc = DateTime.MinValue;
        private readonly CancellationTokenSource _calculationCancellation = new CancellationTokenSource();
        private bool _initialCalculationStarted;
        private DateTime _lastProgressRenderUtc = DateTime.MinValue;
        private bool _reloadPending;

        public LintelCreatorForm_v3(
            LintelOpeningWorkspaceV3 workspace,
            OpeningSelectionHandlerV3 selectionHandler,
            RevitExternalEvent selectionEvent,
            OpeningReloadHandlerV3 reloadHandler,
            RevitExternalEvent reloadEvent,
            LintelPlacementHandlerV3 placementHandler,
            RevitExternalEvent placementEvent,
            LintelTypeReplacementHandlerV3 typeReplacementHandler,
            RevitExternalEvent typeReplacementEvent,
            RevitExternalEvent lintelNumerateEvent,
            RevitExternalEvent nestedElementsNumberingEvent,
            RevitExternalEvent setLintelBaseTypeEvent,
            RevitExternalEvent createSectionsEvent,
            RevitExternalEvent tagLintelsEvent,
            RevitExternalEvent placeSectionsEvent)
        {
            InitializeComponent();
            Workspace = workspace;
            _selectionHandler = selectionHandler;
            _selectionEvent = selectionEvent;
            _reloadHandler = reloadHandler;
            _reloadEvent = reloadEvent;
            _placementHandler = placementHandler;
            _placementEvent = placementEvent;
            _typeReplacementHandler = typeReplacementHandler;
            _typeReplacementEvent = typeReplacementEvent;
            _lintelNumerateEvent = lintelNumerateEvent;
            _nestedElementsNumberingEvent = nestedElementsNumberingEvent;
            _setLintelBaseTypeEvent = setLintelBaseTypeEvent;
            _createSectionsEvent = createSectionsEvent;
            _tagLintelsEvent = tagLintelsEvent;
            _placeSectionsEvent = placeSectionsEvent;
            DataContext = Workspace;
            Closed += Form_Closed;
        }

        public LintelOpeningWorkspaceV3 Workspace { get; }

        private void MainWorkspaceGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (MainWorkspaceGrid == null
                || OpeningsColumn == null
                || VariantsColumn == null
                || EditorColumn == null)
                return;

            double splitterWidth = (FirstSplitterColumn?.ActualWidth ?? 0)
                                   + (SecondSplitterColumn?.ActualWidth ?? 0);
            double maximumVariantsWidth = Math.Max(
                VariantsColumn.MinWidth,
                MainWorkspaceGrid.ActualWidth
                - OpeningsColumn.MinWidth
                - EditorColumn.MinWidth
                - splitterWidth);

            VariantsColumn.MaxWidth = maximumVariantsWidth;
            if (VariantsColumn.ActualWidth > maximumVariantsWidth + 0.5)
                VariantsColumn.Width = new GridLength(maximumVariantsWidth, GridUnitType.Pixel);
        }

        public async void StartInitialCalculation()
        {
            if (_initialCalculationStarted || _isClosing) return;
            _initialCalculationStarted = true;
            await CalculateAllVariantsAsync();
        }

        public void RefreshProgressDisplay(bool force = false)
        {
            if (_isClosing || !IsVisible) return;
            DateTime now = DateTime.UtcNow;
            if (!force && now - _lastProgressRenderUtc < TimeSpan.FromMilliseconds(33)) return;

            _lastProgressRenderUtc = now;
            UpdateLayout();
            Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_reloadPending || _isClosing || Workspace == null) return;
            if (!Workspace.BeginReload()) return;

            _reloadPending = true;
            _reloadHandler.Request(
                (processed, total) => RefreshProgressDisplay(),
                ReloadCompleted);
            if (_reloadEvent.Raise() != RevitExternalEventRequest.Accepted)
            {
                _reloadPending = false;
                Workspace.CancelReload("Revit не принял запрос на повторный сбор проёмов.");
            }
        }

        private async void ReloadCompleted(Exception error)
        {
            _reloadPending = false;
            if (_isClosing) return;
            if (error != null)
            {
                Workspace.CancelReload(error.Message);
                MessageBox.Show(error.Message, "Сбор проёмов", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RefreshProgressDisplay(true);
            await Workspace.RecalculateAllVariantsAsync(_calculationCancellation.Token);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Workspace == null) return;
            Workspace.SearchText = (sender as TextBox)?.Text ?? string.Empty;
        }

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Workspace == null || !(sender is ComboBox comboBox)) return;
            var item = comboBox.SelectedItem as ComboBoxItem;
            string status = item?.Tag as string;
            switch (status)
            {
                case "Success":
                    Workspace.StatusFilter = OpeningStatusV3.Success;
                    break;
                case "Warning":
                    Workspace.StatusFilter = OpeningStatusV3.Warning;
                    break;
                case "Error":
                    Workspace.StatusFilter = OpeningStatusV3.Error;
                    break;
                default:
                    Workspace.StatusFilter = null;
                    break;
            }
        }

        private void SortCriterion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Workspace?.RefreshView();
        }

        private void SortDirection_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is OpeningSortCriterionV3 criterion)
                criterion.IsDescending = !criterion.IsDescending;
        }

        private void AddSortCriterion_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.AddSortCriterion();
        }

        private void RemoveSortCriterion_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is OpeningSortCriterionV3 criterion)
                Workspace?.RemoveSortCriterion(criterion);
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            Workspace?.SetAllChecked(true);
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            Workspace?.SetAllChecked(false);
        }

        private void OpeningCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.NotifySelectionChanged();
        }

        private void OpeningGroups_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Workspace?.SelectedGroup == null || _isClosing) return;
            _selectionHandler.Request(Workspace.SelectedGroup.ElementIds);
            _selectionEvent.Raise();
        }

        private void OpeningTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, sender) || Workspace == null) return;
            Workspace.IsExistingLintelsTabActive = (sender as TabControl)?.SelectedIndex == 1;
        }

        private void ExistingLintelGroups_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isClosing || Workspace == null) return;
            DependencyObject source = e.OriginalSource as DependencyObject;
            var item = ItemsControl.ContainerFromElement(ExistingLintelGroupsListBox, source) as ListBoxItem;
            var group = item?.DataContext as OpeningGroupCardV3;
            if (group == null || ReferenceEquals(Workspace.SelectedGroup, group)) return;

            Workspace.SelectedGroup = group;
            _selectionHandler.Request(group.ElementIds);
            _selectionEvent.Raise();
            e.Handled = true;
        }

        private void Recalculate_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.RecalculateVariants();
        }

        private async void RecalculateAll_Click(object sender, RoutedEventArgs e)
        {
            await CalculateAllVariantsAsync();
        }

        private async Task CalculateAllVariantsAsync()
        {
            if (Workspace == null || _isClosing) return;
            try
            {
                await Workspace.RecalculateAllVariantsAsync(_calculationCancellation.Token);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Расчёт вариантов", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReverseEditorRows_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.ReverseEditorRows();
        }

        private void RestoreEditor_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.RestoreEditorDefault();
        }

        private void LoadExistingEditorType_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.LoadEditorFromExistingType();
        }

        private void SaveVariantChanges_Click(object sender, RoutedEventArgs e)
        {
            if (Workspace == null || _isClosing) return;
            if (!Workspace.IsExistingLintelsTabActive)
            {
                Workspace.SaveEditorChangesToActiveVariant();
                return;
            }

            LintelTypeReplacementRequestV3 request = Workspace.CreateTypeReplacementRequest();
            if (request == null)
            {
                Workspace.CancelLintelTypeReplacement(
                    "Выберите существующую перемычку и состав для замены.");
                return;
            }

            _typeReplacementHandler.Request(request);
            Workspace.BeginLintelTypeReplacement(request.LintelIds.Count);
            if (_typeReplacementEvent.Raise() != RevitExternalEventRequest.Accepted)
                Workspace.CancelLintelTypeReplacement("Revit не принял запрос на замену типа перемычек.");
        }

        private void CreateAndPlaceLintels_Click(object sender, RoutedEventArgs e)
        {
            if (Workspace == null || _isClosing) return;
            LintelPlacementRequestV3 request = Workspace.CreatePlacementRequest();
            if (request == null)
            {
                Workspace.CancelLintelPlacement(
                    "Не выбраны проёмы с рассчитанными вариантами для размещения.");
                return;
            }

            _placementHandler.Request(request);
            Workspace.BeginLintelPlacement(request.Groups.Count);
            if (_placementEvent.Raise() != RevitExternalEventRequest.Accepted)
                Workspace.CancelLintelPlacement("Revit не принял запрос на размещение перемычек.");
        }

        private void AddEditorRow_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.AddEditorRow();
        }

        private void MoveEditorRowUp_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is LintelEditorRowV3 row)
                Workspace?.MoveEditorRow(row, -1);
        }

        private void MoveEditorRowDown_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is LintelEditorRowV3 row)
                Workspace?.MoveEditorRow(row, 1);
        }

        private void RemoveEditorRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is LintelEditorRowV3 row)
                Workspace?.RemoveEditorRow(row);
        }

        private void OpeningGroups_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ReferenceEquals(sender, ExistingLintelGroupsListBox))
            {
                SmoothScrollListBox(
                    ExistingLintelGroupsListBox,
                    ref _existingLintelsSmoothScrollTarget,
                    ref _existingLintelsSmoothScrollUntilUtc,
                    e);
            }
            else
            {
                SmoothScrollListBox(
                    OpeningGroupsListBox,
                    ref _smoothScrollTarget,
                    ref _smoothScrollUntilUtc,
                    e);
            }
        }

        private void Variants_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            SmoothScrollListBox(
                VariantsListBox,
                ref _variantsSmoothScrollTarget,
                ref _variantsSmoothScrollUntilUtc,
                e);
        }

        private static void SmoothScrollListBox(
            ListBox listBox,
            ref double targetOffset,
            ref DateTime animationUntilUtc,
            MouseWheelEventArgs e)
        {
            ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(listBox);
            if (scrollViewer == null || scrollViewer.ScrollableHeight <= 0) return;

            DateTime now = DateTime.UtcNow;
            if (double.IsNaN(targetOffset)
                || now >= animationUntilUtc
                || targetOffset < 0
                || targetOffset > scrollViewer.ScrollableHeight
                || Math.Abs(targetOffset - scrollViewer.VerticalOffset) < 0.5)
            {
                targetOffset = scrollViewer.VerticalOffset;
            }

            targetOffset = Math.Max(0, Math.Min(
                scrollViewer.ScrollableHeight,
                targetOffset - e.Delta * 0.8));
            animationUntilUtc = now.AddMilliseconds(220);
            SmoothScrollAnimatorV3.Animate(scrollViewer, targetOffset);
            e.Handled = true;
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, index);
                if (child is T result) return result;
                result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void NumerateLintels_Click(object sender, RoutedEventArgs e)
        {
            RaiseServiceEvent(_lintelNumerateEvent, "Нумерация перемычек");
        }

        private void NumerateNestedElements_Click(object sender, RoutedEventArgs e)
        {
            RaiseServiceEvent(_nestedElementsNumberingEvent, "Нумерация вложенных элементов");
        }

        private void SetLintelBaseType_Click(object sender, RoutedEventArgs e)
        {
            RaiseServiceEvent(_setLintelBaseTypeEvent, "Определение типа основы");
        }

        private void CreateLintelSections_Click(object sender, RoutedEventArgs e)
        {
            RaiseServiceEvent(_createSectionsEvent, "Создание разрезов");
        }

        private void TagLintels_Click(object sender, RoutedEventArgs e)
        {
            RaiseServiceEvent(_tagLintelsEvent, "Маркировка перемычек");
        }

        private void PlaceLintelSections_Click(object sender, RoutedEventArgs e)
        {
            RaiseServiceEvent(_placeSectionsEvent, "Размещение разрезов на листах");
        }

        private void RaiseServiceEvent(RevitExternalEvent externalEvent, string operationName)
        {
            if (_isClosing || externalEvent == null) return;
            try
            {
                if (externalEvent.Raise() == RevitExternalEventRequest.Accepted) return;
                MessageBox.Show(
                    this,
                    "Revit не принял запрос на операцию «" + operationName + "».",
                    operationName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    operationName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Form_Closed(object sender, EventArgs e)
        {
            _isClosing = true;
            _calculationCancellation.Cancel();
            try
            {
                _selectionEvent?.Dispose();
                _reloadEvent?.Dispose();
                _placementEvent?.Dispose();
                _typeReplacementEvent?.Dispose();
                _lintelNumerateEvent?.Dispose();
                _nestedElementsNumberingEvent?.Dispose();
                _setLintelBaseTypeEvent?.Dispose();
                _createSectionsEvent?.Dispose();
                _tagLintelsEvent?.Dispose();
                _placeSectionsEvent?.Dispose();
            }
            catch
            {
                // Revit может уже завершать внешний контекст; освобождение события тогда не требуется.
            }
        }
    }

    internal static class SmoothScrollAnimatorV3
    {
        private static readonly DependencyProperty AnimatedVerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "AnimatedVerticalOffset",
                typeof(double),
                typeof(SmoothScrollAnimatorV3),
                new PropertyMetadata(0.0, AnimatedVerticalOffsetChanged));

        public static void Animate(ScrollViewer scrollViewer, double targetOffset)
        {
            double currentOffset = scrollViewer.VerticalOffset;
            scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
            scrollViewer.SetValue(AnimatedVerticalOffsetProperty, currentOffset);

            var animation = new DoubleAnimation
            {
                From = currentOffset,
                To = targetOffset,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (sender, args) =>
            {
                scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, null);
                scrollViewer.SetValue(AnimatedVerticalOffsetProperty, targetOffset);
            };
            scrollViewer.BeginAnimation(AnimatedVerticalOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimatedVerticalOffsetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && e.NewValue is double offset)
                scrollViewer.ScrollToVerticalOffset(offset);
        }
    }

    internal sealed class LintelPlacementReportWindowV3 : Window
    {
        private readonly string _report;

        private LintelPlacementReportWindowV3(string report)
        {
            _report = report ?? string.Empty;
            Title = "Отчёт о простановке перемычек";
            Width = 920;
            Height = 680;
            MinWidth = 650;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "Результаты создания типов и размещения перемычек",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(title);

            var reportBox = new TextBox
            {
                Text = _report,
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(10)
            };
            Grid.SetRow(reportBox, 1);
            root.Children.Add(reportBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var copyButton = new Button
            {
                Content = "Копировать",
                MinWidth = 105,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 7, 0)
            };
            copyButton.Click += (sender, args) =>
            {
                try
                {
                    Clipboard.SetText(_report);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        exception.Message,
                        "Копирование отчёта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };
            buttons.Children.Add(copyButton);

            var saveButton = new Button
            {
                Content = "Сохранить в TXT",
                MinWidth = 125,
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 7, 0)
            };
            saveButton.Click += SaveReport_Click;
            buttons.Children.Add(saveButton);

            var closeButton = new Button
            {
                Content = "Закрыть",
                MinWidth = 95,
                Padding = new Thickness(10, 5, 10, 5),
                IsDefault = true
            };
            closeButton.Click += (sender, args) => Close();
            buttons.Children.Add(closeButton);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            Content = root;
        }

        public static void ShowReport(string report)
        {
            var window = new LintelPlacementReportWindowV3(report);
            if (Application.Current != null)
            {
                foreach (Window candidate in Application.Current.Windows)
                {
                    if (candidate is LintelCreatorForm_v3 && candidate.IsVisible)
                    {
                        window.Owner = candidate;
                        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        break;
                    }
                }
            }
            window.ShowDialog();
        }

        private void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Сохранить отчёт о простановке перемычек",
                Filter = "Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*",
                FileName = "Отчёт_перемычки_"
                           + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt",
                AddExtension = true,
                DefaultExt = ".txt"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, _report, new UTF8Encoding(true));
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Сохранение отчёта",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
