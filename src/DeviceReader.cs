#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace PeriDock;

internal static class DeviceReader
{
	internal record AncReading(bool Success, int Status, int Level, string Message);

	internal static Task<AncReading> ReadHeadsetAnc() => ReadAncCore(set: null);
	internal static Task<AncReading> SetHeadsetAnc(int status, int level) => ReadAncCore((status, level));
	internal static bool HasHeadsetAncDevice()
	{
		try { return NativeDevices.EnumerateHid().Any(IsAncDevice); }
		catch { return false; }
	}

	private static bool IsAncDevice(HidDevice device)
		=> device.Product is 0x0576 or 0x0577 && device.UsagePage == 0xff14 &&
			device.Usage == 1 && device.InputBytes == 64 && device.OutputBytes == 64;

	private static async Task<AncReading> ReadAncCore((int Status, int Level)? set)
	{
		var devices = NativeDevices.EnumerateHid().Where(IsAncDevice)
			.OrderByDescending(d => d.Product == 0x0576).ToList();
		if (devices.Count == 0) return new(false, 0, 4, "耳机未连接");
		Exception? last = null;
		foreach (var device in devices)
		{
			try
			{
				using var handle = NativeDevices.Open(device.Path, overlapped: true);
				await using var stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
				if (set is { } target)
				{
					var request = Protocols.HeadsetAncSetRequest(target.Status, target.Level);
					if (await HeadsetAncExchange(stream, request, Protocols.HeadsetAncSetSucceeded) is null)
						throw new IOException("耳机未确认降噪设置");
					await Task.Delay(250);
				}
				var response = await HeadsetAncExchange(stream, Protocols.HeadsetAncRequest(), packet => Protocols.HeadsetAnc(packet).HasValue);
				if (response is null) throw new IOException("耳机未返回降噪状态");
				var state = Protocols.HeadsetAnc(response)!.Value;
				return new(true, state.Status, state.Level, "");
			}
			catch (Exception ex) when (ex is IOException or Win32Exception or OperationCanceledException)
			{
				last = ex;
			}
		}
		return new(false, 0, 4, last?.Message ?? "降噪状态读取失败");
	}

	private static async Task<byte[]?> HeadsetAncExchange(FileStream stream, byte[] request, Func<byte[], bool> accept)
	{
		using var timeout = new CancellationTokenSource(1500);
		await stream.WriteAsync(request, timeout.Token);
		while (!timeout.IsCancellationRequested)
		{
			var response = new byte[64];
			var count = await stream.ReadAsync(response, timeout.Token);
			if (count == 0) throw new IOException("耳机接口已关闭");
			if (count == 64 && accept(response)) return response;
		}
		return null;
	}

	internal static (int X, int Y) SetMouseDpi(DeviceTarget target, int x, int y)
	{
		if (x is < 50 or > 35000 || y is < 50 or > 35000) throw new ArgumentOutOfRangeException(nameof(x), "DPI 范围为 50–35000");
		var actual = DeviceCatalog.FromHid(NativeDevices.EnumerateHid(0))
			.FirstOrDefault(t => t.Id == target.Id) ?? target;
		var devices = actual.Interfaces.Where(d => d.FeatureBytes >= 91).ToList();
		if (devices.Count == 0) throw new IOException("未发现鼠标接口");
		Exception? last = null;
		foreach (var device in devices)
		{
			try
			{
				using var handle = NativeDevices.Open(device.Path, metadataOnly: true);
				var request = Protocols.MouseDpiSetRequest(x, y, actual.Transaction);
				if (!NativeDevices.HidD_SetFeature(handle, request, request.Length))
					throw new Win32Exception(Marshal.GetLastWin32Error(), "设备不接受 DPI 设置");
				Thread.Sleep(100);
				var deadline = Stopwatch.StartNew();
				var acknowledged = false;
				while (deadline.ElapsedMilliseconds < 700)
				{
					var response = new byte[91];
					if (!NativeDevices.HidD_GetFeature(handle, response, response.Length))
						throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 DPI 应答");
					if (Protocols.MouseDpiSetMatches(response, actual.Transaction)) { acknowledged = true; break; }
					Thread.Sleep(70);
				}
				if (!acknowledged) throw new IOException("设备未确认 DPI 写入");
				var readback = Protocols.MouseDpi(MouseQuery(handle, 4, 0x85, 7, actual.Transaction), actual.Transaction);
				if (readback is null || readback.Value.X != x || readback.Value.Y != y)
					throw new IOException("已发送 DPI，但设备未回报新值");
				return readback.Value;
			}
			catch (Exception ex) when (ex is IOException or Win32Exception) { last = ex; }
		}
		throw last ?? new IOException("DPI 设置失败");
	}

	internal static async Task<Reading> Probe(string id)
	{
		return id switch
		{
			"keyboard" => await ReadKeyboard(), 
			"mouse" => ReadMouse(), 
			"headset" => await ReadHeadset(), 
			_ => await ProbeTarget(id), 
		};
	}

	internal static async Task<Reading> RunIsolated(string id, CancellationToken token)
	{
		using Process process = new Process
		{
			StartInfo = new ProcessStartInfo(Environment.ProcessPath, "--probe " + id)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				WorkingDirectory = AppContext.BaseDirectory
			}
		};
		process.Start();
		Task<string> output = process.StandardOutput.ReadToEndAsync(token);
		Task<string> errors = process.StandardError.ReadToEndAsync(token);
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
		deadline.CancelAfter(TimeSpan.FromSeconds(10.0));
		Reading result;
		try
		{
			await process.WaitForExitAsync(deadline.Token);
			if (process.ExitCode != 0)
			{
				throw new IOException("设备查询失败：" + await errors);
			}
			result = JsonSerializer.Deserialize<Reading>(await output) ?? throw new IOException("设备返回了空结果");
		}
		finally
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None);
			}
		}
		return result;
	}

	private static async Task<Reading> ReadKeyboard()
	{
		(Reading? Value, string? Issue) hid;
		try
		{
			hid = await ReadKeyboardHid();
		}
		catch (Exception error) when (Diagnostics.NativeError(error).GetValueOrDefault() == 5)
		{
			hid = (Value: null, Issue: "ROG HID 访问被拒绝（Windows 错误 5）");
		}
		if ((object)hid.Value != null)
		{
			return hid.Value;
		}
		int? cached = NativeDevices.KeyboardWindowsBattery();
		DeviceInformation deviceInformation = (await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(pairingState: true))).FirstOrDefault((DeviceInformation d) => d.Name.Contains("AZOTH X", StringComparison.OrdinalIgnoreCase));
		if ((object)deviceInformation == null)
		{
			return KeyboardFallback(cached, hid.Issue ?? "未发现键盘", (hid.Issue == null) ? new bool?(false) : null);
		}
		using BluetoothLEDevice device = await BluetoothLEDevice.FromIdAsync(deviceInformation.Id);
		if ((object)device == null)
		{
			return KeyboardFallback(cached, hid.Issue ?? "连接状态暂不可读", null);
		}
		if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
		{
			return KeyboardFallback(cached, hid.Issue ?? "蓝牙未连接", (hid.Issue == null) ? new bool?(false) : null);
		}
		GattDeviceServicesResult services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
		int? percent = null;
		bool? charging = null;
		string chargingNote = "蓝牙充电状态暂不可读";
		if (services.Status == GattCommunicationStatus.Success)
		{
			try
			{
				foreach (GattDeviceService service in services.Services)
				{
					GattCharacteristicsResult gattCharacteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
					if (gattCharacteristicsResult.Status != 0)
					{
						continue;
					}
					chargingNote = "蓝牙未提供充电状态";
					foreach (GattCharacteristic characteristic in gattCharacteristicsResult.Characteristics)
					{
						Guid uuid = characteristic.Uuid;
						ushort code = (ushort)((uuid == GattCharacteristicUuids.BatteryLevel) ? 10777 : ((uuid == new Guid("00002a1a-0000-1000-8000-00805f9b34fb")) ? 10778 : ((uuid == new Guid("00002bed-0000-1000-8000-00805f9b34fb")) ? 11245 : 0)));
						if (code == 0 || !characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
						{
							continue;
						}
						if (code != 10777)
						{
							chargingNote = "蓝牙充电状态暂不可读";
						}
						try
						{
							GattReadResult gattReadResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
							if (gattReadResult.Status != 0 || gattReadResult.Value == null || gattReadResult.Value.Length > 16)
							{
								continue;
							}
							using DataReader dataReader = DataReader.FromBuffer(gattReadResult.Value);
							byte[] array = new byte[gattReadResult.Value.Length];
							dataReader.ReadBytes(array);
							if (code == 10777 && array.Length == 1 && array[0] <= 100)
							{
								percent = array[0];
								continue;
							}
							bool? flag = charging;
							if (!flag.HasValue)
							{
								charging = Protocols.BluetoothCharging(code, array);
							}
						}
						catch (Exception ex) when (((ex is IOException || ex is COMException) ? 1 : 0) != 0)
						{
						}
					}
				}
			}
			finally
			{
				foreach (GattDeviceService service2 in services.Services)
				{
					service2.Dispose();
				}
			}
		}
		return new Reading(percent ?? cached, charging, null, null, percent.HasValue ? "蓝牙 GATT · Battery Level" : "Windows 蓝牙属性（系统缓存）", "蓝牙已连接" + ((hid.Issue == null) ? "" : ("；" + hid.Issue)), !percent.HasValue, "蓝牙", true, charging.HasValue ? null : chargingNote);
	}

	private static Reading KeyboardFallback(int? cached, string status, bool? connected)
	{
		return new Reading(cached, null, null, null, "Windows 蓝牙属性（系统缓存）", status, Cached: true, (connected == false) ? "未连接" : "待确认", connected);
	}

	private static async Task<byte[]> KeyboardQuery(HidDevice device, byte report, byte command, byte sub)
	{
		using SafeFileHandle handle = NativeDevices.Open(device.Path, overlapped: true);
		FileStream stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
		byte[] result;
		try
		{
			NativeDevices.HidD_FlushQueue(handle);
			using CancellationTokenSource timeout = new CancellationTokenSource(1000);
			await stream.WriteAsync(Protocols.KeyboardRequest(report, command, sub, device.OutputBytes), timeout.Token);
			byte[] subArray;
			while (true)
			{
				byte[] response = new byte[device.InputBytes];
				int num = await stream.ReadAsync(response, timeout.Token);
				if (num == 0)
				{
					throw new IOException("键盘状态接口已关闭");
				}
				subArray = response[..num];
				if (Protocols.KeyboardMatches(subArray, report, command, sub))
				{
					break;
				}
				if (num >= 3 && subArray[0] == report && subArray[1] == byte.MaxValue && subArray[2] == 170)
				{
					throw new IOException("键盘休眠或未连接");
				}
			}
			result = subArray;
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
		return result;
	}

	private static async Task<(Reading? Value, string? Issue)> ReadKeyboardHid(IEnumerable<HidDevice>? selected = null)
	{
		List<HidDevice> devices = (selected ?? NativeDevices.EnumerateHid(2821)).ToList();
		List<HidDevice> list = (from d in devices.Where(delegate(HidDevice d)
			{
				bool flag2 = d.UsagePage == 65280 && d.Usage == 1;
				if (flag2)
				{
					ushort product = d.Product;
					bool flag3 = (uint)(product - 7204) <= 1u;
					flag2 = (flag3 && d.InputBytes == 65 && d.OutputBytes == 65) || (d.Product == 6862 && d.InputBytes == 64 && d.OutputBytes == 64);
				}
				return flag2;
			})
			orderby d.Product == 7204 descending
			select d).ToList();
		string issue = ((devices.Count > 0 && list.Count == 0) ? "发现 ROG 接口，状态待确认" : null);
		Exception denied = null;
		foreach (HidDevice device in list)
		{
			try
			{
				byte report = (byte)((device.Product == 6862) ? 2 : 0);
				if (report != 2)
				{
					goto IL_0223;
				}
				HidDevice hidDevice = devices.FirstOrDefault((HidDevice d) => d.Product == 6862 && d.UsagePage == 65282 && d.Usage == 1 && d.InputBytes == 64 && d.OutputBytes == 64);
				bool flag = (object)hidDevice == null;
				if (!flag)
				{
					flag = !Protocols.OmniHasAzothX(await KeyboardQuery(hidDevice, 1, 160, 0));
				}
				if (!flag)
				{
					goto IL_0223;
				}
				goto end_IL_00e6;
				IL_0223:
				string connection = Protocols.KeyboardConnection(await KeyboardQuery(device, report, 18, 3), report);
				if (connection != null)
				{
					(int, bool?)? tuple = Protocols.KeyboardPower(await KeyboardQuery(device, report, 18, 1), report);
					if (tuple.HasValue)
					{
						return (Value: new Reading(tuple.Value.Item1, tuple.Value.Item2, null, null, "ROG HID · 连接 / 电源状态", "已连接", Cached: false, connection, true), Issue: null);
					}
					issue = "ROG 电量应答无效";
				}
				end_IL_00e6:;
			}
			catch (Exception ex) when (((ex is IOException || ex is Win32Exception || ex is OperationCanceledException) ? 1 : 0) != 0)
			{
				new Diagnostics(AppContext.BaseDirectory).HidFailure(device, "keyboard-query", ex);
				if (Diagnostics.NativeError(ex).GetValueOrDefault() == 5)
				{
					denied = ex;
				}
				issue = ((ex is OperationCanceledException) ? "ROG 状态查询超时" : Diagnostics.Redact(ex.Message));
			}
		}
		if (denied != null)
		{
			throw denied;
		}
		return (Value: null, Issue: issue);
	}

	private static byte[] MouseQuery(SafeFileHandle handle, byte commandClass, byte command, byte size, byte transaction = 31)
	{
		byte[] array = Protocols.MouseRequest(commandClass, command, size, transaction);
		if (!NativeDevices.HidD_SetFeature(handle, array, array.Length))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "设备不接受查询");
		}
		Thread.Sleep(60);
		Stopwatch stopwatch = Stopwatch.StartNew();
		int num = 0;
		do
		{
			byte[] array2 = new byte[91];
			if (!NativeDevices.HidD_GetFeature(handle, array2, array2.Length))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取设备应答");
			}
			if (Protocols.MouseMatches(array2, commandClass, command, transaction))
			{
				return array2;
			}
			bool flag = array2[2] == transaction && array2[7] == commandClass && array2[8] == command;
			if (flag)
			{
				byte b = array2[1];
				bool flag2 = (uint)(b - 3) <= 2u;
				flag = flag2;
			}
			if (flag)
			{
				return array2;
			}
			if (++num == 4)
			{
				NativeDevices.HidD_SetFeature(handle, array, array.Length);
			}
			Thread.Sleep(70);
		}
		while (stopwatch.ElapsedMilliseconds < 700);
		throw new IOException("未收到匹配的应答，设备可能休眠或接口正被其他软件使用");
	}

	private static Reading ReadMouse(IEnumerable<HidDevice>? selected = null, DeviceProfile? profile = null)
	{
		byte b = profile?.Transaction ?? 31;
		List<HidDevice> list = (selected ?? NativeDevices.EnumerateHid(5426).Where(delegate(HidDevice d)
		{
			ushort product = d.Product;
			return (uint)(product - 192) <= 1u;
		})).Where((HidDevice d) => d.FeatureBytes >= 91).OrderByDescending(delegate(HidDevice d)
		{
			int? num3 = DeviceCatalog.RazerInterface(d.Product);
			if (num3.HasValue)
			{
				int valueOrDefault = num3.GetValueOrDefault();
				return d.Path.Contains($"mi_{valueOrDefault:x2}", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}).ThenByDescending((HidDevice d) => d.UsagePage == 65280)
			.ToList();
		if (list.Count == 0)
		{
			return new Reading(null, null, null, null, "Razer HID", (selected == null) ? "未发现鼠标接口" : "已识别型号，但未找到可用的电量接口", Cached: false, (selected == null) ? "未连接" : (profile?.Connection ?? "待确认"), (selected == null) ? new bool?(false) : null, null, (selected != null) ? ReadFailure.ProtocolUnsupported : ReadFailure.None);
		}
		Exception ex = null;
		bool flag = false;
		foreach (HidDevice item in list)
		{
			try
			{
				using SafeFileHandle handle = NativeDevices.Open(item.Path, overlapped: false, metadataOnly: true);
				byte[] array = MouseQuery(handle, 7, 128, 2, b);
				int? num = Protocols.MouseValue(array, 7, 128, b);
				if (!num.HasValue)
				{
					if (array[1] == 4)
					{
						flag = true;
					}
					continue;
				}
				bool? charging = null;
				(int, int)? tuple = null;
				try
				{
					int? num2 = Protocols.MouseValue(MouseQuery(handle, 7, 132, 2, b), 7, 132, b);
					charging = ((num2.HasValue && num2.GetValueOrDefault() == 0) ? new bool?(false) : ((num2.HasValue && num2.GetValueOrDefault() == 1) ? new bool?(true) : null));
				}
				catch (IOException)
				{
				}
				catch (Win32Exception error)
				{
					new Diagnostics(AppContext.BaseDirectory).HidFailure(item, "charging", error);
				}
				try
				{
					if ((object)profile == null || profile.Kind == "mouse")
					{
						tuple = Protocols.MouseDpi(MouseQuery(handle, 4, 133, 7, b), b);
					}
				}
				catch (IOException)
				{
				}
				catch (Win32Exception error2)
				{
					new Diagnostics(AppContext.BaseDirectory).HidFailure(item, "dpi", error2);
				}
				return new Reading((int)Math.Round((double)num.Value * 100.0 / 255.0), charging, tuple?.Item1, tuple?.Item2, "Razer HID · 电量 / DPI 读取", "已连接", Cached: false, profile?.Connection ?? Protocols.RazerConnection(item.Product), true);
			}
			catch (Exception ex4) when (((ex4 is IOException || ex4 is Win32Exception) ? 1 : 0) != 0)
			{
				new Diagnostics(AppContext.BaseDirectory).HidFailure(item, "mouse-query", ex4);
				if (ex4 == null || Diagnostics.NativeError(ex4).GetValueOrDefault() != 5)
				{
					ex = ex4;
				}
			}
		}
		if (ex != null)
		{
			throw ex;
		}
		return new Reading(null, null, null, null, "Razer HID", flag ? "鼠标休眠或未连接" : "设备未响应", Cached: false, flag ? "未连接" : "待确认", flag ? new bool?(false) : null);
	}

	private static async Task<int?> HeadsetQuery(FileStream stream, byte command, byte route)
	{
		using CancellationTokenSource timeout = new CancellationTokenSource(1200);
		await stream.WriteAsync(Protocols.HeadsetRequest(command, route), timeout.Token);
		while (!timeout.IsCancellationRequested)
		{
			byte[] response = new byte[64];
			switch (await stream.ReadAsync(response, timeout.Token))
			{
			case 0:
				throw new IOException("耳机接口已关闭");
			case 64:
			{
				int? result = Protocols.HeadsetValue(response, command, route);
				if (result.HasValue)
				{
					return result;
				}
				break;
			}
			}
		}
		return null;
	}

	private static async Task<Reading> ReadHeadset()
	{
		Task<bool?> bluetoothTask = HeadsetBluetoothConnected();
		List<HidDevice> list = (from d in NativeDevices.EnumerateHid(5426).Where(delegate(HidDevice d)
			{
				ushort product = d.Product;
				bool flag2 = (uint)(product - 1398) <= 1u;
				return flag2 && d.UsagePage == 65300 && d.Usage == 1 && d.InputBytes == 64 && d.OutputBytes == 64;
			})
			orderby d.Product == 1398 descending
			select d).ToList();
		List<Reading> live = new List<Reading>();
		Exception last = null;
		foreach (HidDevice item in list)
		{
			try
			{
				Reading reading = await ReadHeadsetHid(item);
				if (reading.Connected.GetValueOrDefault())
				{
					live.Add(reading);
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is Win32Exception || ex is OperationCanceledException) ? 1 : 0) != 0)
			{
				last = ex;
			}
		}
		bool? flag = await bluetoothTask;
		List<string> list2 = live.Select((Reading r) => r.Connection).Distinct().ToList();
		if (flag.GetValueOrDefault())
		{
			list2.Add("蓝牙");
		}
		if (list2.Count > 0)
		{
			Reading reading2 = live.FirstOrDefault((Reading r) => r.Percent.HasValue) ?? live.FirstOrDefault() ?? new Reading(null, null, null, null, "Windows 蓝牙连接状态", "蓝牙已连接，电量暂不可读");
			return reading2 with
			{
				Connection = string.Join(" + ", list2),
				Connected = true,
				Status = ((list2.Count > 1) ? "多路连接" : "已连接") + ((!flag.HasValue) ? "；本机蓝牙状态待确认" : "") + ((last == null) ? "" : "；另一接口查询失败"),
				ChargingNote = (reading2.Charging.HasValue ? null : ((live.Count == 0) ? "蓝牙充电状态暂不可读" : "充电状态未知"))
			};
		}
		if (last != null)
		{
			throw last;
		}
		return new Reading(null, null, null, null, "BlackShark HID / Windows 蓝牙", (!flag.HasValue) ? "连接状态暂不可读" : "耳机未连接", Cached: false, (!flag.HasValue) ? "待确认" : "未连接", (!flag.HasValue) ? null : new bool?(false));
	}

	private static async Task<bool?> HeadsetBluetoothConnected()
	{
		try
		{
			return await Read().WaitAsync(TimeSpan.FromSeconds(1.5));
		}
		catch (Exception ex) when (((ex is TimeoutException || ex is UnauthorizedAccessException || ex is COMException) ? 1 : 0) != 0)
		{
			return null;
		}
		static async Task<bool> Read()
		{
			return (await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected))).Any((DeviceInformation d) => d.Name.Contains("BlackShark V3 Pro", StringComparison.OrdinalIgnoreCase));
		}
	}

	private static async Task<Reading> ReadHeadsetHid(HidDevice device)
	{
		using SafeFileHandle handle = NativeDevices.Open(device.Path, overlapped: true);
		FileStream stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
		Reading result;
		try
		{
			ushort product = device.Product;
			bool? connected = Protocols.HeadsetConnected(product, await HeadsetQuery(stream, 32, 0));
			if (connected == false)
			{
				result = new Reading(null, null, null, null, "BlackShark HID", "耳机未连接", Cached: false, "未连接", false);
			}
			else
			{
				int? battery = await HeadsetQuery(stream, 33, 128);
				bool? charging = null;
				try
				{
					int? num = await HeadsetQuery(stream, 42, 128);
					charging = ((num.HasValue && num.GetValueOrDefault() == 0) ? new bool?(false) : ((num.HasValue && num.GetValueOrDefault() == 1) ? new bool?(true) : null));
				}
				catch (OperationCanceledException)
				{
				}
				result = new Reading(battery, charging, null, null, "BlackShark HID · Scythe 协议", battery.HasValue ? "已连接" : "未收到电量", Cached: false, Protocols.RazerConnection(device.Product), (connected.GetValueOrDefault() || battery.HasValue) ? new bool?(true) : null);
			}
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
		return result;
	}

	internal static async Task<List<DeviceTarget>> Discover(IEnumerable<DeviceTarget>? known = null)
	{
		IEnumerable<DeviceTarget> known2 = known;
		List<DeviceTarget> targets = DeviceCatalog.FromHid(NativeDevices.EnumerateHid(0));
		DeviceTarget[] receivers = targets.Where((DeviceTarget t) => t.Interfaces.Any((HidDevice d) => DeviceCatalog.ProfileFor(d)?.Reader == "LogitechReceiver")).ToArray();
		List<DeviceTarget>[] array = await Task.WhenAll(receivers.Select((DeviceTarget t) => LogitechReader.Expand(t, known2 ?? Array.Empty<DeviceTarget>())));
		DeviceTarget[] array2 = receivers;
		foreach (DeviceTarget item in array2)
		{
			targets.Remove(item);
		}
		List<DeviceTarget>[] array3 = array;
		foreach (List<DeviceTarget> collection in array3)
		{
			targets.AddRange(collection);
		}
		bool[] array4 = new bool[2] { true, false };
		foreach (bool lowEnergy in array4)
		{
			try
			{
				foreach (DeviceInformation device in (await DeviceInformation.FindAllAsync(lowEnergy ? BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected) : BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected))).Where((DeviceInformation d) => DeviceCatalog.IsBranded(d.Name)))
				{
					if (lowEnergy || !targets.Any((DeviceTarget t) => t.BluetoothId != null && DeviceCatalog.ModelKey(t.Name) == DeviceCatalog.ModelKey(device.Name)))
					{
						targets.Add(new DeviceTarget("bt-" + DeviceCatalog.Key(device.Id), device.Name.Trim(), DeviceCatalog.Kind(device.Name), Supported: true, device.Id, lowEnergy, "", "蓝牙"));
					}
				}
			}
			catch (Exception ex) when (((ex is UnauthorizedAccessException || ex is COMException) ? 1 : 0) != 0)
			{
				new Diagnostics(AppContext.BaseDirectory).Write("error", "bluetooth", "discover", ex.Message, ex);
			}
		}
		return targets;
	}

	internal static async Task<List<DeviceTarget>> DiscoverIsolated(CancellationToken token, IEnumerable<DeviceTarget>? known = null)
	{
		return JsonSerializer.Deserialize<List<DeviceTarget>>(await RunChild("--discover-known", JsonSerializer.Serialize(known?.ToArray() ?? Array.Empty<DeviceTarget>()), token, 12)) ?? throw new IOException("设备列表为空");
	}

	internal static async Task<Reading> RunTargetIsolated(DeviceTarget target, CancellationToken token)
	{
		return JsonSerializer.Deserialize<Reading>(await RunChild("--target-probe", JsonSerializer.Serialize(target), token, 10)) ?? throw new IOException("设备查询结果为空");
	}

	private static async Task<string> RunChild(string command, string? input, CancellationToken token, int seconds)
	{
		using Process process = new Process
		{
			StartInfo = new ProcessStartInfo(Environment.ProcessPath)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = (input != null),
				CreateNoWindow = true,
				WorkingDirectory = AppContext.BaseDirectory
			}
		};
		process.StartInfo.ArgumentList.Add(command);
		process.Start();
		Task<string> output = process.StandardOutput.ReadToEndAsync(token);
		Task<string> errors = process.StandardError.ReadToEndAsync(token);
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
		deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
		string result;
		try
		{
			if (input != null)
			{
				await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
				process.StandardInput.Close();
			}
			await process.WaitForExitAsync(deadline.Token);
			if (process.ExitCode != 0)
			{
				throw new IOException("设备查询失败：" + await errors);
			}
			result = await output;
		}
		finally
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync(CancellationToken.None);
			}
		}
		return result;
	}

	internal static async Task<Reading> ProbeTarget(string id)
	{
		string id2 = id;
		DeviceTarget deviceTarget = (await Discover()).FirstOrDefault((DeviceTarget t) => t.Id == id2);
		return ((object)deviceTarget != null) ? (await ProbeTarget(deviceTarget)) : Missing();
	}

	internal static async Task<Reading> ProbeTarget(DeviceTarget requested)
	{
		DeviceTarget requested2 = requested;
		if (requested2.BluetoothId != null)
		{
			return await ReadBluetooth(requested2);
		}
		List<DeviceTarget> source = DeviceCatalog.FromHid(NativeDevices.EnumerateHid(0));
		DeviceTarget deviceTarget = source.FirstOrDefault((DeviceTarget t) => t.Id == requested2.Id);
		switch (requested2.LogitechSlot)
		{
		case 1:
		case 2:
		case 3:
		case 4:
		case 5:
		case 6:
		{
			if (requested2.ReceiverId == null)
			{
				break;
			}
			DeviceTarget deviceTarget2 = source.FirstOrDefault((DeviceTarget t) => t.Id == requested2.ReceiverId && t.Interfaces.Any((HidDevice d) => DeviceCatalog.ProfileFor(d)?.Reader == "LogitechReceiver"));
			if ((object)deviceTarget2 != null && requested2.Id == LogitechReader.SlotId(deviceTarget2, requested2.LogitechSlot.Value))
			{
				deviceTarget = LogitechReader.Slot(deviceTarget2, requested2.LogitechSlot.Value, requested2.Name, requested2.Kind);
			}
			break;
		}
		}
		if ((object)deviceTarget == null)
		{
			return Missing();
		}
		return await ReadHidTarget(deviceTarget);
	}

	internal static async Task<Reading> ReadHidTarget(DeviceTarget target, Func<HidDevice[], DeviceProfile, Task<Reading>>? query = null)
	{
		HidDevice hidDevice = target.Interfaces[0];
		DeviceProfile deviceProfile = target.Interfaces.Select(DeviceCatalog.ProfileFor).FirstOrDefault((DeviceProfile p) => (object)p != null);
		HidDevice inaccessible = (from d in target.Interfaces
			where d.AccessError.HasValue
			orderby d.AccessError.GetValueOrDefault() == 5 descending
			select d).FirstOrDefault();
		HidDevice[] array = target.Interfaces.Where((HidDevice d) => !d.AccessError.HasValue).ToArray();
		if (array.Length == 0 && (object)inaccessible != null)
		{
			return HidFailure(target, new Win32Exception(inaccessible.AccessError.Value, "无法访问 HID 接口"));
		}
		if ((object)deviceProfile == null || !target.Supported)
		{
			return new Reading(null, null, null, null, "HID 设备识别", $"已识别 {hidDevice.Vendor:x4}:{hidDevice.Product:x4}，电量协议尚未适配", Cached: false, target.Connection, null, "不向未知型号发送查询命令", ReadFailure.ProtocolUnsupported);
		}
		try
		{
			Reading reading;
			if (query != null)
			{
				reading = await query(array, deviceProfile);
			}
			else
			{
				string reader = deviceProfile.Reader;
				bool flag = ((reader == "Logitech" || reader == "LogitechReceiver") ? true : false);
				Reading reading2 = ((!flag) ? (await ReadHidCore(array, deviceProfile)) : (await LogitechReader.Read(array, deviceProfile, target.LogitechSlot.GetValueOrDefault(byte.MaxValue))));
				reading = reading2;
			}
			Reading reading3 = reading;
			if (!reading3.Percent.HasValue && reading3.Connected != false && (object)inaccessible != null)
			{
				return HidFailure(target, new Win32Exception(inaccessible.AccessError.Value, "部分 HID 接口无法访问"));
			}
			return (reading3.Connection == "待确认") ? reading3 with
			{
				Connection = target.Connection
			} : reading3;
		}
		catch (Exception ex) when (((ex is IOException || ex is Win32Exception || ex is UnauthorizedAccessException || ex is OperationCanceledException) ? 1 : 0) != 0)
		{
			if ((object)inaccessible != null && inaccessible.AccessError.GetValueOrDefault() == 5 && Diagnostics.NativeError(ex).GetValueOrDefault() != 5)
			{
				new Win32Exception(5, "部分 HID 接口访问被拒绝");
			}
			return HidFailure(target, ex);
		}
	}

	internal static Reading HidFailure(DeviceTarget target, Exception error)
	{
		int? num = Diagnostics.NativeError(error);
		bool flag = num.GetValueOrDefault() == 5;
		new Diagnostics(AppContext.BaseDirectory).HidFailure(target.Interfaces[0], "read", error);
		string source = "HID " + target.HardwareId;
		string status = (flag ? "已识别设备；访问被拒绝（Windows 错误 5）" : ((error is OperationCanceledException) ? "已识别设备；查询超时" : ("已识别设备；" + Diagnostics.Redact(error.Message))));
		string connection = target.Connection;
		string chargingNote = (flag ? "可在设置菜单选择“以管理员权限重启”；拒绝访问不能证明是厂商软件占用" : null);
		ReadFailure failure = (flag ? ReadFailure.AccessDenied : ReadFailure.ReadError);
		int? errorCode = num;
		return new Reading(null, null, null, null, source, status, Cached: false, connection, null, chargingNote, failure, errorCode);
	}

	private static async Task<Reading> ReadHidCore(HidDevice[] interfaces, DeviceProfile profile)
	{
		if (MultiBrandReader.Handles(profile.Reader))
		{
			return await MultiBrandReader.Read(interfaces, profile);
		}
		if (profile.Reader == "RazerFeature")
		{
			return ReadMouse(interfaces, profile);
		}
		if (profile.Reader == "Omni")
		{
			(Reading, string) tuple = await ReadKeyboardHid(interfaces);
			return tuple.Item1 ?? new Reading(null, null, null, null, "ROG OMNI", tuple.Item2 ?? "当前配对槽位尚未适配");
		}
		Exception last = null;
		foreach (HidDevice device in interfaces.OrderByDescending((HidDevice d) => d.UsagePage >= 65280))
		{
			try
			{
				if (profile.Reader == "Scythe" && device.UsagePage == 65300 && device.Usage == 1 && device.InputBytes == 64 && device.OutputBytes == 64)
				{
					return await ReadHeadsetHid(device);
				}
				if (profile.Reader == "Razer2020" && device.UsagePage == 65280 && device.Usage == 1 && device.FeatureBytes >= 64)
				{
					return ReadRazer2020(device);
				}
				bool flag = profile.Reader == "RazerPA";
				if (flag)
				{
					ushort usagePage = device.UsagePage;
					bool flag2 = ((usagePage == 65280 || usagePage == 65300) ? true : false);
					flag = flag2;
				}
				if (flag && device.InputBytes == 64 && device.OutputBytes == 64)
				{
					return await ReadPa(device);
				}
				if (profile.Reader.StartsWith("Asus", StringComparison.Ordinal) && device.UsagePage == 65280 && device.InputBytes == ((profile.Kind == "headset") ? 64 : 65) && device.OutputBytes == device.InputBytes && (profile.Kind != "mouse" || device.Path.Contains("mi_00", StringComparison.OrdinalIgnoreCase)))
				{
					return await ReadAsus(device, profile);
				}
			}
			catch (Exception ex) when (((ex is IOException || ex is Win32Exception || ex is UnauthorizedAccessException || ex is OperationCanceledException) ? 1 : 0) != 0)
			{
				new Diagnostics(AppContext.BaseDirectory).HidFailure(device, "query", ex);
				if (ex == null || Diagnostics.NativeError(ex).GetValueOrDefault() != 5)
				{
					last = ex;
				}
			}
		}
		if (last != null)
		{
			throw last;
		}
		return new Reading(null, null, null, null, profile.Reader, "已发现型号，但未找到可用的电量接口", Cached: false, "待确认", null, null, ReadFailure.ProtocolUnsupported);
	}

	private static Reading Missing()
	{
		return new Reading(null, null, null, null, "设备发现", "设备已移除", Cached: false, "未连接", false);
	}

	private static async Task<Reading> ReadAsus(HidDevice device, DeviceProfile profile)
	{
		byte report = (byte)((profile.Kind == "headset") ? 204 : 0);
		byte sub = (byte)((profile.Kind == "keyboard") ? 1 : 7);
		byte[] packet = await AsusQuery(device, report, sub);
		string note = "";
		(int Percent, bool? Charging)? power;
		if (profile.Kind == "mouse")
		{
			power = Protocols.AsusMousePower(packet, profile.Scale);
		}
		else if (profile.Kind == "keyboard")
		{
			power = Protocols.KeyboardPower(packet, report, profile.BatteryOffset);
		}
		else
		{
			power = Protocols.AsusHeadsetPower(packet, device.Product);
			if (power.HasValue)
			{
				try
				{
					byte[] packet2 = await AsusQuery(device, report, 8);
					power = (power.Value.Percent, Protocols.AsusHeadsetCharging(packet2, device.Product));
				}
				catch (Exception ex) when (((ex is IOException || ex is OperationCanceledException) ? 1 : 0) != 0)
				{
					note = "充电状态暂不可读";
				}
				if (device.Product == 6867)
				{
					note = $"左右耳取较低电量；左 {packet[6]}% / 右 {packet[7]}% / 充电盒 {packet[8]}%";
				}
			}
		}
		if (!power.HasValue)
		{
			return new Reading(null, null, null, null, "ROG HID", "电量应答无效或设备休眠");
		}
		string connection = profile.Connection;
		if (profile.Kind == "keyboard")
		{
			try
			{
				connection = Protocols.KeyboardConnection(await AsusQuery(device, report, 3), report) ?? connection;
			}
			catch (Exception ex2) when (((ex2 is IOException || ex2 is OperationCanceledException) ? 1 : 0) != 0)
			{
			}
		}
		int? percent = power.Value.Percent;
		bool? item = power.Value.Charging;
		string status = ((profile.Scale == 25) ? "已连接；电量为 25% 档位估计" : "已连接");
		string connection2 = connection;
		bool? connected = true;
		object chargingNote;
		if (note.Length <= 0)
		{
			bool? item2 = power.Value.Charging;
			chargingNote = ((!item2.HasValue) ? "充电状态未知" : null);
		}
		else
		{
			chargingNote = note;
		}
		return new Reading(percent, item, null, null, "ROG HID · 电源状态", status, Cached: false, connection2, connected, (string?)chargingNote);
	}

	private static async Task<byte[]> AsusQuery(HidDevice device, byte report, byte sub)
	{
		bool flag = report == 0;
		if (flag)
		{
			byte b = sub;
			bool flag2 = ((b == 1 || b == 3 || b == 7) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			bool flag2 = report == 204;
			if (flag2)
			{
				byte b = sub;
				bool flag4 = (uint)(b - 7) <= 1u;
				flag2 = flag4;
			}
			flag3 = flag2;
		}
		if (!flag3)
		{
			throw new ArgumentException("Unknown read query");
		}
		using SafeFileHandle handle = NativeDevices.Open(device.Path, overlapped: true);
		FileStream stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
		byte[] subArray;
		try
		{
			NativeDevices.HidD_FlushQueue(handle);
			using CancellationTokenSource timeout = new CancellationTokenSource(1000);
			byte[] array = new byte[device.OutputBytes];
			array[0] = report;
			array[1] = 18;
			array[2] = sub;
			await stream.WriteAsync(array, timeout.Token);
			int i = 0;
			byte[] response;
			int num;
			while (true)
			{
				if (i < 16)
				{
					response = new byte[device.InputBytes];
					num = await stream.ReadAsync(response, timeout.Token);
					if (num < 3)
					{
						throw new IOException("ROG 电量接口已关闭");
					}
					if (response[0] == report)
					{
						if (response[1] == byte.MaxValue && response[2] == 170)
						{
							throw new IOException("ROG 设备拒绝读取或已休眠");
						}
						if (response[1] == 18 && response[2] == sub)
						{
							break;
						}
					}
					i++;
					continue;
				}
				throw new IOException("未收到匹配的 ROG 电量应答");
			}
			subArray = response[..num];
		}
		finally
		{
			if (stream != null)
			{
				await stream.DisposeAsync();
			}
		}
		return subArray;
	}

	private static Reading ReadRazer2020(HidDevice device)
	{
		using SafeFileHandle handle = NativeDevices.Open(device.Path, overlapped: false, metadataOnly: true);
		byte[] array = new byte[64];
		Convert.FromHexString("FF0A00FD0412F10205").CopyTo(array, 0);
		if (!NativeDevices.HidD_SetFeature(handle, array, array.Length))
		{
			throw new Win32Exception();
		}
		for (int i = 0; i < 5; i++)
		{
			Thread.Sleep(100);
			byte[] array2 = new byte[64];
			array2[0] = byte.MaxValue;
			if (!NativeDevices.HidD_GetFeature(handle, array2, array2.Length))
			{
				throw new Win32Exception();
			}
			(int?, bool?)? tuple = Protocols.Razer2020Power(array2);
			if (tuple.HasValue)
			{
				return new Reading(tuple.Value.Item1, tuple.Value.Item2, null, null, "BlackShark 2020 HID", "已连接", Cached: false, "2.4G", true, tuple.Value.Item2.GetValueOrDefault() ? "此型号充电时百分比不可靠，暂不显示" : null);
			}
		}
		return new Reading(null, null, null, null, "BlackShark 2020 HID", "未收到新鲜的耳机应答");
	}

	private static async Task<Reading> ReadPa(HidDevice device)
	{
		HidDevice device2 = device;
		SafeFileHandle handle = NativeDevices.Open(device2.Path, overlapped: true);
		FileStream stream;
		try
		{
			stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
			Reading result;
			try
			{
				int? battery = await Query(33);
				int? num = ((!battery.HasValue) ? null : (await Query(42)));
				int? num2 = num;
				result = new Reading(battery, (num2.HasValue && num2.GetValueOrDefault() == 0) ? new bool?(false) : ((num2.HasValue && num2.GetValueOrDefault() == 1) ? new bool?(true) : null), null, null, "Razer PA HID", battery.HasValue ? "已连接" : "未收到有效电量应答", Cached: false, battery.HasValue ? "2.4G" : "待确认", battery.HasValue ? new bool?(true) : null);
			}
			finally
			{
				if (stream != null)
				{
					await stream.DisposeAsync();
				}
			}
			return result;
		}
		finally
		{
			if (handle != null)
			{
				((IDisposable)handle).Dispose();
			}
		}
		async Task<int?> Query(byte command)
		{
			using CancellationTokenSource timeout = new CancellationTokenSource(1100);
			int? result2;
			try
			{
				_ = 4;
				try
				{
					NativeDevices.HidD_FlushQueue(handle);
					await stream.WriteAsync(Protocols.PaRequest(device2.Product, 225, true), timeout.Token);
					await Task.Delay(35, timeout.Token);
					await stream.WriteAsync(Protocols.PaRequest(device2.Product, 225, true), timeout.Token);
					await stream.WriteAsync(Protocols.PaRequest(device2.Product, command), timeout.Token);
					int? num4;
					do
					{
						byte[] packet = new byte[64];
						int num3 = await stream.ReadAsync(packet, timeout.Token);
						if (num3 == 0)
						{
							throw new IOException("耳机接口已关闭");
						}
						num4 = Protocols.PaValue(packet[..num3], device2.Product, command);
					}
					while (!num4.HasValue);
					result2 = num4;
				}
				catch (OperationCanceledException)
				{
					result2 = null;
				}
			}
			finally
			{
				using CancellationTokenSource restore = new CancellationTokenSource(300);
				await stream.WriteAsync(Protocols.PaRequest(device2.Product, 225, false), restore.Token);
			}
			return result2;
		}
	}

	private static async Task<Reading> ReadBluetooth(DeviceTarget target)
	{
		DeviceTarget target2 = target;
		string id = target2.BluetoothId ?? throw new ArgumentException("Missing Bluetooth id");
		DeviceInformation info = (await DeviceInformation.FindAllAsync(target2.LowEnergy ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(pairingState: true) : BluetoothDevice.GetDeviceSelectorFromPairingState(pairingState: true))).FirstOrDefault((DeviceInformation d) => d.Id == id && DeviceCatalog.IsBranded(d.Name) && "bt-" + DeviceCatalog.Key(d.Id) == target2.Id);
		if ((object)info == null)
		{
			return Missing();
		}
		if (!target2.LowEnergy)
		{
			using (BluetoothDevice bluetoothDevice = await BluetoothDevice.FromIdAsync(id))
			{
				bool flag = (object)bluetoothDevice != null && bluetoothDevice.ConnectionStatus == BluetoothConnectionStatus.Connected;
				return new Reading(null, null, null, null, "Windows 蓝牙", flag ? "蓝牙已连接；未提供可读电量服务" : "蓝牙未连接", Cached: false, flag ? "蓝牙" : "未连接", flag);
			}
		}
		using BluetoothLEDevice device = await BluetoothLEDevice.FromIdAsync(id);
		if ((object)device == null || device.ConnectionStatus != BluetoothConnectionStatus.Connected)
		{
			return Missing()with
			{
				Source = "蓝牙",
				Status = "蓝牙未连接"
			};
		}
		GattDeviceServicesResult services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
		int? level = null;
		bool? charging = null;
		if (services.Status == GattCommunicationStatus.Success)
		{
			try
			{
				foreach (GattDeviceService service in services.Services)
				{
					GattCharacteristicsResult gattCharacteristicsResult = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
					if (gattCharacteristicsResult.Status != 0)
					{
						continue;
					}
					foreach (GattCharacteristic characteristic in gattCharacteristicsResult.Characteristics)
					{
						ushort code = (ushort)((characteristic.Uuid == GattCharacteristicUuids.BatteryLevel) ? 10777 : ((characteristic.Uuid == new Guid("00002a1a-0000-1000-8000-00805f9b34fb")) ? 10778 : ((characteristic.Uuid == new Guid("00002bed-0000-1000-8000-00805f9b34fb")) ? 11245 : 0)));
						if (code == 0 || !characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
						{
							continue;
						}
						GattReadResult gattReadResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
						if (gattReadResult.Status != 0 || gattReadResult.Value == null || gattReadResult.Value.Length > 16)
						{
							continue;
						}
						using DataReader dataReader = DataReader.FromBuffer(gattReadResult.Value);
						byte[] array = new byte[gattReadResult.Value.Length];
						dataReader.ReadBytes(array);
						if (code == 10777 && array.Length == 1 && array[0] <= 100)
						{
							level = array[0];
							continue;
						}
						bool? flag2 = charging;
						if (!flag2.HasValue)
						{
							charging = Protocols.BluetoothCharging(code, array);
						}
					}
				}
			}
			finally
			{
				foreach (GattDeviceService service2 in services.Services)
				{
					service2.Dispose();
				}
			}
		}
		int? num = ((!level.HasValue) ? NativeDevices.KeyboardWindowsBattery(info.Name) : null);
		return new Reading(level ?? num, charging, null, null, level.HasValue ? "蓝牙 GATT · Battery Level" : "Windows 蓝牙属性（系统缓存）", level.HasValue ? "蓝牙已连接" : (num.HasValue ? "蓝牙已连接；使用系统缓存" : "蓝牙已连接；未提供可读电量"), !level.HasValue && num.HasValue, "蓝牙", true, charging.HasValue ? null : "蓝牙未提供充电状态");
	}
}
