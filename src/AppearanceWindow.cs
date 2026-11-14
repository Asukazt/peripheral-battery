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
    readonly Func<string, Task<ProductImageOption>> import;
    readonly Func<ProductImageOption, bool> choose;
    readonly BatteryState state;
    readonly List<ProductImageOption> values = [];
    string selected;
    ProductImageOption? imported;
    bool closed;
    internal AppearanceWindow(BatteryState state, string selected,
        Func<Task<IReadOnlyList<ProductImageOption>>> load, Func<ProductImageOption, bool> choose,
        Func<string, Task<ProductImageOption>> import)
    {
        this.state = state; this.selected = selected; this.choose = choose; this.import = import;
        Title = "设备颜色与外观"; Width = 600; Height = 460;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var frame = WindowFrame(this);
        var panel = new DockPanel(); frame.Child = panel;
        var header = new StackPanel { Margin = new Thickness(20, 16, 20, 12) };
        header.Children.Add(Text("选择颜色/外观", 20, Ink, FontWeights.SemiBold));
        var name = Text(state.Name, 14, Muted); name.Margin = new Thickness(0, 6, 0, 0); header.Children.Add(name);
        var importButton = Command("folder-open", "导入自定义图片…", async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择设备自定义图片", Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.ico",
                CheckFileExists = true, Multiselect = false, RestoreDirectory = true
            };
            if (dialog.ShowDialog(this) == true) await ImportFile(dialog.FileName);
        });
        importButton.HorizontalAlignment = HorizontalAlignment.Left; importButton.Margin = new Thickness(0, 10, 0, 0);
        importButton.ToolTip = "PNG / JPG / BMP / ICO，最大 10 MB；只保存在本机";
        header.Children.Add(importButton);
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
                var loaded = await load();
                if (closed) return;
                values.Clear(); values.AddRange(loaded);
                if (imported is not null) { values.RemoveAll(v => v.Id == "custom"); values.Insert(0, imported); }
                RenderOptions();
                if (imported is null) status.Text = "图片仅改变软件显示；自定义图片只保存在本机。";
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "获取已取消。"; }
            catch { if (!closed) status.Text = "暂时无法获取图片，请稍后重试。"; }
        };
    }
    internal async Task ImportFile(string file)
    {
        try
        {
            var value = await import(file);
            if (closed) return;
            imported = value;
            values.RemoveAll(v => v.Id == "custom"); values.Insert(0, value);
            Select(value); RenderOptions();
        }
        catch (OperationCanceledException) { }
        catch { if (!closed) status.Text = "导入失败：请选择有效图片（PNG/JPG/BMP/ICO，≤10 MB）并检查目录权限。"; }
    }
    void Select(ProductImageOption value)
    {
        if (!choose(value)) { status.Text = "保存失败，请检查目录是否可写。"; return; }
        selected = value.Id;
        foreach (var card in cards) card.Value.BorderBrush = card.Key == selected ? Green : Line;
        status.Text = "已选择：" + value.Label + "，已保存。";
    }
    void RenderOptions()
    {
        cards.Clear(); options.Children.Clear();
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
            button.Click += (_, _) => Select(value);
        }
    }
}
