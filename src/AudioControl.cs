using System.Runtime.InteropServices;

namespace PeriDock;

internal enum AudioFlow { Render = 0, Capture = 1 }
internal enum AudioRole { Console = 0, Multimedia = 1, Communications = 2 }

internal sealed record AudioEndpoint(string Id, string Name, bool IsDefault)
{
    public string DisplayName => IsDefault ? Name + " · 默认" : Name;
}

internal static class AudioControl
{
    internal static int LastHResult { get; private set; }
    internal static List<AudioEndpoint> List(AudioFlow flow)
    {
        var result = new List<AudioEndpoint>();
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;
        try
        {
            enumerator = CreateEnumerator();
            string? defaultId = null;
            try
            {
                if (enumerator.GetDefaultAudioEndpoint((EDataFlow)flow, ERole.Multimedia, out defaultDevice) == 0)
                    defaultDevice.GetId(out defaultId);
            }
            catch (COMException) { }
            if (enumerator.EnumAudioEndpoints((EDataFlow)flow, 1, out collection) != 0) return result;
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(i, out device) != 0) continue;
                    device.GetId(out var id);
                    result.Add(new(id, FriendlyName(device), id == defaultId));
                }
                catch (COMException) { }
                finally { Release(device); }
            }
            return result.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally
        {
            Release(defaultDevice); Release(collection); Release(enumerator);
        }
    }

    internal static AudioEndpoint? PickHeadset(AudioFlow flow)
    {
        var devices = List(flow);
        var match = devices.FirstOrDefault(d => d.Name.Contains("BlackShark V3 Pro", StringComparison.OrdinalIgnoreCase));
        return match ?? devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
    }

    internal static bool SetDefault(string id, AudioFlow flow)
    {
        object? client = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"));
            if (type is null) return false;
            client = Activator.CreateInstance(type);
            if (client is not IPolicyConfig policy) return false;
            var ok = true;
            foreach (var role in new[] { EConsole, EMultimedia, ECommunications })
                ok &= policy.SetDefaultEndpoint(id, role) >= 0;
            return ok;
        }
        catch (COMException) { return false; }
        finally { Release(client); }
    }

    internal static float? Volume(string id, AudioFlow flow)
    {
        IAudioEndpointVolume? volume = null;
        try
        {
            volume = EndpointVolume(id, flow);
            return volume is not null && volume.GetMasterVolumeLevelScalar(out var value) == 0 ? value : null;
        }
        catch (COMException) { return null; }
        finally { Release(volume); }
    }

    internal static bool SetVolume(string id, AudioFlow flow, float value)
    {
        IAudioEndpointVolume? volume = null;
        try
        {
            volume = EndpointVolume(id, flow);
            if (volume is null) return false;
            var context = Guid.Empty;
            LastHResult = volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref context);
            return LastHResult >= 0;
        }
        catch (COMException) { return false; }
        finally { Release(volume); }
    }

    internal static bool? Muted(string id, AudioFlow flow)
    {
        IAudioEndpointVolume? volume = null;
        try
        {
            volume = EndpointVolume(id, flow);
            return volume is not null && volume.GetMute(out var muted) == 0 ? muted : null;
        }
        catch (COMException) { return null; }
        finally { Release(volume); }
    }

    internal static bool SetMuted(string id, AudioFlow flow, bool muted)
    {
        IAudioEndpointVolume? volume = null;
        try
        {
            volume = EndpointVolume(id, flow);
            if (volume is null) return false;
            var context = Guid.Empty;
            LastHResult = volume.SetMute(muted, ref context);
            return LastHResult >= 0;
        }
        catch (COMException) { return false; }
        finally { Release(volume); }
    }

    static IAudioEndpointVolume? EndpointVolume(string id, AudioFlow flow)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = CreateEnumerator();
            if (enumerator.GetDevice(id, out device) != 0 || device is null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            var hr = device.Activate(ref iid, 23, IntPtr.Zero, out var value);
            return hr == 0 ? value as IAudioEndpointVolume : null;
        }
        finally { Release(device); Release(enumerator); }
    }

    static IMMDeviceEnumerator CreateEnumerator()
        => (IMMDeviceEnumerator)Activator.CreateInstance(
            Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;

    static string FriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(0, out store) != 0 || store is null) return "未命名音频设备";
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var value) != 0) return "未命名音频设备";
            try
            {
                return value.vt == 31 && value.pointerValue != IntPtr.Zero
                    ? Marshal.PtrToStringUni(value.pointerValue) ?? "未命名音频设备"
                    : "未命名音频设备";
            }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            try { Marshal.ReleaseComObject(value); } catch (ArgumentException) { }
    }

    const uint EConsole = 0, EMultimedia = 1, ECommunications = 2;
    static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14
    };

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    [StructLayout(LayoutKind.Explicit)]
    struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointerValue;
    }

    enum EDataFlow { Render, Capture, All }
    enum ERole { Console, Multimedia, Communications }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    sealed class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice(string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object value);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute(bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute(out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(Guid eventContext);
        [PreserveSig] int VolumeStepDown(Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string deviceId, out IntPtr format);
        [PreserveSig] int GetDeviceFormat(string deviceId, int defaultFormat, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string deviceId);
        [PreserveSig] int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string deviceId, int defaultPeriod, out long period, out long minPeriod);
        [PreserveSig] int SetProcessingPeriod(string deviceId, ref long period);
        [PreserveSig] int GetShareMode(string deviceId, out IntPtr mode);
        [PreserveSig] int SetShareMode(string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string deviceId, ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetPropertyValue(string deviceId, ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int SetDefaultEndpoint(string deviceId, uint role);
        [PreserveSig] int SetEndpointVisibility(string deviceId, int visible);
    }

    [DllImport("ole32.dll", PreserveSig = false)]
    static extern void PropVariantClear(ref PROPVARIANT value);
}
