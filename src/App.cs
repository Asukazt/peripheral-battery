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
    internal const string Version = "0.3.1";
    [STAThread]
    static int Main(string[] args)
    {
        var root = AppContext.BaseDirectory;
        var log = new Diagnostics(root);
        if (args.Length == 1 && args[0] is "--discover" or "--discover-known" or "--target-probe")
        {
            try
            {
                if (args[0] is "--discover" or "--discover-known")
                {
                    var known = args[0] == "--discover-known"
                        ? JsonSerializer.Deserialize<DeviceTarget[]>(Console.In.ReadToEnd())
                        : null;
                    Console.WriteLine(JsonSerializer.Serialize(DeviceReader.Discover(known).GetAwaiter().GetResult()));
                }
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
        if (args.Length >= 2 && args[0] == "--anc")
        {
            try
            {
                var result = args[1] == "set" && args.Length == 4
                    ? DeviceReader.SetHeadsetAnc(int.Parse(args[2]), int.Parse(args[3])).GetAwaiter().GetResult()
                    : DeviceReader.ReadHeadsetAnc().GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result));
                return result.Success ? 0 : 1;
            }
            catch (Exception ex) { Console.Error.WriteLine(Diagnostics.Redact(ex.GetType().Name + ": " + ex.Message)); return 1; }
        }
        if (args.Length == 3 && args[0] == "--dpi" && args[1] == "set")
        {
            try
            {
                var dpi = int.Parse(args[2]);
                var target = DeviceReader.Discover().GetAwaiter().GetResult()
                    .FirstOrDefault(t => t.Kind == "mouse" && t.Profile?.Reader == "RazerFeature")
                    ?? throw new IOException("未发现可调 DPI 的鼠标");
                var value = DeviceReader.SetMouseDpi(target, dpi, dpi);
                Console.WriteLine(JsonSerializer.Serialize(new { value.X, value.Y }));
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(Diagnostics.Redact(ex.GetType().Name + ": " + ex.Message)); return 1; }
        }
        if (args.Length == 1 && args[0] == "--audio")
        {
            try
            {
                var items = new[] { AudioFlow.Render, AudioFlow.Capture }
                    .Select(flow => new
                    {
                        Flow = flow.ToString(),
                        Devices = AudioControl.List(flow).Select(d => new
                        {
                            d.Id, d.Name, d.IsDefault,
                            Volume = AudioControl.Volume(d.Id, flow),
                            Muted = AudioControl.Muted(d.Id, flow)
                        })
                    });
                Console.WriteLine(JsonSerializer.Serialize(items));
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(Diagnostics.Redact(ex.GetType().Name + ": " + ex.Message)); return 1; }
        }
        if (args.Length == 2 && args[0] == "--audio-default")
        {
            try
            {
                var flow = args[1] == "capture" ? AudioFlow.Capture : AudioFlow.Render;
                var endpoint = AudioControl.PickHeadset(flow) ?? throw new IOException("未发现音频设备");
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    endpoint.Id, endpoint.Name,
                    Ok = AudioControl.SetDefault(endpoint.Id, flow)
                }));
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(Diagnostics.Redact(ex.GetType().Name + ": " + ex.Message)); return 1; }
        }
        if (args.Length == 4 && args[0] == "--audio-set")
        {
            try
            {
                var flow = args[1] == "capture" ? AudioFlow.Capture : AudioFlow.Render;
                var endpoint = AudioControl.PickHeadset(flow) ?? throw new IOException("未发现音频设备");
                var volume = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var muted = args[3] == "1";
                var ok = AudioControl.SetVolume(endpoint.Id, flow, volume) && AudioControl.SetMuted(endpoint.Id, flow, muted);
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    endpoint.Id, endpoint.Name, Ok = ok,
                    Volume = AudioControl.Volume(endpoint.Id, flow),
                    Muted = AudioControl.Muted(endpoint.Id, flow),
                    HResult = AudioControl.LastHResult
                }));
                return ok ? 0 : 1;
            }
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
                MessageBox.Show("发生错误，诊断文件已尝试保存到程序目录的 logs 文件夹。", AppName, MessageBoxButton.OK, MessageBoxImage.Error);
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
    readonly BatteryUsageStore usage;
    readonly CancellationTokenSource stop = new();
    readonly Dictionary<string, int> misses = [];
    readonly Dictionary<string, DateTimeOffset> nextRead = [];
    readonly HashSet<string> lowNotified = [];
    readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly DispatcherTimer uiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer clickTimer = new() { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
    DockWindow host = null!;
    Popover popover = null!;
    DetailWindow details = null!;
    SettingsWindow? controls;
    BatteryStatsWindow? usageWindow;
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
        productImages = new OfficialProductImages(root, log);
        usage = new BatteryUsageStore(root, log);
        HasHeadsetAncDevice = DeviceReader.HasHeadsetAncDevice();
        ApplyThreshold();
    }

    internal void Start(bool showDetails, bool showPopover)
    {
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(log.LogFolder);
        host = new DockWindow(this);
        popover = new Popover(this); details = new DetailWindow(this);
        TrayMenu.Items.Add("查看详情", null, (_, _) => ShowDetails());
        TrayMenu.Items.Add("电池使用统计", null, (_, _) => ShowBatteryUsage());
        TrayMenu.Items.Add("外设设置", null, (_, _) => ShowSettings());
        dockOption = new Forms.ToolStripMenuItem("显示悬浮窗") { Checked = settings.ShowDock };
        dockOption.Click += (_, _) => SetDockVisible(!settings.ShowDock);
        TrayMenu.Items.Add(dockOption);
        startupOption = new Forms.ToolStripMenuItem("开机启动");
        startupOption.Click += (_, _) => SetStartupEnabled(!startupOption.Checked);
        TrayMenu.Items.Add(startupOption);
        TrayMenu.Opening += (_, _) => UpdateStartupOption();
        UpdateStartupOption();
        TrayMenu.Items.Add("刷新电量", null, (_, _) => _ = Refresh());
        var appearances = new Forms.ToolStripMenuItem("设备图片与颜色");
        TrayMenu.Opening += (_, _) =>
        {
            appearances.DropDownItems.Clear();
            appearances.Enabled = States.Count > 0;
            foreach (var state in States)
            {
                var captured = state;
                appearances.DropDownItems.Add(captured.Name, null, (_, _) => ChooseAppearance(captured));
            }
        };
        TrayMenu.Items.Add(appearances);
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
        log.Write("info", "app", "start", Program.AppName + " " + Program.Version + " started");
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
    internal void ShowSettings()
    {
        clickTimer.Stop(); popover.Pinned = false; popover.Hide();
        HasHeadsetAncDevice = DeviceReader.HasHeadsetAncDevice();
        if (controls is null) controls = new SettingsWindow(this);
        else controls.ReloadAudio();
        controls.Show();
        if (controls.WindowState == WindowState.Minimized) controls.WindowState = WindowState.Normal;
        controls.Activate();
        _ = Refresh(false);
        _ = RefreshHeadsetAnc();
    }
    internal void ShowBatteryUsage()
    {
        clickTimer.Stop(); popover.Pinned = false; popover.Hide();
        if (usageWindow is null) usageWindow = new BatteryStatsWindow(this);
        else usageWindow.Update();
        usageWindow.Show();
        if (usageWindow.WindowState == WindowState.Minimized) usageWindow.WindowState = WindowState.Normal;
        usageWindow.Activate();
        _ = Refresh(false);
    }
    internal UserSettings Settings => settings;
    internal BatteryUsageStore Usage => usage;
    internal DeviceReader.AncReading? HeadsetAnc { get; private set; }
    internal bool HasHeadsetAncDevice { get; private set; }
    internal DeviceTarget? DpiTarget { get; set; }
    internal bool HasAnyMouse => targets.Values.Any(t => t.Kind == "mouse");
    internal void UpdateSettings(UserSettings next)
    {
        try
        {
            next.Save(root);
            settings = next;
            ApplyThreshold();
            lowNotified.Clear();
            message = "设置已保存";
            // The next refresh will alert immediately if a device is already below the new threshold.
            _ = Refresh(false);
            UpdateViews();
        }
        catch (Exception ex) { SettingsError("save-settings", ex); }
    }
    void ApplyThreshold()
    {
        foreach (var state in States.Where(s => s.Supported))
            state.LowThreshold = settings.ThresholdFor(state.Id, state.Kind);
    }
    internal async Task RefreshHeadsetAnc()
    {
        HasHeadsetAncDevice = DeviceReader.HasHeadsetAncDevice();
        try
        {
            HeadsetAnc = await DeviceReader.ReadHeadsetAnc();
            log.Write(HeadsetAnc.Success ? "info" : "error", "headset", "anc-read",
                HeadsetAnc.Success ? $"ANC={HeadsetAnc.Status}; level={HeadsetAnc.Level}" : HeadsetAnc.Message);
        }
        catch (Exception ex) { HeadsetAnc = new(false, 0, 4, Diagnostics.Redact(ex.Message)); }
        UpdateViews();
    }
    internal async Task<bool> SetHeadsetAnc(int status, int level)
    {
        try
        {
            var result = await DeviceReader.SetHeadsetAnc(status, level);
            HeadsetAnc = result;
            log.Write(result.Success ? "info" : "error", "headset", "anc-set",
                result.Success ? $"ANC={result.Status}; level={result.Level}" : result.Message);
            UpdateViews();
            return result.Success && result.Status == status && result.Level == level;
        }
        catch (Exception ex)
        {
            log.Write("error", "headset", "anc-set", ex.Message, ex);
            HeadsetAnc = new(false, 0, 4, Diagnostics.Redact(ex.Message));
            UpdateViews();
            return false;
        }
    }
    internal async Task<(bool Ok, string Message)> SetMouseDpi(int dpi)
    {
        try
        {
            var target = DpiTarget ?? throw new IOException("未发现可调 DPI 的鼠标");
            var value = await Task.Run(() => DeviceReader.SetMouseDpi(target, dpi, dpi));
            var mouse = States.First(s => s.Id == target.Id);
            mouse.ApplyDpi(value.X, value.Y);
            log.Write("info", "mouse", "dpi-set", $"{value.X} × {value.Y} DPI");
            message = "DPI 已生效";
            UpdateViews();
            return (true, $"DPI 已设置为 {value.X}");
        }
        catch (Exception ex)
        {
            log.Write("error", "mouse", "dpi-set", ex.Message, ex);
            message = "DPI 设置失败";
            UpdateViews();
            return (false, "DPI 设置失败：" + Diagnostics.Redact(ex.Message));
        }
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
                    var state = new BatteryState(target.Id, target.Name, target.Name, target.Kind)
                    {
                        LowThreshold = settings.ThresholdFor(target.Id, target.Kind),
                        Supported = target.Supported
                    };
                    States.Add(state);
                    log.Write("info", target.Id, "discover", target.Name + "; " + target.Kind + "; " + target.HardwareId +
                        "; " + (target.Supported ? "protocol available" : "battery protocol unavailable"));
                }
            }
            var removed = States.Where(s => discovered.All(t => t.Id != s.Id)).ToArray();
            foreach (var state in removed)
            {
                States.Remove(state); targets.Remove(state.Id);
                misses.Remove(state.Id); nextRead.Remove(state.Id); lowNotified.Remove(state.Id);
            }
            States.Sort((a, b) =>
            {
                int Order(BatteryState value) => value.Kind == "keyboard" ? 0 : value.Kind == "mouse" ? 1 : 2;
                var order = Order(a).CompareTo(Order(b));
                return order == 0 ? string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) : order;
            });
            HasHeadsetAncDevice = DeviceReader.HasHeadsetAncDevice();
            DpiTarget = discovered.FirstOrDefault(t => t.Kind == "mouse" && t.Reader == "RazerFeature");
            ApplyThreshold();
            UpdateViews();
            foreach (var state in States.Where(s => s.ProductImagePath is null))
                _ = LoadProductImage(state);
            using var slots = new SemaphoreSlim(3);
            var jobs = States.Where(s => force || !nextRead.TryGetValue(s.Id, out var due) || due <= DateTimeOffset.UtcNow)
                .Select(async state =>
                {
                    var acquired = false;
                    try
                    {
                        await slots.WaitAsync(stop.Token);
                        acquired = true;
                        var target = targets[state.Id];
                        var reading = target.Supported ? await DeviceReader.RunTargetIsolated(target, stop.Token) :
                            new Reading(null, null, null, null, "HID " + target.HardwareId,
                                "已识别型号；电量协议尚未适配", ChargingNote: "未向未知型号发送查询");
                        var now = DateTimeOffset.UtcNow;
                        state.Apply(reading, now);
                        usage.Record(state, now);
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
                    finally { if (acquired) slots.Release(); }
                    UpdateViews();
                }).ToArray();
            await Task.WhenAll(jobs);
            if (Exiting) return;
            message = States.Count == 0 ? "未发现已适配外设" : errors.Count > 0 ? "读取异常 · 已记录日志" :
                States.Any(s => !s.LastReadSucceeded) ? "部分设备未响应" :
                States.Any(s => s.Cached) ? "部分电量为系统缓存" : "暂无错误";
            var path = Path.Combine(root, "data", "status.json");
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(States, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
            CheckLowBattery();
        }
        catch (Exception ex) when (!Exiting) { log.Write("error", "app", "refresh", ex.Message, ex); message = "更新异常 · 已记录日志"; }
        finally { refreshing = false; if (!Exiting) UpdateViews(); }
    }
    async Task LoadProductImage(BatteryState state)
    {
        var key = OfficialProductImages.FileKey(state.Name);
        var selected = settings.ImageChoices.GetValueOrDefault(key) ?? "auto";
        if (selected == "generic")
        {
            if (!state.GenericImage) { state.GenericImage = true; UpdateViews(); }
            return;
        }
        try
        {
            string? path;
            var custom = OfficialProductImages.CustomPath(root, state.Name);
            if (selected == "custom" && File.Exists(custom)) path = custom;
            else if (selected == "generic") path = null;
            else if (selected == "auto") path = await productImages.GetAsync(state.Name, stop.Token);
            else
            {
                var option = (await productImages.GetOptionsAsync(state.Name, stop.Token))
                    .FirstOrDefault(o => o.Id == selected);
                path = option?.FilePath ?? await productImages.GetAsync(state.Name, stop.Token);
            }
            if (!Exiting && path is not null && state.ProductImagePath != path &&
                selected == (settings.ImageChoices.GetValueOrDefault(key) ?? "auto"))
            {
                state.ProductImagePath = path;
                state.GenericImage = false;
                UpdateViews();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    internal void ChooseAppearance(BatteryState state)
    {
        var key = OfficialProductImages.FileKey(state.Name);
        new AppearanceWindow(state, settings.ImageChoices.GetValueOrDefault(key) ?? "auto",
            () => productImages.GetOptionsAsync(state.Name, stop.Token),
            choice =>
            {
                try
                {
                    settings = settings with
                    {
                        ImageChoices = new(settings.ImageChoices) { [key] = choice.Id }
                    };
                    settings.Save(root);
                    foreach (var item in States.Where(s => OfficialProductImages.FileKey(s.Name) == key))
                    {
                        item.ProductImagePath = choice.FilePath;
                        item.GenericImage = choice.Id == "generic";
                    }
                    UpdateViews();
                    return true;
                }
                catch (Exception ex)
                {
                    SettingsError("save-image-choice", ex);
                    return false;
                }
            },
            file => productImages.ImportAsync(state.Name, file, stop.Token)).Show();
    }
    void UpdateViews()
    {
        if (Exiting) return;
        host.Update(States); popover.Update(States, refreshing); details.Update(States, refreshing, message);
        usageWindow?.Update();
        controls?.Update();
        var summary = string.Join(" · ", States.GroupBy(s => s.Kind).Select(g =>
            g.Key switch { "keyboard" => "键盘", "mouse" => "鼠标", "headset" => "耳机", _ => "设备" }
            + "：" + string.Join("/", g.Select(s => s.PercentText))));
        tray.Text = summary.Length == 0 ? Program.AppName + " · 未发现外设" :
            summary.Length < 63 ? summary : summary[..60] + "…";
    }
    void CheckLowBattery()
    {
        if (!settings.LowBatteryAlert) return;
        foreach (var state in States)
        {
            var low = state.LastReadSucceeded && !state.IsStale(DateTimeOffset.UtcNow) &&
                state.Connected != false && state.Charging != true && state.IsLow;
            if (!low) { lowNotified.Remove(state.Id); continue; }
            if (!lowNotified.Add(state.Id)) continue;
            tray.ShowBalloonTip(6000, Program.AppName,
                $"{state.ShortName} 电量低于 {state.LowThreshold}%：{state.PercentText}",
                Forms.ToolTipIcon.Warning);
        }
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
        tray.Visible = false; tray.Dispose(); host.Close(); popover.Close(); details.Close();
        controls?.Close(); usageWindow?.Close(); TrayMenu.Dispose();
        log.Write("info", "app", "exit", Program.AppName + " stopped");
        Application.Current.Shutdown();
    }
}
