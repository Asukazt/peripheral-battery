namespace PeriDock;

// G7 can repeat a startup 80% and isolated low spikes; the upstream calibration requires spaced samples.
internal sealed class MchoseG7Filter
{
    readonly List<int> samples=[];
    DateTimeOffset? seen,sampled,eightyAt;
    (int Level,DateTimeOffset At)? low;
    int eighties;
    int? Centre()=>samples.Count==0?null:(int)Math.Round(samples.Order().Skip((samples.Count-1)/2).Take(samples.Count%2==0?2:1).Average());
    internal Reading Apply(Reading value,DateTimeOffset now)
    {
        if(value.Percent is not int level)return value;
        if(value.Charging==true || seen is null || now<seen || now-seen>=TimeSpan.FromMinutes(10))
        {samples.Clear();sampled=null;eighties=0;eightyAt=null;low=null;}
        seen=now;
        if(value.Charging==true){samples.Add(level);sampled=now;}
        else if(level==80 && samples.Count==0)
        {
            if(eightyAt is null || now-eightyAt>=TimeSpan.FromSeconds(5))
            {eightyAt=now;if(++eighties>=5){samples.Add(80);sampled=now;}}
        }
        else
        {
            eighties=0;eightyAt=null;var centre=Centre();
            if(level==80 && centre.HasValue && Math.Abs(centre.Value-80)>=10)low=null;
            else if(sampled is null || now-sampled>=TimeSpan.FromSeconds(5))
            {
                if(centre-level>20)
                {
                    if(low is { } pending && Math.Abs(pending.Level-level)<=5 && now-pending.At>=TimeSpan.FromSeconds(5))
                    {samples.Clear();samples.Add(pending.Level);low=null;}
                    else low=(level,now);
                }
                else low=null;
                samples.Add(level);if(samples.Count>5)samples.RemoveAt(0);sampled=now;
            }
            else if(low is { } pending && (centre is null || centre-level<=20 || Math.Abs(pending.Level-level)>5))low=null;
        }
        var filtered=Centre();
        var uncertain=eighties>=5 || (filtered.HasValue && filtered!=level);
        return value with {Percent=filtered,Cached=uncertain,
            Source="MCHOSE G7 HID · 中位数估计",Status=filtered is null?"G7 启动 80% 待确认":
                eighties>=5?"多次重复 80%；可能是设备缓存，非已确认实时电量":"G7 电量经过抖动过滤（估计）"};
    }
}
