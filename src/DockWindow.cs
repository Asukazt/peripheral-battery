using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using static PeriDock.Visuals;
using Point = System.Windows.Point;
using Forms = System.Windows.Forms;

namespace PeriDock;

internal sealed class DockWindow : Window
{
    readonly Controller controller;
    readonly List<(TextBlock Percent, Ellipse Dot, Grid Tile, Image Product)> tiles = [];
    readonly Grid grid;
    readonly ScrollViewer scroll;
    string tileKey = "";
    internal DockWindow(Controller controller)
    {
        this.controller = controller;
        Title = Program.AppName + " · 常驻悬浮窗";
        Width = 480; Height = 76;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        var frame = WindowFrame(this);
        grid = new Grid { Margin = new Thickness(8, 5, 8, 5) };
        scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden };
        scroll.PreviewMouseWheel += (_, e) => { scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta); e.Handled = true; };
        frame.Child = scroll;
        System.Windows.Automation.AutomationProperties.SetName(scroll, "外设列表");
        BuildTiles(controller.States);
        System.Windows.Automation.AutomationProperties.SetName(this, Program.AppName + " 常驻悬浮窗");
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { controller.ShowDetails(); e.Handled = true; return; }
            var start = new Point(Left, Top);
            try { DragMove(); } catch (InvalidOperationException) { }
            if (Math.Abs(Left - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(Top - start.Y) < SystemParameters.MinimumVerticalDragDistance)
                controller.ScheduleClick();
            e.Handled = true;
        };
        MouseRightButtonUp += (_, _) => controller.TrayMenu.Show(Forms.Cursor.Position);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) controller.ShowDetails();
            if (e.Key == Key.Space) controller.TogglePopover();
        };
        SourceInitialized += (_, _) => PositionAtCorner();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PositionAtCorner);
        Closing += (_, e) => { if (!controller.Exiting) { e.Cancel = true; controller.SetDockVisible(false); } };
        Update(controller.States);
    }

    void BuildTiles(IReadOnlyList<BatteryState> states)
    {
        grid.Children.Clear(); grid.ColumnDefinitions.Clear(); tiles.Clear();
        Width = Math.Max(180, Math.Min(640, states.Count * 156 + 18));
        if (states.Count == 0) grid.Children.Add(Text("暂无外设", 14, Muted));
        for (var i = 0; i < states.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(156) });
            var tile = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tile.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            var state = states[i];
            var product = Product(state, 68, 54);
            tile.Children.Add(ProductChoice(state, product, () => controller.ChooseAppearance(state)));
            var percent = Text("--", 19, Ink, FontWeights.SemiBold);
            percent.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(percent, 1); tile.Children.Add(percent);
            var dot = Dot(10); Grid.SetColumn(dot, 2); tile.Children.Add(dot);
            Grid.SetColumn(tile, i); grid.Children.Add(tile);
            if (i < states.Count - 1)
            {
                var separator = new Border { Width = 1, Height = 40, Background = Line, HorizontalAlignment = HorizontalAlignment.Right };
                Grid.SetColumn(separator, i); grid.Children.Add(separator);
            }
            tiles.Add((percent, dot, tile, product));
        }
    }

    internal void PositionAtCorner()
    {
        var screen = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var corner = transform.Transform(new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        Left = corner.X - Width - 16;
        Top = corner.Y - Height - 12;
    }

    internal void Update(IReadOnlyList<BatteryState> states)
    {
        var key = string.Join("|", states.Select(s => s.Id));
        if (key != tileKey) { tileKey = key; BuildTiles(states); if (IsVisible) PositionAtCorner(); }
        for (var i = 0; i < tiles.Count; i++)
        {
            var state = states[i];
            UpdateProduct(tiles[i].Product, state);
            tiles[i].Percent.Text = state.PercentText;
            tiles[i].Percent.Foreground = state.IsStale(DateTimeOffset.UtcNow) ? Muted : state.Percent <= 25 ? Amber : Ink;
            tiles[i].Dot.Fill = StateBrush(state);
            tiles[i].Tile.ToolTip = $"{state.Name} · {state.PercentText}\n{state.Connection}" +
                (state.Kind == "mouse" ? " · " + state.DpiText : "") + "\n" + StateText(state) +
                (state.ChargingNote is null ? "" : "\n" + state.ChargingNote);
        }
    }
}
