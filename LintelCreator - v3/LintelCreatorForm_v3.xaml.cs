using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RevitExternalEvent = Autodesk.Revit.UI.ExternalEvent;

namespace FerrumAddinDev.LintelCreator_v3
{
    public partial class LintelCreatorForm_v3 : Window
    {
        private readonly OpeningSelectionHandlerV3 _selectionHandler;
        private readonly RevitExternalEvent _selectionEvent;
        private bool _isClosing;
        private double _smoothScrollTarget = double.NaN;
        private DateTime _smoothScrollUntilUtc = DateTime.MinValue;
        private double _variantsSmoothScrollTarget = double.NaN;
        private DateTime _variantsSmoothScrollUntilUtc = DateTime.MinValue;

        public LintelCreatorForm_v3(
            LintelOpeningWorkspaceV3 workspace,
            OpeningSelectionHandlerV3 selectionHandler,
            RevitExternalEvent selectionEvent)
        {
            InitializeComponent();
            Workspace = workspace;
            _selectionHandler = selectionHandler;
            _selectionEvent = selectionEvent;
            DataContext = Workspace;
            Closed += Form_Closed;
        }

        public LintelOpeningWorkspaceV3 Workspace { get; }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Workspace.Reload();
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Сбор проёмов", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void Recalculate_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.RecalculateVariants();
        }

        private void RecalculateAll_Click(object sender, RoutedEventArgs e)
        {
            Workspace?.RecalculateAllVariants();
        }

        private void OpeningGroups_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            SmoothScrollListBox(
                OpeningGroupsListBox,
                ref _smoothScrollTarget,
                ref _smoothScrollUntilUtc,
                e);
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

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Form_Closed(object sender, EventArgs e)
        {
            _isClosing = true;
            try
            {
                _selectionEvent?.Dispose();
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
}
