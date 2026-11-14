using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static PeriDock.Visuals;

namespace PeriDock;

internal sealed class AppearanceWindow : Window
{
    readonly WrapPanel options = new() { Margin = new Thickness(12) };
    readonly TextBlock status = Text("正在获取可用颜色…", 13, Muted);
    readonly Dictionary<string, Border> cards = [];
    bool closed;
    internal AppearanceWindow(BatteryState state, string selected,
        Func<Task<IReadOnlyList<ProductImageOption>>> load, Func<ProductImageOption, bool> choose)
    {
        Title = "设备颜色与外观"; Width = 600; Height = 460;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var frame = WindowFrame(this);
        var panel = new DockPanel(); frame.Child = panel;
        var header = new StackPanel { Margin = new Thickness(20, 16, 20, 12) };
        header.Children.Add(Text("选择颜色/外观", 20, Ink, FontWeights.SemiBold));
        var name = Text(state.Name, 14, Muted); name.Margin = new Thickness(0, 6, 0, 0); header.Children.Add(name);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var footer = new DockPanel { Margin = new Thickness(16, 8, 16, 12) };
        var close = Command("x", "关闭", Close); DockPanel.SetDock(close, Dock.Right); footer.Children.Add(close);
        status.MaxWidth = 420; footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        panel.Children.Add(new ScrollViewer { Content = options, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => closed = true;
        Loaded += async (_, _) =>
        {
            try
            {
                var values = await load();
                if (closed) return;
                foreach (var value in values)
                {
                    var preview = new BatteryState(state.Id, state.Name, state.ShortName, state.Kind) { ProductImagePath = value.FilePath };
                    var content = new StackPanel(); content.Children.Add(Product(preview, 140, 104));
                    var label = Text(value.Label, 14); label.HorizontalAlignment = HorizontalAlignment.Center;
                    label.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(label);
                    var button = new Button { Content = content, Background = Surface, BorderThickness = new Thickness(0),
                        Padding = new Thickness(6), Cursor = Cursors.Hand, Foreground = Ink };
                    var border = new Border { Child = button, BorderThickness = new Thickness(2),
                        BorderBrush = value.Id == selected ? Green : Line, Margin = new Thickness(4), CornerRadius = new CornerRadius(4) };
                    cards[value.Id] = border; options.Children.Add(border);
                    System.Windows.Automation.AutomationProperties.SetName(button, "选择" + value.Label);
                    button.Click += (_, _) =>
                    {
                        if (!choose(value)) { status.Text = "保存失败，请检查目录是否可写。"; return; }
                        foreach (var card in cards) card.Value.BorderBrush = card.Key == value.Id ? Green : Line;
                        status.Text = "已选择：" + value.Label + "，已保存。";
                    };
                }
                status.Text = values.Any(v => v.Id is not ("auto" or "local" or "generic"))
                    ? "仅改变显示图片，不修改设备灯光或固件。" : "官网暂无可用配色，可继续使用自动图片。";
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "获取已取消。"; }
            catch { if (!closed) status.Text = "暂时无法获取图片，请稍后重试。"; }
        };
    }
}
