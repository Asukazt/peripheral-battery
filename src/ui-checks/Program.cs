using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var failures = new List<string>();
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
        foreach (var failure in failures) Console.WriteLine("FAIL " + failure);
        Console.WriteLine(failures.Count == 0 ? "PASS window text bounds and nonblank DPI renders" : $"FAIL {failures.Count} layout checks");
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
