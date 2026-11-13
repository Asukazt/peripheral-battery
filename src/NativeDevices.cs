using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PeriDock;

internal record HidDevice(string Path, ushort Product, ushort UsagePage, ushort Usage,
    ushort InputBytes, ushort OutputBytes, ushort FeatureBytes, ushort Vendor = 0x1532,
    string Name = "", string Container = "");

internal static class NativeDevices
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfo
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct InterfaceInfo
    {
        public uint Size;
        public Guid ClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Attributes
    {
        public uint Size;
        public ushort Vendor, Product, Version;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Caps
    {
        public ushort Usage, UsagePage, InputBytes, OutputBytes, FeatureBytes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort Links, InputButtons, InputValues, InputIndices, OutputButtons,
            OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle handle, StringBuilder value, int length);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll", SetLastError = true)] internal static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] data, int length);
    [DllImport("hid.dll", SetLastError = true)] internal static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] data, int length);
    [DllImport("hid.dll", SetLastError = true)] internal static extern bool HidD_FlushQueue(SafeFileHandle handle);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref InterfaceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceInfo info, IntPtr detail, uint length, out uint required, IntPtr device);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceInfo info, IntPtr detail, uint length, out uint required, ref DeviceInfo device);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfo info, uint property, out uint type, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    internal static SafeFileHandle Open(string path, bool overlapped = false, bool metadataOnly = false)
    {
        var handle = CreateFileW(path, metadataOnly ? 0 : 0xc0000000, 3, IntPtr.Zero, 3,
            overlapped ? 0x40000000u : 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "无法打开设备接口");
        }
        return handle;
    }

    internal static List<HidDevice> EnumerateHid(ushort vendor = 0x1532)
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        var result = new List<HidDevice>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new InterfaceInfo { Size = (uint)Marshal.SizeOf<InterfaceInfo>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref info)) break;
                SetupDiGetDeviceInterfaceDetailW(set, ref info, IntPtr.Zero, 0, out var needed, IntPtr.Zero);
                if (needed < 8 || needed > 65536) continue;
                var detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    var deviceInfo = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref info, detail, needed, out _, ref deviceInfo)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4) ?? "";
                    if (!path.Contains($"vid_{vendor:x4}", StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle = Open(path, metadataOnly: true);
                    var attrs = new Attributes { Size = (uint)Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attrs) || attrs.Vendor != vendor) continue;
                    var productName = new StringBuilder(256);
                    HidD_GetProductString(handle, productName, productName.Capacity * 2);
                    var containerKey = new PropertyKey(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
                    var containerBytes = new byte[16];
                    var container = SetupDiGetDevicePropertyW(set, ref deviceInfo, ref containerKey, out var containerType,
                        containerBytes, 16, out var containerSize, 0) && containerType == 13 && containerSize == 16
                        ? new Guid(containerBytes).ToString("N") : "";
                    if (!HidD_GetPreparsedData(handle, out var pp)) continue;
                    try
                    {
                        if (HidP_GetCaps(pp, out var caps) >= 0)
                            result.Add(new(path, attrs.Product, caps.UsagePage, caps.Usage,
                                caps.InputBytes, caps.OutputBytes, caps.FeatureBytes, vendor, productName.ToString().Trim(), container));
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                catch (Win32Exception) { }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    internal static int? KeyboardWindowsBattery(string expectedName = "AZOTH X")
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, "BTHLE", IntPtr.Zero, 6);
        if (set == new IntPtr(-1)) return null;
        try
        {
            for (uint index = 0; ; index++)
            {
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info)) break;
                var text = new byte[2048];
                if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, 12, out _, text, (uint)text.Length, out _)) continue;
                var name = Encoding.Unicode.GetString(text).TrimEnd('\0');
                if (!name.Contains(expectedName, StringComparison.OrdinalIgnoreCase)) continue;
                var key = new PropertyKey(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2);
                var bytes = new byte[8];
                if (SetupDiGetDevicePropertyW(set, ref info, ref key, out var type, bytes, 8, out var size, 0)
                    && size >= 1 && type is 3 or 7 && bytes[0] <= 100) return bytes[0];
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }
}
