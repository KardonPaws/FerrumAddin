using Autodesk.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FerrumAddinDev
{
    public enum RibbonTabHeaderMode
    {
        IconAndText,
        TextOnly,
        IconOnly
    }

    internal static class RibbonTabHeaderManager
    {
        private const string IconTag = "FerrumAddin.RibbonTabIcon";
        private const int MaxApplyAttempts = 12;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(120);

        private static RibbonControl ribbon;
        private static string tabTitle;
        private static RibbonTabHeaderMode mode;
        private static DispatcherTimer retryTimer;
        private static int applyAttempt;
        private static bool refreshPending;
        private static WeakReference<FrameworkElement> iconReference;

        public static void Initialize(string title, RibbonTabHeaderMode headerMode)
        {
            tabTitle = title;
            mode = headerMode;
            WatchRibbon(ComponentManager.Ribbon);
            ScheduleApply();
        }

        public static void Apply(RibbonTabHeaderMode headerMode)
        {
            mode = headerMode;
            WatchRibbon(ComponentManager.Ribbon);
            ScheduleApply();
        }

        public static void Shutdown()
        {
            StopRetryTimer();

            if (ribbon != null)
                ribbon.SizeChanged -= Ribbon_SizeChanged;

            FrameworkElement icon;
            if (iconReference != null && iconReference.TryGetTarget(out icon))
                icon.Unloaded -= Icon_Unloaded;

            iconReference = null;
            ribbon = null;
        }

        private static void WatchRibbon(RibbonControl currentRibbon)
        {
            if (ReferenceEquals(ribbon, currentRibbon))
                return;

            if (ribbon != null)
                ribbon.SizeChanged -= Ribbon_SizeChanged;

            ribbon = currentRibbon;

            if (ribbon != null)
                ribbon.SizeChanged += Ribbon_SizeChanged;
        }

        private static void ScheduleApply()
        {
            StopRetryTimer();
            applyAttempt = 0;

            if (TryApply())
                return;

            if (ribbon == null || ribbon.Dispatcher == null)
                return;

            retryTimer = new DispatcherTimer(DispatcherPriority.Loaded, ribbon.Dispatcher)
            {
                Interval = RetryDelay
            };
            retryTimer.Tick += RetryTimer_Tick;
            retryTimer.Start();
        }

        private static void RetryTimer_Tick(object sender, EventArgs e)
        {
            applyAttempt++;
            if (TryApply() || applyAttempt >= MaxApplyAttempts)
                StopRetryTimer();
        }

        private static void StopRetryTimer()
        {
            if (retryTimer == null)
                return;

            retryTimer.Stop();
            retryTimer.Tick -= RetryTimer_Tick;
            retryTimer = null;
        }

        private static bool TryApply()
        {
            try
            {
                if (ribbon == null || string.IsNullOrWhiteSpace(tabTitle))
                    return false;

                RibbonTab ribbonTab = ribbon.Tabs.FirstOrDefault(tab =>
                    string.Equals(tab.Name, tabTitle, StringComparison.Ordinal) ||
                    string.Equals(tab.Title, tabTitle, StringComparison.Ordinal));

                if (ribbonTab == null)
                    return false;

                FrameworkElement tabButton = FindTabButton(ribbon, ribbonTab, tabTitle);
                if (tabButton == null)
                    return false;

                TextBlock titleBlock = FindTitleBlock(tabButton, tabTitle);
                Panel contentPanel = FindContentPanel(tabButton, titleBlock);
                if (contentPanel == null || titleBlock == null)
                    return false;

                titleBlock.Visibility = mode == RibbonTabHeaderMode.IconOnly
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                FrameworkElement icon = contentPanel.Children
                    .OfType<FrameworkElement>()
                    .FirstOrDefault(item => string.Equals(item.Tag as string, IconTag, StringComparison.Ordinal));

                if (mode == RibbonTabHeaderMode.TextOnly)
                {
                    if (icon != null)
                    {
                        icon.Unloaded -= Icon_Unloaded;
                        contentPanel.Children.Remove(icon);
                    }

                    iconReference = null;
                    return true;
                }

                Thickness margin = mode == RibbonTabHeaderMode.IconAndText
                    ? new Thickness(0, 0, 4, 0)
                    : new Thickness(0);

                if (icon == null)
                {
                    icon = CreateVectorIcon(margin);
                    contentPanel.Children.Insert(0, icon);
                }
                else
                {
                    icon.Margin = margin;
                }

                TrackIcon(icon);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static FrameworkElement CreateVectorIcon(Thickness margin)
        {
            Grid icon = new Grid
            {
                Width = 16,
                Height = 16,
                Margin = margin,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Focusable = false,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                ClipToBounds = true,
                Tag = IconTag
            };

            icon.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 90, 165))
            });

            icon.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 15.2,3.4 L 3.4,3.4 L 3.4,8.3 L 11.3,8.3 L 11.3,12.3 L 3.4,12.3 L 3.4,15.2"),
                Stroke = Brushes.White,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Square,
                StrokeEndLineCap = PenLineCap.Square,
                StrokeLineJoin = PenLineJoin.Miter,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            });

            return icon;
        }

        private static void TrackIcon(FrameworkElement icon)
        {
            FrameworkElement previousIcon;
            if (iconReference != null && iconReference.TryGetTarget(out previousIcon) &&
                !ReferenceEquals(previousIcon, icon))
            {
                previousIcon.Unloaded -= Icon_Unloaded;
            }

            icon.Unloaded -= Icon_Unloaded;
            icon.Unloaded += Icon_Unloaded;
            iconReference = new WeakReference<FrameworkElement>(icon);
        }

        private static void Icon_Unloaded(object sender, RoutedEventArgs e)
        {
            FrameworkElement icon = sender as FrameworkElement;
            if (icon != null)
                icon.Unloaded -= Icon_Unloaded;

            iconReference = null;
            RequestRefresh();
        }

        private static void Ribbon_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RequestRefresh();
        }

        private static void RequestRefresh()
        {
            if (refreshPending || ribbon == null || ribbon.Dispatcher == null)
                return;

            refreshPending = true;
            ribbon.Dispatcher.BeginInvoke(new Action(() =>
            {
                refreshPending = false;
                if (!TryApply())
                    ScheduleApply();
            }), DispatcherPriority.Loaded);
        }

        private static FrameworkElement FindTabButton(
            DependencyObject root,
            RibbonTab ribbonTab,
            string title)
        {
            FrameworkElement titleMatch = null;
            Queue<DependencyObject> queue = new Queue<DependencyObject>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                FrameworkElement element = current as FrameworkElement;

                if (element != null && element.GetType().Name == "RibbonTabButton")
                {
                    if (ReferenceEquals(element.DataContext, ribbonTab))
                        return element;

                    if (titleMatch == null)
                    {
                        TextBlock text = FindTitleBlock(element, title);
                        if (text != null)
                            titleMatch = element;
                    }
                }

                int childCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                    queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }

            return titleMatch;
        }

        private static TextBlock FindTitleBlock(DependencyObject root, string title)
        {
            TextBlock namedText = FindDescendant<TextBlock>(root, "mContent");
            if (namedText != null)
                return namedText;

            return FindDescendants<TextBlock>(root)
                .FirstOrDefault(text => string.Equals(text.Text, title, StringComparison.Ordinal));
        }

        private static Panel FindContentPanel(DependencyObject tabButton, TextBlock titleBlock)
        {
            StackPanel namedPanel = FindDescendant<StackPanel>(tabButton, "mStackPanel");
            if (namedPanel != null)
                return namedPanel;

            DependencyObject current = titleBlock == null
                ? null
                : VisualTreeHelper.GetParent(titleBlock);

            while (current != null && !ReferenceEquals(current, tabButton))
            {
                Panel panel = current as Panel;
                if (panel != null)
                    return panel;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static T FindDescendant<T>(DependencyObject root, string name)
            where T : FrameworkElement
        {
            return FindDescendants<T>(root)
                .FirstOrDefault(element => string.Equals(element.Name, name, StringComparison.Ordinal));
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
                yield break;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                T match = child as T;
                if (match != null)
                    yield return match;

                foreach (T descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
