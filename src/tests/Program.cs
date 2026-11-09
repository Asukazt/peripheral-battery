using PeriDock;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Win32;

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
Console.WriteLine($"{checks - failures.Count}/{checks} checks passed");
return failures.Count == 0 ? 0 : 1;
