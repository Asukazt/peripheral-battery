using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static PeriDock.Visuals;
using Point = System.Windows.Point;

namespace PeriDock;

internal sealed class BatteryStatsWindow : Window
{
    sealed record StatsDevice(string Id, string Name, string Kind, int? Percent, bool Current, bool Supported);

    readonly Controller controller;
    readonly StackPanel deviceList, sessionList;
    readonly TextBlock deviceTitle, deviceSubtitle, chargeInfo, footer;
    readonly TextBlock todayValue, todaySub, sessionValue, sessionSub;
    readonly TextBlock rateValue, rateSub, remainingValue, remainingSub;
    readonly Button range24, range7, range30, clear;
    readonly UsageChart chart;
    readonly Dictionary<string, (Button Button, TextBlock Percent, TextBlock Sub)> deviceRows = [];
    string deviceKey = "", chartKey = "", selectedId = "";
    TimeSpan range = TimeSpan.FromHours(24);

    internal BatteryStatsWindow(Controller controller)
    {
        this.controller = controller;
        Title = Program.AppName + " · 电池使用统计";
        Width = 900; Height = 660; MinWidth = 820; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png")));
        var frame = WindowFrame(this);
        var grid = new Grid(); frame.Child = grid;
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Margin = new Thickness(20, 0, 10, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(Brand());
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        };
        var close = Command("x", "关闭", Hide, true);
        Grid.SetColumn(close, 1); header.Children.Add(close);
        grid.Children.Add(new Border { Child = header, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1) });

        var body = new Grid { Margin = new Thickness(20, 14, 20, 16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1); grid.Children.Add(body);

        var left = new Border
        {
            Background = Brush("#24282C"), BorderBrush = Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10)
        };
        left.Child = new StackPanel
        {
            Children =
            {
                Text("设备", 13, Muted, FontWeights.SemiBold),
                new ScrollViewer
                {
                    Content = deviceList = new StackPanel { Margin = new Thickness(0, 10, 0, 0) },
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Margin = new Thickness(0, 2, 0, 0)
                }
            }
        };
        body.Children.Add(left);

        var right = new Grid { Margin = new Thickness(18, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
        Grid.SetColumn(right, 1); body.Children.Add(right);

        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        deviceTitle = Text("电池使用统计", 18, Ink, FontWeights.SemiBold);
        deviceSubtitle = Text("正在读取设备记录…", 12, Muted);
        title.Children.Add(deviceTitle); title.Children.Add(deviceSubtitle);
        right.Children.Add(title);

        var rangeRow = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center };
        var ranges = new StackPanel { Orientation = Orientation.Horizontal };
        range24 = RangeButton("24 小时", TimeSpan.FromHours(24));
        range7 = RangeButton("7 天", TimeSpan.FromDays(7));
        range30 = RangeButton("30 天", TimeSpan.FromDays(30));
        ranges.Children.Add(range24); ranges.Children.Add(range7); ranges.Children.Add(range30);
        DockPanel.SetDock(ranges, Dock.Left); rangeRow.Children.Add(ranges);
        chargeInfo = Text("", 12, Muted);
        chargeInfo.VerticalAlignment = VerticalAlignment.Center;
        chargeInfo.Margin = new Thickness(14, 0, 0, 0);
        DockPanel.SetDock(chargeInfo, Dock.Left); rangeRow.Children.Add(chargeInfo);
        Grid.SetRow(rangeRow, 1); right.Children.Add(rangeRow);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
        var todayMetric = Metric("今日放电");
        todayValue = todayMetric.Value; todaySub = todayMetric.Sub;
        var sessionMetric = Metric("本次放电");
        sessionValue = sessionMetric.Value; sessionSub = sessionMetric.Sub;
        var rateMetric = Metric("平均耗电");
        rateValue = rateMetric.Value; rateSub = rateMetric.Sub;
        var remainingMetric = Metric("预计剩余");
        remainingValue = remainingMetric.Value; remainingSub = remainingMetric.Sub;
        var todayCard = todayMetric.Card;
        var sessionCard = sessionMetric.Card;
        var rateCard = rateMetric.Card;
        var remainingCard = remainingMetric.Card;
        metrics.Children.Add(todayCard); metrics.Children.Add(sessionCard);
        metrics.Children.Add(rateCard); metrics.Children.Add(remainingCard);
        Grid.SetRow(metrics, 2); right.Children.Add(metrics);

        var chartFrame = new Border
        {
            Background = Brush("#2A2E32"), BorderBrush = Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6)
        };
        chart = new UsageChart();
        chartFrame.Child = chart;
        Grid.SetRow(chartFrame, 3); right.Children.Add(chartFrame);

        var recent = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        recent.Children.Add(Text("最近放电", 12, Muted, FontWeights.SemiBold));
        sessionList = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        recent.Children.Add(sessionList);
        Grid.SetRow(recent, 4); right.Children.Add(recent);

        var bottom = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center };
        footer = Text("仅统计连接且持续放电的时间；休眠、断开和充电期间不计入。", 11, Muted);
        footer.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(footer, Dock.Left); bottom.Children.Add(footer);
        clear = new Button
        {
            Content = Text("清空该设备统计", 11, Muted),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4), Cursor = System.Windows.Input.Cursors.Hand,
            Template = ButtonTemplate, ToolTip = "只删除当前设备的电量历史"
        };
        clear.Click += (_, _) => ClearSelected();
        DockPanel.SetDock(clear, Dock.Right); bottom.Children.Add(clear);
        Grid.SetRow(bottom, 5); right.Children.Add(bottom);

        Closing += (_, e) => { if (!controller.Exiting) { e.Cancel = true; Hide(); } };
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Hide(); };
        Update();
    }

    internal void Update()
    {
        if (controller.Exiting) return;
        var devices = CollectDevices();
        var key = string.Join("|", devices.Select(d => d.Id + "~" + d.Name + "~" + (d.Current ? "1" : "0")));
        if (key != deviceKey)
        {
            deviceKey = key;
            RebuildDevices(devices);
        }
        if (selectedId.Length == 0 || devices.All(d => d.Id != selectedId))
            selectedId = devices.FirstOrDefault(d => d.Current)?.Id ?? devices.FirstOrDefault()?.Id ?? "";
        for (var i = 0; i < devices.Count; i++)
        {
            var item = devices[i];
            if (!deviceRows.TryGetValue(item.Id, out var row)) continue;
            row.Percent.Text = item.Percent.HasValue ? item.Percent + "%" : "--";
            row.Percent.Foreground = item.Percent.HasValue ? Ink : Muted;
            row.Sub.Text = !item.Supported ? "暂未适配电量" : item.Current ? KindName(item.Kind) : "历史记录";
            row.Button.BorderBrush = item.Id == selectedId ? Green : Line;
            row.Button.Background = item.Id == selectedId ? Brush("#2E3D34") : Brush("#30353A");
        }

        StyleRange(range24, range == TimeSpan.FromHours(24));
        StyleRange(range7, range == TimeSpan.FromDays(7));
        StyleRange(range30, range == TimeSpan.FromDays(30));

        var selected = devices.FirstOrDefault(d => d.Id == selectedId);
        var samples = selectedId.Length == 0 ? [] : controller.Usage.SamplesFor(selectedId);
        var currentPercent = selected?.Percent ?? samples.LastOrDefault()?.Percent;
        var summary = BatteryUsage.Compute(samples, DateTimeOffset.UtcNow, range, currentPercent);
        deviceTitle.Text = selected?.Name ?? "电池使用统计";
        deviceSubtitle.Text = selected is null ? "未发现已适配外设" :
            $"{KindName(selected.Kind)} · {(selected.Percent.HasValue ? "当前 " + selected.Percent + "%" : "当前电量未知")}" +
            (selected.Current ? "" : " · 设备当前未连接");
        chargeInfo.Text = $"充电 {summary.ChargeCount} 次 · 上次充电 {Ago(summary.LastChargeEnd)}";

        todayValue.Text = summary.SampleCount == 0 ? "暂无数据" : BatteryUsage.Duration(summary.TodayDischarge);
        todaySub.Text = summary.SampleCount == 0 ? "等待首次记录" : $"下降 {summary.TodayDrop:0.#}%";
        sessionValue.Text = summary.CurrentSession is { } session
            ? BatteryUsage.Duration(session.Duration) : "暂无数据";
        sessionSub.Text = summary.CurrentSession is { } current
            ? $"{current.StartPercent}% → {current.EndPercent}%" + (current.Ongoing ? " · 进行中" : "")
            : "等待完整放电记录";
        rateValue.Text = summary.SampleCount == 0 ? "暂无数据" : BatteryUsage.Rate(summary.DrainPerHour);
        rateSub.Text = summary.SampleCount == 0 ? "等待首次记录" : $"区间下降 {summary.PercentDropped:0.#}%";
        remainingValue.Text = summary.SampleCount == 0 ? "暂无数据" : BatteryUsage.Remaining(summary.EstimatedRemaining);
        remainingSub.Text = summary.EstimatedRemaining.HasValue ? "按当前耗电速度" : "需要至少 1 小时放电样本";

        var nextChartKey = selectedId + "|" + range.Ticks + "|" + summary.SampleCount + "|" +
            (samples.LastOrDefault()?.Time.Ticks ?? 0);
        if (nextChartKey != chartKey)
        {
            chartKey = nextChartKey;
            chart.SetData(samples, summary.RangeStart, summary.RangeEnd, range);
        }
        RenderSessions(summary);
        clear.IsEnabled = samples.Count > 0;
    }

    List<StatsDevice> CollectDevices()
    {
        var result = new List<StatsDevice>();
        foreach (var state in controller.States)
            result.Add(new StatsDevice(state.Id, state.Name, state.Kind, state.Percent, true, state.Supported));
        foreach (var saved in controller.Usage.Devices())
        {
            if (result.Any(d => d.Id == saved.Id)) continue;
            result.Add(new StatsDevice(saved.Id, saved.Name, saved.Kind, saved.Percent, false, true));
        }
        return result;
    }

    void RebuildDevices(IReadOnlyList<StatsDevice> devices)
    {
        deviceList.Children.Clear();
        deviceRows.Clear();
        if (devices.Count == 0)
        {
            deviceList.Children.Add(Text("未发现已适配外设", 12, Muted));
            return;
        }
        foreach (var item in devices)
        {
            var name = Text(item.Name, 12, Ink, FontWeights.SemiBold);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis; name.MaxHeight = 20;
            var sub = Text(!item.Supported ? "暂未适配电量" : item.Current ? KindName(item.Kind) : "历史记录", 11, Muted);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(name); labels.Children.Add(sub);
            var percent = Text(item.Percent.HasValue ? item.Percent + "%" : "--", 14, Ink, FontWeights.SemiBold);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(labels);
            Grid.SetColumn(percent, 1); row.Children.Add(percent);
            var button = new Button
            {
                Content = row, Height = 54, Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(9, 4, 9, 4), Background = Brush("#30353A"),
                BorderBrush = Line, BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Template = ButtonTemplate,
                ToolTip = item.Name
            };
            var id = item.Id;
            button.Click += (_, _) => { selectedId = id; Update(); };
            deviceList.Children.Add(button);
            deviceRows[id] = (button, percent, sub);
        }
    }

    void RenderSessions(BatteryUsageSummary summary)
    {
        sessionList.Children.Clear();
        if (summary.Sessions.Count == 0)
        {
            sessionList.Children.Add(Text("还没有完整的一次放电记录", 12, Muted));
            return;
        }
        foreach (var session in summary.Sessions.Take(3))
        {
            var line = Text(
                $"{session.Start.ToLocalTime():M/d HH:mm} → {session.End.ToLocalTime():HH:mm}  ·  " +
                $"{BatteryUsage.Duration(session.Duration)}  ·  {session.StartPercent}% → {session.EndPercent}%" +
                (session.Ongoing ? "  · 进行中" : ""), 11, Muted);
            line.Margin = new Thickness(0, 0, 0, 3);
            sessionList.Children.Add(line);
        }
    }

    void ClearSelected()
    {
        var selected = CollectDevices().FirstOrDefault(d => d.Id == selectedId);
        if (selected is null) return;
        if (MessageBox.Show(this, $"确定删除「{selected.Name}」的全部电量历史？此操作不可恢复。",
                "清空统计", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        controller.Usage.Delete(selected.Id);
        chartKey = "";
        Update();
    }

    Button RangeButton(string label, TimeSpan value)
    {
        var button = new Button
        {
            Content = Text(label, 12, Ink), Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 8, 0), Background = Brush("#30353A"),
            BorderBrush = Line, BorderThickness = new Thickness(1), Template = ButtonTemplate,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, _) => { range = value; chartKey = ""; Update(); };
        return button;
    }

    static void StyleRange(Button button, bool selected)
    {
        button.BorderBrush = selected ? Green : Line;
        button.Background = selected ? Brush("#2E3D34") : Brush("#30353A");
    }

    static (Border Card, TextBlock Value, TextBlock Sub) Metric(string title)
    {
        var value = Text("暂无数据", 18, Ink, FontWeights.SemiBold);
        var sub = Text("等待记录", 11, Muted);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(Text(title, 11, Muted));
        panel.Children.Add(value);
        panel.Children.Add(sub);
        var card = new Border
        {
            Background = Brush("#2A2E32"), BorderBrush = Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 8, 0), Child = panel
        };
        return (card, value, sub);
    }

    static string KindName(string kind) => kind switch
    {
        "keyboard" => "键盘", "mouse" => "鼠标", "headset" => "耳机", _ => "设备"
    };

    static string Ago(DateTimeOffset? value)
    {
        if (value is null) return "无记录";
        var span = DateTimeOffset.UtcNow - value.Value;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalMinutes < 1) return "刚刚";
        if (span.TotalHours < 1) return $"{Math.Round(span.TotalMinutes)} 分钟前";
        if (span.TotalDays < 1) return $"{Math.Round(span.TotalHours)} 小时前";
        return $"{Math.Round(span.TotalDays)} 天前";
    }
}

internal sealed class UsageChart : FrameworkElement
{
    IReadOnlyList<BatterySample> samples = [];
    DateTimeOffset start, end;
    TimeSpan range;

    internal void SetData(IReadOnlyList<BatterySample> data, DateTimeOffset from, DateTimeOffset to, TimeSpan span)
    {
        samples = data; start = from; end = to; range = span;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 140 || height < 90) return;
        var pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Microsoft YaHei UI");
        var plot = new Rect(44, 8, Math.Max(20, width - 56), Math.Max(20, height - 34));
        var gridPen = new Pen(Line, 1) { DashStyle = new DashStyle([2, 5], 0) };
        for (var percent = 0; percent <= 100; percent += 25)
        {
            var y = plot.Bottom - plot.Height * percent / 100;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = new FormattedText(percent + "%", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, Muted, pixels);
            dc.DrawText(label, new Point(plot.Left - label.Width - 6, y - label.Height / 2));
        }
        var format = range <= TimeSpan.FromHours(26) ? "HH:mm" : "M/d";
        for (var i = 0; i <= 2; i++)
        {
            var time = start + TimeSpan.FromTicks((end - start).Ticks * i / 2);
            var label = new FormattedText(
                TimeZoneInfo.ConvertTime(time, TimeZoneInfo.Local).ToString(format, CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, Muted, pixels);
            var x = plot.Left + plot.Width * i / 2 - (i == 0 ? 0 : i == 2 ? label.Width : label.Width / 2);
            dc.DrawText(label, new Point(x, plot.Bottom + 8));
        }

        var points = samples.Where(s => s.Time >= start.AddHours(-2) && s.Time <= end).OrderBy(s => s.Time).ToList();
        if (points.Count < 2)
        {
            var hint = new FormattedText("样本不足，继续使用一段时间后会生成曲线", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 12, Muted, pixels);
            dc.DrawText(hint, new Point(plot.Left + (plot.Width - hint.Width) / 2,
                plot.Top + (plot.Height - hint.Height) / 2));
            return;
        }

        Point Map(BatterySample sample)
        {
            var total = (end - start).TotalSeconds;
            var x = total <= 0 ? plot.Left : plot.Left + (sample.Time - start).TotalSeconds / total * plot.Width;
            var y = plot.Bottom - plot.Height * Math.Clamp(sample.Percent, 0, 100) / 100d;
            return new Point(Math.Clamp(x, plot.Left - 2, plot.Right + 2), y);
        }

        dc.PushClip(new RectangleGeometry(plot));
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            if (b.Time - a.Time > BatteryUsage.GapLimit) continue;
            var charging = a.Charging == true || b.Charging == true;
            var pen = new Pen(charging ? Amber : Green, charging ? 1.6 : 1.9)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round
            };
            dc.DrawLine(pen, Map(a), Map(b));
        }
        var last = Map(points[^1]);
        dc.DrawEllipse(Green, null, last, 3.2, 3.2);
        dc.Pop();
    }
}
