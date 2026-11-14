using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace PeriDock;

internal sealed record DeviceProfile(ushort Vendor, ushort Product, string Name, string Kind, string Reader,
    string Connection = "待确认", byte Transaction = 0x1f, int Scale = 1, int BatteryOffset = 6);

internal sealed record DeviceTarget(string Id, string Name, string Kind, bool Supported,
    string? BluetoothId = null, bool LowEnergy = false, string HardwareId = "", string Connection = "待确认")
{
    [JsonIgnore] public HidDevice[] Interfaces { get; init; } = [];
}

internal static class DeviceCatalog
{
    internal static IReadOnlyList<DeviceProfile> Profiles { get; } = Create();
    internal static DeviceProfile? Find(ushort vendor, ushort product)
        => Profiles.FirstOrDefault(p => p.Vendor == vendor && p.Product == product);
    internal static string Key(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..24].ToLowerInvariant();

    internal static int? RazerInterface(ushort product) => product switch
    {
        0x00c0 => 0,
        0x025a or 0x025c or 0x0290 or 0x0296 or 0x02d5 => 2,
        0x0292 or 0x0298 or 0x02d7 or 0x0271 or 0x0258 or 0x02ba or 0x02b9 => 3,
        _ => null
    };

    internal static List<DeviceTarget> FromHid(IEnumerable<HidDevice> devices)
    {
        var candidates = devices.Where(d => d.Vendor is 0x1532 or 0x0b05).Where(d =>
            d.Vendor == 0x1532 || Find(d.Vendor, d.Product) is not null || IsBranded(d.Name)).ToArray();
        return candidates.GroupBy(d => $"{d.Vendor:x4}:{d.Product:x4}:{(d.Container.Length > 0 ? d.Container : d.Path)}")
            .Select(group =>
            {
                var first = group.First();
                var profile = Find(first.Vendor, first.Product);
                var name = profile?.Name ?? (first.Name.Length > 0 ? first.Name : $"外设 {first.Vendor:x4}:{first.Product:x4}");
                return new DeviceTarget("hid-" + Key(group.Key), name, profile?.Kind ?? Kind(name),
                    profile is not null && profile.Reader != "Unsupported", HardwareId: $"{first.Vendor:x4}:{first.Product:x4}",
                    Connection: profile?.Connection ?? "待确认") { Interfaces = group.ToArray() };
            }).OrderBy(t => t.Kind == "keyboard" ? 0 : t.Kind == "mouse" ? 1 : 2).ThenBy(t => t.Name).ToList();
    }

    internal static bool IsBranded(string name)
        => name.Contains("Razer", StringComparison.OrdinalIgnoreCase) || name.Contains("ROG", StringComparison.OrdinalIgnoreCase);
    internal static string ModelKey(string name)
        => string.Concat(name.ToLowerInvariant().Where(char.IsLetterOrDigit)).Replace("razer", "").Replace("rog", "");
    internal static string Kind(string name)
        => Profiles.OrderByDescending(p => p.Name.Length).FirstOrDefault(p => ModelKey(name).Contains(ModelKey(p.Name), StringComparison.Ordinal))?.Kind ??
           (name.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ? "keyboard" :
            name.Contains("mouse", StringComparison.OrdinalIgnoreCase) ? "mouse" :
            name.Contains("headset", StringComparison.OrdinalIgnoreCase) || name.Contains("headphone", StringComparison.OrdinalIgnoreCase) ? "headset" : "device");

    static IReadOnlyList<DeviceProfile> Create()
    {
        var list = new List<DeviceProfile>();
        void RazerPair(string name, ushort wired, ushort wireless, byte transaction = 0x1f, string kind = "mouse", byte? wiredTransaction = null)
        {
            list.Add(new(0x1532, wired, "Razer " + name, kind, "RazerFeature", "USB", wiredTransaction ?? transaction));
            list.Add(new(0x1532, wireless, "Razer " + name, kind, "RazerFeature", "2.4G", transaction));
        }
        void RazerSingle(string name, ushort product, byte transaction = 0x1f)
            => list.Add(new(0x1532, product, "Razer " + name, "mouse", "RazerFeature", "2.4G", transaction));
        RazerPair("Mamba (2012)", 0x0024, 0x0025, 0xff);
        RazerPair("Naga Epic Chroma", 0x003e, 0x003f, 0xff);
        RazerPair("Mamba", 0x0044, 0x0045, 0xff);
        RazerPair("Lancehead", 0x0059, 0x005a, 0x3f);
        RazerPair("Lancehead Wireless", 0x0070, 0x006f);
        RazerPair("Mamba Wireless", 0x0073, 0x0072, 0x3f);
        RazerPair("Pro Click", 0x0080, 0x0077);
        RazerPair("Viper Ultimate", 0x007a, 0x007b, 0xff);
        RazerPair("DeathAdder V2 Pro", 0x007c, 0x007d, 0x3f);
        RazerPair("Basilisk Ultimate", 0x0086, 0x0088);
        RazerPair("Naga Pro", 0x008f, 0x0090);
        RazerPair("Viper Mini SE", 0x009e, 0x009f);
        RazerPair("Viper V2 Pro", 0x00a5, 0x00a6);
        RazerPair("Naga V2 Pro", 0x00a7, 0x00a8);
        RazerPair("Basilisk V3 Pro", 0x00aa, 0x00ab);
        RazerPair("Cobra Pro", 0x00af, 0x00b0);
        RazerPair("DeathAdder V3 Pro", 0x00b6, 0x00b7);
        RazerPair("DeathAdder V4 Pro", 0x00be, 0x00bf);
        RazerPair("Viper V3 Pro", 0x00c0, 0x00c1);
        RazerPair("DeathAdder V3 Pro", 0x00c2, 0x00c3);
        RazerPair("DeathAdder V3 HyperSpeed", 0x00c4, 0x00c5);
        RazerPair("Pro Click V2 Vertical Edition", 0x00c7, 0x00c8);
        RazerPair("Basilisk V3 Pro 35K", 0x00cc, 0x00cd);
        RazerPair("Pro Click V2", 0x00d0, 0x00d1);
        RazerPair("Basilisk Mobile", 0x00d3, 0x00d4);
        RazerPair("Basilisk V3 Pro 35K Phantom Green", 0x00d6, 0x00d7);
        RazerSingle("Naga Epic", 0x001f, 0xff);
        RazerSingle("Ouroboros", 0x0032, 0xff);
        RazerSingle("Atheris", 0x0062);
        RazerSingle("Basilisk X HyperSpeed", 0x0083, 0xff);
        RazerSingle("Orochi V2", 0x0094);
        RazerSingle("Pro Click Mini", 0x009a);
        RazerSingle("DeathAdder V2 X HyperSpeed", 0x009c);
        RazerSingle("HyperPolling Dongle", 0x00b3);
        RazerSingle("Naga V2 HyperSpeed", 0x00b4);
        RazerSingle("Viper V3 HyperSpeed", 0x00b8);
        RazerSingle("Basilisk V3 X HyperSpeed", 0x00b9);
        RazerPair("DeathStalker V2 Pro", 0x0292, 0x0290, 0x9f, "keyboard", 0x1f);
        RazerPair("DeathStalker V2 Pro TKL", 0x0298, 0x0296, 0x9f, "keyboard", 0x1f);
        RazerPair("BlackWidow V3 Mini HyperSpeed", 0x0258, 0x0271, 0x9f, "keyboard", 0x1f);
        RazerPair("BlackWidow V4 Mini HyperSpeed", 0x02b9, 0x02ba, 0x9f, "keyboard", 0x1f);
        RazerPair("BlackWidow V4 Tenkeyless HyperSpeed", 0x02d7, 0x02d5, 0x9f, "keyboard", 0x1f);
        RazerPair("BlackWidow V3 Pro", 0x025a, 0x025c, 0x9f, "keyboard", 0x3f);
        list.Add(new(0x1532, 0x0576, "Razer BlackShark V3 Pro", "headset", "Scythe", "USB"));
        list.Add(new(0x1532, 0x0577, "Razer BlackShark V3 Pro", "headset", "Scythe", "2.4G"));
        list.Add(new(0x1532, 0x0528, "Razer BlackShark V2 Pro (2020)", "headset", "Razer2020", "2.4G"));
        foreach (var (product, name) in new (ushort, string)[] { (0x0555, "BlackShark V2 Pro (2023)"),
            (0x0556, "BlackShark V2 Pro (2023)"), (0x0557, "BlackShark V2 Pro (2023)"), (0x053a, "Barracuda Pro") })
            list.Add(new(0x1532, product, "Razer " + name, "headset", product == 0x0557 ? "Unsupported" : "RazerPA", "2.4G"));

        void AsusMouse(string name, ushort wired, ushort wireless, int scale = 1)
        {
            list.Add(new(0x0b05, wired, name, "mouse", "AsusMouse", "USB", Scale: scale));
            list.Add(new(0x0b05, wireless, name, "mouse", "AsusMouse", "2.4G", Scale: scale));
        }
        AsusMouse("ROG Gladius III Aimpoint", 0x1a70, 0x1a72);
        AsusMouse("ROG Gladius III Eva 2", 0x1b0a, 0x1b0c);
        AsusMouse("ROG Gladius III Wireless", 0x197d, 0x197f);
        AsusMouse("ROG Chakram X", 0x1a18, 0x1a1a);
        AsusMouse("ROG Harpe Ace Aim Lab Edition", 0x1a92, 0x1a94);
        AsusMouse("ROG Keris Wireless Aimpoint", 0x1a66, 0x1a68);
        AsusMouse("ROG Spatha X", 0x1977, 0x1979);
        AsusMouse("TX Gaming Mouse Mini", 0x1af3, 0x1af5);
        AsusMouse("TUF Gaming Mini Miku Edition", 0x1c56, 0x1c57);
        AsusMouse("ROG Chakram", 0x18e3, 0x18e5, 25);
        AsusMouse("ROG Keris Wireless", 0x195e, 0x1960, 25);
        AsusMouse("ROG Keris EVA Edition", 0x1a57, 0x1a59, 25);
        AsusMouse("ROG Pugio II", 0x1906, 0x1908, 25);
        AsusMouse("ROG Strix Impact II Wireless", 0x1947, 0x1949, 25);
        list.Add(new(0x0b05, 0x19f4, "TUF Gaming M4 Wireless", "mouse", "AsusMouse", "2.4G"));
        list.Add(new(0x0b05, 0x1a8d, "TX Gaming Mouse", "mouse", "AsusMouse"));
        foreach (var (product, name, offset) in new (ushort, string, int)[] {
            (0x1a83,"ROG Azoth",6), (0x1a85,"ROG Azoth",6), (0x1b3f,"ROG Azoth Extreme",6),
            (0x1cef,"ROG Azoth Extreme SE",6), (0x1c24,"ROG Azoth X",6), (0x1c25,"ROG Azoth X",6),
            (0x193c,"ROG Falchion",11), (0x193e,"ROG Falchion",11), (0x1b04,"ROG Falchion RX",11),
            (0x196b,"ROG Claymore II",6), (0x1aae,"ROG Strix Scope II 96",6),
            (0x1b78,"ROG Strix Scope II 96 RX",6), (0x1a07,"ROG Strix Scope RX TKL",6) })
            list.Add(new(0x0b05, product, name, "keyboard", "AsusKeyboard", BatteryOffset: offset));
        foreach (var (product, name) in new (ushort, string)[] { (0x1afa,"ROG Delta II"),
            (0x1d41,"ROG Delta II KJP"), (0x1b84,"ROG Pelta"), (0x1ad3,"ROG Cetra SpeedNova") })
            list.Add(new(0x0b05, product, name, "headset", "AsusHeadset", "2.4G"));
        // shortcut: OMNI keeps the already verified Azoth X route; other slots need separate routing tests.
        list.Add(new(0x0b05, 0x1ace, "ROG OMNI Receiver", "keyboard", "Omni"));
        return list.AsReadOnly();
    }
}
