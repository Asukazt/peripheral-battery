using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using static PeriDock.Visuals;
using Point = System.Windows.Point;

namespace PeriDock;

internal sealed class DeviceRow
{
    internal Grid Root { get; } = new();
    readonly TextBlock percent, status, updated, extra;
    readonly Image connectionIcon, product;
    readonly Ellipse dot;
    readonly Border fill;
    readonly bool compact;
    readonly double barWidth;

    internal DeviceRow(BatteryState state, bool compact, Controller controller)
    {
        this.compact = compact;
        barWidth = compact ? 140 : 228;
        Root.Height = compact ? 96 : 148;
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 86 : 220) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 144 : 230) });
        product = Product(state, compact ? 78 : 184, compact ? 74 : 128);
        product.HorizontalAlignment = HorizontalAlignment.Center;
        Root.Children.Add(ProductChoice(state, product, () => controller.ChooseAppearance(state)));
        var labels = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(compact ? 8 : 18, 0, 12, 0)
        };
        Grid.SetColumn(labels, 1); Root.Children.Add(labels);
        var name = Text(state.Name, compact ? 13 : 16, Ink, FontWeights.SemiBold);
        name.MaxHeight = compact ? 40 : 64;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        labels.Children.Add(name);
        var connection = new Grid { Margin = new Thickness(0, compact ? 10 : 14, 0, 0) };
        connection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        connection.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connectionIcon = Icon("ellipsis", 18, Muted);
        connectionIcon.HorizontalAlignment = HorizontalAlignment.Left;
        connection.Children.Add(connectionIcon);
        extra = Text("", compact ? 12 : 14, Muted);
        extra.MaxHeight = compact ? 32 : 48;
        extra.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(extra, 1); connection.Children.Add(extra); labels.Children.Add(connection);
        var battery = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(battery, 2); Root.Children.Add(battery);
        percent = Text("--", compact ? 28 : 32, Ink, FontWeights.SemiBold);
        battery.Children.Add(percent);
        var track = new Border
        {
            Background = Brush("#444950"), Width = barWidth, Height = 8,
            CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true
        };
        fill = new Border
        {
            Height = 8, Background = Green, HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(4)
        };
        track.Child = fill; battery.Children.Add(track);
        var below = new DockPanel { Margin = new Thickness(0, 15, 0, 0), LastChildFill = false };
        updated = Text("", 12, Muted); DockPanel.SetDock(updated, Dock.Left); below.Children.Add(updated);
        var health = new StackPanel { Orientation = Orientation.Horizontal };
        dot = Dot(9); dot.Margin = new Thickness(0, 0, 7, 0); health.Children.Add(dot);
        status = Text("", 12); health.Children.Add(status);
        DockPanel.SetDock(health, Dock.Right); below.Children.Add(health);
        if (!compact) battery.Children.Add(below);
        Update(state);
    }

    internal void Update(BatteryState state)
    {
        UpdateProduct(product, state);
        var stale = state.IsStale(DateTimeOffset.UtcNow);
        percent.Text = state.PercentText;
        percent.Foreground = stale ? Muted : state.Percent <= 25 ? Amber : Ink;
        fill.Width = barWidth * (state.Percent ?? 0) / 100d;
        fill.Background = stale ? Muted : state.Percent <= 25 ? Amber : Green;
        extra.Text = state.Connection +
            (state.Kind == "mouse" ? " · " + state.DpiText + (state.DpiText == "未知" ? " DPI" : "") : "") +
            (compact ? " · " + StateText(state) : "");
        var icon = state.Connection == "蓝牙" ? "bluetooth" :
            state.Connection == "USB" ? "usb" : state.Connection.Contains("2.4G") ? "radio-tower" : "ellipsis";
        connectionIcon.Source = IconSource(icon, state.Connection == "蓝牙" ? Brush("#6EA7DC") : Muted);
        updated.Text = state.LastSuccess.HasValue ? $"上次读取：{state.LastSuccess.Value.ToLocalTime():HH:mm}" : "尚未读取";
        status.Text = StateText(state);
        status.Foreground = dot.Fill = StateBrush(state);
        status.ToolTip = state.ChargingNote ?? state.Status;
        Root.ToolTip = $"{state.Name}\n{extra.Text}\n{state.Source}\n{state.Status}" +
            (state.ChargingNote is null ? "" : "\n" + state.ChargingNote);
    }
}

internal sealed class Popover : Window
{
    readonly Controller controller;
    readonly List<DeviceRow> rows = [];
    readonly StackPanel list;
    string rowKey = "";
    readonly TextBlock footer;
    readonly Button refresh;
    internal bool Pinned { get; set; }

    internal Popover(Controller controller)
    {
        this.controller = controller;
        Title = Program.AppName + " · 电量"; Width = 540; Height = 426;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        var frame = WindowFrame(this);
        var grid = new Grid(); frame.Child = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(20, 0, 14, 0) };
        refresh = Command("refresh-cw", "刷新", () => _ = controller.Refresh(), true);
        DockPanel.SetDock(refresh, Dock.Right); header.Children.Add(refresh);
        header.Children.Add(Brand());
        var headerFrame = new Border { Child = header, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.Children.Add(headerFrame);
        list = new StackPanel { Margin = new Thickness(20, 6, 20, 6) };
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        footer = Text("等待首次读取", 13, Muted); footer.Margin = new Thickness(20, 0, 20, 0);
        var bottom = new Border { Child = footer, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(bottom, 2); grid.Children.Add(bottom);
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) controller.ShowDetails(); };
        Deactivated += (_, _) => { if (Pinned) { Pinned = false; Hide(); } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Pinned = false; Hide(); } };
        Closing += (_, e) => { if (!controller.Exiting) { e.Cancel = true; Hide(); Pinned = false; } };
    }

    internal void Update(IReadOnlyList<BatteryState> states, bool refreshing)
    {
        var key = string.Join("|", states.Select(s => s.Id));
        if (key != rowKey || rows.Count == 0)
        {
            rowKey = key; rows.Clear(); list.Children.Clear();
            foreach (var state in states)
            {
                var row = new DeviceRow(state, true, controller); rows.Add(row); list.Children.Add(row.Root);
            }
            if (states.Count == 0) list.Children.Add(Text("未发现 ROG / Razer 外设", 14, Muted));
        }
        for (var i = 0; i < rows.Count; i++) rows[i].Update(states[i]);
        refresh.IsEnabled = !refreshing;
        var last = states.Select(s => s.LastSuccess).DefaultIfEmpty().Max();
        footer.Text = refreshing ? "正在更新…" : last.HasValue ? $"更新于 {last.Value.ToLocalTime():HH:mm}" : "等待首次读取";
    }

    internal void OpenAt(System.Drawing.Rectangle anchor, bool pinned)
    {
        Pinned |= pinned;
        if (!IsVisible) Show();
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var bottomRight = transform.Transform(new Point(anchor.Right, anchor.Top));
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(anchor.Right, anchor.Top));
        var min = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var max = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        Left = Math.Clamp(bottomRight.X - Width, min.X + 8, Math.Max(min.X + 8, max.X - Width - 8));
        Top = Math.Clamp(bottomRight.Y - Height - 8, min.Y + 8, Math.Max(min.Y + 8, max.Y - Height - 8));
        if (pinned) Activate();
    }
}

internal sealed class DetailWindow : Window
{
    readonly Controller controller;
    readonly List<DeviceRow> rows = [];
    readonly StackPanel list;
    string rowKey = "";
    readonly TextBlock status;
    readonly Ellipse dot;
    readonly Button refresh;

    internal DetailWindow(Controller controller)
    {
        this.controller = controller;
        Title = Program.AppName + " · 外设电量";
        Width = 860; Height = 600; MinWidth = 800; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png")));
        var frame = WindowFrame(this);
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 0, CornerRadius = new CornerRadius(8), GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(5)
        });
        var grid = new Grid(); frame.Child = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
        var header = new Grid { Margin = new Thickness(20, 0, 10, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Brand());
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tools, 1); header.Children.Add(tools);
        tools.Children.Add(Command("minus", "最小化", () => WindowState = WindowState.Minimized, true));
        var maximize = Command("square", "最大化", ToggleMaximize, true);
        tools.Children.Add(maximize);
        tools.Children.Add(Command("x", "关闭", Close, true));
        StateChanged += (_, _) =>
        {
            ((Image)((StackPanel)maximize.Content).Children[0]).Source =
                IconSource(WindowState == WindowState.Maximized ? "copy" : "square", Muted);
            maximize.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
            System.Windows.Automation.AutomationProperties.SetName(maximize, (string)maximize.ToolTip);
        };
        var headerFrame = new Border { Child = header, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) };
        grid.Children.Add(headerFrame);
        list = new StackPanel { Margin = new Thickness(22, 4, 22, 4) };
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        var bottom = new DockPanel { LastChildFill = true, Margin = new Thickness(18, 0, 22, 0) };
        var bottomFrame = new Border { Child = bottom, BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(bottomFrame, 2); grid.Children.Add(bottomFrame);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(commands, Dock.Left); bottom.Children.Add(commands);
        refresh = Command("refresh-cw", "刷新", () => _ = controller.Refresh()); commands.Children.Add(refresh);
        void Separator() => commands.Children.Add(new Border
        {
            Width = 1, Height = 24, Background = Line, Margin = new Thickness(12, 0, 12, 0)
        });
        Separator(); commands.Children.Add(Command("folder-open", "打开日志文件夹", controller.OpenLogs));
        Separator(); commands.Children.Add(Command("download", "导出诊断包", controller.Export));
        commands.Children.Add(Command("ellipsis", "设置", () => controller.TrayMenu.Show(System.Windows.Forms.Cursor.Position), true));
        var health = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        dot = Dot(10); dot.Margin = new Thickness(0, 0, 8, 0); health.Children.Add(dot);
        status = Text("正在读取…", 13, Muted);
        status.MaxWidth = 180; status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis;
        health.Children.Add(status); bottom.Children.Add(health);
        Closing += (_, e) => { if (!controller.Exiting) { e.Cancel = true; Hide(); } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
    }

    void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) { WindowState = WindowState.Normal; return; }
        WindowState = WindowState.Maximized;
    }

    internal void Update(IReadOnlyList<BatteryState> states, bool refreshing, string message)
    {
        var key = string.Join("|", states.Select(s => s.Id));
        if (key != rowKey || rows.Count == 0)
        {
            rowKey = key; rows.Clear(); list.Children.Clear();
            foreach (var state in states)
            {
                if (rows.Count > 0) list.Children.Add(new Border { Height = 1, Background = Line });
                var row = new DeviceRow(state, false, controller); rows.Add(row); list.Children.Add(row.Root);
            }
            if (states.Count == 0) list.Children.Add(Text("未发现 ROG / Razer 外设", 16, Muted));
        }
        for (var i = 0; i < rows.Count; i++) rows[i].Update(states[i]);
        refresh.IsEnabled = !refreshing;
        status.Text = refreshing ? "正在读取…" : message;
        status.ToolTip = message;
        dot.Fill = refreshing ? Muted : message == "暂无错误" ? Green : Amber;
    }
}
