namespace PeriDock;

internal static class MultiBrandProtocols
{
    internal static byte[] Request(string reader, byte command=0, ushort sequence=1, byte report=0)
    {
        byte[] Frame(int length, params byte[] prefix) { var value=new byte[length]; prefix.CopyTo(value,0); return value; }
        return reader switch
        {
            "Compx" => Convert.FromHexString("0804000000000000000000000000000049"),
            "AtkNearlink" or "AtkKeyboard" => Frame(reader=="AtkKeyboard"?32:64,report,(byte)sequence,(byte)(sequence>>8),0x1a),
            "AtkBitmouse" => Bitmouse(command==0?(byte)7:command),
            "AulaKeyboard" => Frame(33,0,0xaa,0x10),
            "AulaMouse" => Frame(32,4,0,0,command==0?(byte)0x1a:command),
            "MchoseMouse" => Inverted(report==0?(byte)0x11:report),
            "MchoseG7" => Frame(65,0,0x55,0x30,0xa5,0x0b,0x2e,1,1,1),
            "MchoseA5" => Frame(65,0,0,0,2,2,0,0x83),
            "MchoseKeyboard" => Frame(20,0x13,0x4a,1,0,0,2),
            "MchoseV9" => Frame(64,0x55,0x65,1),
            "MchoseTurbo" => command==1?Frame(64,0x55,0x65,1):Frame(64,0x55,0xaa,1,0,1,0,1,0x0b,0),
            "HyperXCloud2" => Frame(52,6,0xff,0xbb,command==0?(byte)2:command),
            "HyperXKingston" => Frame(62,6,0,2,0,0x9a,0,0,0x68,0x4a,0x8e,0x0a,0,0,0,0xbb,command==0?(byte)2:command),
            "HyperXCloud3" => Frame(62,0x66,command==0?(byte)0x89:command),
            "HyperXCloud3S" => Frame(64,0x0c,2,3,1,0,command==0?(byte)6:command),
            "HyperXAlpha" => Frame(31,0x21,0xbb,command==0?(byte)0x0b:command),
            "HyperXAlpha2" or "HyperXHaste" => Frame(64,command==0?(byte)0x50:command,command==0x52?(byte)1:(byte)2),
            "HyperXFlight" => Frame(20,0x21,0xff,5),
            "LogitechCenturion" => Frame(64,0x51,8,0,3,0x1a,0,3,0,4,0x0a),
            _ => throw new ArgumentException("Unsupported battery request")
        };
    }
    static byte[] Inverted(byte report)
    {
        var p=Enumerable.Repeat((byte)0xff,report==0x11?21:65).ToArray();p[0]=report;p[1]=0xf9;return p;
    }
    static byte[] Bitmouse(byte command)
    {
        var p=new byte[64];p[0]=8;p[2]=0x72;p[3]=2;p[5]=1;p[6]=command;p[7]=1;p[1]=(byte)p.Skip(2).Sum(b=>(int)b);return p;
    }
    static Reading Value(string source,int? percent,bool? charging=null,string status="已读取")
        => new(percent,charging,null,null,source,status,Connected:true,ChargingNote:charging is null?"此协议未提供可确认的充电状态":null);
    internal static Reading? Power(string reader,byte[] p,ushort sequence=1,byte report=0)
    {
        bool Header(params byte[] prefix)=> p.AsSpan().StartsWith(prefix);
        switch(reader)
        {
            case "Compx":
                if(p.Length<17 || !Header(8,4) || p.Take(17).Sum(b=>(int)b)%256!=0x55 || p[6]>100)return null;
                return Value("CompX HID",p[6],p[7]!=0);
            case "AtkNearlink": case "AtkKeyboard":
                if(p.Length<(reader=="AtkKeyboard"?10:64) || !Header(report,(byte)sequence,(byte)(sequence>>8),0x1a) || p[8]>100)return null;
                if(reader=="AtkKeyboard" && (p[5]!=0 || p[6]!=0 || p[9]>2))return null;
                return Value("ATK Evision HID",p[8],p[9]!=0);
            case "AtkBitmouse":
                if(p.Length<8 || !Header(8,0x72) || p[2]==0xff || p[5]!=7 || p[6]<1 || p[7] is 0 or >100)return null;
                return Value("ATK BITMOUSE HID",p[7]);
            case "AulaKeyboard":
                var at=Header(0,0x55,0x10)?1:Header(0x55,0x10)?0:-1;
                if(at<0 || p.Length<=at+25 || p[at+25] is 0 or >100)return null;
                return Value("AULA 键盘 HID",p[at+25]);
            case "AulaMouse":
                if(p.Length<10 || !Header(4,0,0,0x1a) || p[5]!=0 || p[6]!=0 || p[8] is 0 or >100 || p[9]>2)return null;
                return Value("AULA Evision HID",p[8],p[9] is 1 or 2,"接收器电量；无线在线状态待确认") with {Connected=null,Cached=true};
            case "MchoseMouse":
                if(p.Length<13 || p[0] is not (0x11 or 0x12) || p[1]!=0xf9)return null;
                var data=p.Skip(2).Select(b=>(byte)(b^0xff)).ToArray();var vid=data[0]|data[1]<<8;
                if(vid is not (0x5253 or 0x3837) || data[9]>100 || data[10]>1)return null;
                return Value("MCHOSE Realtek HID",data[9],data[10]==1);
            case "MchoseG7":
                if(p.Length<10 || !Header(0xaa,0x30) || p[8]>100 || p[9]>1)return null;
                return Value("MCHOSE G7 HID",p[8],p[9]==1,"设备原始读数（可能抖动）");
            case "MchoseA5":
                at=Header(0xa1)?0:Header(0,0xa1)?1:-1;
                if(at<0 || p.Length<at+8 || p[at+4]!=0 || p[at+5]!=0x83 || p[at+7]>100)return null;
                return Value("MCHOSE A5 HID",p[at+7],p[at+6]!=0);
            case "MchoseKeyboard":
                if(p.Length<20 || !Header(0x13,0x4a,1) || p[5]>100 || (byte)p.Take(19).Sum(b=>(int)b)!=p[19])return null;
                return Value("MCHOSE K87 HID",p[5]);
            case "MchoseV9":
                if(p.Length<4 || !Header(0x55,0x65) || p[2]>100)return null;
                return Value("MCHOSE V9 HID",p[2],p[3] is 0?true:p[3] is 2?false:null);
            case "MchoseTurbo":
                if(Header(0x55,0x65) && p.Length>=4 && p[2]<=100 && p[3] is 2 or 3 or 4)
                    return Value("MCHOSE Turbo HID",p[2],p[3]==2?false:null);
                if(p.Length<11 || !Header(0x55,0xaa,1) || p[3]>1 || p[4]<3 || p.Length<8+p[4] || p[5]!=0 || p[6]!=1 || p[7]!=0x0b || p[8]==0 || (p[9]&0x7f)>100)return null;
                var checksum=p[3]^p[4]^p[5]^p[6]^p[7];
                for(var i=8;i<7+p[4];i++)checksum^=p[i];
                if(p[7+p[4]]!=(p[3]==1?checksum:0))return null;
                return Value("MCHOSE Turbo HID",p[9]&0x7f);
            case "HyperXCloud2": case "HyperXKingston":
                if(p.Length<8 || !(reader=="HyperXCloud2"?Header(6,0xff,0xbb,2):Header(0x0b,0,0xbb,2)) || p[7]>100)return null;
                return Value("HyperX Cloud II HID",p[7]);
            case "HyperXCloud3":
                if(p.Length<5 || p[0]!=0x66 || p[1] is not (0x89 or 0x0d) || (p[2]==0 && p[3]==0) || p[4]>100)return null;
                return Value("HyperX Cloud III HID",p[4]);
            case "HyperXCloud3S":
                if(p.Length<7 || !Header(0x0c) || p[5]!=6 || p[6]>100)return null;
                return Value("HyperX Cloud III S HID",p[6]);
            case "HyperXAlpha":
                if(p.Length<4 || !Header(0x21,0xbb,0x0b) || p[3]>100)return null;
                return Value("HyperX Cloud Alpha HID",p[3]);
            case "HyperXAlpha2": case "HyperXHaste":
                if(p.Length<(reader=="HyperXHaste"?10:7) || !Header(0x51,2) || p[2]>100)return null;
                return Value("HyperX NGENUITY HID",p[2],reader=="HyperXAlpha2"?(p[6]&0x80)!=0:Charge(p[3]));
            case "HyperXFlight":
                if(p.Length is not (15 or 20))return null;
                var mv=(p[3]<<8)|p[4];if(mv is <2500 or >5000)return null;
                if(mv>0x100b)return Value("HyperX Cloud Flight HID",null,true,"充电中；电压不能换算实时百分比");
                var percent=mv<=3648?0.00125*mv:mv>3975?100:0.00000002547505*Math.Pow(mv,4)-0.0003900299*Math.Pow(mv,3)+2.238321*mv*mv-5706.256*mv+5452299;
                return Value("HyperX Cloud Flight 电压估计",Math.Clamp((int)Math.Round(percent),0,100),false,"电压换算估计，非设备直报百分比");
            case "LogitechCenturion":
                if(p.Length<13 || !Header(0x51,0x0b) || p[8]!=4 || p[10]>100)return null;
                return Value("Logitech Centurion HID",p[10],p[12]==2);
            default:return null;
        }
    }
    internal static bool? Charge(byte state)=> state switch {0=>false,1 or 2=>true,_=>null};
    internal static bool? Charging(string reader,byte[] p)
    {
        if(reader=="HyperXCloud3" && p.Length>=3 && p[0]==0x66 && p[1] is 0x8a or 0x0c)return Charge(p[2]);
        if(reader=="HyperXCloud3S" && p.Length>=7 && p[0]==0x0c && p[5]==0x48)return Charge(p[6]);
        if(reader is "HyperXCloud2" or "HyperXKingston" && p.Length>=5 && p[2]==0xbb && p[3]==3 &&
            (reader=="HyperXCloud2"?p[0]==6 && p[1]==0xff:p[0]==0x0b && p[1]==0))return Charge(p[4]);
        if(reader=="HyperXAlpha" && p.Length>=4 && p.AsSpan().StartsWith(new byte[]{0x21,0xbb,0x0c}) && p[3]<=1)return p[3]==1;
        if(reader=="AtkBitmouse" && p.Length>=8 && p[0]==8 && p[1]==0x72 && p[2]!=0xff && p[5]==15 && p[6]>=1 && p[7]<=1)return p[7]==1;
        return null;
    }
}
