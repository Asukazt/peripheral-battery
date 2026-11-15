using System.IO;
using System.Text.Json;

namespace PeriDock;

internal sealed record UserSettings(bool ShowDock = false, Dictionary<string, string>? ImageChoices = null)
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
