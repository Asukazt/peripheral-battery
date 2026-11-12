using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PeriDock;

internal static class Program
{
    internal const string AppName = "Peripheral Battery";
    [STAThread]
    static int Main(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var log = new Diagnostics(root);
        if (args.Length == 2 && args[0] == "--probe")
        {
            try { Console.WriteLine(JsonSerializer.Serialize(DeviceReader.Probe(args[1]).GetAwaiter().GetResult())); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(Diagnostics.Redact(ex.GetType().Name + ": " + ex.Message)); return 1; }
        }
        using var instance = new Mutex(true, @"Local\PeriDock-Widget", out var created);
        if (!created)
        {
            if (args.Contains("--startup")) return 0;
            Directory.CreateDirectory(Path.Combine(root, "data"));
            var signal = args.Contains("--exit") ? "exit" : args.Contains("--popover") ? "show-popover" : "show-details";
            File.WriteAllText(Path.Combine(root, "data", signal), "");
            return 0;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Crash(e.ExceptionObject as Exception ?? new Exception("Unknown unhandled error"));
        TaskScheduler.UnobservedTaskException += (_, e) => { log.Write("error", "app", "task", e.Exception.Message, e.Exception); e.SetObserved(); };
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) =>
            {
                log.Crash(e.Exception);
                MessageBox.Show("发生错误，诊断文件已尝试保存到 " + log.LogFolder + "。", AppName, MessageBoxButton.OK, MessageBoxImage.Error);
                app.Shutdown(1);
                e.Handled = true;
            };
            app.Startup += (_, _) =>
            {
                var controller = new Controller(root, log);
                controller.Start(args.Contains("--details"), args.Contains("--popover"));
            };
            return app.Run();
        }
        catch (Exception ex) { log.Crash(ex); return 1; }
    }
}

internal sealed class Controller
{
    internal List<BatteryState> States { get; } =
    [
        new("keyboard", "ROG 夜魔 X / Azoth X", "夜魔 X"),
        new("mouse", "Razer 毒蝰 V3 Pro", "毒蝰 V3 Pro"),
        new("headset", "Razer 旋风黑鲨 V3 Pro", "黑鲨 V3 Pro")
    ];
    internal Forms.ContextMenuStrip TrayMenu { get; } = new();
    internal bool Exiting { get; private set; }
    readonly string root;
    readonly Diagnostics log;
    readonly CancellationTokenSource stop = new();
    readonly Dictionary<string, int> misses = [];
    readonly Dictionary<string, DateTimeOffset> nextRead = [];
    readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer uiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer clickTimer = new() { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
    DockWindow host = null!;
    Popover popover = null!;
    DetailWindow details = null!;
    Forms.NotifyIcon tray = null!;
    Forms.ToolStripMenuItem dockOption = null!, startupOption = null!;
    UserSettings settings;
    bool refreshing;
    bool anchorFromTray;
    System.Drawing.Rectangle trayAnchor;
    string message = "暂无错误";
    internal Controller(string root, Diagnostics log)
    {
        this.root = root; this.log = log;
        settings = UserSettings.Load(root, log);
    }

    internal void Start(bool showDetails, bool showPopover)
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(log.LogFolder);
        host = new DockWindow(this);
        popover = new Popover(this); details = new DetailWindow(this);
        TrayMenu.Items.Add("查看详情", null, (_, _) => ShowDetails());
        dockOption = new Forms.ToolStripMenuItem("显示悬浮窗") { Checked = settings.ShowDock };
        dockOption.Click += (_, _) => SetDockVisible(!settings.ShowDock);
        TrayMenu.Items.Add(dockOption);
        startupOption = new Forms.ToolStripMenuItem("开机启动");
        startupOption.Click += (_, _) => SetStartupEnabled(!startupOption.Checked);
        TrayMenu.Items.Add(startupOption);
        TrayMenu.Opening += (_, _) => UpdateStartupOption();
        UpdateStartupOption();
        TrayMenu.Items.Add("刷新电量", null, (_, _) => _ = Refresh());
        TrayMenu.Items.Add("打开日志", null, (_, _) => OpenLogs());
        TrayMenu.Items.Add(new Forms.ToolStripSeparator());
        TrayMenu.Items.Add("退出 " + Program.AppName, null, (_, _) => Exit());
        tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(Path.Combine(root, "assets", "peridock-tray.ico")),
            Text = Program.AppName + " · 正在读取电量", Visible = true, ContextMenuStrip = TrayMenu
        };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ScheduleClick(true); };
        tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowDetails(); };
        clickTimer.Tick += (_, _) => { clickTimer.Stop(); TogglePopover(); };
        refreshTimer.Tick += (_, _) => _ = Refresh(false);
        uiTimer.Tick += (_, _) =>
        {
            UpdateViews();
            var signal = Path.Combine(root, "data", "show-details");
            if (File.Exists(signal)) { File.Delete(signal); ShowDetails(); }
            signal = Path.Combine(root, "data", "show-popover");
            if (File.Exists(signal)) { File.Delete(signal); popover.OpenAt(Anchor(), true); }
            signal = Path.Combine(root, "data", "exit");
            if (File.Exists(signal)) { File.Delete(signal); Exit(); }
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        if (settings.ShowDock) host.Show();
        refreshTimer.Start(); uiTimer.Start();
        log.Write("info", "app", "start", Program.AppName + " 0.1.1 started");
        if (showDetails) ShowDetails();
        else if (showPopover) popover.OpenAt(Anchor(), true);
        _ = Refresh();
    }

    internal void SetDockVisible(bool visible)
    {
        try
        {
            var next = settings with { ShowDock = visible };
            next.Save(root);
            settings = next;
            if (visible) { host.Show(); host.PositionAtCorner(); }
            else host.Hide();
            dockOption.Checked = visible;
        }
        catch (Exception ex) { SettingsError("save-settings", ex); }
    }
    void UpdateStartupOption()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(StartupSettings.KeyPath);
            startupOption.Checked = StartupSettings.IsEnabled(key, Path.Combine(root, "PeriDock.exe"));
        }
        catch (Exception ex) { log.Write("error", "app", "read-startup", ex.Message, ex); }
    }
    void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StartupSettings.KeyPath);
            StartupSettings.SetEnabled(key, Path.Combine(root, "PeriDock.exe"), enabled);
            startupOption.Checked = StartupSettings.IsEnabled(key, Path.Combine(root, "PeriDock.exe"));
        }
        catch (Exception ex) { SettingsError("save-startup", ex); }
    }
    void SettingsError(string operation, Exception ex)
    {
        log.Write("error", "app", operation, ex.Message, ex);
        message = "设置保存失败 · 已记录日志";
        tray.ShowBalloonTip(5000, Program.AppName, message, Forms.ToolTipIcon.Error);
        UpdateViews();
    }

    void OnPowerChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            Application.Current.Dispatcher.BeginInvoke(() => { host.PositionAtCorner(); nextRead.Clear(); _ = Refresh(); });
    }
    void OnDisplayChanged(object? sender, EventArgs e)
        => Application.Current.Dispatcher.BeginInvoke(() => host.PositionAtCorner());
    void RememberTrayAnchor()
    {
        var cursor = Forms.Cursor.Position;
        trayAnchor = new System.Drawing.Rectangle(cursor.X - 18, cursor.Y - 18, 36, 36);
    }
    internal void ScheduleClick(bool fromTray = false)
    {
        anchorFromTray = fromTray;
        if (fromTray) RememberTrayAnchor();
        clickTimer.Stop(); clickTimer.Start();
    }
    System.Drawing.Rectangle Anchor()
    {
        if (!anchorFromTray && host.IsVisible) return Visuals.PhysicalBounds(host);
        if (anchorFromTray && !trayAnchor.IsEmpty) return trayAnchor;
        var cursor = Forms.Cursor.Position;
        return new System.Drawing.Rectangle(cursor.X, cursor.Y, 1, 1);
    }
    internal void TogglePopover()
    {
        clickTimer.Stop();
        if (popover.IsVisible && popover.Pinned) { popover.Pinned = false; popover.Hide(); }
        else popover.OpenAt(Anchor(), true);
    }
    internal void ShowDetails()
    {
        clickTimer.Stop(); popover.Pinned = false; popover.Hide();
        details.Show();
        if (details.WindowState == WindowState.Minimized) details.WindowState = WindowState.Normal;
        details.Activate();
    }
    internal async Task Refresh(bool force = true)
    {
        if (refreshing || Exiting) return;
        refreshing = true; UpdateViews();
        var errors = new List<string>();
        try
        {
            var jobs = States.Where(s => force || !nextRead.TryGetValue(s.Id, out var due) || due <= DateTimeOffset.UtcNow)
                .Select(async state =>
                {
                    try
                    {
                        var reading = await DeviceReader.RunIsolated(state.Id, stop.Token);
                        state.Apply(reading, DateTimeOffset.UtcNow);
                        var missed = !state.LastReadSucceeded;
                        misses[state.Id] = missed ? misses.GetValueOrDefault(state.Id) + 1 : 0;
                        if (missed && misses[state.Id] >= 3) nextRead[state.Id] = DateTimeOffset.UtcNow.AddSeconds(120);
                        else nextRead.Remove(state.Id);
                        log.Write("info", state.Id, "read",
                            $"{state.PercentText}; {state.Connection}; {state.Status}; charging={state.Charging?.ToString() ?? "unknown"}; {state.ChargingNote}; {state.DpiText}");
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        var reason = ex is OperationCanceledException ? "设备查询超时" : Diagnostics.Redact(ex.Message);
                        state.Apply(new(null, null, null, null, state.Source, reason), DateTimeOffset.UtcNow);
                        misses[state.Id] = misses.GetValueOrDefault(state.Id) + 1;
                        nextRead[state.Id] = DateTimeOffset.UtcNow.AddSeconds(misses[state.Id] >= 3 ? 120 : 30);
                        errors.Add(state.ShortName);
                        log.Write("error", state.Id, "read", reason, ex);
                    }
                    UpdateViews();
                }).ToArray();
            await Task.WhenAll(jobs);
            if (Exiting) return;
            message = errors.Count > 0 ? "读取异常 · 已记录日志" :
                States.Any(s => !s.LastReadSucceeded) ? "部分设备未响应" :
                States.Any(s => s.Cached) ? "键盘为系统缓存电量" : "暂无错误";
            var path = Path.Combine(root, "data", "status.json");
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(States, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (!Exiting) { log.Write("error", "app", "refresh", ex.Message, ex); message = "更新异常 · 已记录日志"; }
        finally { refreshing = false; if (!Exiting) UpdateViews(); }
    }
    void UpdateViews()
    {
        if (Exiting) return;
        host.Update(States); popover.Update(States, refreshing); details.Update(States, refreshing, message);
        var summary = "键盘 " + States[0].PercentText + " · 鼠标 " + States[1].PercentText + " · 耳机 " + States[2].PercentText;
        tray.Text = summary.Length < 63 ? summary : Program.AppName;
    }
    internal void OpenLogs()
    {
        try { Directory.CreateDirectory(log.LogFolder); Process.Start(new ProcessStartInfo(log.LogFolder) { UseShellExecute = true }); }
        catch (Exception ex) { log.Write("error", "app", "open-logs", ex.Message, ex); }
    }
    internal void Export()
    {
        try
        {
            var zip = log.Export(States);
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + zip + "\"") { UseShellExecute = true });
            message = "诊断包已导出"; UpdateViews();
        }
        catch (Exception ex) { log.Write("error", "app", "export", ex.Message, ex); message = "导出失败 · 已记录日志"; UpdateViews(); }
    }
    void Exit()
    {
        Exiting = true; stop.Cancel();
        refreshTimer.Stop(); uiTimer.Stop(); clickTimer.Stop();
        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        tray.Visible = false; tray.Dispose(); host.Close(); popover.Close(); details.Close(); TrayMenu.Dispose();
        log.Write("info", "app", "exit", Program.AppName + " stopped");
        Application.Current.Shutdown();
    }
}
