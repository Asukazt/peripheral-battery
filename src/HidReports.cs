using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace PeriDock;
internal interface IReportSession : IDisposable
{
    Task<byte[]?> Query(byte[] request, Func<byte[],bool> matches, bool feature=false, bool featureFallback=false, int timeoutMs=900);
}
internal sealed class HidReports : IReportSession
{
    readonly List<(HidDevice Device,SafeFileHandle Handle,FileStream? Stream)> channels=[];
    internal HidReports(HidDevice[] devices,bool featureOnly=false)
    {
        Exception? last=null;
        foreach(var device in devices)
        {
            try
            {
                var handle=NativeDevices.Open(device.Path,overlapped:!featureOnly,metadataOnly:featureOnly);
                FileStream? stream=null;
                try {if(!featureOnly)stream=new FileStream(handle,FileAccess.ReadWrite,1,true);}
                catch {handle.Dispose();throw;}
                channels.Add((device,handle,stream));
            }
            catch(Exception ex) when(ex is Win32Exception or IOException or UnauthorizedAccessException)
            { if(last is null || Diagnostics.NativeError(last)!=5)last=ex; }
        }
        if(channels.Count==0)throw last ?? new IOException("未找到可用 HID 电量接口");
    }
    public async Task<byte[]?> Query(byte[] request,Func<byte[],bool> matches,bool feature=false,bool featureFallback=false,int timeoutMs=900)
    {
        if(request.Length<1 || request.Length>1024 || timeoutMs is <1 or >2500)throw new ArgumentException("Invalid battery exchange");
        if(feature)return await Feature(request,matches,timeoutMs);
        foreach(var c in channels)NativeDevices.HidD_FlushQueue(c.Handle);
        using var timeout=new CancellationTokenSource(timeoutMs);
        Exception? last=null; var written=false;
        var headsetRegister=request.Length==20 && request[0]==0x11 && request[2]==5 && request[3]==0x0d &&
            channels.All(c=>c.Device.Vendor==0x046d && c.Device.Product==0x0ac4);
        foreach(var c in channels.Where(c=>c.Stream is not null && c.Device.OutputBytes>=request.Length).OrderBy(c=>c.Device.OutputBytes))
        {
            var output=new byte[c.Device.OutputBytes];request.CopyTo(output,0);
            try {await c.Stream!.WriteAsync(output,timeout.Token);written=true;if(!headsetRegister)break;}
            catch(IOException ex) when(featureFallback && (ex.HResult&0xffff)==1 && c.Device.FeatureBytes>=request.Length)
            { SetFeature(c.Handle,request,c.Device.FeatureBytes);written=true;break; }
            catch(Exception ex) when(ex is IOException or Win32Exception) {last=ex;}
        }
        if(!written && featureFallback && last is null)
        {
            var c=channels.FirstOrDefault(c=>c.Device.FeatureBytes>=request.Length);
            if(c.Handle is not null){SetFeature(c.Handle,request,c.Device.FeatureBytes);written=true;}
        }
        if(!written)throw last ?? new IOException("此 HID collection 不支持该电量报告长度");
        var reads=channels.Where(c=>c.Stream is not null && c.Device.InputBytes>0)
            .Select(c=>Read(c.Stream!,c.Device.InputBytes,matches,timeout.Token)).ToList();
        try
        {
            while(reads.Count>0)
            {
                var ready=await Task.WhenAny(reads);reads.Remove(ready);
                try {var result=await ready;if(result is not null)return result;}
                catch(Exception ex) when(ex is IOException or Win32Exception or UnauthorizedAccessException)
                {if(last is null || Diagnostics.NativeError(last)!=5)last=ex;}
            }
            if(last is not null)throw last;
            return null;
        }
        finally
        {
            timeout.Cancel();
            try {await Task.WhenAll(reads);}
            catch(Exception ex) when(ex is IOException or Win32Exception or UnauthorizedAccessException) { }
        }
    }
    static async Task<byte[]?> Read(FileStream stream,int length,Func<byte[],bool> matches,CancellationToken token)
    {
        try
        {
            for(var i=0;i<128;i++)
            {
                var bytes=new byte[length];var count=await stream.ReadAsync(bytes,token);
                if(count==0)return null;
                var packet=bytes[..count];if(matches(packet))return packet;
            }
            return null;
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){return null;}
    }
    static void SetFeature(SafeFileHandle handle,byte[] request,int size)
    {
        if(size<request.Length)throw new IOException("Feature report is too short");
        var packet=request.ToArray();
        if(!NativeDevices.HidD_SetFeature(handle,packet,packet.Length))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法发送电量查询 feature report");
    }
    async Task<byte[]?> Feature(byte[] request,Func<byte[],bool> matches,int timeoutMs)
    {
        var c=channels.FirstOrDefault(c=>c.Device.FeatureBytes>=request.Length);
        if(c.Handle is null)throw new IOException("未找到对应 feature report");
        var deadline=DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            SetFeature(c.Handle,request,c.Device.FeatureBytes);
            await Task.Delay(60);
            var packet=new byte[request.Length];packet[0]=request[0];
            if(!NativeDevices.HidD_GetFeature(c.Handle,packet,packet.Length))throw new Win32Exception(Marshal.GetLastWin32Error(),"无法读取电量 feature report");
            if(matches(packet))return packet;
        }while(DateTime.UtcNow<deadline);
        return null;
    }
    public void Dispose()
    {foreach(var c in channels){c.Stream?.Dispose();c.Handle.Dispose();}}
}
