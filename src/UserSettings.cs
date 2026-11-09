using System.Runtime.Versioning;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PeriDock;

internal sealed record UserSettings(bool ShowDock = false)
{
    internal static UserSettings Load(string root, Diagnostics log)
    {
        var file = Path.Combine(root, "data", "settings.json");
        if (!File.Exists(file)) return new();
        try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(file)) ?? throw new JsonException("设置内容为空"); }
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
