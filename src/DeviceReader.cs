using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace PeriDock;

internal static partial class DeviceReader
{
    internal static async Task<Reading> Probe(string id)
    {
        return id switch
        {
            "keyboard" => await ReadKeyboard(),
            "mouse" => ReadMouse(),
            "headset" => await ReadHeadset(),
            _ => await ProbeTarget(id)
        };
    }

    internal static async Task<Reading> RunIsolated(string id, CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(Environment.ProcessPath!, "--probe " + id)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            }
        };
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0) throw new IOException("设备查询失败：" + await errors);
            return JsonSerializer.Deserialize<Reading>(await output)
                ?? throw new IOException("设备返回了空结果");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    static async Task<Reading> ReadKeyboard()
    {
        (Reading? Value, string? Issue) hid;
        try { hid = await ReadKeyboardHid(); }
        catch (Exception ex) when (Diagnostics.NativeError(ex) == 5)
        {
            hid = (null, "ROG HID 访问被拒绝（Windows 错误 5）");
        }
        if (hid.Value is not null) return hid.Value;
        var cached = NativeDevices.KeyboardWindowsBattery();
        var devices = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
        var info = devices.FirstOrDefault(d => d.Name.Contains("AZOTH X", StringComparison.OrdinalIgnoreCase));
        if (info is null)
            return KeyboardFallback(cached, hid.Issue ?? "未发现键盘", hid.Issue is null ? false : null);
        using var device = await BluetoothLEDevice.FromIdAsync(info.Id);
        if (device is null)
            return KeyboardFallback(cached, hid.Issue ?? "连接状态暂不可读", null);
        if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
            return KeyboardFallback(cached, hid.Issue ?? "蓝牙未连接", hid.Issue is null ? false : null);
        var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
        int? percent = null;
        bool? charging = null;
        var chargingNote = "蓝牙充电状态暂不可读";
        if (services.Status == GattCommunicationStatus.Success)
        {
            try
            {
                foreach (var service in services.Services)
                {
                    var characteristics = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (characteristics.Status != GattCommunicationStatus.Success) continue;
                    chargingNote = "蓝牙未提供充电状态";
                    foreach (var characteristic in characteristics.Characteristics)
                    {
                        var uuid = characteristic.Uuid;
                        var code = uuid == GattCharacteristicUuids.BatteryLevel ? (ushort)0x2a19 :
                            uuid == new Guid("00002a1a-0000-1000-8000-00805f9b34fb") ? (ushort)0x2a1a :
                            uuid == new Guid("00002bed-0000-1000-8000-00805f9b34fb") ? (ushort)0x2bed : (ushort)0;
                        if (code == 0 || !characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read)) continue;
                        if (code != 0x2a19) chargingNote = "蓝牙充电状态暂不可读";
                        try
                        {
                            var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                            if (value.Status != GattCommunicationStatus.Success || value.Value is null || value.Value.Length > 16) continue;
                            using var reader = DataReader.FromBuffer(value.Value);
                            var bytes = new byte[value.Value.Length];
                            reader.ReadBytes(bytes);
                            if (code == 0x2a19 && bytes.Length == 1 && bytes[0] <= 100) percent = bytes[0];
                            else charging ??= Protocols.BluetoothCharging(code, bytes);
                        }
                        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { }
                    }
                }
            }
            finally { foreach (var service in services.Services) service.Dispose(); }
        }
        return new(percent ?? cached, charging, null, null, percent.HasValue ? "蓝牙 GATT · Battery Level" : "Windows 蓝牙属性（系统缓存）",
            "蓝牙已连接" + (hid.Issue is null ? "" : "；" + hid.Issue), !percent.HasValue, "蓝牙", true,
            charging.HasValue ? null : chargingNote);
    }

    static Reading KeyboardFallback(int? cached, string status, bool? connected)
        => new(cached, null, null, null, "Windows 蓝牙属性（系统缓存）", status, true,
            connected == false ? "未连接" : "待确认", connected);

    static async Task<byte[]> KeyboardQuery(HidDevice device, byte report, byte command, byte sub)
    {
        using var handle = NativeDevices.Open(device.Path, overlapped: true);
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
        NativeDevices.HidD_FlushQueue(handle);
        using var timeout = new CancellationTokenSource(1000);
        await stream.WriteAsync(Protocols.KeyboardRequest(report, command, sub, device.OutputBytes), timeout.Token);
        while (true)
        {
            var response = new byte[device.InputBytes];
            var count = await stream.ReadAsync(response, timeout.Token);
            if (count == 0) throw new IOException("键盘状态接口已关闭");
            var packet = response[..count];
            if (Protocols.KeyboardMatches(packet, report, command, sub)) return packet;
            if (count >= 3 && packet[0] == report && packet[1] == 0xff && packet[2] == 0xaa)
                throw new IOException("键盘休眠或未连接");
        }
    }

    static async Task<(Reading? Value, string? Issue)> ReadKeyboardHid(IEnumerable<HidDevice>? selected = null)
    {
        var devices = (selected ?? NativeDevices.EnumerateHid(0x0b05)).ToList();
        var candidates = devices.Where(d => d.UsagePage == 0xff00 && d.Usage == 1 &&
            (d.Product is 0x1c24 or 0x1c25 && d.InputBytes == 65 && d.OutputBytes == 65 ||
             d.Product == 0x1ace && d.InputBytes == 64 && d.OutputBytes == 64))
            .OrderByDescending(d => d.Product == 0x1c24).ToList();
        string? issue = devices.Count > 0 && candidates.Count == 0 ? "发现 ROG 接口，状态待确认" : null;
        Exception? denied = null;
        foreach (var device in candidates)
        {
            try
            {
                var report = device.Product == 0x1ace ? (byte)2 : (byte)0;
                if (report == 2)
                {
                    var control = devices.FirstOrDefault(d => d.Product == 0x1ace && d.UsagePage == 0xff02 &&
                        d.Usage == 1 && d.InputBytes == 64 && d.OutputBytes == 64);
                    if (control is null || !Protocols.OmniHasAzothX(await KeyboardQuery(control, 1, 0xa0, 0))) continue;
                }
                var connection = Protocols.KeyboardConnection(await KeyboardQuery(device, report, 0x12, 3), report);
                if (connection is null) continue;
                var power = Protocols.KeyboardPower(await KeyboardQuery(device, report, 0x12, 1), report);
                if (power is null) { issue = "ROG 电量应答无效"; continue; }
                return (new(power.Value.Percent, power.Value.Charging, null, null, "ROG HID · 连接 / 电源状态",
                    "已连接", Connection: connection, Connected: true), null);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or OperationCanceledException)
            {
                new Diagnostics(AppContext.BaseDirectory).HidFailure(device, "keyboard-query", ex);
                if (Diagnostics.NativeError(ex) == 5) denied = ex;
                issue = ex is OperationCanceledException ? "ROG 状态查询超时" : Diagnostics.Redact(ex.Message);
            }
        }
        if (denied is not null) throw denied;
        return (null, issue);
    }

    static byte[] MouseQuery(Microsoft.Win32.SafeHandles.SafeFileHandle handle, byte commandClass, byte command, byte size, byte transaction = 0x1f)
    {
        var request = Protocols.MouseRequest(commandClass, command, size, transaction);
        if (!NativeDevices.HidD_SetFeature(handle, request, request.Length))
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "设备不接受查询");
        Thread.Sleep(60);
        var deadline = Stopwatch.StartNew();
        var attempts = 0;
        do
        {
            var response = new byte[91];
            if (!NativeDevices.HidD_GetFeature(handle, response, response.Length))
                throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "无法读取设备应答");
            if (Protocols.MouseMatches(response, commandClass, command, transaction)) return response;
            if (response[2] == transaction && response[7] == commandClass && response[8] == command && response[1] is 3 or 4 or 5) return response;
            if (++attempts == 4) NativeDevices.HidD_SetFeature(handle, request, request.Length);
            Thread.Sleep(70);
        } while (deadline.ElapsedMilliseconds < 700);
        throw new IOException("未收到匹配的应答，设备可能休眠或接口正被其他软件使用");
    }

    static Reading ReadMouse(IEnumerable<HidDevice>? selected = null, DeviceProfile? profile = null)
    {
        var transaction = profile?.Transaction ?? (byte)0x1f;
        var devices = (selected ?? NativeDevices.EnumerateHid().Where(d => d.Product is 0x00c0 or 0x00c1))
            .Where(d => d.FeatureBytes >= 91)
            .OrderByDescending(d => DeviceCatalog.RazerInterface(d.Product) is int index &&
                d.Path.Contains($"mi_{index:x2}", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(d => d.UsagePage == 0xff00).ToList();
        if (devices.Count == 0) return new(null, null, null, null, "Razer HID",
            selected is null ? "未发现鼠标接口" : "已识别型号，但未找到可用的电量接口",
            Connection: selected is null ? "未连接" : profile?.Connection ?? "待确认",
            Connected: selected is null ? false : null,
            Failure: selected is null ? ReadFailure.None : ReadFailure.ProtocolUnsupported);
        Exception? last = null;
        var disconnected = false;
        foreach (var device in devices)
        {
            try
            {
                using var handle = NativeDevices.Open(device.Path, metadataOnly: true);
                var batteryPacket = MouseQuery(handle, 7, 0x80, 2, transaction);
                var battery = Protocols.MouseValue(batteryPacket, 7, 0x80, transaction);
                if (battery is null)
                {
                    if (batteryPacket[1] == 4) disconnected = true;
                    continue;
                }
                bool? charging = null;
                (int X, int Y)? dpi = null;
                try
                {
                    var value = Protocols.MouseValue(MouseQuery(handle, 7, 0x84, 2, transaction), 7, 0x84, transaction);
                    charging = value is 0 ? false : value is 1 ? true : null;
                }
                catch (IOException) { }
                catch (Win32Exception ex) { new Diagnostics(AppContext.BaseDirectory).HidFailure(device, "charging", ex); }
                try
                {
                    if (profile is null || profile.Kind == "mouse" && transaction == 0x1f)
                        dpi = Protocols.MouseDpi(MouseQuery(handle, 4, 0x85, 7, transaction), transaction);
                }
                catch (IOException) { }
                catch (Win32Exception ex) { new Diagnostics(AppContext.BaseDirectory).HidFailure(device, "dpi", ex); }
                return new((int)Math.Round(battery.Value * 100d / 255), charging, dpi?.X, dpi?.Y,
                    "Razer HID · 电量 / DPI 读取", "已连接",
                    Connection: profile?.Connection ?? Protocols.RazerConnection(device.Product), Connected: true);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception)
            {
                new Diagnostics(AppContext.BaseDirectory).HidFailure(device, "mouse-query", ex);
                if (last is null || Diagnostics.NativeError(last) != 5) last = ex;
            }
        }
        if (last is not null) throw last;
        return new(null, null, null, null, "Razer HID", disconnected ? "鼠标休眠或未连接" : "设备未响应",
            Connection: disconnected ? "未连接" : "待确认", Connected: disconnected ? false : null);
    }

    static async Task<int?> HeadsetQuery(FileStream stream, byte command, byte route)
    {
        using var timeout = new CancellationTokenSource(1200);
        await stream.WriteAsync(Protocols.HeadsetRequest(command, route), timeout.Token);
        while (!timeout.IsCancellationRequested)
        {
            var response = new byte[64];
            var count = await stream.ReadAsync(response, timeout.Token);
            if (count == 0) throw new IOException("耳机接口已关闭");
            if (count != 64) continue;
            var result = Protocols.HeadsetValue(response, command, route);
            if (result is not null) return result;
        }
        return null;
    }

    static async Task<Reading> ReadHeadset()
    {
        var bluetoothTask = HeadsetBluetoothConnected();
        var devices = NativeDevices.EnumerateHid().Where(d => d.Product is 0x0576 or 0x0577
            && d.UsagePage == 0xff14 && d.Usage == 1 && d.InputBytes == 64 && d.OutputBytes == 64)
            .OrderByDescending(d => d.Product == 0x0576).ToList();
        var live = new List<Reading>();
        Exception? last = null;
        foreach (var device in devices)
        {
            try
            {
                var result = await ReadHeadsetHid(device);
                if (result.Connected == true) live.Add(result);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or OperationCanceledException) { last = ex; }
        }
        var bluetooth = await bluetoothTask;
        var connections = live.Select(r => r.Connection).Distinct().ToList();
        if (bluetooth == true) connections.Add("蓝牙");
        if (connections.Count > 0)
        {
            var value = live.FirstOrDefault(r => r.Percent.HasValue) ?? live.FirstOrDefault() ??
                new(null, null, null, null, "Windows 蓝牙连接状态", "蓝牙已连接，电量暂不可读");
            return value with
            {
                Connection = string.Join(" + ", connections), Connected = true,
                Status = (connections.Count > 1 ? "多路连接" : "已连接") +
                    (bluetooth is null ? "；本机蓝牙状态待确认" : "") +
                    (last is null ? "" : "；另一接口查询失败"),
                ChargingNote = value.Charging.HasValue ? null :
                    live.Count == 0 ? "蓝牙充电状态暂不可读" : "充电状态未知"
            };
        }
        if (last is not null) throw last;
        return new(null, null, null, null, "BlackShark HID / Windows 蓝牙", bluetooth is null ? "连接状态暂不可读" : "耳机未连接",
            Connection: bluetooth is null ? "待确认" : "未连接", Connected: bluetooth is null ? null : false);
    }

    static async Task<bool?> HeadsetBluetoothConnected()
    {
        async Task<bool> Read()
        {
            var connected = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected));
            return connected.Any(d => d.Name.Contains("BlackShark V3 Pro", StringComparison.OrdinalIgnoreCase));
        }
        try { return await Read().WaitAsync(TimeSpan.FromSeconds(1.5)); }
        catch (Exception ex) when (ex is TimeoutException or UnauthorizedAccessException or
            System.Runtime.InteropServices.COMException) { return null; }
    }

    static async Task<Reading> ReadHeadsetHid(HidDevice device)
    {
        using var handle = NativeDevices.Open(device.Path, overlapped: true);
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, 1, isAsync: true);
        var connected = Protocols.HeadsetConnected(device.Product, await HeadsetQuery(stream, 0x20, 0));
        if (connected == false) return new(null, null, null, null, "BlackShark HID", "耳机未连接",
            Connection: "未连接", Connected: false);
        var battery = await HeadsetQuery(stream, 0x21, 0x80);
        bool? charging = null;
        try
        {
            var charge = await HeadsetQuery(stream, 0x2a, 0x80);
            charging = charge is 0 ? false : charge is 1 ? true : null;
        }
        catch (OperationCanceledException) { }
        return new(battery, charging, null, null, "BlackShark HID · Scythe 协议", battery.HasValue ? "已连接" : "未收到电量",
            Connection: Protocols.RazerConnection(device.Product), Connected: connected == true || battery.HasValue ? true : null);
    }
}
