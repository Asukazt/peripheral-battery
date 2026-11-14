using PeriDock;

internal static class MultiBrandTests
{
    internal static void Run(Action<string, Action> check)
    {
        void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
        HidDevice Device(ushort vid, ushort pid, ushort page, ushort usage, string name = "", string container = "one", ushort size = 64)
            => new($"test-{vid:x4}-{pid:x4}-{page:x4}-{container}", pid, page, usage, size, size, 65, vid, name, container);
        check("six new brands are discovered on Bluetooth without treating other words as brands", () =>
        {
            foreach (var name in new[] { "Logitech MX Master 3S", "罗技 K780", "HyperX Cloud III", "MCHOSE G7", "迈从 K87", "ATK X1", "AULA F75", "狼蛛 F87", "VGN F1", "VXE R1" }) Equal(true, DeviceCatalog.IsBranded(name));
            Equal(false, DeviceCatalog.IsBranded("CHATK keyboard")); Equal(false, DeviceCatalog.IsBranded("INVGN device"));
        });
        check("known new devices route by VID PID and preserve two identical physical units", () =>
        {
            var found = DeviceCatalog.FromHid([Device(0x3554,0xf505,0xff02,2), Device(0x3554,0xf505,0xff02,2,container:"two"),
                Device(0x03f0,0x05b7,0xff13,1), Device(0x41e4,0x2001,0xff02,1), Device(0x0c45,0xfefc,0xff80,1,"AULA F87 Pro V2C")]);
            Equal(5, found.Count); Equal(5, found.Select(t => t.Id).Distinct().Count());
            Equal(true, found.All(t => t.Supported)); Equal(2, found.Count(t => t.Name.Contains("VGN")));
        });
        check("shared OEM ids require product identity and unknown models receive no query", () =>
        {
            var unrelated=DeviceCatalog.FromHid([Device(0x3554,0xfa09,0xff02,2,"LEOBOG Hi75C Pro"), Device(0x320f,0x5088,0xff1c,0x92,"Other Keyboard")]);
            Equal(false, unrelated.Any(t => t.Supported));
            var unknown=DeviceCatalog.FromHid([Device(0x03f0,0xffff,0xff13,1,"HyperX New Model")]).Single();
            Equal(false, unknown.Supported);
            var reading=DeviceReader.ReadHidTarget(unknown, (_,_) => throw new Exception("must not query")).GetAwaiter().GetResult();
            Equal(ReadFailure.ProtocolUnsupported, reading.Failure);
        });
        check("CompX request and captured response validate command checksum and level", () =>
        {
            Equal("0804000000000000000000000000000049", Convert.ToHexString(MultiBrandProtocols.Request("Compx")).ToLowerInvariant());
            var p=Convert.FromHexString("08040000000250000FAD0000000000003B");
            Equal(80, MultiBrandProtocols.Power("Compx",p)?.Percent); Equal(false, MultiBrandProtocols.Power("Compx",p)?.Charging);
            p[16]^=1; Equal<Reading?>(null,MultiBrandProtocols.Power("Compx",p));
            Equal<Reading?>(null,MultiBrandProtocols.Power("Compx",p[..8]));
        });
        check("ATK Evision replies must echo the current request sequence", () =>
        {
            var p=new byte[64]; p[0]=5;p[1]=0x34;p[2]=0x12;p[3]=0x1a;p[8]=59;p[9]=1;
            var req=MultiBrandProtocols.Request("AtkNearlink",sequence:0x1234,report:5);
            Equal(64,req.Length);Equal((byte)0x34,req[1]);Equal((byte)0x1a,req[3]);
            Equal(59,MultiBrandProtocols.Power("AtkNearlink",p,0x1234,5)?.Percent);
            Equal<Reading?>(null,MultiBrandProtocols.Power("AtkNearlink",p,0x1235,5));
            p[8]=101;Equal<Reading?>(null,MultiBrandProtocols.Power("AtkNearlink",p,0x1234,5));
        });
        check("AULA full and incremental keyboard frames work and cable configuration is not battery", () =>
        {
            var p=new byte[33];p[1]=0x55;p[2]=0x10;p[3]=0x18;p[26]=96;
            Equal(96,MultiBrandProtocols.Power("AulaKeyboard",p)?.Percent);p[3]=0;p[26]=95;
            Equal(95,MultiBrandProtocols.Power("AulaKeyboard",p)?.Percent);
            Equal<bool?>(null,MultiBrandProtocols.Power("AulaKeyboard",p)?.Charging);
            Equal("Unsupported",DeviceCatalog.Find(0x38a6,0x2712)?.Reader);
        });
        check("MCHOSE inverted response rejects zero sleeping data and foreign vendor payload", () =>
        {
            var p=new byte[21];p[0]=0x11;p[1]=0xf9;var data=new byte[]{0x53,0x52,0x31,0,0,0,0,0,9,72,1};
            for(var i=0;i<data.Length;i++)p[i+2]=(byte)(data[i]^0xff);
            Equal(72,MultiBrandProtocols.Power("MchoseMouse",p)?.Percent);Equal(true,MultiBrandProtocols.Power("MchoseMouse",p)?.Charging);
            p[2]=0;Equal<Reading?>(null,MultiBrandProtocols.Power("MchoseMouse",p));
            Equal<Reading?>(null,MultiBrandProtocols.Power("MchoseMouse",new byte[65]));
            var req=MultiBrandProtocols.Request("MchoseMouse",report:0x11);Equal(21,req.Length);Equal((byte)0xf9,req[1]);Equal((byte)0xff,req[^1]);
        });
        check("MCHOSE headset status meanings are not shared across different chip families", () =>
        {
            Equal(true,MultiBrandProtocols.Power("MchoseV9",[0x55,0x65,60,0])?.Charging);
            Equal(false,MultiBrandProtocols.Power("MchoseTurbo",[0x55,0x65,60,2])?.Charging);
            Equal<bool?>(null,MultiBrandProtocols.Power("MchoseTurbo",[0x55,0x65,60,3])?.Charging);
            Equal<Reading?>(null,MultiBrandProtocols.Power("MchoseTurbo",[0x55,0x65,60,26]));
            Equal<Reading?>(null,MultiBrandProtocols.Power("MchoseV9",[0x55,0x65,101,0]));
        });
        check("MCHOSE K87 full flag is not invented as charging", () =>
        {
            var p=Convert.FromHexString("134A0100026401000000000000000000000000C5");
            Equal(100,MultiBrandProtocols.Power("MchoseKeyboard",p)?.Percent);Equal<bool?>(null,MultiBrandProtocols.Power("MchoseKeyboard",p)?.Charging);
            p[19]^=1;Equal<Reading?>(null,MultiBrandProtocols.Power("MchoseKeyboard",p));
        });
        check("HyperX Cloud III requires fresh battery state and a matching command", () =>
        {
            Equal(48,MultiBrandProtocols.Power("HyperXCloud3",[0x66,0x89,0x0e,0xd7,0x30])?.Percent);
            Equal<Reading?>(null,MultiBrandProtocols.Power("HyperXCloud3",[0x66,0x89,0,0,48]));
            Equal<Reading?>(null,MultiBrandProtocols.Power("HyperXCloud3",[0x66,0x8a,1,0,48]));
            Equal<bool?>(null,MultiBrandProtocols.Charging("HyperXCloud3",[0x66,0x8a,0xff]));
            Equal(true,MultiBrandProtocols.Charging("HyperXCloud3",[0x66,0x0c,2]));
        });
        check("HyperX battery families retain independent framing and field offsets", () =>
        {
            Equal(76,MultiBrandProtocols.Power("HyperXCloud2",[6,0xff,0xbb,2,0,0,0,76])?.Percent);
            Equal(76,MultiBrandProtocols.Power("HyperXKingston",[0x0b,0,0xbb,2,0,0,0,76])?.Percent);
            Equal(51,MultiBrandProtocols.Power("HyperXAlpha2",[0x51,2,51,0,0x1a,0,0x80])?.Percent);
            Equal(75,MultiBrandProtocols.Power("HyperXAlpha",[0x21,0xbb,0x0b,75])?.Percent);
            Equal<Reading?>(null,MultiBrandProtocols.Power("HyperXAlpha",[0x21,0xbb,0x0c,75]));
            Equal(64,MultiBrandProtocols.Power("HyperXHaste",[0x51,2,64,0xff,0,0,0,0,0,0])?.Percent);
            Equal<bool?>(null,MultiBrandProtocols.Power("HyperXHaste",[0x51,2,64,0xff,0,0,0,0,0,0])?.Charging);
        });
        check("new readers never select pointer or standard keyboard collections", () =>
        {
            var profile=DeviceCatalog.Find(0x3554,0xf505)!;
            Equal(false,MultiBrandReader.Accepts(Device(0x3554,0xf505,1,2),profile));
            Equal(false,MultiBrandReader.Accepts(Device(0x3554,0xf505,0xff02,2,size:8),profile));
            Equal(true,MultiBrandReader.Accepts(Device(0x3554,0xf505,0xff02,2),profile));
            Equal(false,MultiBrandReader.Accepts(Device(0x03f0,0x05b7,0x0c,1),DeviceCatalog.Find(0x03f0,0x05b7)!));
        });
        check("new brand access denial preserves identity transport and clears old live readings", () =>
        {
            var device=Device(0x3554,0xf505,0xff02,2) with {AccessError=5};
            var target=DeviceCatalog.FromHid([device]).Single();
            var read=DeviceReader.ReadHidTarget(target).GetAwaiter().GetResult();
            Equal(ReadFailure.AccessDenied,read.Failure);Equal(5,read.ErrorCode);Equal("2.4G",read.Connection);
            var state=new BatteryState(target.Id,target.Name,target.Name,target.Kind);
            state.Apply(new(55,true,800,800,"fixture","ok"),DateTimeOffset.UtcNow);state.Apply(read,DateTimeOffset.UtcNow);
            Equal<int?>(null,state.Percent);Equal<bool?>(null,state.Charging);Equal<int?>(null,state.DpiX);
        });
        check("Logitech HID++ matches slot feature software id and rejects unrelated replies", () =>
        {
            var req=new byte[]{0x11,2,5,0x1a};
            Equal(true,LogitechReader.Matches([0x11,2,5,0x1a,80,0,0],req));
            Equal(false,LogitechReader.Matches([0x11,1,5,0x1a,80,0,0],req));
            Equal(false,LogitechReader.Matches([0x11,2,5,0x1b,80,0,0],req));
            Equal(true,LogitechReader.Matches([0x10,2,0x8f,5,0x1a,8,0],req));
            Equal(false,LogitechReader.Matches([0x10,2,0x8f,6,0x1a,8,0],req));
        });
        check("Logitech unified grades and voltage readings disclose estimates and do not invent charging percent", () =>
        {
            Equal(72,LogitechReader.Battery(0x1004,[72,0,0])?.Percent);
            Equal(50,LogitechReader.Battery(0x1004,[0,4,0])?.Percent);
            Equal(true,LogitechReader.Battery(0x1004,[0,4,0])!.Status.Contains("估计"));
            Equal<Reading?>(null,LogitechReader.Battery(0x1000,[0xff,0,0]));
            Equal<Reading?>(null,LogitechReader.Battery(0x1001,[0xff,0xff,0]));
            Equal<int?>(null,LogitechReader.Battery(0x1001,[0x0f,0xe3,0x80])?.Percent);
            Equal(true,LogitechReader.Battery(0x1001,[0x0f,0xe3,0x80])?.Charging);
            Equal(100,LogitechReader.Battery(0x1001,[0x0f,0xe3,0x81])?.Percent);
            Equal<Reading?>(null,LogitechReader.Battery(0x1f20,[0x0f,0xe3,0]));
        });
        check("G7 calibration rejects startup eighty and confirms spaced low readings without sharing state", () =>
        {
            var now=DateTimeOffset.UtcNow;var f=new MchoseG7Filter();
            Reading Raw(int p)=>new(p,false,null,null,"MCHOSE G7 HID","raw",Connected:true);
            for(var i=0;i<4;i++)Equal<int?>(null,f.Apply(Raw(80),now.AddSeconds(i*5)).Percent);
            var fallback=f.Apply(Raw(80),now.AddSeconds(20));Equal(80,fallback.Percent);Equal(true,fallback.Cached);
            f=new();Equal(46,f.Apply(Raw(46),now).Percent);
            var outlier=f.Apply(Raw(80),now.AddSeconds(5));Equal(46,outlier.Percent);Equal(true,outlier.Cached);
            Equal(46,f.Apply(Raw(46),now.AddSeconds(10)).Percent);
            Equal(46,f.Apply(Raw(10),now.AddSeconds(15)).Percent);
            Equal(10,f.Apply(Raw(11),now.AddSeconds(20)).Percent);
            Equal<int?>(null,f.Apply(Raw(80),now.AddMinutes(11)).Percent);
            Equal<int?>(null,new MchoseG7Filter().Apply(Raw(80),now).Percent);
        });
        check("BITMOUSE captured nonzero sequence is payload metadata rather than a charging flag", () =>
        {
            Equal(90,MultiBrandProtocols.Power("AtkBitmouse",Convert.FromHexString("087200050007015ABC1000"))?.Percent);
            Equal<bool?>(null,MultiBrandProtocols.Power("AtkBitmouse",Convert.FromHexString("087200050007015ABC1000"))?.Charging);
            Equal(true,MultiBrandProtocols.Charging("AtkBitmouse",[8,0x72,0,5,0,15,1,1]));
        });
        check("every new fixed protocol reaches only its allowed collection and reads through the session", () =>
        {
            var fixtures=new (ushort Vid,ushort Pid,ushort Page,ushort Usage,string Name,byte[] Reply)[]{
                (0x3554,0xf505,0xff02,2,"",Convert.FromHexString("08040000000250000FAD0000000000003B")),
                (0x373b,0x108c,0xffb3,1,"",new byte[64]),
                (0x373b,0x1216,0xff05,1,"",[8,0x72,0,5,0,7,1,90]),
                (0x320f,0x5055,0xff1c,0x92,"ATK V75X",new byte[32]),
                (0x0c45,0xfefc,0xff80,1,"AULA F87 Pro V2C",new byte[33]),
                (0x320f,0x22a0,0xff1c,0x92,"AULA V9 Max+",new byte[32]),
                (0x5253,0x1020,0xff01,1,"",new byte[21]),
                (0x2023,0xf013,0xffff,1,"",[0xa1,0,0,0,0,0x83,0,44,0]),
                (0xa8a5,0x2255,0xff01,0x10,"",[0xaa,0x30,0xa5,0x0b,0x0a,1,1,1,46,0]),
                (0x41e4,0x2001,0xff02,1,"",Convert.FromHexString("134A0100026401000000000000000000000000C5")),
                (0x291d,0x385d,0xff00,1,"",[0x55,0x65,60,2]),
                (0x3837,0x6008,0xff00,1,"",[0x55,0x65,60,2]),
                (0x03f0,0x0696,0xff90,0x303,"",[6,0xff,0xbb,2,0,0,0,76]),
                (0x0951,0x1718,0xff13,1,"",[0x0b,0,0xbb,2,0,0,0,76]),
                (0x03f0,0x05b7,0xff13,1,"",[0x66,0x89,0x0e,0xd7,48]),
                (0x03f0,0x02cc,0xff13,1,"",[0x0c,2,3,1,0,6,75]),
                (0x03f0,0x098d,0xff43,0x202,"",[0x21,0xbb,0x0b,75]),
                (0x03f0,0x08be,0xff13,0xff00,"",[0x51,2,51,0,0x1a,0,0x80]),
                (0x03f0,0x0d97,0xff00,1,"",[0x51,2,64,0xff,0,0,0,0,0,0]),
                (0x0951,0x16c4,0xff53,0x303,"",new byte[20]),
                (0x046d,0x0af7,0xffa0,1,"",new byte[13])
            };
            foreach(var f in fixtures)
            {
                var profile=DeviceCatalog.Find(f.Vid,f.Pid)!;
                var d=Device(f.Vid,f.Pid,f.Page,f.Usage,f.Name,size:65) with {Path="fixture-mi_03-mi_02"};
                if(profile.Reader=="AulaKeyboard"){f.Reply[1]=0x55;f.Reply[2]=0x10;f.Reply[26]=95;}
                if(profile.Reader=="MchoseMouse")
                {
                    f.Reply[0]=0x11;f.Reply[1]=0xf9;
                    var data=new byte[]{0x53,0x52,0x31,0,0,0,0,0,9,72,1};
                    for(var i=0;i<data.Length;i++)f.Reply[i+2]=(byte)(data[i]^0xff);
                }
                if(profile.Reader=="HyperXFlight"){f.Reply[3]=0x0f;f.Reply[4]=0x3c;}
                if(profile.Reader=="LogitechCenturion"){f.Reply[0]=0x51;f.Reply[1]=0x0b;f.Reply[8]=4;f.Reply[10]=70;f.Reply[12]=2;}
                var session=new FixtureReports((q,feature)=>
                {
                    if(profile.Reader=="MchoseMouse"){Equal(true,feature);Equal(21,q.Length);Equal((byte)0xff,q[^1]);}
                    if(profile.Reader=="MchoseA5"){Equal(true,feature);Equal("00000002020083",Convert.ToHexString(q[..7]).ToLowerInvariant());}
                    if(profile.Reader=="HyperXAlpha" && q[2]==3)return [0x21,0xbb,3,0];
                    if(profile.Reader=="HyperXHaste" && q[0]==0x52)return [0x53,1,1];
                    if(profile.Reader=="AulaMouse" && q[3] is 1 or 2)return [4,0,0,q[3]];
                    if(profile.Reader is "AtkNearlink" or "AtkKeyboard")
                    {Array.Copy(q,f.Reply,4);f.Reply[8]=59;f.Reply[9]=1;}
                    if(profile.Reader=="AulaMouse"){f.Reply[0]=4;f.Reply[3]=0x1a;f.Reply[8]=80;}
                    return f.Reply;
                });
                var read=MultiBrandReader.Read([d,Device(f.Vid,f.Pid,1,2)],profile,chosen=>
                {Equal(1,chosen.Length);return session;}).GetAwaiter().GetResult();
                if(!read.Percent.HasValue)throw new Exception(profile.Reader+" did not return a level");
                Equal(true,session.Disposed);
                if(profile.Reader=="AulaMouse"){Equal((byte)2,session.Requests[^1][3]);Equal(true,read.Cached);Equal<bool?>(null,read.Connected);}
            }
        });
        check("fresh battery survives an optional charging error and denied queries keep Win32 five", () =>
        {
            var p=DeviceCatalog.Find(0x03f0,0x05b7)!;
            var session=new FixtureReports((q,_)=>q[1]==0x89?[0x66,0x89,1,1,48]:throw new System.ComponentModel.Win32Exception(5));
            var read=MultiBrandReader.Read([Device(0x03f0,0x05b7,0xff13,1)],p,_=>session).GetAwaiter().GetResult();
            Equal(48,read.Percent);Equal<bool?>(null,read.Charging);Equal(true,session.Disposed);
            var target=DeviceCatalog.FromHid([Device(0x3554,0xf505,0xff02,2)]).Single();
            var denied=DeviceReader.ReadHidTarget(target,(devices,profile)=>MultiBrandReader.Read(devices,profile,_=>throw new System.ComponentModel.Win32Exception(5))).GetAwaiter().GetResult();
            Equal(ReadFailure.AccessDenied,denied.Failure);Equal<int?>(null,denied.Percent);
        });
        check("HyperX off state is not confused with an attached receiver", () =>
        {
            foreach(var alpha in new[]{true,false})
            {
                var p=DeviceCatalog.Find(0x03f0,alpha?(ushort)0x098d:(ushort)0x0d97)!;
                var d=Device(p.Vendor,p.Product,alpha?(ushort)0xff43:(ushort)0xff00,alpha?(ushort)0x202:(ushort)1) with {Path="fixture-mi_02"};
                var s=new FixtureReports((_,_)=>alpha?[0x21,0xbb,3,1]:[0x53,1,0]);
                var value=MultiBrandReader.Read([d],p,_=>s).GetAwaiter().GetResult();
                Equal(false,value.Connected);Equal<int?>(null,value.Percent);Equal(1,s.Requests.Count);
            }
        });
        check("Logitech separates two receiver instances and keeps only previously known sleeping slots", () =>
        {
            var receiver=DeviceCatalog.FromHid([Device(0x046d,0xc52b,0xff00,2)]).Single();
            var other=DeviceCatalog.FromHid([Device(0x046d,0xc52b,0xff00,2,container:"two")]).Single();
            var sleeping=LogitechReader.Slot(receiver,2,"Logitech MX Keys","keyboard");
            var unrelated=LogitechReader.Slot(other,2,"Logitech MX Keys","keyboard");
            var session=new FixtureReports((q,_)=>q[1]==1?[0x11,1,q[2],q[3],2,0,0x5a]:
                [0x10,q[1],0x8f,q[2],q[3],(byte)(q[1]==2?9:8),0]);
            var found=LogitechReader.Expand(receiver,[sleeping,unrelated],_=>session).GetAwaiter().GetResult();
            Equal(2,found.Count);Equal(true,found.Any(t=>t.Id==sleeping.Id));Equal(false,found.Any(t=>t.Id==unrelated.Id));
            Equal(2,found.Select(t=>t.LogitechSlot).Distinct().Count());Equal(true,found.All(t=>t.ReceiverId==receiver.Id));
        });
        check("Logitech feature discovery routes a battery response to the requested slot", () =>
        {
            var d=Device(0x046d,0xc52b,0xff00,2,size:20);var profile=DeviceCatalog.Find(d.Vendor,d.Product)!;
            var s=new FixtureReports((q,_)=>q[2]==0?[0x11,q[1],0,q[3],5,0,0]:
                [0x11,q[1],5,q[3],72,0,0]);
            var value=LogitechReader.Read([d],profile,3,_=>s).GetAwaiter().GetResult();
            Equal(72,value.Percent);Equal(true,s.Requests.All(q=>q[1]==3));Equal(2,s.Requests.Count);
            Equal(true,s.Requests[0][3]!=s.Requests[1][3]);Equal(true,s.Disposed);
        });
        check("AULA temporary query session closes after a missing battery reply", () =>
        {
            var p=DeviceCatalog.Find(0x320f,0x22a0)!;
            var session=new FixtureReports((q,_)=>q[3] is 1 or 2?[4,0,0,q[3]]:null);
            var reading=MultiBrandReader.Read([Device(p.Vendor,p.Product,0xff1c,0x92,"AULA V9 Max+")],p,_=>session).GetAwaiter().GetResult();
            Equal(ReadFailure.ReadError,reading.Failure);Equal<int?>(null,reading.Percent);
            Equal((byte)2,session.Requests[^1][3]);Equal(true,session.Disposed);
        });
        check("MCHOSE documented report variants retry format errors but never hide access denied", () =>
        {
            foreach(var code in new[]{87,5})
            {
                var p=DeviceCatalog.Find(0x5253,0x1020)!;
                var reply=new byte[65];reply[0]=0x12;reply[1]=0xf9;
                var data=new byte[]{0x53,0x52,0x31,0,0,0,0,0,9,72,1};
                for(var i=0;i<data.Length;i++)reply[i+2]=(byte)(data[i]^0xff);
                var s=new FixtureReports((q,_)=>q[0]==0x11?throw new System.ComponentModel.Win32Exception(code):reply);
                try
                {
                    Equal(72,MultiBrandReader.Read([Device(p.Vendor,p.Product,0xff01,1)],p,_=>s).GetAwaiter().GetResult().Percent);
                    Equal(87,code);Equal(2,s.Requests.Count);
                }
                catch(System.ComponentModel.Win32Exception ex){Equal(5,ex.NativeErrorCode);Equal(5,code);Equal(1,s.Requests.Count);}
                Equal(true,s.Disposed);
            }
        });
        check("AULA cleanup error does not replace the primary access denied code", () =>
        {
            var p=DeviceCatalog.Find(0x320f,0x22a0)!;
            var s=new FixtureReports((q,_)=>q[3]==1?[4,0,0,1]:throw new System.ComponentModel.Win32Exception(q[3]==2?87:5));
            try
            {
                MultiBrandReader.Read([Device(p.Vendor,p.Product,0xff1c,0x92,"AULA V9 Max+")],p,_=>s).GetAwaiter().GetResult();
                throw new Exception("Expected denied query");
            }
            catch(System.ComponentModel.Win32Exception ex){Equal(5,ex.NativeErrorCode);}
            Equal((byte)2,s.Requests[^1][3]);Equal(true,s.Disposed);
        });
        check("shared OEM identification keeps an inaccessible sibling without querying an unrelated product", () =>
        {
            var target=DeviceCatalog.FromHid([Device(0x0c45,0xfefc,1,6,"AULA F87 Pro V2C"),
                Device(0x0c45,0xfefc,0xff80,1,"HID compliant") with {AccessError=5}]).Single();
            Equal(2,target.Interfaces.Length);
            var value=DeviceReader.ReadHidTarget(target).GetAwaiter().GetResult();
            Equal(ReadFailure.AccessDenied,value.Failure);
        });
    }
}

sealed class FixtureReports(Func<byte[],bool,byte[]?> respond) : IReportSession
{
    internal List<byte[]> Requests {get;}=[];
    internal bool Disposed {get;private set;}
    public Task<byte[]?> Query(byte[] request,Func<byte[],bool> matches,bool feature=false,bool featureFallback=false,int timeoutMs=900)
    {
        if(timeoutMs is <1 or >2500)throw new Exception("Unbounded exchange");
        Requests.Add(request);var p=respond(request,feature);
        return Task.FromResult(p is not null && matches(p)?p:null);
    }
    public void Dispose()=>Disposed=true;
}
