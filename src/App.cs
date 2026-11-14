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
    internal const string AppName = "Peripheral Battery Preview";
    [STAThread]
    static int Main(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var log = new Diagnostics(root);
        if (args.Length == 2 && args[0] == "--wait-for-exit")
        {
            if (!Elevation.IsElevated || !Elevation.WaitForParent(args[1])) return 1;
            args = [];
        }
        if (args.Length == 1 && args[0] is "--discover" or "--target-probe")
        {
            try
            {
                if (args[0] == "--discover") Console.WriteLine(JsonSerializer.Serialize(DeviceReader.Discover().GetAwaiter().GetResult()));
                else
                {
                    var target = JsonSerializer.Deserialize<DeviceTarget>(Console.In.ReadToEnd()) ?? throw new ArgumentException("Missing device");
                    Console.WriteLine(JsonSerializer.Serialize(DeviceReader.ProbeTarget(target).GetAwaiter().GetResult()));
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(Diagnostics.Redact(ex.GetType().Name + ": " + ex.Message)); return 1; }
        }
        if (args.Length == 2 && args[0] == "--probe")
        {
            try { Console.WriteLine(JsonSerializer.Serialize(DeviceReader.Probe(args[1]).GetAwaiter().GetResult())); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(Diagnostics.Redact(ex.GetType().Name + ": " + ex.Message)); return 1; }
        }
        using var instance = new Mutex(true, @"Local\PeriDock-Compatibility-Preview-" + DeviceCatalog.Key(root), out var created);
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
    internal List<BatteryState> States { get; } = [];
    readonly Dictionary<string, DeviceTarget> targets = [];
    internal Forms.ContextMenuStrip TrayMenu { get; } = new();
    internal bool Exiting { get; private set; }
    readonly string root;
    readonly Diagnostics log;
    readonly OfficialProductImages productImages;
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
    Forms.ToolStripMenuItem dockOption = null!, startupOption = null!, elevationOption = null!;
    UserSettings settings;
    bool refreshing;
    bool anchorFromTray;
    System.Drawing.Rectangle trayAnchor;
    string message = "暂无错误";
    internal Controller(string root, Diagnostics log)
    {
        this.root = root; this.log = log;
        productImages = new(root, log);
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
        // The preview must never replace the stable installation's startup registration.
        startupOption.Enabled = false;
        TrayMenu.Opening += (_, _) => UpdateStartupOption();
        UpdateStartupOption();
        TrayMenu.Items.Add("刷新电量", null, (_, _) => _ = Refresh());
        var appearances = new Forms.ToolStripMenuItem("设备图片与颜色");
        TrayMenu.Opening += (_, _) =>
        {
            appearances.DropDownItems.Clear(); appearances.Enabled = States.Count > 0;
            foreach (var state in States) appearances.DropDownItems.Add(state.Name, null, (_, _) => ChooseAppearance(state));
        };
        TrayMenu.Items.Add(appearances);
        TrayMenu.Items.Add("打开日志", null, (_, _) => OpenLogs());
        elevationOption = new Forms.ToolStripMenuItem("以管理员权限重启…") { Enabled = false };
        elevationOption.Click += (_, _) => RestartElevated();
        TrayMenu.Items.Add(elevationOption);
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
        log.Write("info", "app", "start", Program.AppName + " 0.2.0-preview.4 started");
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
            var discovered = await DeviceReader.DiscoverIsolated(stop.Token);
            if (Exiting) return;
            foreach (var target in discovered)
            {
                targets[target.Id] = target;
                if (States.All(s => s.Id != target.Id))
                {
                    States.Add(new(target.Id, target.Name, target.Name, target.Kind));
                    log.Write("info", target.Id, "discover", target.Name + "; " + target.Kind + "; " + target.HardwareId +
                        "; " + (target.Supported ? "adapter available; hardware verification required" : "battery protocol unavailable"));
                }
            }
            var removed = States.Where(s => discovered.All(t => t.Id != s.Id)).ToArray();
            foreach (var state in removed)
            {
                States.Remove(state); targets.Remove(state.Id); misses.Remove(state.Id); nextRead.Remove(state.Id);
            }
            States.Sort((a, b) =>
            {
                int Order(BatteryState value) => value.Kind == "keyboard" ? 0 : value.Kind == "mouse" ? 1 : 2;
                var order = Order(a).CompareTo(Order(b));
                return order == 0 ? string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) : order;
            });
            UpdateViews();
            foreach (var state in States.Where(s => s.ProductImagePath is null)) _ = LoadProductImage(state);
            using var probeSlots = new SemaphoreSlim(3);
            var jobs = States.Where(s => force || !nextRead.TryGetValue(s.Id, out var due) || due <= DateTimeOffset.UtcNow)
                .Select(async state =>
                {
                    var acquired = false;
                    try
                    {
                        await probeSlots.WaitAsync(stop.Token);
                        acquired = true;
                        var target = targets[state.Id];
                        var reading = await DeviceReader.RunTargetIsolated(target, stop.Token);
                        state.Apply(reading, DateTimeOffset.UtcNow);
                        var missed = !state.LastReadSucceeded;
                        misses[state.Id] = missed ? misses.GetValueOrDefault(state.Id) + 1 : 0;
                        if (missed && misses[state.Id] >= 3) nextRead[state.Id] = DateTimeOffset.UtcNow.AddSeconds(120);
                        else nextRead.Remove(state.Id);
                        log.Write(reading.Failure == ReadFailure.AccessDenied ? "error" : "info", state.Id, "read",
                            $"{target.HardwareId}; Win32={reading.ErrorCode}; {state.PercentText}; {state.Connection}; {state.Status}; charging={state.Charging?.ToString() ?? "unknown"}; {state.ChargingNote}; {state.DpiText}");
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        var reason = ex is OperationCanceledException ? "设备查询超时" : Diagnostics.Redact(ex.Message);
                        state.Apply(new(null, null, null, null, state.Source, reason,
                            Connection: state.Connection == "识别中" ? targets[state.Id].Connection : state.Connection,
                            Failure: ReadFailure.ReadError), DateTimeOffset.UtcNow);
                        misses[state.Id] = misses.GetValueOrDefault(state.Id) + 1;
                        nextRead[state.Id] = DateTimeOffset.UtcNow.AddSeconds(misses[state.Id] >= 3 ? 120 : 30);
                        errors.Add(state.ShortName);
                        log.Write("error", state.Id, "read", reason, ex);
                    }
                    finally { if (acquired) probeSlots.Release(); }
                    UpdateViews();
                }).ToArray();
            await Task.WhenAll(jobs);
            if (Exiting) return;
            message = States.Count == 0 ? "未发现 ROG / Razer 外设" : errors.Count > 0 ? "读取异常 · 已记录日志" :
                States.Any(s => s.Failure == ReadFailure.AccessDenied) ?
                    (Elevation.IsElevated ? "管理员权限下仍被拒绝 · 请检查驱动或设备接口" : "访问被拒绝 · 可选择管理员重启") :
                States.Any(s => !s.LastReadSucceeded) ? "部分设备未响应" :
                States.Any(s => s.Cached) ? "部分电量为系统缓存" : "暂无错误";
            var path = Path.Combine(root, "data", "status.json");
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(States, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (!Exiting)
        {
            foreach (var state in States)
                state.Apply(new(null, null, null, null, state.Source, "设备发现失败；本次读数无效",
                    Connection: state.Connection, Failure: ReadFailure.ReadError), DateTimeOffset.UtcNow);
            log.Write("error", "app", "refresh", ex.Message, ex); message = "更新异常 · 已记录日志";
        }
        finally { refreshing = false; if (!Exiting) UpdateViews(); }
    }
    async Task LoadProductImage(BatteryState state)
    {
        var selected = settings.ImageChoices?.GetValueOrDefault(OfficialProductImages.FileKey(state.Name)) ?? "auto";
        try
        {
            var custom = OfficialProductImages.CustomPath(root, state.Name);
            var file = selected == "custom" && File.Exists(custom) ? custom :
                selected == "auto" ? await productImages.GetAsync(state.Name, stop.Token) : selected == "generic" ? null :
                (await productImages.GetOptionsAsync(state.Name, stop.Token)).FirstOrDefault(o => o.Id == selected)?.FilePath ??
                    await productImages.GetAsync(state.Name, stop.Token);
            if (!Exiting && file is not null && state.ProductImagePath != file &&
                selected == (settings.ImageChoices?.GetValueOrDefault(OfficialProductImages.FileKey(state.Name)) ?? "auto"))
            {
                state.ProductImagePath = file;
                UpdateViews();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    internal void ChooseAppearance(BatteryState state)
    {
        var key = OfficialProductImages.FileKey(state.Name);
        var window = new AppearanceWindow(state, settings.ImageChoices?.GetValueOrDefault(key) ?? "auto",
            () => productImages.GetOptionsAsync(state.Name, stop.Token), choice =>
            {
                try
                {
                    var selections = new Dictionary<string, string>(settings.ImageChoices ?? []);
                    selections[key] = choice.Id;
                    var next = settings with { ImageChoices = selections }; next.Save(root); settings = next;
                    foreach (var device in States.Where(s => OfficialProductImages.FileKey(s.Name) == key)) device.ProductImagePath = choice.FilePath;
                    UpdateViews(); return true;
                }
                catch (Exception ex) { SettingsError("save-image-choice", ex); return false; }
            }, file => productImages.ImportAsync(state.Name, file, stop.Token));
        window.Show();
    }
    void UpdateViews()
    {
        if (Exiting) return;
        elevationOption.Enabled = !Elevation.IsElevated && States.Any(s => s.Failure == ReadFailure.AccessDenied);
        host.Update(States); popover.Update(States, refreshing); details.Update(States, refreshing, message);
        var summary = string.Join(" · ", States.Select(s => s.TrayText));
        tray.Text = summary.Length == 0 ? Program.AppName + " · 未发现外设" : summary.Length < 63 ? summary : summary[..60] + "…";
    }
    internal void OpenLogs()
    {
        try { Directory.CreateDirectory(log.LogFolder); Process.Start(new ProcessStartInfo(log.LogFolder) { UseShellExecute = true }); }
        catch (Exception ex) { log.Write("error", "app", "open-logs", ex.Message, ex); }
    }
    void RestartElevated()
    {
        if (Elevation.IsElevated || !States.Any(s => s.Failure == ReadFailure.AccessDenied)) return;
        try
        {
            if (Elevation.Start(Environment.ProcessPath!, Environment.ProcessId)) Exit();
        }
        catch (Exception ex)
        {
            log.Write("error", "app", "restart-elevated", ex.Message, ex);
            message = "管理员重启失败 · 当前实例继续运行";
            UpdateViews();
        }
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
