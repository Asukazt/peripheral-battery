using System.Runtime.Versioning;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PeriDock;

internal sealed record UserSettings(bool ShowDock = false)
{
    public bool LowBatteryAlert { get; init; } = true;
    public int LowBatteryThreshold { get; init; } = 20;
    public Dictionary<string, int> LowBatteryThresholds { get; init; } = new();
    public Dictionary<string, string> ImageChoices { get; init; } = new();
    internal int ThresholdFor(string deviceId, string? kind = null)
    {
        if (LowBatteryThresholds.TryGetValue(deviceId, out var value)) return Math.Clamp(value, 5, 80);
        if (kind is not null && LowBatteryThresholds.TryGetValue(kind, out value)) return Math.Clamp(value, 5, 80);
        return LowBatteryThreshold;
    }
    internal UserSettings WithThreshold(string deviceId, int value)
        => this with
        {
            LowBatteryThresholds = new(LowBatteryThresholds) { [deviceId] = Math.Clamp(value, 5, 80) }
        };
    internal static UserSettings Load(string root, Diagnostics log)
    {
        var file = Path.Combine(root, "data", "settings.json");
        if (!File.Exists(file)) return new();
        try
        {
            var loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(file)) ?? throw new JsonException("设置内容为空");
            return loaded with
            {
                LowBatteryThreshold = Math.Clamp(loaded.LowBatteryThreshold, 5, 80),
                ImageChoices = loaded.ImageChoices ?? new(),
                LowBatteryThresholds = loaded.LowBatteryThresholds?
                    .ToDictionary(pair => pair.Key, pair => Math.Clamp(pair.Value, 5, 80)) ?? new()
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            log.Write("error", "app", "load-settings", ex.Message, ex);
            return new();
        }
    }
    internal void Save(string root)
    {
        var file = Path.Combine(root, "data", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
}

[SupportedOSPlatform("windows")]
internal static class StartupSettings
{
    internal const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool IsEnabled(RegistryKey? key, string executable)
        => string.Equals(key?.GetValue("Peripheral Battery") as string, $"\"{executable}\" --startup", StringComparison.OrdinalIgnoreCase);
    internal static void SetEnabled(RegistryKey key, string executable, bool enabled)
    {
        if (enabled) key.SetValue("Peripheral Battery", $"\"{executable}\" --startup", RegistryValueKind.String);
        else key.DeleteValue("Peripheral Battery", false);
    }
}
