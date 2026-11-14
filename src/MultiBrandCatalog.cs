namespace PeriDock;

internal static partial class DeviceCatalog
{
    internal static IReadOnlySet<ushort> Vendors { get; } = new HashSet<ushort>
        { 0x1532,0x0b05,0x046d,0x03f0,0x0951,0x5253,0x3837,0xa8a5,0x2023,0x291d,0x41e4,0x373b,0x3554,0x320f,0x0c45,0x38a6 };
    internal static DeviceProfile? ProfileFor(HidDevice d)
    {
        var profile = Find(d.Vendor,d.Product);
        if (profile is not null)
            return profile.MatchName is null || d.Name.StartsWith(profile.MatchName,StringComparison.OrdinalIgnoreCase) ? profile : null;
        if (d.Vendor == 0x046d && d.UsagePage == 0xff00 && d.Usage is 1 or 2)
            return new(d.Vendor,d.Product,d.Name.Length > 0 ? d.Name : "Logitech HID++ 设备",Kind(d.Name),
                d.Name.Contains("receiver",StringComparison.OrdinalIgnoreCase) || d.Name.Contains("dongle",StringComparison.OrdinalIgnoreCase) ? "LogitechReceiver" : "Logitech");
        return null;
    }
    static IEnumerable<DeviceProfile> MultiBrandProfiles()
    {
        var list = new List<DeviceProfile>();
        void Add(ushort vid,ushort pid,string name,string kind,string reader,string connection="2.4G",string? match=null)
            => list.Add(new(vid,pid,name,kind,reader,connection,MatchName:match));
        foreach(var pid in new ushort[]{0xc548,0xc52b,0xc532,0xc52f,0xc518,0xc51a,0xc51b,0xc521,0xc525,0xc526,0xc52e,0xc531,0xc534,0xc535,0xc537,0xc539,0xc53a,0xc53d,0xc53f,0xc541,0xc545,0xc547,0xc54d})
            Add(0x046d,pid,"Logitech HID++ 接收器","device","LogitechReceiver");
        foreach(var (pid,name) in new (ushort,string)[]{(0x0a66,"G533"),(0x0ac4,"G535"),(0x0a5c,"G633"),(0x0a89,"G635"),(0x0a5b,"G933"),(0x0a87,"G935"),(0x0ab5,"G733"),(0x0afe,"G733"),(0x0b1f,"G733"),(0x0aa7,"G PRO"),(0x0aaa,"G PRO X"),(0x0aba,"G PRO X"),(0x0afb,"G PRO X 2"),(0x0afc,"G PRO X 2")})
            Add(0x046d,pid,"Logitech "+name,"headset","Logitech");
        Add(0x046d,0x0af7,"Logitech G PRO X 2 LIGHTSPEED","headset","LogitechCenturion");
        Add(0x046d,0x0b45,"Logitech PRO X 3 LIGHTSPEED","headset","Unsupported");
        foreach(var pid in new ushort[]{0x0696,0x018b}) Add(0x03f0,pid,"HyperX Cloud II Wireless","headset","HyperXCloud2");
        Add(0x0951,0x1718,"HyperX Cloud II Wireless","headset","HyperXKingston");
        foreach(var pid in new ushort[]{0x05b7,0x0c9d}) Add(0x03f0,pid,"HyperX Cloud III Wireless","headset","HyperXCloud3");
        foreach(var pid in new ushort[]{0x0795,0x0995}) Add(0x03f0,pid,"HyperX Cloud II Core Wireless","headset","HyperXCloud3");
        foreach(var pid in new ushort[]{0x02cc,0x06be}) Add(0x03f0,pid,"HyperX Cloud III S Wireless","headset","HyperXCloud3S");
        Add(0x03f0,0x098d,"HyperX Cloud Alpha Wireless","headset","HyperXAlpha");
        Add(0x03f0,0x08be,"HyperX Cloud Alpha 2","headset","HyperXAlpha2");
        foreach(var pid in new ushort[]{0x16c4,0x1723}) Add(0x0951,pid,"HyperX Cloud Flight Wireless","headset","HyperXFlight");
        foreach(var pid in new ushort[]{0x0d97,0x0f98}) Add(0x03f0,pid,"HyperX Pulsefire Haste 2 Wireless","mouse","HyperXHaste");
        Add(0x5253,0x1020,"MCHOSE M7 / L7 接收器","mouse","MchoseMouse");
        Add(0x5253,0x0031,"MCHOSE M7 Ultra","mouse","MchoseMouse","USB");
        Add(0x3837,0x100b,"MCHOSE A7 V2 Ultra","mouse","MchoseMouse");
        Add(0x3837,0x1015,"MCHOSE G98 V3","keyboard","Unsupported");
        Add(0xa8a5,0x2255,"MCHOSE G7","mouse","MchoseG7","USB / 2.4G（待确认）");
        foreach(var pid in new ushort[]{0xf013,0xf015,0xf019}) Add(0x2023,pid,"MCHOSE A5 Pro Max","mouse","MchoseA5",pid==0xf019?"USB":"2.4G");
        Add(0x291d,0x385d,"MCHOSE V9 Pro","headset","MchoseV9","USB / 2.4G（待确认）");
        foreach(var pid in new ushort[]{0x6008,0x600a}) Add(0x3837,pid,"MCHOSE V9 Turbo+","headset","MchoseTurbo",pid==0x600a?"USB":"2.4G");
        Add(0x41e4,0x2001,"MCHOSE K87","keyboard","MchoseKeyboard");
        foreach(var (vid,pid,name,connection) in new (ushort,ushort,string,string)[]{
            (0x3554,0xf58f,"VXE R1 SE+","USB"),(0x373b,0x1085,"VXE R1 SE+","2.4G"),
            (0x3554,0xf58a,"VXE R1 Pro / R1 Pro Max","2.4G"),(0x3554,0xf58c,"VXE R1 Pro Max","USB"),
            (0x3554,0xf505,"VGN Dragonfly 4K","2.4G"),(0x3554,0xf506,"VGN Dragonfly 4K","USB"),
            (0x373b,0x11b6,"ATK A9 Ultimate","USB"),(0x373b,0x11d9,"ATK A9 Ultimate","2.4G"),
            (0x373b,0x10c9,"ATK A9 SE","2.4G"),(0x373b,0x101a,"ATK X1 Pro","2.4G"),(0x373b,0x101b,"ATK Fire X 8K","2.4G")})
            Add(vid,pid,name,"mouse","Compx",connection);
        Add(0x373b,0x108c,"ATK X1 Ultimate","mouse","AtkNearlink");
        Add(0x373b,0x108b,"ATK X1 Ultimate","mouse","AtkNearlink","USB");
        Add(0x373b,0x1216,"ATK / VXE NK 接收器","mouse","AtkBitmouse");
        // Receiver polling can wake V75X from sleep; only its powered USB channel is polled.
        Add(0x320f,0x5088,"ATK V75X","keyboard","Unsupported",match:"ATK V75X");
        Add(0x320f,0x5055,"ATK V75X","keyboard","AtkKeyboard","USB","ATK V75X");
        Add(0x0c45,0xfefc,"AULA F87 Pro V2C","keyboard","AulaKeyboard",match:"AULA");
        Add(0x38a6,0x2712,"AULA F87 Pro V2C","keyboard","Unsupported","USB","AULA");
        Add(0x3554,0xfa09,"AULA F75","keyboard","Compx",match:"AULA");
        Add(0x320f,0x22a0,"AULA V9 Max+","mouse","AulaMouse",match:"AULA");
        Add(0x320f,0x22b7,"AULA V9 Max+","mouse","AulaMouse","USB","AULA");
        return list;
    }
}
