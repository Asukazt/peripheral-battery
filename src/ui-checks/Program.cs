using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PeriDock;

internal static class UiChecks
{
    [STAThread]
    static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? Path.Combine(AppContext.BaseDirectory, "renders"));
        Directory.CreateDirectory(output);
        var controller = new Controller(AppContext.BaseDirectory, new Diagnostics(AppContext.BaseDirectory));
        using var snapshot = JsonDocument.Parse(File.ReadAllText(args.FirstOrDefault() ??
            Path.Combine(AppContext.BaseDirectory, "status.sample.json")));
        foreach (var value in snapshot.RootElement.EnumerateArray())
        {
            var id = value.GetProperty("Id").GetString()!;
            var name = value.TryGetProperty("Name", out var label) ? label.GetString()! : id;
            var kind = value.TryGetProperty("Kind", out var category) ? category.GetString()! : id;
            var state = new BatteryState(id, name, name, kind);
            controller.States.Add(state);
            int? Number(string key) => value.GetProperty(key).ValueKind == JsonValueKind.Null ? null : value.GetProperty(key).GetInt32();
            var charging = value.GetProperty("Charging");
            state.Apply(new Reading(Number("Percent"), charging.ValueKind == JsonValueKind.Null ? null : charging.GetBoolean(),
                Number("DpiX"), Number("DpiY"), value.GetProperty("Source").GetString()!,
                value.GetProperty("Status").GetString()!,
                value.TryGetProperty("Cached", out var cached) && cached.GetBoolean(),
                value.TryGetProperty("Connection", out var connection) ? connection.GetString()! : "待确认",
                value.TryGetProperty("Connected", out var connected) && connected.ValueKind != JsonValueKind.Null ? connected.GetBoolean() : null,
                value.TryGetProperty("ChargingNote", out var note) ? note.GetString() : null,
                value.TryGetProperty("Failure", out var failure) ? (ReadFailure)failure.GetInt32() : ReadFailure.None,
                value.TryGetProperty("ErrorCode", out var error) && error.ValueKind != JsonValueKind.Null ? error.GetInt32() : null), DateTimeOffset.Now);
        }
        var windows = new List<(string Name, Window Window)>();
        var failures = new List<string>();
        void AddWindows(string suffix)
        {
            var details = new DetailWindow(controller);
            details.Update(controller.States, false, controller.States.Any(s => s.Failure == ReadFailure.AccessDenied)
                ? "访问被拒绝 · 可选择管理员重启" : "暂无错误");
            windows.Add(("PeriDock-details" + suffix, details));
            var popover = new Popover(controller);
            popover.Update(controller.States, false);
            windows.Add(("PeriDock-popover" + suffix, popover));
            var dock = new DockWindow(controller);
            dock.Update(controller.States);
            windows.Add(("PeriDock-dock" + suffix, dock));
            // Rebuild each existing surface after additions, removals and an empty list.
            var transitions = controller.States.ToArray();
            controller.States.Clear();
            details.Update(controller.States, false, "暂无错误"); popover.Update(controller.States, false); dock.Update(controller.States);
            controller.States.AddRange(transitions);
            details.Update(controller.States, false, "暂无错误"); popover.Update(controller.States, false); dock.Update(controller.States);
        }
        AddWindows("");
        foreach (var (_, window) in windows)
        {
            var root = (FrameworkElement)window.Content;
            void Update()
            {
                if (window is DetailWindow details) details.Update(controller.States, false, "暂无错误");
                if (window is Popover popover) popover.Update(controller.States, false);
                if (window is DockWindow dock) dock.Update(controller.States);
                root.Measure(new Size(window.Width, window.Height));
                root.Arrange(new Rect(0, 0, window.Width, window.Height)); root.UpdateLayout();
            }
            Image[] Products() => Descendants(root).OfType<Image>().Where(i =>
                System.Windows.Automation.AutomationProperties.GetName(i).EndsWith(" 产品图")).ToArray();
            Update(); var before = Products();
            var photo = Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png");
            controller.States[1].ProductImagePath = photo; Update(); var after = Products();
            if (before.Length != 3 || !before.SequenceEqual(after) || after[1].Source is not BitmapImage ||
                ReferenceEquals(after[0].Source, after[1].Source)) failures.Add("product photo did not update the existing " + window.GetType().Name);
            controller.States[1].ProductImagePath = null; Update();
            if (Products()[1].Source is BitmapImage) failures.Add("generic image choice did not clear the photo");
        }
        var appearanceRoot = Path.Combine(Path.GetTempPath(), "PeriDock-appearance-" + Guid.NewGuid().ToString("N"));
        var imageService = new OfficialProductImages(appearanceRoot, new Diagnostics(appearanceRoot));
        var white = new ProductImageOption("white", "白色", Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png"));
        if (args.Length > 2)
        {
            var folder = Path.Combine(Path.GetFullPath(args[2]), "data", "device-images");
            var key = OfficialProductImages.FileKey("Razer Basilisk V3 Pro");
            var variants = JsonSerializer.Deserialize<OfficialImageVariant[]>(File.ReadAllText(Path.Combine(folder, key + ".options.json")))!;
            var variant = variants.First(v => v.Label == "白色");
            white = new(variant.Id, variant.Label, Path.Combine(folder, key + "-" + variant.Id + ".png"));
        }
        var appearance = new AppearanceWindow(new("appearance", "Razer Basilisk V3 Pro", "Basilisk", "mouse"), "auto",
            () => System.Threading.Tasks.Task.FromResult<IReadOnlyList<ProductImageOption>>([
                new("auto", "自动", null), white, new("generic", "通用图标", null)]), value =>
            {
                new UserSettings(true, new() { ["test"] = value.Id }).Save(appearanceRoot); return true;
            }, file => imageService.ImportAsync("Razer Basilisk V3 Pro", file));
        appearance.Show(); appearance.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var whiteButton = Descendants((FrameworkElement)appearance.Content).OfType<Button>().Single(b =>
            System.Windows.Automation.AutomationProperties.GetName(b) == "选择白色");
        whiteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (UserSettings.Load(appearanceRoot, new Diagnostics(appearanceRoot)).ImageChoices?["test"] != white.Id ||
            !Descendants((FrameworkElement)appearance.Content).OfType<TextBlock>().Any(t => t.Text.Contains("已选择：白色，已保存")))
            failures.Add("appearance selection did not save and confirm white");
        appearance.Hide();
        windows.Add(("PeriDock-appearance-white", appearance));
        var pending = new System.Threading.Tasks.TaskCompletionSource<IReadOnlyList<ProductImageOption>>();
        var custom = new AppearanceWindow(new("custom", "Unknown model", "Custom", "mouse"), "auto",
            () => pending.Task, value =>
            {
                new UserSettings(true, new() { ["test"] = value.Id }).Save(appearanceRoot); return true;
            }, file => imageService.ImportAsync("Unknown model", file));
        custom.Show(); custom.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!Descendants((FrameworkElement)custom.Content).OfType<Button>().Any(b =>
            System.Windows.Automation.AutomationProperties.GetName(b) == "导入自定义图片…")) failures.Add("custom import entry missing while official images are loading");
        void Await(System.Threading.Tasks.Task task)
        {
            while (!task.IsCompleted) custom.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            task.GetAwaiter().GetResult();
        }
        var sourceFile = Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png");
        Await(custom.ImportFile(sourceFile));
        pending.SetResult([new("auto", "自动", null), new("generic", "通用图标", null)]);
        custom.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (UserSettings.Load(appearanceRoot, new Diagnostics(appearanceRoot)).ImageChoices?["test"] != "custom" ||
            !Descendants((FrameworkElement)custom.Content).OfType<TextBlock>().Any(t => t.Text == "已选择：自定义图片，已保存。"))
            failures.Add("custom import was lost when official image loading finished");
        var customFile = OfficialProductImages.CustomPath(appearanceRoot, "Unknown model");
        var customState = new BatteryState("custom", "Unknown model", "Custom", "mouse") { ProductImagePath = customFile };
        var previewImage = Visuals.Product(customState, 80, 80); var originalSource = previewImage.Source;
        Await(custom.ImportFile(Path.Combine(AppContext.BaseDirectory, "assets", "mouse-display.png")));
        File.SetLastWriteTimeUtc(customFile, DateTime.UtcNow.AddSeconds(1));
        Visuals.UpdateProduct(previewImage, customState);
        if (ReferenceEquals(originalSource, previewImage.Source)) failures.Add("replacement custom image stayed stale in image cache");
        Await(custom.ImportFile(Path.Combine(appearanceRoot, "missing.png")));
        if (UserSettings.Load(appearanceRoot, new Diagnostics(appearanceRoot)).ImageChoices?["test"] != "custom" ||
            !Descendants((FrameworkElement)custom.Content).OfType<TextBlock>().Any(t => t.Text.StartsWith("导入失败"))) failures.Add("failed import cleared the previous choice or lacked feedback");
        custom.Hide(); windows.Add(("PeriDock-appearance-custom", custom));
        controller.States[0].Apply(new Reading(40, true, null, null, "ROG HID", "已连接",
            Connection: "蓝牙", Connected: true), DateTimeOffset.Now);
        controller.States[1].Apply(new Reading(50, false, 1600, 1600, "Razer HID", "已连接",
            Connection: "USB", Connected: true), DateTimeOffset.Now);
        controller.States[2].Apply(new Reading(60, true, null, null, "BlackShark HID", "多路连接",
            Connection: "USB + 2.4G + 蓝牙", Connected: true), DateTimeOffset.Now);
        AddWindows("-multi-test");
        foreach (var state in controller.States)
            state.Apply(new Reading(100, false, 1600, 1600, state.Source, "已连接",
                Connection: state.Connection, Connected: true), DateTimeOffset.Now);
        AddWindows("-full-test");
        foreach (var state in controller.States)
            state.Apply(new(null, null, null, null, state.Source, "已识别设备；访问被拒绝（Windows 错误 5）",
                Connection: state.Kind == "keyboard" ? "蓝牙" : state.Kind == "mouse" ? "USB" : "2.4G",
                ChargingNote: "可在设置菜单选择以管理员权限重启；拒绝访问不等于厂商软件占用",
                Failure: ReadFailure.AccessDenied, ErrorCode: 5), DateTimeOffset.Now);
        if (controller.States.Any(s => s.PercentText != "--" || Visuals.StateText(s) != "访问被拒绝" || s.Connected == false))
            failures.Add("permission failure exposes stale battery or claims disconnection");
        AddWindows("-denied-test");
        foreach (var state in controller.States)
            state.Apply(new(null, null, null, null, state.Source, "已识别型号；电量协议尚未适配",
                Connection: state.Connection, Failure: ReadFailure.ProtocolUnsupported), DateTimeOffset.Now);
        if (controller.States.Any(s => Visuals.StateText(s) != "协议未适配")) failures.Add("unsupported protocols are not identified");
        AddWindows("-unsupported-test");
        foreach (var state in controller.States)
            state.Apply(new Reading(null, null, null, null, state.Source, "未连接",
                Connection: "未连接", Connected: false), DateTimeOffset.Now);
        AddWindows("-offline-test");
        for (var i = 0; i < 8; i++)
        {
            var state = new BatteryState("extra-" + i, "Razer BlackWidow V4 Tenkeyless HyperSpeed " + i, "Extra", "keyboard");
            state.Apply(new(76, false, null, null, "Test", "已连接", Connection: "2.4G", Connected: true), DateTimeOffset.Now);
            controller.States.Add(state);
        }
        AddWindows("-many-test");
        controller.States.Clear();
        foreach (var (name,kind,source,status) in new (string,string,string,string)[] {
            ("Logitech MX Keys Advanced Wireless Illuminated Keyboard","keyboard","Logitech HID++","已读取 · 槽位 1"),
            ("Logitech MX Master 3S","mouse","Logitech HID++","电压估计 · 槽位 2"),
            ("HyperX Cloud Alpha 2 Wireless","headset","HyperX NGENUITY HID","已读取"),
            ("MCHOSE M7 / L7 共用接收器","mouse","MCHOSE Realtek HID","已读取"),
            ("ATK X1 Ultimate Nearlink","mouse","ATK Evision HID","已读取"),
            ("AULA F87 Pro V2C","keyboard","AULA 键盘 HID","充电状态未知"),
            ("VGN Dragonfly 4K / VXE R1 Pro Max","mouse","CompX HID","已读取") })
        {
            var state=new BatteryState("brand-"+controller.States.Count,name,name,kind);
            state.Apply(new(60,null,null,null,source,status,Connection:"2.4G",Connected:true),DateTimeOffset.Now);
            controller.States.Add(state);
        }
        AddWindows("-sixbrands-test");
        controller.States.Clear();
        AddWindows("-empty-test");
        foreach (var (name, window) in windows)
        {
            var root = (FrameworkElement)window.Content;
            void Layout(double width, double height)
            {
                root.Measure(new Size(width, height));
                root.Arrange(new Rect(0, 0, width, height));
                root.UpdateLayout();
            }
            Layout(window.Width, window.Height);
            if (window is Popover && root is Border frame && frame.Child is FrameworkElement panel &&
                panel.DesiredSize.Height + frame.Padding.Top + frame.Padding.Bottom +
                frame.BorderThickness.Top + frame.BorderThickness.Bottom > window.Height + 0.5)
                failures.Add($"{name}: content is clipped by the fixed window height");
            foreach (var text in Descendants(root).OfType<TextBlock>())
            {
                if (text.Visibility != Visibility.Visible || string.IsNullOrEmpty(text.Text)) continue;
                var inScroll = false;
                for (var ancestor = VisualTreeHelper.GetParent(text); ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                    if (ancestor is ScrollContentPresenter) { inScroll = true; break; }
                var bounds = text.TransformToAncestor(root).TransformBounds(new Rect(text.RenderSize));
                if (!inScroll && (bounds.Left < -0.5 || bounds.Top < -0.5 ||
                    bounds.Right > window.Width + 0.5 || bounds.Bottom > window.Height + 0.5))
                    failures.Add($"{name}: text outside window: {text.Text}; {bounds}");
                for (var parent = VisualTreeHelper.GetParent(text); parent is not null && parent != root; parent = VisualTreeHelper.GetParent(parent))
                {
                    if (parent is not FrameworkElement element || element is not Border && element is not StackPanel) continue;
                    var local = text.TransformToAncestor(element).TransformBounds(new Rect(text.RenderSize));
                    if (local.Bottom > element.ActualHeight + 0.5)
                        failures.Add($"{name}: text overflows {element.GetType().Name}: {text.Text}");
                }
            }
            if (window is DetailWindow)
            {
                Layout(window.MinWidth, window.MinHeight);
                foreach (var grid in Descendants(root).OfType<Grid>().Where(g => g.RowDefinitions.Count > 1))
                foreach (var child in grid.Children.OfType<StackPanel>())
                {
                    var row = Grid.GetRow(child);
                    var top = grid.RowDefinitions.Take(row).Sum(r => r.ActualHeight);
                    var bounds = child.TransformToAncestor(grid).TransformBounds(new Rect(child.RenderSize));
                    if (bounds.Top < top - 0.5 || bounds.Bottom > top + grid.RowDefinitions[row].ActualHeight + 0.5)
                        failures.Add($"{name}: content overlaps another row at minimum window size");
                }
                Layout(window.Width, window.Height);
            }
            if (name.Contains("many-test"))
            {
                var scroll = Descendants(root).OfType<ScrollViewer>().Single();
                if (window is DockWindow && scroll.ExtentWidth <= scroll.ViewportWidth ||
                    window is not DockWindow && scroll.ExtentHeight <= scroll.ViewportHeight)
                    failures.Add(name + ": many devices cannot scroll");
                scroll.ScrollToEnd(); root.UpdateLayout();
                if (window is DockWindow) scroll.ScrollToRightEnd();
                root.UpdateLayout();
            }
            foreach (var dpi in new[] { 96d, 144d, 192d })
            {
                var bitmap = new RenderTargetBitmap((int)(window.Width * dpi / 96), (int)(window.Height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(window.Background, null, new Rect(0, 0, window.Width, window.Height));
                    dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, window.Width, window.Height));
                }
                bitmap.Render(visual);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                if (pixels.Count(b => b != 0) < pixels.Length / 4) failures.Add(name + ": blank rendering");
                var centerOffset = ((bitmap.PixelHeight / 2) * bitmap.PixelWidth + 4) * 4;
                if (pixels[centerOffset] > 90 || pixels[centerOffset + 1] > 90 || pixels[centerOffset + 2] > 90)
                    failures.Add(name + ": unexpected light window surface");
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, $"{name}-{(int)(dpi / 96 * 100)}.png"));
                encoder.Save(file);
            }
            Console.WriteLine($"{name}: rendered at 100%, 150%, 200%");
        }
        Directory.Delete(appearanceRoot, true);
        foreach (var failure in failures) Console.WriteLine("FAIL " + failure);
        Console.WriteLine(failures.Count == 0 ? "PASS window text bounds and nonblank DPI renders" : $"FAIL {failures.Count} layout checks");
        Dispatcher.CurrentDispatcher.InvokeShutdown();
        return failures.Count == 0 ? 0 : 1;
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
