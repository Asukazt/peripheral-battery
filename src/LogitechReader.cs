using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PeriDock;

internal static class LogitechReader
{
    internal static bool Accepts(HidDevice d) => d.Vendor==0x046d &&
        ((d.UsagePage==0xff00 && d.Usage is 1 or 2) || (d.UsagePage==0xff43 && d.Usage==0x202) ||
         (d.Product==0x0ac4 && d.UsagePage==0x0c && d.Usage==1)) && d.InputBytes>=7;
    internal static string SlotId(DeviceTarget receiver,byte slot)=>receiver.Id+"-s"+slot;
    internal static DeviceTarget Slot(DeviceTarget receiver,byte slot,string name,string kind)
        =>receiver with {Id=SlotId(receiver,slot),Name=name,Kind=kind,LogitechSlot=slot,ReceiverId=receiver.Id};
    internal static bool Matches(byte[] p,byte[] q)
    {
        if(q.Length<4 || p.Length<4 || p[0] is not (0x10 or 0x11) || p[1]!=q[1])return false;
        if(p[2] is 0x8f or 0xff)return p.Length>=6 && p[3]==q[2] && p[4]==q[3];
        return p[2]==q[2] && p[3]==q[3];
    }
    static readonly (int Mv,int Level)[] Curve=[(4186,100),(4067,90),(3989,80),(3922,70),(3859,60),(3811,50),(3778,40),(3751,30),(3717,20),(3671,10),(3646,5),(3579,2),(3500,0)];
    static int Estimate(int mv,(int Mv,int Level)[] curve)
    {
        for(var i=1;i<curve.Length;i++)if(mv>=curve[i].Mv)
            return Math.Clamp((int)Math.Round(curve[i].Level+(double)(mv-curve[i].Mv)*(curve[i-1].Level-curve[i].Level)/(curve[i-1].Mv-curve[i].Mv)),0,100);
        return 0;
    }
    internal static Reading? Battery(ushort feature,byte[] p)
    {
        if(p.Length<3)return null;
        int? level;bool? charging;var estimate=false;
        if(feature is 0x1004 or 0x1000)
        {
            if(p[0]>100)return null;level=p[0];
            charging=p[2] switch {0=>false,1 or 2 or 3 or 4=>true,_=>null};
            if(feature==0x1004 && level==0 && p[1]!=0)
            {level=p[1] switch {8=>90,4=>50,2=>20,1=>5,_=>null};estimate=true;}
            if(level==0 || level is null)return null;
        }
        else if(feature is 0x1001 or 0x1f20)
        {
            if(feature==0x1f20 && (p[2]&1)==0)return null;
            var mv=p[0]<<8|p[1];if(mv is <2500 or >5000)return null;
            charging=feature==0x1001?(p[2]&0x80)!=0:(p[2]&2)!=0;
            level=charging==true?(feature==0x1001 && (p[2]&3)==1?100:null):Estimate(mv,Curve);
            estimate=level.HasValue && charging!=true;
        }
        else return null;
        return new(level,charging,null,null,$"Logitech HID++ {feature:x4}",
            estimate?"电量为档位或电压估计":level is null?"充电中；不使用电压伪造百分比":"已读取",
            Connected:true,ChargingNote:charging is null?"充电状态未知":null);
    }
    sealed class Channel(IReportSession session,int timeoutMs)
    {
        int swid=Random.Shared.Next(6);
        internal byte Error {get;private set;}
        internal async Task<byte[]?> Ask(byte slot,byte feature,byte function,params byte[] parameters)
        {
            var q=new byte[20];q[0]=0x11;q[1]=slot;q[2]=feature;q[3]=(byte)((function<<4)|(0x0a+(swid++%6)));
            parameters.CopyTo(q,4);return await Exchange(q);
        }
        internal async Task<byte[]?> Exchange(byte[] q)
        {
            Error=0;var p=await session.Query(q,p=>Matches(p,q),timeoutMs:timeoutMs);
            if(p is null)return null;
            if(p[2] is 0x8f or 0xff){Error=p[5];return null;}
            return p[4..];
        }
        internal async Task<byte> Feature(byte slot,ushort feature)
            =>(await Ask(slot,0,0,(byte)(feature>>8),(byte)feature))?.FirstOrDefault()??0;
    }
    internal static async Task<List<DeviceTarget>> Expand(DeviceTarget receiver,IEnumerable<DeviceTarget> known,Func<HidDevice[],IReportSession>? open=null)
    {
        var result=known.Where(t=>t.ReceiverId==receiver.Id && t.LogitechSlot is >=1 and <=6)
            .Select(t=>Slot(receiver,t.LogitechSlot!.Value,t.Name,t.Kind)).ToDictionary(t=>t.Id);
        var devices=receiver.Interfaces.Where(Accepts).ToArray();
        if(devices.Length==0)return result.Count>0?result.Values.ToList():[receiver];
        try
        {
            using var session=open?.Invoke(devices)??new HidReports(devices);var ch=new Channel(session,150);
            var elapsed=Stopwatch.StartNew();
            for(byte slot=1;slot<=6 && elapsed.ElapsedMilliseconds<7000;slot++)
            {
                var ping=await ch.Ask(slot,0,1,0,0,0x5a);
                if(ping is null)
                {
                    if(ch.Error==8)result.Remove(SlotId(receiver,slot));
                    // Older HID++ 1 devices lack the feature table; only a real battery reply proves a slot.
                    if(ch.Error==1 && await Legacy(ch,slot) is not null)
                        result[SlotId(receiver,slot)]=Slot(receiver,slot,$"Logitech HID++ 1.0 外设 · 槽位 {slot}","device");
                    continue;
                }
                if(ping.Length<3 || ping[2]!=0x5a)continue;
                var name=$"Logitech 外设 · 槽位 {slot}";var kind="device";
                var fi=await ch.Feature(slot,5);
                if(fi!=0)
                {
                    var length=(await ch.Ask(slot,fi,0))?.FirstOrDefault()??0;
                    var bytes=new List<byte>();
                    while(bytes.Count<Math.Min((int)length,64) && elapsed.ElapsedMilliseconds<7000)
                    {
                        var piece=await ch.Ask(slot,fi,1,(byte)bytes.Count);if(piece is null || piece.Length==0)break;
                        bytes.AddRange(piece.Take(16));
                    }
                    if(length>0 && bytes.Count>=length)
                    {
                        var text=Encoding.UTF8.GetString(bytes.Take(length).ToArray());
                        text=new string(text.Where(c=>!char.IsControl(c)).ToArray()).Trim();
                        if(text.Length>0)name=text.StartsWith("Logitech",StringComparison.OrdinalIgnoreCase)?text:"Logitech "+text;
                    }
                    var type=(await ch.Ask(slot,fi,2))?.FirstOrDefault();
                    kind=type is 0 or 2?"keyboard":type is 3 or 4 or 5?"mouse":DeviceCatalog.Kind(name);
                }
                result[SlotId(receiver,slot)]=Slot(receiver,slot,name,kind);
            }
        }
        catch(Exception ex) when(ex is IOException or Win32Exception or UnauthorizedAccessException or OperationCanceledException)
        {new Diagnostics(AppContext.BaseDirectory).HidFailure(receiver.Interfaces[0],"slots",ex);}
        return result.Count>0?result.Values.ToList():[receiver];
    }
    static async Task<Reading?> Legacy(Channel ch,byte slot)
    {
        foreach(byte reg in new byte[]{0x0d,7})
        {
            var p=await ch.Exchange([0x10,slot,0x81,reg,0,0,0]);if(p is null || p.Length<3)continue;
            int? level=reg==0x0d?p[0]<=100?p[0]:null:p[0] switch {7=>90,5=>50,3=>20,1=>5,_=>null};
            if(level is null)continue;
            var charging=reg==0x0d?(p[2]&0xf0) is 0x50 or 0x90:(p[1]&0x21)==0x21 || (p[1]&0x22)==0x22;
            return new(level,charging,null,null,"Logitech HID++ 1.0",reg==7?"档位估计":"已读取",Connected:true);
        }
        return null;
    }
    internal static async Task<Reading> Read(HidDevice[] devices,DeviceProfile profile,byte slot=0xff,Func<HidDevice[],IReportSession>? open=null)
    {
        var selected=devices.Where(Accepts).ToArray();
        if(selected.Length==0)return new(null,null,null,null,"Logitech HID++","已识别；未找到 HID++ 电量 collection",Failure:ReadFailure.ProtocolUnsupported);
        if(profile.Reader=="LogitechReceiver" && slot==0xff)return new(null,null,null,null,"Logitech HID++","接收器已识别；未发现可响应的配对槽位",Failure:ReadFailure.ReadError);
        using var session=open?.Invoke(selected)??new HidReports(selected);var ch=new Channel(session,450);
        if(profile.Product==0x0ac4)
        {
            var p=await ch.Exchange([0x11,slot,5,0x0d,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]);
            if(p is {Length:>=3})
            {
                var mv=p[0]<<8|p[1];
                if(mv is >=2500 and <=5000)return new(p[2]==3?null:Estimate(mv,[(4175,100),(3817,50),(3766,30),(3730,20),(3664,5),(3310,0)]),
                    p[2]==3,null,null,"Logitech G535 HID++","电压估计；充电时不换算百分比",Connected:true);
            }
        }
        foreach(ushort feature in new ushort[]{0x1004,0x1000,0x1001,0x1f20})
        {
            var fi=await ch.Feature(slot,feature);if(fi==0)continue;
            var p=await ch.Ask(slot,fi,feature==0x1004?(byte)1:(byte)0);
            if(p is not null)
            {
                if(feature==0x1f20 && p.Length>=3 && (p[2]&1)==0)
                    return new(null,null,null,null,"Logitech HID++","接收器已连接；耳机未连接",Connected:false);
                var value=Battery(feature,p);if(value is not null)return value;
            }
        }
        var legacy=selected.Any(d=>d.UsagePage==0xff00 && d.Usage==1 && d.OutputBytes>=7)?await Legacy(ch,slot):null;
        return legacy??new(null,null,null,null,"Logitech HID++","已识别配对设备；休眠、未响应或电量功能未适配",Failure:ReadFailure.ReadError);
    }
}
