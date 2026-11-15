using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace PeriDock;

internal static class StartupSettings
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    static string UserId { get { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; } }
    internal static string TaskName(string executable, string userId)
        => "Peripheral Battery - " + DeviceCatalog.Key(Path.GetFullPath(executable) + "|" + userId);

    internal static string TaskXml(string executable, string userId)
    {
        executable = Path.GetFullPath(executable);
        XElement Element(string name, object value) => new(Ns + name, value);
        return new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            Element("RegistrationInfo", Element("Description", "Peripheral Battery: administrator startup for this user and installation.")),
            Element("Triggers", Element("LogonTrigger", new object[] {
                Element("Enabled", true), Element("UserId", userId), Element("Delay", "PT5S") })),
            Element("Principals", new XElement(Ns + "Principal", new XAttribute("id", "User"),
                Element("UserId", userId), Element("LogonType", "InteractiveToken"), Element("RunLevel", "HighestAvailable"))),
            Element("Settings", new object[] {
                Element("MultipleInstancesPolicy", "IgnoreNew"), Element("DisallowStartIfOnBatteries", false),
                Element("StopIfGoingOnBatteries", false), Element("AllowStartOnDemand", true),
                Element("Enabled", true), Element("ExecutionTimeLimit", "PT0S") }),
            new XElement(Ns + "Actions", new XAttribute("Context", "User"),
                Element("Exec", new object[] { Element("Command", executable), Element("Arguments", "--startup"),
                    Element("WorkingDirectory", Path.GetDirectoryName(executable)!) }))).ToString();
    }

    internal static bool Matches(string xml, string executable, string userId)
    {
        var task = XElement.Parse(xml);
        var principal = task.Element(Ns + "Principals")?.Elements().SingleOrDefault();
        var actions = task.Element(Ns + "Actions")?.Elements().ToArray() ?? [];
        var triggers = task.Element(Ns + "Triggers")?.Elements().ToArray() ?? [];
        return SameUser(principal?.Element(Ns + "UserId")?.Value, userId) &&
            principal?.Element(Ns + "LogonType")?.Value == "InteractiveToken" &&
            principal.Element(Ns + "RunLevel")?.Value == "HighestAvailable" &&
            actions.Length == 1 && actions[0].Name == Ns + "Exec" &&
            string.Equals(actions[0].Element(Ns + "Command")?.Value, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase) &&
            actions[0].Element(Ns + "Arguments")?.Value == "--startup" &&
            triggers.Length == 1 && triggers[0].Name == Ns + "LogonTrigger" &&
            SameUser(triggers[0].Element(Ns + "UserId")?.Value, userId) &&
            (triggers[0].Element(Ns + "Enabled")?.Value ?? "true") == "true";
    }
    static bool SameUser(string? value, string userId)
    {
        if (value == userId) return true;
        if (string.IsNullOrEmpty(value)) return false;
        try { return ((SecurityIdentifier)new NTAccount(value).Translate(typeof(SecurityIdentifier))).Value == userId; }
        catch (IdentityNotMappedException) { return false; }
    }

    static T WithFolder<T>(Func<dynamic, T> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)!)!;
        object? folder = null;
        try { service.Connect(); folder = service.GetFolder(@"\"); return action(folder); }
        finally { if (folder is not null) Marshal.FinalReleaseComObject(folder); Marshal.FinalReleaseComObject(service); }
    }
    static object? Find(dynamic folder, string name)
    {
        try { return folder.GetTask(name); }
        catch (FileNotFoundException) { return null; }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
    }
    internal static bool IsEnabled(string executable)
    {
        var userId = UserId;
        return WithFolder<bool>(folder =>
        {
            object? task = Find(folder, TaskName(executable, userId));
            try { return task is not null && (bool)((dynamic)task).Enabled && Matches((string)((dynamic)task).Xml, executable, userId); }
            finally { if (task is not null) Marshal.FinalReleaseComObject(task); }
        });
    }
    internal static void SetEnabled(string executable, bool enabled)
    {
        var userId = UserId;
        WithFolder<bool>(folder =>
        {
            var name = TaskName(executable, userId);
            object? task = Find(folder, name);
            try
            {
                if (task is not null && !Matches((string)((dynamic)task).Xml, executable, userId))
                    throw new InvalidOperationException("同名计划任务配置不同，未覆盖；请检查任务计划程序");
                if (enabled)
                {
                    // InteractiveToken stores no password; registration is authorized by the elevated app.
                    object registered = folder.RegisterTask(name, TaskXml(executable, userId), 6, userId, null, 3, null);
                    Marshal.FinalReleaseComObject(registered);
                }
                else if (task is not null) folder.DeleteTask(name, 0);
                return true;
            }
            finally { if (task is not null) Marshal.FinalReleaseComObject(task); }
        });
        if (IsEnabled(executable) != enabled) throw new InvalidOperationException("开机启动计划任务验证失败");
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        RemoveLegacy(key, executable);
    }
    static bool HasLegacy(RegistryKey? key, string executable)
        => string.Equals(key?.GetValue("Peripheral Battery") as string, $"\"{executable}\" --startup", StringComparison.OrdinalIgnoreCase);
    internal static void RemoveLegacy(RegistryKey? key, string executable)
    {
        if (HasLegacy(key, executable)) key!.DeleteValue("Peripheral Battery", false);
    }
    internal static void MigrateLegacy(string executable, RegistryKey? legacy = null)
    {
        if (legacy is not null)
        {
            if (HasLegacy(legacy, executable)) { SetEnabled(executable, true); RemoveLegacy(legacy, executable); }
            return;
        }
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        if (HasLegacy(key, executable)) SetEnabled(executable, true);
    }
}
