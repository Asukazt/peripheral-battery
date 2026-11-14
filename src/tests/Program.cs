using PeriDock;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;

var failures = new List<string>();
var checks = 0;
void Check(string name, Action test)
{
    checks++;
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + failures[^1]); }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}");
}

Check("mouse battery request has the documented command and checksum", () =>
{
    var p = Protocols.MouseRequest(0x07, 0x80, 2);
    Equal(91, p.Length);
    Equal((byte)0x1f, p[2]);
    Equal((byte)0x07, p[7]);
    Equal((byte)0x80, p[8]);
    Equal((byte)0x85, p[89]);
});
Check("valid mouse battery and foreign command rejection", () =>
{
    var p = new byte[91];
    p[1] = 2; p[2] = 0x1f; p[6] = 2; p[7] = 7; p[8] = 0x80;
    p[10] = 128; p[89] = 5;
    Equal(128, Protocols.MouseValue(p, 7, 0x80) ?? -1);
    Equal<int?>(null, Protocols.MouseValue(p, 7, 0x84));
    p[89] ^= 1;
    Equal<int?>(null, Protocols.MouseValue(p, 7, 0x80));
    Equal<int?>(null, Protocols.MouseValue(p[..80], 7, 0x80));
});
Check("mouse DPI uses big-endian X and Y and rejects impossible values", () =>
{
    var p = new byte[91];
    p[1] = 2; p[2] = 0x1f; p[6] = 7; p[7] = 4; p[8] = 0x85;
    p[10] = 3; p[11] = 0x20; p[12] = 3; p[13] = 0x20;
    p[89] = 0x86;
    Equal((800, 800), Protocols.MouseDpi(p) ?? (0, 0));
    p[10] = 0; p[11] = 0; p[89] = 0xa5;
    Equal<(int, int)?>(null, Protocols.MouseDpi(p));
});
Check("headset query command and checksum match Scythe protocol", () =>
{
    var p = Protocols.HeadsetRequest(0x21, 0x80);
    Equal(64, p.Length); Equal((byte)0xc7, p[62]);
});
Check("headset rejects wrong route, corruption and out-of-range battery", () =>
{
    var p = new byte[64];
    p[0] = 2; p[1] = 2; p[2] = 0x60; p[6] = 5;
    p[9] = 0x80; p[10] = 0x21; p[11] = 1; p[12] = 1; p[13] = 76;
    p[62] = 0x88;
    Equal(76, Protocols.HeadsetValue(p, 0x21, 0x80) ?? -1);
    Equal<int?>(null, Protocols.HeadsetValue(p, 0x21, 0));
    p[13] = 101; p[62] = 0xa1;
    Equal<int?>(null, Protocols.HeadsetValue(p, 0x21, 0x80));
    p[62] ^= 1;
    Equal<int?>(null, Protocols.HeadsetValue(p, 0x21, 0x80));
});
Check("wired headset stays connected when its wireless-link response is disconnected", () =>
{
    var packet = Convert.FromHexString("02026000000006000000200102000000" +
        new string('0', 92) + "4500");
    var wireless = Protocols.HeadsetValue(packet, 0x20, 0);
    Equal<int?>(0, wireless);
    Equal<bool?>(true, Protocols.HeadsetConnected(0x0576, wireless));
    Equal<bool?>(false, Protocols.HeadsetConnected(0x0577, wireless));
    Equal<bool?>(true, Protocols.HeadsetConnected(0x0577, 1));
    Equal<bool?>(null, Protocols.HeadsetConnected(0x0577, null));
    Equal<bool?>(true, Protocols.HeadsetConnected(0x0576, null));
});
Check("failed refresh retains battery but clears charging and DPI freshness", () =>
{
    var s = new BatteryState("mouse", "Mouse", "Mouse");
    var now = DateTimeOffset.UtcNow;
    s.Apply(new Reading(58, true, 800, 800, "HID", "已连接"), now);
    s.Apply(new Reading(null, null, null, null, "HID", "未响应"), now.AddSeconds(30));
    Equal(58, s.Percent ?? -1);
    Equal(true, s.IsStale(now.AddSeconds(30)));
    Equal<bool?>(null, s.Charging);
    Equal("未知", s.DpiText);
    Equal(now, s.LastSuccess);
});
Check("age makes a previously successful reading stale", () =>
{
    var s = new BatteryState("keyboard", "Keyboard", "Keyboard");
    var now = DateTimeOffset.UtcNow;
    s.Apply(new Reading(36, null, null, null, "Windows", "系统电量"), now);
    Equal(false, s.IsStale(now.AddSeconds(60)));
    Equal(true, s.IsStale(now.AddSeconds(95)));
});
Check("connection changes independently from charging and clears on failed detection", () =>
{
    var s = new BatteryState("keyboard", "Keyboard", "Keyboard");
    var now = DateTimeOffset.UtcNow;
    s.Apply(new Reading(40, true, null, null, "ROG", "已连接", Connection: "蓝牙", Connected: true), now);
    Equal("蓝牙", s.Connection);
    Equal<bool?>(true, s.Charging);
    s.Apply(new Reading(41, false, null, null, "ROG", "已连接", Connection: "USB", Connected: true), now.AddSeconds(10));
    Equal("USB", s.Connection);
    Equal<bool?>(false, s.Charging);
    s.Apply(new Reading(null, null, null, null, "ROG", "查询超时"), now.AddSeconds(20));
    Equal("待确认", s.Connection);
    Equal<bool?>(null, s.Charging);
    Equal(41, s.Percent);
});
Check("cached battery never claims a live connection or refreshes the last success", () =>
{
    var s = new BatteryState("keyboard", "Keyboard", "Keyboard");
    var now = DateTimeOffset.UtcNow;
    s.Apply(new Reading(40, true, null, null, "ROG", "已连接", Connection: "USB", Connected: true), now);
    s.Apply(new Reading(40, true, null, null, "Windows", "蓝牙未连接", true, "未连接", false), now.AddMinutes(1));
    Equal("未连接", s.Connection);
    Equal<bool?>(null, s.Charging);
    Equal(false, s.LastReadSucceeded);
    Equal(now, s.LastSuccess);
    Equal(true, s.IsStale(now.AddMinutes(1)));
});
Check("a confirmed connection and charging remain usable without a battery percentage", () =>
{
    var s = new BatteryState("keyboard", "Keyboard", "Keyboard");
    s.Apply(new Reading(null, true, null, null, "ROG", "电量暂不可读",
        Connection: "蓝牙", Connected: true), DateTimeOffset.UtcNow);
    Equal("蓝牙", s.Connection);
    Equal<bool?>(true, s.Charging);
    Equal(false, s.LastReadSucceeded);
});
Check("Razer transport comes from the queried interface rather than charging", () =>
{
    Equal("USB", Protocols.RazerConnection(0x00c0));
    Equal("2.4G", Protocols.RazerConnection(0x00c1));
    Equal("USB", Protocols.RazerConnection(0x0576));
    Equal("2.4G", Protocols.RazerConnection(0x0577));
    Equal("待确认", Protocols.RazerConnection(0x1234));
});
Check("ROG queries allow only power, connection and receiver-slot reads", () =>
{
    var power = Protocols.KeyboardRequest(0, 0x12, 1, 65);
    Equal(65, power.Length);
    Equal((byte)0x12, power[1]);
    Equal((byte)1, power[2]);
    Equal(true, power.Skip(3).All(b => b == 0));
    var refused = false;
    try { Protocols.KeyboardRequest(0, 0x51, 1, 65); }
    catch (ArgumentException) { refused = true; }
    Equal(true, refused);
});
Check("ROG power and connection reject foreign, short and invalid reports", () =>
{
    var p = new byte[64];
    p[0] = 2; p[1] = 0x12; p[2] = 1; p[6] = 45; p[9] = 1;
    Equal((45, (bool?)true), Protocols.KeyboardPower(p, 2)!.Value);
    p[9] = 0;
    Equal((45, (bool?)false), Protocols.KeyboardPower(p, 2)!.Value);
    p[9] = 255;
    Equal((45, (bool?)null), Protocols.KeyboardPower(p, 2)!.Value);
    Equal<(int, bool?)?>(null, Protocols.KeyboardPower(p, 0));
    Equal<(int, bool?)?>(null, Protocols.KeyboardPower(p[..9], 2));
    p[6] = 101;
    Equal<(int, bool?)?>(null, Protocols.KeyboardPower(p, 2));
    p[2] = 3; p[5] = 2;
    Equal("蓝牙", Protocols.KeyboardConnection(p, 2));
    p[5] = 1;
    Equal("2.4G", Protocols.KeyboardConnection(p, 2));
    p[5] = 0;
    Equal("USB", Protocols.KeyboardConnection(p, 2));
    p[5] = 255;
    Equal<string?>(null, Protocols.KeyboardConnection(p, 2));
    p[1] = 255; p[2] = 0xaa;
    Equal<string?>(null, Protocols.KeyboardConnection(p, 2));
});
Check("OMNI paired slots are checked before addressing the keyboard", () =>
{
    byte[] slot = [1, 0xa0, 0, 1, 0, 0x25, 0x1c, 2, 4];
    Equal(true, Protocols.OmniHasAzothX(slot));
    slot[5] = 0x85;
    Equal(false, Protocols.OmniHasAzothX(slot));
    slot[5] = 0x25; slot[3] = 2;
    Equal(false, Protocols.OmniHasAzothX(slot));
});
Check("Bluetooth charging rejects unknown and malformed state fields", () =>
{
    Equal<bool?>(true, Protocols.BluetoothCharging(0x2a1a, [0x33]));
    Equal<bool?>(false, Protocols.BluetoothCharging(0x2a1a, [0x23]));
    Equal<bool?>(null, Protocols.BluetoothCharging(0x2a1a, [0x03]));
    Equal<bool?>(null, Protocols.BluetoothCharging(0x2a1a, []));
    Equal<bool?>(true, Protocols.BluetoothCharging(0x2bed, [0, 0x21, 0]));
    Equal<bool?>(false, Protocols.BluetoothCharging(0x2bed, [0, 0x41, 0]));
    Equal<bool?>(null, Protocols.BluetoothCharging(0x2bed, [0, 1, 0]));
    Equal<bool?>(null, Protocols.BluetoothCharging(0x2bed, [0, 0x20, 0]));
    Equal<bool?>(null, Protocols.BluetoothCharging(0x2bed, [1, 0x21, 0]));
    Equal<bool?>(true, Protocols.BluetoothCharging(0x2bed, [3, 0x21, 0, 1, 0, 50]));
    Equal<bool?>(null, Protocols.BluetoothCharging(0x2bed, [3, 0x21, 0, 1, 0, 101]));
    Equal<bool?>(null, Protocols.BluetoothCharging(0x2a19, [50]));
});
Check("error files and diagnostic ZIP redact identifiers and keep errors", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "PeriDock-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var d = new Diagnostics(root);
        d.Write("error", "mouse", "read", @"User C:\Users\Someone\secret.txt \\?\hid#vid_1532&pid_00c1#serial123 11:22:33:44:55:66", new InvalidOperationException("controlled test"));
        d.Crash(new InvalidOperationException("controlled crash"));
        var zip = d.Export([new BatteryState("mouse", "Mouse", "Mouse")]);
        using var archive = ZipFile.OpenRead(zip);
        Equal(true, archive.Entries.Any(e => e.FullName.EndsWith(".jsonl")));
        Equal(true, archive.Entries.Any(e => e.Name.StartsWith("crash-")));
        foreach (var e in archive.Entries)
        {
            using var r = new StreamReader(e.Open());
            var text = r.ReadToEnd();
            Equal(false, text.Contains("Someone"));
            Equal(false, text.Contains("serial123"));
            Equal(false, text.Contains("11:22:33:44:55:66"));
            if (e.Name == "status.json")
            {
                using var status = JsonDocument.Parse(text);
                var device = status.RootElement.GetProperty("devices")[0];
                Equal(true, device.TryGetProperty("Connected", out _));
                Equal(true, device.TryGetProperty("ChargingNote", out _));
            }
        }
    }
    finally { Directory.Delete(root, true); }
});
Check("dock preference defaults hidden and persists both visible and hidden across reloads", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "PeriDock-settings-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var log = new Diagnostics(root);
        Equal(false, UserSettings.Load(root, log).ShowDock);
        new UserSettings(true).Save(root);
        Equal(true, UserSettings.Load(root, log).ShowDock);
        new UserSettings(false).Save(root);
        Equal(false, UserSettings.Load(root, log).ShowDock);
        Equal(false, File.Exists(Path.Combine(root, "data", "settings.json.tmp")));
    }
    finally { Directory.Delete(root, true); }
});
Check("invalid settings are kept for diagnosis and do not force the dock open", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "PeriDock-settings-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "data"));
    try
    {
        var file = Path.Combine(root, "data", "settings.json");
        File.WriteAllText(file, "{broken");
        var log = new Diagnostics(root);
        Equal(false, UserSettings.Load(root, log).ShowDock);
        Equal("{broken", File.ReadAllText(file));
        Equal(true, log.LastError.Length > 0);
    }
    finally { Directory.Delete(root, true); }
});
Check("startup registration quotes paths and disabling preserves unrelated entries", () =>
{
    if (!OperatingSystem.IsWindows()) return;
    var keyPath = @"Software\PeriDock-test-" + Guid.NewGuid().ToString("N");
    try
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue("Unrelated", "keep");
        Equal(false, StartupSettings.IsEnabled(key, @"D:\Peripheral Battery\PeriDock.exe"));
        StartupSettings.SetEnabled(key, @"D:\Peripheral Battery\PeriDock.exe", true);
        Equal("\"D:\\Peripheral Battery\\PeriDock.exe\" --startup", key.GetValue("Peripheral Battery") as string);
        Equal(true, StartupSettings.IsEnabled(key, @"D:\Peripheral Battery\PeriDock.exe"));
        Equal(false, StartupSettings.IsEnabled(key, @"D:\Other\PeriDock.exe"));
        StartupSettings.SetEnabled(key, @"D:\Peripheral Battery\PeriDock.exe", false);
        Equal(false, StartupSettings.IsEnabled(key, @"D:\Peripheral Battery\PeriDock.exe"));
        Equal("keep", key.GetValue("Unrelated") as string);
    }
    finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
});
Check("Razer transaction families validate their own replies and reject foreign ids", () =>
{
    foreach (var transaction in new byte[] { 0x1f, 0x3f, 0x9f, 0xff })
    {
        var packet = Protocols.MouseRequest(7, 0x80, 2, transaction);
        packet[1] = 2; packet[10] = 128; packet[89] ^= 128;
        Equal<int?>(128, Protocols.MouseValue(packet, 7, 0x80, transaction));
        Equal<int?>(null, Protocols.MouseValue(packet, 7, 0x80, (byte)(transaction ^ 1)));
    }
});
Check("ROG mouse percentages and coarse gauges reject standby and invalid replies", () =>
{
    var packet = new byte[65]; packet[1] = 0x12; packet[2] = 7; packet[5] = 76;
    Equal((76, (bool?)false), Protocols.AsusMousePower(packet, 1) ?? (-1, null));
    packet[5] = 3;
    Equal((75, (bool?)false), Protocols.AsusMousePower(packet, 25) ?? (-1, null));
    packet[5] = 5;
    Equal<(int, bool?)?>(null, Protocols.AsusMousePower(packet, 25));
    packet[5] = 0;
    Equal<(int, bool?)?>(null, Protocols.AsusMousePower(packet, 1));
    packet[10] = 1;
    Equal((0, (bool?)true), Protocols.AsusMousePower(packet, 1) ?? (-1, null));
    packet[2] = 8;
    Equal<(int, bool?)?>(null, Protocols.AsusMousePower(packet, 1));
});
Check("Falchion uses the percentage field instead of the ten-step gauge", () =>
{
    var packet = new byte[65]; packet[1] = 0x12; packet[2] = 1; packet[6] = 8; packet[11] = 76;
    Equal(76, Protocols.KeyboardPower(packet, 0, 11)?.Percent ?? -1);
    Equal<(int, bool?)?>(null, Protocols.KeyboardPower(packet[..11], 0, 11));
});
Check("catalog groups HID collections by physical container, keeping identical mice separate", () =>
{
    var a = new HidDevice("a", 0x00c1, 0xff00, 1, 0, 0, 91, Container: "container-1");
    var b = a with { Path = "b", UsagePage = 1 };
    var c = a with { Path = "c", Container = "container-2" };
    var targets = DeviceCatalog.FromHid([a, b, c]);
    Equal(2, targets.Count);
    Equal(2, targets[0].Interfaces.Length);
    Equal(false, targets[0].Id == targets[1].Id);
    Equal(true, targets.All(t => t.Kind == "mouse" && t.Supported));
});
Check("unlisted and wired-only products never gain a battery protocol from the vendor alone", () =>
{
    Equal<DeviceProfile?>(null, DeviceCatalog.Find(0x1532, 0x00cb));
    Equal<DeviceProfile?>(null, DeviceCatalog.Find(0x9999, 0x00c1));
    var device = new HidDevice("unknown", 0xffff, 0xff00, 1, 65, 65, 91, Name: "Razer New Wireless Mouse");
    var targets = DeviceCatalog.FromHid([device]);
    Equal(1, targets.Count); Equal(false, targets[0].Supported);
    Equal(true, DeviceCatalog.Profiles.Select(p => (p.Vendor, p.Product)).Distinct().Count() == DeviceCatalog.Profiles.Count);
});
Check("ROG headset payload validates both earbuds and keeps charging unknown on malformed responses", () =>
{
    var p = new byte[64]; p[0] = 0xcc; p[1] = 0x12; p[2] = 7; p[6] = 82; p[7] = 65; p[8] = 50;
    Equal(65, Protocols.AsusHeadsetPower(p, 0x1ad3)?.Percent ?? -1);
    Equal(82, Protocols.AsusHeadsetPower(p, 0x1afa)?.Percent ?? -1);
    p[7] = 255;
    Equal<(int, bool?)?>(null, Protocols.AsusHeadsetPower(p, 0x1ad3));
    p[2] = 8; p[5] = 3;
    Equal<bool?>(true, Protocols.AsusHeadsetCharging(p, 0x1ad3));
    Equal<bool?>(null, Protocols.AsusHeadsetCharging(p, 0x1afa));
    p[0] = 0;
    Equal<bool?>(null, Protocols.AsusHeadsetCharging(p, 0x1ad3));
});
Check("2020 headset hides unreliable charging levels and refuses stale receiver replies", () =>
{
    var p = new byte[64]; Convert.FromHexString("FF0F05FE12041F08050305010EA03200").CopyTo(p, 0);
    Equal(((int?)50, (bool?)false), Protocols.Razer2020Power(p) ?? (-1, null));
    p[11] = 9; p[14] = 80;
    Equal(((int?)null, (bool?)true), Protocols.Razer2020Power(p) ?? (-1, null));
    p[1] = 1; p[2] = 0;
    Equal<(int?, bool?)?>(null, Protocols.Razer2020Power(p));
});
Check("PA headset variants reject wrong commands, oversized payloads and impossible battery values", () =>
{
    foreach (ushort product in new ushort[] { 0x0555, 0x0556, 0x053a })
    {
        var query = Protocols.PaRequest(product, 0x21);
        Equal(64, query.Length);
        var echo = product == 0x053a ? 13 : 12;
        var p = new byte[64]; p[0] = query[0]; p[3] = 0x50; p[4] = 0x49;
        p[echo] = 0x21; p[echo + 1] = 1; p[echo + 2] = 1; p[echo + 3] = 76;
        Equal<int?>(76, Protocols.PaValue(p, product, 0x21));
        Equal<int?>(null, Protocols.PaValue(p, product, 0x2a));
        p[echo + 3] = 101;
        Equal<int?>(null, Protocols.PaValue(p, product, 0x21));
        p[echo + 3] = 76; p[echo + 2] = 60;
        Equal<int?>(null, Protocols.PaValue(p, product, 0x21));
        var on = Protocols.PaRequest(product, 0xe1, true);
        var off = Protocols.PaRequest(product, 0xe1, false);
        Equal(1, on.Zip(off).Count(pair => pair.First != pair.Second));
    }
});
Check("Bluetooth model suffixes classify correctly and serialized discovery excludes local HID paths", () =>
{
    Equal("headset", DeviceCatalog.Kind("ROG DELTA II (BT)"));
    Equal("keyboard", DeviceCatalog.Kind("ROG AZOTH X"));
    Equal("mouse", DeviceCatalog.Kind("Razer Basilisk V3 Pro 35K"));
    var device = new HidDevice("private-device-path", 0x00c1, 0xff00, 1, 0, 0, 91, Container: "private-container");
    var json = JsonSerializer.Serialize(DeviceCatalog.FromHid([device]));
    Equal(false, json.Contains("private-"));
    Equal(true, json.Contains("1532:00c1"));
});
Check("access denied hides previous battery from every percentage consumer", () =>
{
    var state = new BatteryState("mouse", "Mouse", "Mouse");
    var now = DateTimeOffset.UtcNow;
    state.Apply(new(58, true, 1600, 1600, "HID", "已连接", Connection: "USB", Connected: true), now);
    var denied = JsonSerializer.Deserialize<Reading>("""
        {"Percent":null,"Charging":null,"DpiX":null,"DpiY":null,"Source":"HID 1532:00c0",
         "Status":"已识别设备；访问被拒绝（Windows 错误 5）","Connection":"USB","Failure":1,"ErrorCode":5}
        """)!;
    state.Apply(denied, now.AddSeconds(30));
    Equal("--", state.PercentText);
    Equal<int?>(null, state.Percent);
    Equal("USB", state.Connection);
    Equal(false, state.LastReadSucceeded);
    Equal<bool?>(null, state.Charging);
    Equal("未知", state.DpiText);
    Equal(now, state.LastSuccess);
});
Check("denied HID metadata retains the same physical identity and safe hardware diagnostics", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "PeriDock-access-" + Guid.NewGuid().ToString("N"));
    try
    {
        var log = new Diagnostics(root);
        var path = @"\\?\HID#VID_1532&PID_00C0&MI_00#private-serial";
        var device = NativeDevices.InspectHidInterface(path, 0x1532, "Mouse", "private-container",
            () => throw new Win32Exception(5, @"Denied C:\Users\Someone\secret " + path), log)!;
        Equal<ushort>(0x00c0, device.Product);
        Equal<int?>(5, device.AccessError);
        var target = DeviceCatalog.FromHid([device]).Single();
        var readable = device with { AccessError = null, FeatureBytes = 91 };
        Equal(DeviceCatalog.FromHid([readable]).Single().Id, target.Id);
        Equal("USB", target.Connection);
        var calls = 0;
        var reading = DeviceReader.ReadHidTarget(target, (_, _) => { calls++; throw new Exception("Must not query denied metadata"); }).GetAwaiter().GetResult();
        Equal(0, calls);
        Equal(ReadFailure.AccessDenied, reading.Failure);
        Equal<int?>(5, reading.ErrorCode);
        Equal("USB", reading.Connection);
        Equal(false, reading.Connected == false);
        var json = JsonSerializer.Serialize(target);
        Equal(false, json.Contains("private-"));
        foreach (var file in Directory.GetFiles(log.LogFolder))
        {
            using var entry = JsonDocument.Parse(File.ReadAllText(file));
            Equal(5, entry.RootElement.GetProperty("nativeError").GetInt32());
            var message = entry.RootElement.GetProperty("message").GetString()!;
            Equal(true, message.Contains("VID=1532") && message.Contains("PID=00c0"));
            Equal(false, File.ReadAllText(file).Contains("private-serial"));
            Equal(false, File.ReadAllText(file).Contains("Someone"));
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Check("read access denial survives isolated JSON and preserves USB or receiver transport", () =>
{
    foreach (var (product, connection) in new (ushort, string)[] { (0x00c0, "USB"), (0x00c1, "2.4G"), (0x0577, "2.4G") })
    {
        var target = DeviceCatalog.FromHid([new HidDevice("controlled", product, 0xff00, 1, 64, 64, 91)]).Single();
        foreach (var error in new Exception[] { new Win32Exception(5, "controlled access denial"), new UnauthorizedAccessException("controlled denial") })
        {
            var result = DeviceReader.ReadHidTarget(target, (_, _) => throw error).GetAwaiter().GetResult();
            var restored = JsonSerializer.Deserialize<Reading>(JsonSerializer.Serialize(result))!;
            Equal(ReadFailure.AccessDenied, restored.Failure);
            Equal<int?>(5, restored.ErrorCode);
            Equal(connection, restored.Connection);
            Equal<int?>(null, restored.Percent);
            Equal<bool?>(null, restored.Connected);
        }
    }
});
Check("working collection wins over an inaccessible sibling and known denial wins over generic failure", () =>
{
    var accessible = new HidDevice("good", 0x00c0, 1, 2, 0, 0, 91, Container: "same-device");
    var denied = accessible with { Path = "denied", AccessError = 5, FeatureBytes = 0 };
    var target = DeviceCatalog.FromHid([denied, accessible]).Single();
    var live = DeviceReader.ReadHidTarget(target, (interfaces, _) =>
    {
        Equal(1, interfaces.Length);
        Equal("good", interfaces[0].Path);
        return Task.FromResult(new Reading(50, true, 1600, 1600, "HID", "已连接", Connection: "USB", Connected: true));
    }).GetAwaiter().GetResult();
    Equal<int?>(50, live.Percent);
    Equal(ReadFailure.None, live.Failure);
    var failed = DeviceReader.ReadHidTarget(target, (_, _) => throw new IOException("generic failure")).GetAwaiter().GetResult();
    Equal(ReadFailure.AccessDenied, failed.Failure);
    Equal<int?>(5, failed.ErrorCode);
});
Check("unsupported protocols and missing collections remain visible without sending guessed commands", () =>
{
    var unknown = DeviceCatalog.FromHid([new HidDevice("unknown", 0xffff, 1, 2, 0, 0, 91)]).Single();
    var calls = 0;
    var unsupported = DeviceReader.ReadHidTarget(unknown, (_, _) => { calls++; throw new Exception("Unexpected query"); }).GetAwaiter().GetResult();
    Equal(0, calls);
    Equal(ReadFailure.ProtocolUnsupported, unsupported.Failure);
    Equal(false, unsupported.Connected == false);
    var noFeature = DeviceCatalog.FromHid([new HidDevice("no-feature", 0x00c0, 1, 2, 0, 0, 0)]).Single();
    var missing = DeviceReader.ReadHidTarget(noFeature).GetAwaiter().GetResult();
    Equal(ReadFailure.ProtocolUnsupported, missing.Failure);
    Equal("USB", missing.Connection);
    Equal(false, missing.Connected == false);
});
Check("other native failures keep their error code and transport without requesting elevation", () =>
{
    var device = new HidDevice("controlled", 0x00c0, 1, 2, 0, 0, 91);
    var target = DeviceCatalog.FromHid([device]).Single();
    var result = DeviceReader.ReadHidTarget(target, (_, _) => throw new Win32Exception(32, "controlled sharing violation")).GetAwaiter().GetResult();
    Equal(ReadFailure.ReadError, result.Failure);
    Equal<int?>(32, result.ErrorCode);
    Equal("USB", result.Connection);
    Equal<int?>(31, Diagnostics.NativeError(new IOException("controlled I/O error", unchecked((int)0x8007001f))));
    Equal<int?>(null, Diagnostics.NativeError(new InvalidOperationException("not a Win32 HRESULT")));
});
Check("fresh USB, receiver and Bluetooth readings recover after denial and preserve cache semantics", () =>
{
    foreach (var connection in new[] { "USB", "2.4G", "蓝牙" })
    {
        var state = new BatteryState("controlled", "Device", "Device");
        var now = DateTimeOffset.UtcNow;
        state.Apply(new(null, null, null, null, "HID", "Denied", Connection: connection,
            Failure: ReadFailure.AccessDenied, ErrorCode: 5), now);
        state.Apply(new(40, connection == "蓝牙" ? null : true, 1600, 1600, "Fixture", "已连接", Connection: connection, Connected: true), now.AddSeconds(1));
        Equal("40%", state.PercentText);
        Equal(connection, state.Connection);
        Equal(ReadFailure.None, state.Failure);
        Equal<int?>(null, state.ErrorCode);
        Equal(true, state.LastReadSucceeded);
        state.Apply(new(39, true, 1600, 1600, "Windows cache", "缓存", Cached: true, Connection: connection, Connected: true), now.AddSeconds(2));
        Equal(false, state.LastReadSucceeded);
        Equal<bool?>(null, state.Charging);
        Equal("未知", state.DpiText);
        Equal(now.AddSeconds(1), state.LastSuccess);
    }
});
Check("a failed query without a static transport keeps the last known connection", () =>
{
    var state = new BatteryState("keyboard", "ROG", "ROG");
    var now = DateTimeOffset.UtcNow;
    state.Apply(new(75, false, null, null, "ROG HID", "已连接", Connection: "USB", Connected: true), now);
    state.Apply(new(null, null, null, null, "ROG HID", "访问被拒绝", Failure: ReadFailure.AccessDenied, ErrorCode: 5), now.AddSeconds(1));
    Equal("USB", state.Connection);
    Equal<bool?>(null, state.Connected);
    Equal("--", state.PercentText);
    state.Apply(new(null, null, null, null, "HID", "已移除", Connection: "未连接", Connected: false), now.AddSeconds(2));
    Equal("未连接", state.Connection);
});
Check("elevation is explicit, uses the current executable and treats UAC cancellation as no restart", () =>
{
    ProcessStartInfo? info = null;
    var started = Elevation.Start(@"D:\Controlled App\PeriDock.exe", 123, value => { info = value; return new Process(); });
    Equal(true, started);
    Equal("runas", info!.Verb);
    Equal(true, info.UseShellExecute);
    Equal(@"D:\Controlled App\PeriDock.exe", info.FileName);
    Equal("--wait-for-exit", info.ArgumentList[0]);
    Equal("123", info.ArgumentList[1]);
    Equal(false, Elevation.Start("controlled", 123, _ => throw new Win32Exception(1223)));
    Equal(false, Elevation.Start("controlled", 123, _ => null));
    var raised = false;
    try { Elevation.Start("controlled", 123, _ => throw new Win32Exception(2)); }
    catch (Win32Exception ex) { raised = ex.NativeErrorCode == 2; }
    Equal(true, raised);
    Equal(false, Elevation.WaitForParent("invalid"));
    Equal(false, Elevation.WaitForParent("-1"));
    Equal(false, Elevation.WaitForParent(Environment.ProcessId.ToString()));
});
Check("official images match model and transport suffix without sending unknown names", () =>
{
    Equal("https://www.razer.com/gaming-mice/razer-viper-v3-pro",
        OfficialProductImages.PageFor("Razer Viper V3 Pro (BT)")?.AbsoluteUri);
    Equal("https://rog.asus.com/keyboards/keyboards/aura-rgb/rog-azoth-x/",
        OfficialProductImages.PageFor("ROG AZOTH X")?.AbsoluteUri);
    Equal<Uri?>(null, OfficialProductImages.PageFor("Razer Alice's custom mouse 123456"));
    Equal<Uri?>(null, OfficialProductImages.PageFor("Razer Viper V3 Pro 35K unknown variant"));
});
Check("official image parser validates model, host and metadata attribute order", () =>
{
    var page = new Uri("https://www.razer.com/gaming-mice/razer-basilisk-v3-pro");
    var html = "<meta content='Razer Basilisk V3 Pro | Razer' property='og:title'>" +
        "<meta content='https://assets2.razerzone.com/product.jpg?w=512&amp;h=512' property='og:image'>";
    Equal("https://assets2.razerzone.com/product.jpg?w=512&h=512",
        OfficialProductImages.ImageFromPage(page, html)?.AbsoluteUri);
    Equal<Uri?>(null, OfficialProductImages.ImageFromPage(page, html.Replace("Basilisk V3 Pro", "Basilisk V3")));
    Equal<Uri?>(null, OfficialProductImages.ImageFromPage(page, html.Replace("assets2.razerzone.com", "assets2.razerzone.com.evil.example")));
    Equal<Uri?>(null, OfficialProductImages.ImageFromPage(page, html.Replace("https://assets2", "http://assets2")));
    Equal<Uri?>(null, OfficialProductImages.ImageFromPage(page, html.Replace("assets2.razerzone.com", "127.0.0.1")));
    Equal<Uri?>(null, OfficialProductImages.ImageFromPage(new Uri("https://evil.example/razer-basilisk-v3-pro"), html));
});
string ImageTestRoot() => Path.Combine(Path.GetTempPath(), "PeriDock-images-" + Guid.NewGuid().ToString("N"));
HttpResponseMessage ImageResponse(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "text/html") };
var officialHtml = "<meta property='og:title' content='Razer Basilisk V3 Pro'>" +
    "<meta property='og:image' content='https://assets2.razerzone.com/product.png'>";
var imageFixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "peridock.png"));
Check("product images download once per model, retain source and work offline", () =>
{
    var root = ImageTestRoot();
    try
    {
        using var handler = new ImageHttpHandler(request =>
        {
            if (request.RequestUri!.Host == "www.razer.com") return ImageResponse(officialHtml);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(imageFixture) };
            response.Content.Headers.ContentType = new("image/png"); return response;
        });
        using var client = new HttpClient(handler);
        var images = new OfficialProductImages(root, new Diagnostics(root), client);
        var first = images.GetAsync("Razer Basilisk V3 Pro");
        Equal(true, ReferenceEquals(first, images.GetAsync("Razer Basilisk V3 Pro (BT)")));
        var path = first.GetAwaiter().GetResult();
        Equal(true, path is not null && File.Exists(path)); Equal(2, handler.Seen.Count);
        Equal(true, File.ReadAllText(path + ".source.json").Contains("https://assets2.razerzone.com/product.png"));
        using var offlineHandler = new ImageHttpHandler(_ => throw new HttpRequestException("offline"));
        using var offline = new HttpClient(offlineHandler);
        Equal(path, new OfficialProductImages(root, new Diagnostics(root), offline).GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
        Equal(0, offlineHandler.Seen.Count);
        File.WriteAllText(path!, "corrupt cache");
        Equal(path, new OfficialProductImages(root, new Diagnostics(root), client).GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
        Equal(4, handler.Seen.Count);
        Equal(0, Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length);
        var state = new BatteryState("test", "test", "test") { ProductImagePath = path };
        Equal(false, JsonSerializer.Serialize(state).Contains(path!));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Check("per-model local pictures win without leaking one mouse image to other models", () =>
{
    var root = ImageTestRoot();
    try
    {
        var path = OfficialProductImages.OverridePath(root, "Razer Viper V3 Pro");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, imageFixture);
        using var handler = new ImageHttpHandler(_ => throw new HttpRequestException("offline"));
        using var client = new HttpClient(handler);
        var images = new OfficialProductImages(root, new Diagnostics(root), client);
        Equal(path, images.GetAsync("Razer Viper V3 Pro (BT)").GetAwaiter().GetResult()); Equal(0, handler.Seen.Count);
        Equal<string?>(null, images.GetAsync("Razer Alice's private mouse").GetAwaiter().GetResult()); Equal(0, handler.Seen.Count);
        Equal<string?>(null, images.GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult()); Equal(2, handler.Seen.Count);
        Equal(true, File.ReadAllBytes(path).SequenceEqual(imageFixture));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Check("failed image fetches back off across restarts and retry after a day", () =>
{
    var root = ImageTestRoot();
    try
    {
        using var handler = new ImageHttpHandler(_ => new(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var images = new OfficialProductImages(root, new Diagnostics(root), client);
        Equal<string?>(null, images.GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
        Equal<string?>(null, images.GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
        Equal<string?>(null, new OfficialProductImages(root, new Diagnostics(root), client).GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
        Equal(2, handler.Seen.Count);
        var retry = Directory.GetFiles(root, "*.retry", SearchOption.AllDirectories).Single();
        File.SetLastWriteTimeUtc(retry, DateTime.UtcNow.AddHours(-25));
        Equal<string?>(null, new OfficialProductImages(root, new Diagnostics(root), client).GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
        Equal(4, handler.Seen.Count);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
Check("official image redirects never leave the vendor or change product model", () =>
{
    foreach (var location in new[] { "https://127.0.0.1/product.png", "https://www.razer.com/gaming-mice/razer-basilisk-v3-pro-35k" })
    {
        var root = ImageTestRoot();
        try
        {
            using var handler = new ImageHttpHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(location); return response;
            });
            using var client = new HttpClient(handler);
            Equal<string?>(null, new OfficialProductImages(root, new Diagnostics(root), client).GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
            Equal(2, handler.Seen.Count);
            Equal(true, handler.Seen.All(uri => uri.Host == "www.razer.com" &&
                uri.AbsolutePath is "/gaming-mice/razer-basilisk-v3-pro/buy" or "/gaming-mice/razer-basilisk-v3-pro"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
});
Check("invalid, oversized and active image content all fall back safely", () =>
{
    foreach (var type in new[] { "image/svg+xml", "image/png", "oversized" })
    {
        var root = ImageTestRoot();
        try
        {
            using var handler = new ImageHttpHandler(request =>
            {
                if (request.RequestUri!.Host == "www.razer.com") return ImageResponse(officialHtml);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(type == "oversized" ? new byte[6 * 1024 * 1024] : [1, 2, 3]) };
                response.Content.Headers.ContentType = new(type == "oversized" ? "image/png" : type); return response;
            });
            using var client = new HttpClient(handler);
            Equal<string?>(null, new OfficialProductImages(root, new Diagnostics(root), client).GetAsync("Razer Basilisk V3 Pro").GetAwaiter().GetResult());
            Equal(0, Directory.GetFiles(root, "*.png", SearchOption.AllDirectories).Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
});
Check("tray tooltip shows device category and percentage rather than model names", () =>
{
    var mouse = new BatteryState("test", "Razer Viper V3 Pro", "Viper", "mouse");
    mouse.Apply(new(10, false, 1600, 1600, "HID", "已连接", Connected: true), DateTimeOffset.UtcNow);
    Equal("鼠标：10%", mouse.TrayText);
    mouse.Apply(new(null, null, null, null, "HID", "访问被拒绝", Failure: ReadFailure.AccessDenied), DateTimeOffset.UtcNow);
    Equal("鼠标：--", mouse.TrayText);
    Equal("键盘：--", new BatteryState("kb", "ROG AZOTH X", "Azoth", "keyboard").TrayText);
    Equal("耳机：--", new BatteryState("hs", "Razer", "Razer", "headset").TrayText);
});
Check("Razer official color options are model specific and reject foreign image hosts", () =>
{
    var page = OfficialProductImages.PageFor("Razer Basilisk V3 Pro")!;
    var options = new[] { ("Black", "Razer Basilisk V3 Pro", "assets3.razerzone.com"),
        ("White", "Razer Basilisk V3 Pro", "assets3.razerzone.com"),
        ("Black", "Razer Basilisk V3 Pro 35K", "assets3.razerzone.com"),
        ("Pink", "Razer Basilisk V3 Pro", "evil.example") }.Select(v => new {
            name = v.Item2 + " - " + v.Item1,
            images = new[] { new { format = "product", url = "https://" + v.Item3 + "/" + v.Item1 + ".png" } },
            variantOptionQualifiers = new[] { new { qualifier = "color", value = v.Item1 } }
        });
    var html = "<script id='ng-state' type='application/json'>" + JsonSerializer.Serialize(new {
        nested = new { baseOptions = new[] { new { options } } }
    }) + "</script>";
    var values = OfficialProductImages.VariantsFromPage(page, html);
    Equal(2, values.Count); Equal("黑色", values[0].Label); Equal("白色", values[1].Label);
    Equal(false, values[0].Id == values[1].Id);
});
Check("ROG gallery color options ignore related models and mixed-color hero images", () =>
{
    var page = OfficialProductImages.PageFor("ROG Harpe Ace Aim Lab Edition")!;
    var html = "<img alt='ROG Harpe Ace Aim Lab Edition in black and white' src='https://dlcdnwebimgs.asus.com/hero'>" +
        "<img alt='ROG Harpe Ace Aim Lab Edition Black front' src='https://dlcdnwebimgs.asus.com/black'>" +
        "<img alt='ROG Harpe Ace Aim Lab Edition Moonlight White front' src='https://dlcdnwebimgs.asus.com/white'>" +
        "<img alt='ROG Chakram X White' src='https://dlcdnwebimgs.asus.com/other'>";
    var values = OfficialProductImages.VariantsFromPage(page, html);
    Equal(2, values.Count); Equal("黑色", values[0].Label); Equal("月光白", values[1].Label);
});
Check("color preferences persist without resetting dock visibility", () =>
{
    var root = ImageTestRoot();
    try
    {
        var key = OfficialProductImages.FileKey("Razer Viper V3 Pro");
        var settings = new UserSettings(true, new() { [key] = "generic" }); settings.Save(root);
        var loaded = UserSettings.Load(root, new Diagnostics(root));
        Equal(true, loaded.ShowDock); Equal("generic", loaded.ImageChoices![key]);
        (loaded with { ShowDock = false }).Save(root);
        Equal("generic", UserSettings.Load(root, new Diagnostics(root)).ImageChoices![key]);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});
if (args.Length == 2 && args[0] == "--official-images")
{
    foreach (var name in new[] { "Razer Basilisk V3 Pro", "Razer DeathStalker V2 Pro", "ROG Harpe Ace Aim Lab Edition", "ROG Azoth X", "ROG Pelta" })
        Check("live official product image: " + name, () =>
        {
            var images = new OfficialProductImages(args[1], new Diagnostics(args[1]));
            var file = images.GetAsync(name).GetAwaiter().GetResult();
            Equal(true, file is not null && File.Exists(file) && File.Exists(file + ".source.json"));
            Console.WriteLine("IMAGE " + name + " => " + file);
        });
    foreach (var name in new[] { "Razer Basilisk V3 Pro", "ROG Harpe Ace Aim Lab Edition" })
        Check("live official color selection and offline restore: " + name, () =>
        {
            var values = new OfficialProductImages(args[1], new Diagnostics(args[1])).GetOptionsAsync(name).GetAwaiter().GetResult();
            var colors = values.Where(v => v.Id is not ("auto" or "local" or "generic")).ToArray();
            Equal(true, colors.Length >= 2);
            Equal(true, colors.Any(v => v.Label.Contains("白")));
            var metadata = Path.Combine(args[1], "data", "device-images", OfficialProductImages.FileKey(name) + ".options.json");
            File.SetLastWriteTimeUtc(metadata, DateTime.UtcNow.AddDays(-8));
            using var handler = new ImageHttpHandler(_ => throw new HttpRequestException("offline"));
            using var offline = new HttpClient(handler);
            var restored = new OfficialProductImages(args[1], new Diagnostics(args[1]), offline).GetOptionsAsync(name).GetAwaiter().GetResult();
            Equal(true, colors.All(c => restored.Any(r => r.Id == c.Id && r.FilePath == c.FilePath)));
            Console.WriteLine("COLORS " + name + ": " + string.Join(", ", colors.Select(c => c.Label)));
        });
}
Console.WriteLine($"{checks - failures.Count}/{checks} checks passed");
if (failures.Count == 0 && args.Length == 2 && args[0] == "--catalog")
{
    var profiles = DeviceCatalog.Profiles;
    var catalog = profiles.Select(p => new {
        vid = $"{p.Vendor:x4}", pid = $"{p.Product:x4}", p.Name, p.Kind, p.Reader, p.Connection,
        protocolAvailable = p.Reader != "Unsupported", hardwareVerified = false
    });
    File.WriteAllText(args[1], JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Catalog: {profiles.Count} product IDs, {profiles.Select(p => p.Name).Distinct().Count()} names; " +
        $"{profiles.Count(p => p.Reader != "Unsupported")} IDs with protocol adapters");
}
return failures.Count == 0 ? 0 : 1;

sealed class ImageHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    internal List<Uri> Seen { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        Seen.Add(request.RequestUri!);
        return Task.FromResult(response(request));
    }
}
