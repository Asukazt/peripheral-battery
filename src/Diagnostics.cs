using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.ComponentModel;

namespace PeriDock;

internal sealed class Diagnostics
{
    readonly string root;
    readonly object sync = new();
    readonly Dictionary<string, (string Message, DateTimeOffset Time)> recent = [];
    internal string LogFolder => Path.Combine(root, "logs");
    internal string LastError { get; private set; } = "";
    internal Diagnostics(string root) { this.root = root; }
    internal static string Redact(string text)
    {
        text = Regex.Replace(text, @"(?i)[a-z]:\\users\\[^\\\s""<>]+", @"C:\Users\[user]");
        text = Regex.Replace(text, @"(?i)\\\\\?\\(?:hid|usb|bth)[^\s""<>]*", "[device-path]");
        return Regex.Replace(text, @"(?i)\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\b", "[address]");
    }
    object Entry(string level, string device, string operation, string message, Exception? error)
        => new { time = DateTimeOffset.Now, version = "0.2.0-preview.2", level, device, operation,
            message = Redact(message), exception = error?.GetType().Name,
            nativeError = error is null ? null : NativeError(error),
            stack = error is null ? null : Redact(error.ToString()) };
    internal static int? NativeError(Exception error) => error is Win32Exception native ? native.NativeErrorCode :
        error is UnauthorizedAccessException ? 5 :
        (error.HResult & 0xffff0000) == 0x80070000 ? error.HResult & 0xffff : null;
    internal void HidFailure(HidDevice device, string operation, Exception error)
        => Write("error", $"{device.Vendor:x4}:{device.Product:x4}", operation,
            $"VID={device.Vendor:x4}; PID={device.Product:x4}; Win32={NativeError(error)?.ToString() ?? "unknown"}; " +
            (NativeError(error) == 5 ? "访问被拒绝；当前权限无法访问设备接口" : Redact(error.Message)), error);
    internal void Write(string level, string device, string operation, string message, Exception? error = null)
    {
        lock (sync)
        {
            if (level == "error") LastError = $"{device}: {Redact(message)}";
            var key = device + ":" + operation;
            if (recent.TryGetValue(key, out var previous) && previous.Message == message &&
                DateTimeOffset.Now - previous.Time < TimeSpan.FromMinutes(5)) return;
            recent[key] = (message, DateTimeOffset.Now);
            Save(JsonSerializer.Serialize(Entry(level, device, operation, message, error)) + Environment.NewLine,
                $"peridock-{DateTime.Now:yyyy-MM-dd}.jsonl", true);
        }
    }
    internal void Crash(Exception error)
    {
        lock (sync)
            Save(JsonSerializer.Serialize(Entry("fatal", "app", "crash", error.Message, error)),
                $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json", false);
    }
    void Save(string content, string file, bool append)
    {
        try { SaveIn(LogFolder, file, content, append); }
        catch
        {
            try { SaveIn(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PeriDock", "logs"), file, content, append); }
            catch { }
        }
    }
    static void SaveIn(string directory, string file, string content, bool append)
    {
        Directory.CreateDirectory(directory);
        if (append) File.AppendAllText(Path.Combine(directory, file), content);
        else File.WriteAllText(Path.Combine(directory, file), content);
        var files = Directory.EnumerateFiles(directory, "peridock-*.jsonl")
            .Concat(Directory.EnumerateFiles(directory, "crash-*.json")).Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long total = 0;
        foreach (var info in files)
        {
            total += info.Length;
            if (info.Name != file && (info.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-14) || total > 50L * 1024 * 1024))
                info.Delete();
        }
    }
    internal string Export(IEnumerable<BatteryState> states)
    {
        lock (sync)
        {
            Directory.CreateDirectory(LogFolder);
            var folder = Path.Combine(root, "diagnostics"); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Peripheral-Battery-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss-fff}.zip");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry("status.json").Open()))
                writer.Write(JsonSerializer.Serialize(new { version = "0.2.0-preview.2", time = DateTimeOffset.Now,
                    os = Environment.OSVersion.VersionString, devices = states.Select(s => new {
                        s.Id, s.Name, s.Connection, s.Connected, s.Percent, s.Charging, s.ChargingNote, s.DpiX, s.DpiY,
                        s.LastSuccess, s.Source, s.Status, s.Cached, s.Failure, s.ErrorCode }) }, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var file in Directory.EnumerateFiles(LogFolder, "peridock-*.jsonl")
                .Concat(Directory.EnumerateFiles(LogFolder, "crash-*.json")))
            {
                using var writer = new StreamWriter(zip.CreateEntry("logs/" + Path.GetFileName(file)).Open());
                writer.Write(Redact(File.ReadAllText(file)));
            }
            return path;
        }
    }
}
