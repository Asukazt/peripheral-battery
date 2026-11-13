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
    internal static async Task<List<DeviceTarget>> Discover()
    {
        var targets = DeviceCatalog.FromHid(NativeDevices.EnumerateHid(0x1532).Concat(NativeDevices.EnumerateHid(0x0b05)));
        foreach (var lowEnergy in new[] { true, false })
        {
            try
            {
                var selector = lowEnergy ? BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected) :
                    BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
                var devices = await DeviceInformation.FindAllAsync(selector);
                foreach (var device in devices.Where(d => DeviceCatalog.IsBranded(d.Name)))
                {
                    // A paired BLE device may also expose classic Bluetooth; prefer its readable Battery Service.
                    if (!lowEnergy && targets.Any(t => t.BluetoothId is not null && DeviceCatalog.ModelKey(t.Name) == DeviceCatalog.ModelKey(device.Name))) continue;
                    targets.Add(new("bt-" + DeviceCatalog.Key(device.Id), device.Name.Trim(), DeviceCatalog.Kind(device.Name), true, device.Id, lowEnergy));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                new Diagnostics(AppContext.BaseDirectory).Write("error", "bluetooth", "discover", ex.Message, ex);
            }
        }
        return targets;
    }

    internal static async Task<List<DeviceTarget>> DiscoverIsolated(CancellationToken token)
    {
        var result = await RunChild("--discover", null, token, 12);
        return JsonSerializer.Deserialize<List<DeviceTarget>>(result) ?? throw new IOException("设备列表为空");
    }

    internal static async Task<Reading> RunTargetIsolated(DeviceTarget target, CancellationToken token)
        => JsonSerializer.Deserialize<Reading>(await RunChild("--target-probe", JsonSerializer.Serialize(target), token, 10))
           ?? throw new IOException("设备查询结果为空");

    static async Task<string> RunChild(string command, string? input, CancellationToken token, int seconds)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = input is not null, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        } };
        process.StartInfo.ArgumentList.Add(command);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            if (input is not null) { await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token); process.StandardInput.Close(); }
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0) throw new IOException("设备查询失败：" + await errors);
            return await output;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
        }
    }

    internal static async Task<Reading> ProbeTarget(string id)
    {
        var targets = await Discover();
        var target = targets.FirstOrDefault(t => t.Id == id);
        return target is null ? Missing() : await ProbeTarget(target);
    }

    internal static async Task<Reading> ProbeTarget(DeviceTarget requested)
    {
        if (requested.BluetoothId is not null) return await ReadBluetooth(requested);
        // Re-enumerate instead of trusting paths or protocol claims supplied to the child process.
        var target = DeviceCatalog.FromHid(NativeDevices.EnumerateHid(0x1532).Concat(NativeDevices.EnumerateHid(0x0b05)))
            .FirstOrDefault(t => t.Id == requested.Id);
        if (target is null) return Missing();
        var first = target.Interfaces[0];
        var profile = DeviceCatalog.Find(first.Vendor, first.Product);
        if (profile is null || !target.Supported)
            return new(null, null, null, null, "HID 设备识别", $"已识别 {first.Vendor:x4}:{first.Product:x4}，电量协议尚未适配",
                ChargingNote: "不向未知型号发送查询命令");
        if (profile.Reader == "RazerFeature") return ReadMouse(target.Interfaces, profile);
        if (profile.Reader == "Omni")
        {
            // shortcut: only the verified Azoth X slot is queried until other OMNI routes have captured replies.
            var result = await ReadKeyboardHid(target.Interfaces);
            return result.Value ?? new(null, null, null, null, "ROG OMNI", result.Issue ?? "当前配对槽位尚未适配");
        }
        Exception? last = null;
        foreach (var device in target.Interfaces.OrderByDescending(d => d.UsagePage >= 0xff00))
        {
            try
            {
                if (profile.Reader == "Scythe" && device.UsagePage == 0xff14 && device.Usage == 1 && device.InputBytes == 64 && device.OutputBytes == 64)
                    return await ReadHeadsetHid(device);
                if (profile.Reader == "Razer2020" && device.UsagePage == 0xff00 && device.Usage == 1 && device.FeatureBytes >= 64)
                    return ReadRazer2020(device);
                if (profile.Reader == "RazerPA" && device.UsagePage is 0xff00 or 0xff14 && device.InputBytes == 64 && device.OutputBytes == 64)
                    return await ReadPa(device);
                if (profile.Reader.StartsWith("Asus", StringComparison.Ordinal) && device.UsagePage == 0xff00 &&
                    device.InputBytes == (profile.Kind == "headset" ? 64 : 65) && device.OutputBytes == device.InputBytes &&
                    (profile.Kind != "mouse" || device.Path.Contains("mi_00", StringComparison.OrdinalIgnoreCase)))
                    return await ReadAsus(device, profile);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or OperationCanceledException) { last = ex; }
        }
        if (last is not null) throw last;
        return new(null, null, null, null, profile.Reader, "已发现型号，但未找到可用的电量接口");
    }

    static Reading Missing() => new(null, null, null, null, "设备发现", "设备已移除", Connection: "未连接", Connected: false);

    static async Task<Reading> ReadAsus(HidDevice device, DeviceProfile profile)
    {
        var report = profile.Kind == "headset" ? (byte)0xcc : (byte)0;
        var command = profile.Kind == "keyboard" ? (byte)1 : (byte)7;
        var packet = await AsusQuery(device, report, command);
        (int Percent, bool? Charging)? power;
        var note = "";
        if (profile.Kind == "mouse") power = Protocols.AsusMousePower(packet, profile.Scale);
        else if (profile.Kind == "keyboard") power = Protocols.KeyboardPower(packet, report, profile.BatteryOffset);
        else
        {
            power = Protocols.AsusHeadsetPower(packet, device.Product);
            if (power is not null)
            {
                try
                {
                    var charge = await AsusQuery(device, report, 8);
                    power = (power.Value.Percent, Protocols.AsusHeadsetCharging(charge, device.Product));
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { note = "充电状态暂不可读"; }
                if (device.Product == 0x1ad3) note = $"左右耳取较低电量；左 {packet[6]}% / 右 {packet[7]}% / 充电盒 {packet[8]}%";
            }
        }
        if (power is null) return new(null, null, null, null, "ROG HID", "电量应答无效或设备休眠");
        var connection = profile.Connection;
        if (profile.Kind == "keyboard")
        {
            try { connection = Protocols.KeyboardConnection(await AsusQuery(device, report, 3), report) ?? connection; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
        }
        return new(power.Value.Percent, power.Value.Charging, null, null, "ROG HID · 电源状态",
            profile.Scale == 25 ? "已连接；电量为 25% 档位估计" : "已连接", Connection: connection, Connected: true,
            ChargingNote: note.Length > 0 ? note : power.Value.Charging is null ? "充电状态未知" : null);
    }

    static async Task<byte[]> AsusQuery(HidDevice device, byte report, byte sub)
    {
        if (!((report == 0 && sub is 1 or 3 or 7) || (report == 0xcc && sub is 7 or 8))) throw new ArgumentException("Unknown read query");
        using var handle = NativeDevices.Open(device.Path, overlapped: true);
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        NativeDevices.HidD_FlushQueue(handle);
        using var timeout = new CancellationTokenSource(1000);
        var request = new byte[device.OutputBytes];
        request[0] = report; request[1] = 0x12; request[2] = sub;
        await stream.WriteAsync(request, timeout.Token);
        for (var i = 0; i < 16; i++)
        {
            var response = new byte[device.InputBytes];
            var count = await stream.ReadAsync(response, timeout.Token);
            if (count < 3) throw new IOException("ROG 电量接口已关闭");
            if (response[0] != report) continue;
            if (response[1] == 0xff && response[2] == 0xaa) throw new IOException("ROG 设备拒绝读取或已休眠");
            if (response[1] == 0x12 && response[2] == sub) return response[..count];
        }
        throw new IOException("未收到匹配的 ROG 电量应答");
    }

    static Reading ReadRazer2020(HidDevice device)
    {
        using var handle = NativeDevices.Open(device.Path, metadataOnly: true);
        var request = new byte[64];
        Convert.FromHexString("FF0A00FD0412F10205").CopyTo(request, 0);
        if (!NativeDevices.HidD_SetFeature(handle, request, request.Length)) throw new Win32Exception();
        for (var i = 0; i < 5; i++)
        {
            Thread.Sleep(100);
            var packet = new byte[64]; packet[0] = 0xff;
            if (!NativeDevices.HidD_GetFeature(handle, packet, packet.Length)) throw new Win32Exception();
            var power = Protocols.Razer2020Power(packet);
            if (power is not null) return new(power.Value.Percent, power.Value.Charging, null, null, "BlackShark 2020 HID",
                "已连接", Connection: "2.4G", Connected: true,
                ChargingNote: power.Value.Charging == true ? "此型号充电时百分比不可靠，暂不显示" : null);
        }
        return new(null, null, null, null, "BlackShark 2020 HID", "未收到新鲜的耳机应答");
    }

    static async Task<Reading> ReadPa(HidDevice device)
    {
        using var handle = NativeDevices.Open(device.Path, overlapped: true);
        await using var stream = new FileStream(handle, FileAccess.ReadWrite, 1, true);
        async Task<int?> Query(byte command)
        {
            using var timeout = new CancellationTokenSource(1100);
            try
            {
                NativeDevices.HidD_FlushQueue(handle);
                await stream.WriteAsync(Protocols.PaRequest(device.Product, 0xe1, true), timeout.Token);
                await Task.Delay(35, timeout.Token);
                await stream.WriteAsync(Protocols.PaRequest(device.Product, 0xe1, true), timeout.Token);
                await stream.WriteAsync(Protocols.PaRequest(device.Product, command), timeout.Token);
                while (true)
                {
                    var packet = new byte[64];
                    var count = await stream.ReadAsync(packet, timeout.Token);
                    if (count == 0) throw new IOException("耳机接口已关闭");
                    var value = Protocols.PaValue(packet[..count], device.Product, command);
                    if (value is not null) return value;
                }
            }
            catch (OperationCanceledException) { return null; }
            finally
            {
                // Restore the temporary query mode after every exchange, including timeouts.
                using var restore = new CancellationTokenSource(300);
                await stream.WriteAsync(Protocols.PaRequest(device.Product, 0xe1, false), restore.Token);
            }
        }
        var battery = await Query(0x21);
        var charge = battery.HasValue ? await Query(0x2a) : null;
        return new(battery, charge is 0 ? false : charge is 1 ? true : null, null, null, "Razer PA HID",
            battery.HasValue ? "已连接" : "未收到有效电量应答", Connection: battery.HasValue ? "2.4G" : "待确认",
            Connected: battery.HasValue ? true : null);
    }

    static async Task<Reading> ReadBluetooth(DeviceTarget target)
    {
        var id = target.BluetoothId ?? throw new ArgumentException("Missing Bluetooth id");
        var selector = target.LowEnergy ? BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) : BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var devices = await DeviceInformation.FindAllAsync(selector);
        var info = devices.FirstOrDefault(d => d.Id == id && DeviceCatalog.IsBranded(d.Name) && "bt-" + DeviceCatalog.Key(d.Id) == target.Id);
        if (info is null) return Missing();
        if (!target.LowEnergy)
        {
            using var classic = await BluetoothDevice.FromIdAsync(id);
            var connected = classic?.ConnectionStatus == BluetoothConnectionStatus.Connected;
            return new(null, null, null, null, "Windows 蓝牙", connected ? "蓝牙已连接；未提供可读电量服务" : "蓝牙未连接",
                Connection: connected ? "蓝牙" : "未连接", Connected: connected);
        }
        using var device = await BluetoothLEDevice.FromIdAsync(id);
        if (device?.ConnectionStatus != BluetoothConnectionStatus.Connected) return Missing() with { Source = "蓝牙", Status = "蓝牙未连接" };
        var services = await device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Uncached);
        int? level = null; bool? charging = null;
        if (services.Status == GattCommunicationStatus.Success)
        {
            try
            {
                foreach (var service in services.Services)
                {
                    var result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (result.Status != GattCommunicationStatus.Success) continue;
                    foreach (var characteristic in result.Characteristics)
                    {
                        var code = characteristic.Uuid == GattCharacteristicUuids.BatteryLevel ? (ushort)0x2a19 :
                            characteristic.Uuid == new Guid("00002a1a-0000-1000-8000-00805f9b34fb") ? (ushort)0x2a1a :
                            characteristic.Uuid == new Guid("00002bed-0000-1000-8000-00805f9b34fb") ? (ushort)0x2bed : (ushort)0;
                        if (code == 0 || !characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read)) continue;
                        var value = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (value.Status != GattCommunicationStatus.Success || value.Value is null || value.Value.Length > 16) continue;
                        using var reader = DataReader.FromBuffer(value.Value);
                        var bytes = new byte[value.Value.Length]; reader.ReadBytes(bytes);
                        if (code == 0x2a19 && bytes.Length == 1 && bytes[0] <= 100) level = bytes[0];
                        else charging ??= Protocols.BluetoothCharging(code, bytes);
                    }
                }
            }
            finally { foreach (var service in services.Services) service.Dispose(); }
        }
        var cached = level is null ? NativeDevices.KeyboardWindowsBattery(info.Name) : null;
        return new(level ?? cached, charging, null, null, level.HasValue ? "蓝牙 GATT · Battery Level" : "Windows 蓝牙属性（系统缓存）",
            level.HasValue ? "蓝牙已连接" : cached.HasValue ? "蓝牙已连接；使用系统缓存" : "蓝牙已连接；未提供可读电量",
            Cached: !level.HasValue && cached.HasValue, Connection: "蓝牙", Connected: true,
            ChargingNote: charging.HasValue ? null : "蓝牙未提供充电状态");
    }
}
