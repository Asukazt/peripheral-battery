using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace PeriDock;

internal static class Elevation
{
    internal static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    internal static bool Start(string executable, int parentId, Func<ProcessStartInfo, Process?>? launch = null)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory
        };
        info.ArgumentList.Add("--wait-for-exit");
        info.ArgumentList.Add(parentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            using var child = (launch ?? Process.Start)(info);
            return child is not null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return false; }
    }

    internal static bool WaitForParent(string value)
    {
        if (!int.TryParse(value, out var id) || id <= 0 || id == Environment.ProcessId) return false;
        try
        {
            using var parent = Process.GetProcessById(id);
            if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
            return parent.WaitForExit(10000);
        }
        catch (ArgumentException) { return true; } // The original instance already exited.
        catch (Win32Exception) { return false; }
        catch (InvalidOperationException) { return true; }
    }
}
