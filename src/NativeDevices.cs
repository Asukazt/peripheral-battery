#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace PeriDock;

internal static class NativeDevices
{
	internal struct DeviceInfo
	{
		public uint Size;

		public Guid ClassGuid;

		public uint DevInst;

		public nint Reserved;
	}

	private struct InterfaceInfo
	{
		public uint Size;

		public Guid ClassGuid;

		public uint Flags;

		public nint Reserved;
	}

	private struct Attributes
	{
		public uint Size;

		public ushort Vendor;

		public ushort Product;

		public ushort Version;
	}

	private struct Caps
	{
		public ushort Usage;

		public ushort UsagePage;

		public ushort InputBytes;

		public ushort OutputBytes;

		public ushort FeatureBytes;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
		public ushort[] Reserved;

		public ushort Links;

		public ushort InputButtons;

		public ushort InputValues;

		public ushort InputIndices;

		public ushort OutputButtons;

		public ushort OutputValues;

		public ushort OutputIndices;

		public ushort FeatureButtons;

		public ushort FeatureValues;

		public ushort FeatureIndices;
	}

	private struct PropertyKey
	{
		public Guid Format;

		public uint Id;

		public PropertyKey(Guid format, uint id)
		{
			Format = format;
			Id = id;
		}
	}

	[DllImport("hid.dll")]
	private static extern void HidD_GetHidGuid(out Guid guid);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);

	[DllImport("hid.dll", CharSet = CharSet.Unicode)]
	private static extern bool HidD_GetProductString(SafeFileHandle handle, StringBuilder value, int length);

	[DllImport("hid.dll", SetLastError = true)]
	private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);

	[DllImport("hid.dll")]
	private static extern bool HidD_FreePreparsedData(nint data);

	[DllImport("hid.dll")]
	private static extern int HidP_GetCaps(nint data, out Caps caps);

	[DllImport("hid.dll", SetLastError = true)]
	internal static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] data, int length);

	[DllImport("hid.dll", SetLastError = true)]
	internal static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] data, int length);

	[DllImport("hid.dll", SetLastError = true)]
	internal static extern bool HidD_FlushQueue(SafeFileHandle handle);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint SetupDiGetClassDevsW(nint guid, string? enumerator, nint parent, uint flags);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint SetupDiGetClassDevsW(ref Guid guid, string? enumerator, nint parent, uint flags);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceInfo info);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref InterfaceInfo info, nint detail, uint length, out uint required, nint device);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, ref InterfaceInfo info, nint detail, uint length, out uint required, ref DeviceInfo device);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiDestroyDeviceInfoList(nint set);

	[DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceInfo info);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDeviceRegistryPropertyW(nint set, ref DeviceInfo info, uint property, out uint type, byte[] buffer, uint size, out uint required);

	[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDevicePropertyW(nint set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] buffer, uint size, out uint required, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

	internal static SafeFileHandle Open(string path, bool overlapped = false, bool metadataOnly = false)
	{
		SafeFileHandle safeFileHandle = CreateFileW(path, (!metadataOnly) ? 3221225472u : 0u, 3u, IntPtr.Zero, 3u, overlapped ? 1073741824u : 0u, IntPtr.Zero);
		if (safeFileHandle.IsInvalid)
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			safeFileHandle.Dispose();
			throw new Win32Exception(lastWin32Error, "无法打开设备接口");
		}
		return safeFileHandle;
	}

	internal static List<HidDevice> EnumerateHid(ushort vendor = 5426)
	{
		HidD_GetHidGuid(out var guid);
		nint num = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 18u);
		if (num == new IntPtr(-1))
		{
			throw new Win32Exception();
		}
		List<HidDevice> list = new List<HidDevice>();
		try
		{
			uint num2 = 0u;
			while (true)
			{
				InterfaceInfo interfaceInfo = default(InterfaceInfo);
				interfaceInfo.Size = (uint)Marshal.SizeOf<InterfaceInfo>();
				InterfaceInfo info = interfaceInfo;
				if (!SetupDiEnumDeviceInterfaces(num, IntPtr.Zero, ref guid, num2, ref info))
				{
					break;
				}
				SetupDiGetDeviceInterfaceDetailW(num, ref info, IntPtr.Zero, 0u, out var required, IntPtr.Zero);
				if (required >= 8 && required <= 65536)
				{
					nint num3 = Marshal.AllocHGlobal((int)required);
					try
					{
						Marshal.WriteInt32(num3, (IntPtr.Size == 8) ? 8 : 6);
						DeviceInfo deviceInfo = default(DeviceInfo);
						deviceInfo.Size = (uint)Marshal.SizeOf<DeviceInfo>();
						DeviceInfo device = deviceInfo;
						if (SetupDiGetDeviceInterfaceDetailW(num, ref info, num3, required, out var required2, ref device))
						{
							string path = Marshal.PtrToStringUni(num3 + 4) ?? "";
							Match match = Regex.Match(path, "(?i)vid_([0-9a-f]{4})&pid_");
							if (match.Success)
							{
								ushort actualVendor = Convert.ToUInt16(match.Groups[1].Value, 16);
								if (!((vendor == 0) ? (!DeviceCatalog.Vendors.Contains(actualVendor)) : (actualVendor != vendor)))
								{
									PropertyKey key = new PropertyKey(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2u);
									byte[] array = new byte[16];
									uint type;
									uint required3;
									string container = ((SetupDiGetDevicePropertyW(num, ref device, ref key, out type, array, 16u, out required3, 0u) && type == 13 && required3 == 16) ? new Guid(array).ToString("N") : "");
									byte[] array2 = new byte[2048];
									uint required4;
									string name = (SetupDiGetDeviceRegistryPropertyW(num, ref device, 0u, out required2, array2, (uint)array2.Length, out required4) ? Encoding.Unicode.GetString(array2).TrimEnd('\0') : "");
									HidDevice hidDevice = InspectHidInterface(path, actualVendor, name, container, delegate
									{
										using SafeFileHandle handle = Open(path, overlapped: false, metadataOnly: true);
										Attributes attributes = default(Attributes);
										attributes.Size = (uint)Marshal.SizeOf<Attributes>();
										Attributes attributes2 = attributes;
										if (!HidD_GetAttributes(handle, ref attributes2))
										{
											throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 HID 标识");
										}
										if (attributes2.Vendor == actualVendor)
										{
											StringBuilder stringBuilder = new StringBuilder(256);
											HidD_GetProductString(handle, stringBuilder, stringBuilder.Capacity * 2);
											if (!HidD_GetPreparsedData(handle, out var data))
											{
												throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 HID 接口能力");
											}
											try
											{
												Caps caps;
												return (HidP_GetCaps(data, out caps) >= 0) ? new HidDevice(path, attributes2.Product, caps.UsagePage, caps.Usage, caps.InputBytes, caps.OutputBytes, caps.FeatureBytes, actualVendor, (stringBuilder.Length > 0) ? stringBuilder.ToString().Trim() : name, container) : null;
											}
											finally
											{
												HidD_FreePreparsedData(data);
											}
										}
										return (HidDevice?)null;
									}, new Diagnostics(AppContext.BaseDirectory));
									if ((object)hidDevice != null)
									{
										list.Add(hidDevice);
									}
								}
							}
						}
					}
					finally
					{
						Marshal.FreeHGlobal(num3);
					}
				}
				num2++;
			}
			return list;
		}
		finally
		{
			SetupDiDestroyDeviceInfoList(num);
		}
	}

	internal static HidDevice? InspectHidInterface(string path, ushort vendor, string name, string container, Func<HidDevice?> inspect, Diagnostics log)
	{
		try
		{
			return inspect();
		}
		catch (Win32Exception ex)
		{
			Match match = Regex.Match(path, $"(?i)vid_{vendor:x4}&pid_([0-9a-f]{{4}})(?:[&#\\\\]|$)");
			if (!match.Success)
			{
				throw;
			}
			ushort product = Convert.ToUInt16(match.Groups[1].Value, 16);
			HidDevice hidDevice = new HidDevice(path, product, 0, 0, 0, 0, 0, vendor, name, container, ex.NativeErrorCode);
			log.HidFailure(hidDevice, "enumerate", ex);
			return hidDevice;
		}
	}

	internal static int? KeyboardWindowsBattery(string expectedName = "AZOTH X")
	{
		nint num = SetupDiGetClassDevsW(IntPtr.Zero, "BTHLE", IntPtr.Zero, 6u);
		if (num == new IntPtr(-1))
		{
			return null;
		}
		try
		{
			uint num2 = 0u;
			while (true)
			{
				DeviceInfo deviceInfo = default(DeviceInfo);
				deviceInfo.Size = (uint)Marshal.SizeOf<DeviceInfo>();
				DeviceInfo info = deviceInfo;
				if (!SetupDiEnumDeviceInfo(num, num2, ref info))
				{
					break;
				}
				byte[] array = new byte[2048];
				if (SetupDiGetDeviceRegistryPropertyW(num, ref info, 12u, out var _, array, (uint)array.Length, out var _) && Encoding.Unicode.GetString(array).TrimEnd('\0').Contains(expectedName, StringComparison.OrdinalIgnoreCase))
				{
					PropertyKey key = new PropertyKey(new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2u);
					byte[] array2 = new byte[8];
					uint type2;
					uint required2;
					bool flag = SetupDiGetDevicePropertyW(num, ref info, ref key, out type2, array2, 8u, out required2, 0u) && required2 >= 1;
					if (flag)
					{
						bool flag2 = ((type2 == 3 || type2 == 7) ? true : false);
						flag = flag2;
					}
					if (flag && array2[0] <= 100)
					{
						return array2[0];
					}
				}
				num2++;
			}
		}
		finally
		{
			SetupDiDestroyDeviceInfoList(num);
		}
		return null;
	}
}
