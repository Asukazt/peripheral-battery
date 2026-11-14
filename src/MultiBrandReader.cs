using System.ComponentModel;
using System.IO;

namespace PeriDock;

internal static class MultiBrandReader
{
    internal static bool Handles(string reader) => reader is "Compx" or "AtkNearlink" or "AtkBitmouse" or "AtkKeyboard" or "AulaKeyboard" or "AulaMouse"
        or "MchoseMouse" or "MchoseA5" or "MchoseG7" or "MchoseKeyboard" or "MchoseV9" or "MchoseTurbo"
        or "HyperXCloud2" or "HyperXKingston" or "HyperXCloud3" or "HyperXCloud3S" or "HyperXAlpha" or "HyperXAlpha2" or "HyperXHaste" or "HyperXFlight" or "LogitechCenturion";
    internal static bool Accepts(HidDevice d, DeviceProfile p)
    {
        if(d.Vendor!=p.Vendor || d.Product!=p.Product || (p.MatchName is not null && !d.Name.StartsWith(p.MatchName,StringComparison.OrdinalIgnoreCase)))return false;
        bool Output(int length)=>d.OutputBytes>=length && d.InputBytes>0;
        return p.Reader switch
        {
            "Compx" => d.UsagePage==0xff02 && d.Usage==2 && Output(17),
            "AtkNearlink" => d.UsagePage==(d.Product==0x108c?0xffb3:0xffb2) && d.Usage==1 && Output(64),
            "AtkBitmouse" => d.UsagePage==0xff05 && d.Usage==1 && Output(64),
            "AtkKeyboard" or "AulaMouse" => d.UsagePage==0xff1c && d.Usage==0x92 && Output(32),
            "AulaKeyboard" => d.UsagePage==0xff80 && d.Usage==1 && Output(33),
            "MchoseMouse" => d.UsagePage==0xff01 && d.Usage==1 && d.FeatureBytes>=21,
            "MchoseA5" => d.UsagePage==0xffff && d.FeatureBytes>=65,
            "MchoseG7" => d.UsagePage==0xff01 && d.Usage==0x10 && Output(65),
            "MchoseKeyboard" => d.UsagePage==0xff02 && Output(20),
            "MchoseV9" or "MchoseTurbo" => d.UsagePage>=0xff00 && Output(64),
            "HyperXCloud2" => d.UsagePage==0xff90 && d.Usage==0x303 && Output(52),
            "HyperXKingston" => d.UsagePage==0xff13 && d.Usage==1 && Output(62),
            "HyperXCloud3" => d.UsagePage==0xff13 && d.Usage==1 && (Output(62) || (d.FeatureBytes>=62 && d.InputBytes>0)),
            "HyperXCloud3S" => d.Path.Contains("mi_03",StringComparison.OrdinalIgnoreCase) && (d.UsagePage>=0xff00 || d.UsagePage==0x0c) && d.InputBytes>0,
            "HyperXAlpha" => d.UsagePage==0xff43 && d.Usage==0x202 && Output(31),
            "HyperXAlpha2" => d.UsagePage==0xff13 && d.Usage==0xff00 && Output(64),
            "HyperXHaste" => d.UsagePage==0xff00 && d.Usage==1 && d.Path.Contains("mi_02",StringComparison.OrdinalIgnoreCase) && Output(64),
            "HyperXFlight" => d.UsagePage is 0xff53 or 0xff90 && d.Usage==0x303 && Output(20),
            "LogitechCenturion" => d.UsagePage==0xffa0 && d.Usage==1 && Output(64),
            _=>false
        };
    }
    internal static async Task<Reading> Read(HidDevice[] devices,DeviceProfile profile,Func<HidDevice[],IReportSession>? open=null)
    {
        var selected=devices.Where(d=>Accepts(d,profile)).OrderByDescending(d=>d.UsagePage>=0xff00).ToArray();
        if(selected.Length==0)return new(null,null,null,null,profile.Reader,"已识别型号；未找到匹配的电量接口",Failure:ReadFailure.ProtocolUnsupported);
        var reader=profile.Reader;
        using var session=open?.Invoke(selected) ?? new HidReports(selected,reader is "MchoseMouse" or "MchoseA5");
        var sequence=(ushort)Random.Shared.Next(1,65536);
        var report=reader=="AtkNearlink"?(byte)(profile.Product==0x108c?5:4):reader=="AtkKeyboard"?(byte)4:(byte)0;
        Task<byte[]?> Query(byte[] request,Func<byte[],bool> predicate,bool feature=false)=>session.Query(request,predicate,feature,
            reader=="HyperXCloud3",reader=="HyperXFlight"?2000:1000);
        Reading Empty()=>new(null,null,null,null,reader,"已识别设备；未收到有效的新鲜电量应答（可能休眠或协议变体）",Failure:ReadFailure.ReadError);
        Reading Offline()=>new(null,null,null,null,reader,"接收器已连接；外设未连接",Connected:false);
        if(reader is "HyperXAlpha" or "HyperXHaste")
        {
            var alpha=reader=="HyperXAlpha";
            var link=await Query(MultiBrandProtocols.Request(reader,alpha?(byte)3:(byte)0x52),p=>p.Length>=(alpha?4:3) &&
                (alpha?p[0]==0x21 && p[1]==0xbb && p[2]==3:p[0]==0x53 && p[1]==1));
            if(link is null)return Empty();
            if(alpha?link[3]==1:link[2]==0)return Offline();
            if(!alpha && link[2]!=1)return Empty();
        }
        var aula=reader=="AulaMouse";
        Exception? failure=null;
        try
        {
            if(aula)
            {
                var start=await Query(MultiBrandProtocols.Request(reader,1),p=>p.Length>=4 && p[0]==4 && p[1]==0 && p[2]==0 && p[3]==1);
                if(start is null)return Empty();
            }
            Reading? result=null;
            foreach(var attempt in reader=="MchoseMouse"?new byte[]{0x11,0x12}:reader=="MchoseTurbo"?new byte[]{0,1}:new byte[]{0})
            {
                if(reader=="MchoseMouse" && selected.All(d=>d.FeatureBytes<(attempt==0x11?21:65)))continue;
                var request=MultiBrandProtocols.Request(reader,reader=="MchoseTurbo"?attempt:(byte)0,sequence,reader=="MchoseMouse"?attempt:report);
                byte[]? packet;
                try {packet=await Query(request,p=>MultiBrandProtocols.Power(reader,p,sequence,report) is not null,reader is "MchoseMouse" or "MchoseA5");}
                catch(Exception ex) when(reader=="MchoseMouse" && attempt==0x11 && Diagnostics.NativeError(ex)!=5 &&
                    ex is IOException or Win32Exception)
                {continue;}
                if(packet is not null){result=MultiBrandProtocols.Power(reader,packet,sequence,report);break;}
            }
            if(result is null)return Empty();
            byte chargeCommand=reader switch {"HyperXCloud2" or "HyperXKingston"=>3,"HyperXCloud3"=>0x8a,"HyperXCloud3S"=>0x48,"HyperXAlpha"=>0x0c,"AtkBitmouse"=>15,_=>0};
            if(chargeCommand!=0)
            {
                try
                {
                    var charge=await Query(MultiBrandProtocols.Request(reader,chargeCommand),p=>MultiBrandProtocols.Charging(reader,p).HasValue);
                    var charging=charge is null?null:MultiBrandProtocols.Charging(reader,charge);
                    result=result with {Charging=charging,ChargingNote=charging is null?"充电状态暂不可读":null};
                }
                catch(Exception ex) when(ex is IOException or Win32Exception or UnauthorizedAccessException or OperationCanceledException)
                {result=result with {Charging=null,ChargingNote="充电状态暂不可读；电量查询已成功"};}
            }
            return result;
        }
        catch(Exception ex){failure=ex;throw;}
        finally
        {
            if(aula)
            {
                try {await session.Query(MultiBrandProtocols.Request(reader,2),p=>p.Length>=4 && p[0]==4 && p[3]==2,timeoutMs:250);}
                catch(Exception ex) when(failure is not null && ex is IOException or Win32Exception or UnauthorizedAccessException or OperationCanceledException) { }
            }
        }
    }
}
