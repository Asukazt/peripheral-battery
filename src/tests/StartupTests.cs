using System.Xml.Linq;
using Microsoft.Win32;
using System.Security.Principal;

namespace PeriDock;

internal static class StartupTests
{
    internal static void Run(Action<string, Action> check, bool integration)
    {
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        const string sid = "S-1-5-21-123-456-789-1001";
        const string exe = @"D:\Battery & Icons\PeriDock.exe";
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        check("startup task runs only for the current interactive user with highest privileges", () =>
        {
            var xml = XDocument.Parse(StartupSettings.TaskXml(exe, sid));
            Require(xml.Descendants(ns + "LogonTrigger").Single().Element(ns + "UserId")!.Value == sid, "Wrong logon user");
            Require(xml.Descendants(ns + "LogonType").Single().Value == "InteractiveToken", "Password or background session requested");
            Require(xml.Descendants(ns + "RunLevel").Single().Value == "HighestAvailable", "Administrator startup missing");
            Require(xml.Descendants(ns + "Command").Single().Value == exe, "Executable escaped incorrectly");
            Require(xml.Descendants(ns + "Arguments").Single().Value == "--startup", "Wrong startup arguments");
            Require(xml.Descendants(ns + "WorkingDirectory").Single().Value == @"D:\Battery & Icons", "Wrong working directory");
            Require(xml.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value == "false", "Battery startup blocked");
            Require(xml.Descendants(ns + "StopIfGoingOnBatteries").Single().Value == "false", "Battery power would stop the app");
            Require(xml.Descendants(ns + "ExecutionTimeLimit").Single().Value == "PT0S", "Long-running app would be terminated");
            Require(StartupSettings.Matches(xml.ToString(), exe, sid), "Own task not recognized");
        });
        check("startup tasks distinguish installation paths and user identities", () =>
        {
            Require(StartupSettings.TaskName(exe, sid) == StartupSettings.TaskName(exe.ToLowerInvariant(), sid), "Case changed task name");
            Require(StartupSettings.TaskName(exe, sid) != StartupSettings.TaskName(@"D:\Other\PeriDock.exe", sid), "Other install collided");
            Require(StartupSettings.TaskName(exe, sid) != StartupSettings.TaskName(exe, sid + "1"), "Other user collided");
            Require(!StartupSettings.Matches(StartupSettings.TaskXml(exe, sid), @"D:\Other\PeriDock.exe", sid), "Other install accepted");
            Require(!StartupSettings.Matches(StartupSettings.TaskXml(exe, sid), exe, sid + "1"), "Other user accepted");
        });
        check("startup status rejects extra actions, arguments, wrong privilege and disabled triggers", () =>
        {
            foreach (var mutation in new Action<XDocument>[] {
                x => x.Descendants(ns + "Arguments").Single().Value = "--exit",
                x => x.Descendants(ns + "RunLevel").Single().Value = "LeastPrivilege",
                x => x.Descendants(ns + "LogonType").Single().Value = "Password",
                x => x.Descendants(ns + "LogonTrigger").Single().Element(ns + "Enabled")!.Value = "false",
                x => x.Descendants(ns + "Actions").Single().Add(new XElement(ns + "Exec", new XElement(ns + "Command", "other.exe"))),
                x => x.Descendants(ns + "Triggers").Single().RemoveNodes()
            })
            {
                var xml = XDocument.Parse(StartupSettings.TaskXml(exe, sid)); mutation(xml);
                Require(!StartupSettings.Matches(xml.ToString(), exe, sid), "Invalid task reported as enabled");
            }
        });
        check("Windows-normalized account names and omitted default-enabled triggers still match", () =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            var xml = XDocument.Parse(StartupSettings.TaskXml(exe, identity.User!.Value));
            var trigger = xml.Descendants(ns + "LogonTrigger").Single();
            trigger.Element(ns + "UserId")!.Value = identity.Name;
            trigger.Element(ns + "Enabled")!.Remove();
            Require(StartupSettings.Matches(xml.ToString(), exe, identity.User.Value), "Scheduler normalization lost startup status");
        });
        if (!integration) return;
        check("real scheduled tasks enable, disable and preserve other installations; legacy startup migrates", () =>
        {
            Require(Elevation.IsElevated, "Integration needs an elevated test process");
            var root = Path.Combine(Path.GetTempPath(), "PeriDock-startup-test-" + Guid.NewGuid().ToString("N"));
            var first = Path.Combine(root, "a", "PeriDock.exe");
            var second = Path.Combine(root, "b", "PeriDock.exe");
            var keyPath = @"Software\PeriDock-startup-test-" + Guid.NewGuid().ToString("N");
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(keyPath);
                key.SetValue("Unrelated", "keep");
                key.SetValue("Peripheral Battery", $"\"{first}\" --startup");
                StartupSettings.MigrateLegacy(second, key);
                Require(!StartupSettings.IsEnabled(second), "Unrelated legacy install migrated");
                StartupSettings.MigrateLegacy(first, key);
                Require(StartupSettings.IsEnabled(first), "Legacy migration did not enable task");
                Require(key.GetValue("Peripheral Battery") is null, "Legacy Run entry not removed");
                StartupSettings.SetEnabled(second, true);
                StartupSettings.SetEnabled(first, false);
                Require(!StartupSettings.IsEnabled(first), "Task disable failed");
                Require(StartupSettings.IsEnabled(second), "Other installation was disabled");
                Require((string?)key.GetValue("Unrelated") == "keep", "Unrelated entry changed");
            }
            finally
            {
                StartupSettings.SetEnabled(first, false);
                StartupSettings.SetEnabled(second, false);
                Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
            }
        });
    }
}
