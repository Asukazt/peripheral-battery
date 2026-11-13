using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Xml.Linq;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace PeriDock;

internal static class Visuals
{
    internal static readonly Brush Ink = Brush("#F2F3F5");
    internal static readonly Brush Muted = Brush("#A5ABB3");
    internal static readonly Brush Green = Brush("#2DB578");
    internal static readonly Brush Amber = Brush("#F2A632");
    internal static readonly Brush Line = Brush("#3B4046");
    internal static readonly Brush Surface = Brush("#262A2E");
    internal static readonly Brush Header = Brush("#24282C");
    internal static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    internal static DrawingImage IconSource(string name, Brush brush)
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "assets", "icons", name + ".svg"));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var pen = new Pen(brush, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        foreach (var e in xml.Root!.Elements())
        {
            double A(string key, double fallback = 0) => double.TryParse(e.Attribute(key)?.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var v) ? v : fallback;
            Geometry? geometry = e.Name.LocalName switch
            {
                "path" => Geometry.Parse(e.Attribute("d")!.Value),
                "rect" => new RectangleGeometry(new Rect(A("x"), A("y"), A("width"), A("height")), A("rx"), A("rx")),
                "circle" => new EllipseGeometry(new Point(A("cx"), A("cy")), A("r"), A("r")),
                "line" => new LineGeometry(new Point(A("x1"), A("y1")), new Point(A("x2"), A("y2"))),
                "polyline" or "polygon" => Geometry.Parse("M" + e.Attribute("points")!.Value + (e.Name.LocalName == "polygon" ? " Z" : "")),
                _ => null
            };
            if (geometry is not null) group.Children.Add(new GeometryDrawing(null, pen, geometry));
        }
        group.Freeze();
        return new DrawingImage(group);
    }

    internal static Image Icon(string name, double size = 18, Brush? brush = null)
        => new() { Source = IconSource(name, brush ?? Ink), Width = size, Height = size, Stretch = Stretch.Uniform };

    internal static Border WindowFrame(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.Foreground = Ink;
        window.FontFamily = new FontFamily("Microsoft YaHei UI");
        window.UseLayoutRounding = true;
        var frame = new Border
        {
            Background = Surface, BorderBrush = Brush("#50565E"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8)
        };
        window.Content = frame;
        return frame;
    }

    internal static Image Product(string id, double width, double height)
    {
        if (id is not ("keyboard" or "mouse" or "headset"))
        {
            var generic = Icon("battery-full", Math.Min(width, height), Muted);
            generic.Width = width; generic.Height = height;
            return generic;
        }
        var file = id + "-display.png";
        return new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", file))),
            Width = width, Height = height, Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
    }

    internal static StackPanel Brand()
    {
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png"))),
            Width = 28, Height = 30
        });
        var text = Text(Program.AppName, 21, Ink, FontWeights.SemiBold);
        text.Margin = new Thickness(10, 0, 0, 0);
        brand.Children.Add(text);
        return brand;
    }

    internal static Ellipse Dot(double size = 10) => new()
    {
        Width = size, Height = size, Fill = Muted, VerticalAlignment = VerticalAlignment.Center
    };

    internal static Brush StateBrush(BatteryState state) => state.IsStale(DateTimeOffset.UtcNow) ? Muted :
        state.Charging == true || state.Percent <= 25 ? Amber : Green;

    internal static string StateText(BatteryState state) => state.IsStale(DateTimeOffset.UtcNow)
        ? state.Cached ? "缓存" : state.Connected == false ? "未连接" : "未更新"
        : state.Charging == true ? "充电中" : state.Charging == false ? "正常" : "充电未知";

    internal static string ProductName(string id) => id switch
    {
        "keyboard" => "华硕 ROG 夜魔 X",
        "mouse" => "雷蛇毒蝰 V3 Pro",
        _ => "雷蛇旋风黑鲨 V3 Pro"
    };

    internal static System.Drawing.Rectangle PhysicalBounds(Window window)
    {
        var topLeft = window.PointToScreen(new Point(0, 0));
        var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
        return System.Drawing.Rectangle.FromLTRB((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
    }

    static readonly ControlTemplate ButtonTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="frame" Background="{TemplateBinding Background}" CornerRadius="4"
                  BorderBrush="Transparent" BorderThickness="1">
            <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="frame" Property="Background" Value="#383E44"/></Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="frame" Property="BorderBrush" Value="#2DB578"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    internal static TextBlock Text(string text, double size = 14, Brush? brush = null, FontWeight? weight = null)
        => new()
        {
            Text = text, FontSize = size, Foreground = brush ?? Ink, FontWeight = weight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        };

    internal static Button Command(string icon, string title, Action action, bool iconOnly = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Icon(icon, iconOnly ? 22 : 24, Muted));
        if (!iconOnly)
        {
            var label = Text(title, 14); label.Margin = new Thickness(9, 0, 0, 0);
            content.Children.Add(label);
        }
        var button = new Button
        {
            Content = content, ToolTip = title, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(10, 8, 10, 8),
            Cursor = System.Windows.Input.Cursors.Hand, MinWidth = 40, MinHeight = 40,
            Template = ButtonTemplate, FocusVisualStyle = null
        };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Click += (_, _) => action();
        return button;
    }
}
