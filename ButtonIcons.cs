using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using ShapePath = System.Windows.Shapes.Path;

namespace FerrumAddinDev
{
    /// <summary>Shared, font-independent outline icons for dialog and ribbon buttons.</summary>
    public static class ButtonIcons
    {
        private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>
        {
            { "Run", "M5,3 L13,8 5,13 Z" },
            { "Check", "M2,8 L6,12 14,3" },
            { "Close", "M4,4 L12,12 M12,4 L4,12" },
            { "Add", "M8,2 L8,14 M2,8 L14,8" },
            { "Delete", "M3,4 L13,4 M6,4 L6,2 10,2 10,4 M4,4 L5,14 11,14 12,4 M7,6 L7,12 M9,6 L9,12" },
            { "Refresh", "M13,6 A5,5 0 1 0 13,10 M13,2 L13,6 9,6" },
            { "Save", "M2,2 L12,2 14,4 14,14 2,14 Z M5,2 L5,6 11,6 11,2 M5,14 L5,9 11,9 11,14" },
            { "Folder", "M2,5 L2,3 6,3 8,5 14,5 14,13 2,13 Z" },
            { "Load", "M8,2 L8,10 M4,6 L8,10 12,6 M2,10 L2,14 14,14 14,10" },
            { "Copy", "M6,5 L14,5 14,14 6,14 Z M10,5 L10,2 2,2 2,10 6,10" },
            { "Filter", "M2,2 L14,2 10,7 10,12 6,14 6,7 Z" },
            { "EyeOff", "M2,8 Q8,1 14,8 Q8,15 2,8 M2,2 L14,14 M6,6 L10,10" },
            { "Layers", "M2,5 L8,2 14,5 8,8 Z M2,8 L8,11 14,8 M2,11 L8,14 14,11" },
            { "Family", "M2,6 L8,2 14,6 M3,5 L3,14 13,14 13,5 M6,14 L6,9 10,9 10,14" },
            { "Compare", "M2,3 L6,3 6,13 2,13 Z M10,3 L14,3 14,13 10,13 Z M6,8 L10,8" },
            { "Settings", "M2,4 L14,4 M2,8 L14,8 M2,12 L14,12 M5,2 L5,6 M11,6 L11,10 M7,10 L7,14" },
            { "Number", "M6,2 L4,14 M12,2 L10,14 M2,6 L14,6 M2,10 L14,10" },
            { "Section", "M2,2 L14,2 14,14 2,14 Z M8,2 L8,14 M5,6 L8,9 11,6" },
            { "Tag", "M2,2 L8,2 14,8 8,14 2,8 Z M5,5 L5.1,5" },
            { "Sheet", "M3,2 L10,2 13,5 13,14 3,14 Z M10,2 L10,5 13,5 M5,8 L11,8 M5,11 L11,11" },
            { "Reverse", "M2,5 L13,5 M10,2 L13,5 10,8 M14,11 L3,11 M6,8 L3,11 6,14" },
            { "Link", "M6,10 L10,6 M6,6 L4,6 A3,3 0 0 0 4,12 L7,12 9,10 M10,10 L12,10 A3,3 0 0 0 12,4 L9,4 7,6" },
            { "Chart", "M2,2 L2,14 14,14 M5,12 L5,8 M9,12 L9,5 M13,12 L13,2" },
            { "Beam", "M2,3 L14,3 14,6 10,6 10,10 14,10 14,13 2,13 2,10 6,10 6,6 2,6 Z" },
            { "Rebar", "M2,3 L14,3 M2,8 L14,8 M2,13 L14,13 M4,2 L4,14 M8,2 L8,14 M12,2 L12,14" },
            { "Blocks", "M2,3 L14,3 14,13 2,13 Z M2,8 L14,8 M8,3 L8,8 M5,8 L5,13 M11,8 L11,13" },
            { "Dimension", "M2,3 L2,13 M14,3 L14,13 M2,8 L14,8 M5,5 L2,8 5,11 M11,5 L14,8 11,11" },
            { "Pipe", "M2,2 L7,2 7,9 14,9 14,14 5,14 2,11 Z" },
            { "Pit", "M2,3 L2,10 8,14 14,10 14,3 M2,3 L8,7 14,3 M8,7 L8,14" },
            { "Line", "M2,13 L14,3 M2,11 L2,14 5,14 M11,2 L14,2 14,5" }
        };

        public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
            "Kind", typeof(string), typeof(ButtonIcons), new PropertyMetadata(null, OnKindChanged));

        public static string GetKind(DependencyObject target) => (string)target.GetValue(KindProperty);
        public static void SetKind(DependencyObject target, string value) => target.SetValue(KindProperty, value);

        private static Geometry GetGeometry(string kind)
        {
            var geometry = Geometry.Parse(Paths[kind]);
            geometry.Freeze();
            return geometry;
        }

        private static void OnKindChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var button = target as ButtonBase;
            if (button == null || args.NewValue == null) return;

            // Keep Content intact: existing bindings, handlers and automation names still see the label.
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var icon = new FrameworkElementFactory(typeof(ShapePath));
            icon.SetValue(ShapePath.DataProperty, GetGeometry((string)args.NewValue));
            icon.SetValue(ShapePath.StretchProperty, Stretch.Uniform);
            icon.SetValue(ShapePath.StrokeThicknessProperty, 1.3);
            icon.SetValue(ShapePath.StrokeStartLineCapProperty, PenLineCap.Round);
            icon.SetValue(ShapePath.StrokeEndLineCapProperty, PenLineCap.Round);
            icon.SetValue(FrameworkElement.WidthProperty, 12.0);
            icon.SetValue(FrameworkElement.HeightProperty, 12.0);
            icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 5, 0));
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetBinding(ShapePath.StrokeProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ButtonBase), 1)
            });
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding());
            label.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(icon);
            panel.AppendChild(label);
            button.ContentTemplate = new DataTemplate { VisualTree = panel };
        }

        public static ImageSource CreateImage(string kind, int size)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(size / 16.0, size / 16.0));
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(43, 83, 117)), 1.3)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                context.DrawGeometry(null, pen, GetGeometry(kind));
                context.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        public static PushButtonData CreateRibbonButton(string name, string text, string assembly, string command)
        {
            string kind;
            switch (name)
            {
                case "frmConfig": kind = "Settings"; break;
                case "frmManager": kind = "Family"; break;
                case "frmComparison": kind = "Compare"; break;
                case "mepName": kind = "Pipe"; break;
                case "LintelCreator": case "LintelCreator2": kind = "Beam"; break;
                case "GrillageCreator": case "WallArm": kind = "Rebar"; break;
                case "FBSCreator": case "SlabsLayout": kind = "Blocks"; break;
                case "Column Sections": case "Shaft Sections": kind = "Section"; break;
                case "PilonsDimensions": kind = "Dimension"; break;
                case "PilessFromDWG": kind = "Line"; break;
                case "LinkedFiles": kind = "Link"; break;
                case "Статистика": kind = "Chart"; break;
                case "foundationPit": kind = "Pit"; break;
                default: kind = "Layers"; break;
            }
            return new PushButtonData(name, text, assembly, command)
            {
                Image = CreateImage(kind, 16),
                LargeImage = CreateImage(kind, 32)
            };
        }
    }
}
