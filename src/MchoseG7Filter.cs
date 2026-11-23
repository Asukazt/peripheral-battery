#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeriDock;

internal sealed class MchoseG7Filter
{
	private readonly List<int> samples = new List<int>();

	private DateTimeOffset? seen;

	private DateTimeOffset? sampled;

	private DateTimeOffset? eightyAt;

	private (int Level, DateTimeOffset At)? low;

	private int eighties;

	private int? Centre()
	{
		if (samples.Count != 0)
		{
			return (int)Math.Round(samples.Order().Skip((samples.Count - 1) / 2).Take((samples.Count % 2 != 0) ? 1 : 2)
				.Average());
		}
		return null;
	}

	internal Reading Apply(Reading value, DateTimeOffset now)
	{
		int? percent = value.Percent;
		int valueOrDefault;
		if (percent.HasValue)
		{
			valueOrDefault = percent.GetValueOrDefault();
			if (!value.Charging.GetValueOrDefault())
			{
				DateTimeOffset? dateTimeOffset = seen;
				if (dateTimeOffset.HasValue)
				{
					DateTimeOffset value2 = now;
					dateTimeOffset = seen;
					if (!(value2 < dateTimeOffset))
					{
						value2 = now;
						dateTimeOffset = seen;
						if (!(value2 - dateTimeOffset >= TimeSpan.FromMinutes(10.0)))
						{
							goto IL_0102;
						}
					}
				}
			}
			samples.Clear();
			sampled = null;
			eighties = 0;
			eightyAt = null;
			low = null;
			goto IL_0102;
		}
		return value;
		IL_03af:
		samples.Add(valueOrDefault);
		if (samples.Count > 5)
		{
			samples.RemoveAt(0);
		}
		sampled = now;
		goto IL_046e;
		IL_0102:
		seen = now;
		if (value.Charging.GetValueOrDefault())
		{
			samples.Add(valueOrDefault);
			sampled = now;
		}
		else if (valueOrDefault == 80 && samples.Count == 0)
		{
			DateTimeOffset? dateTimeOffset = eightyAt;
			if (dateTimeOffset.HasValue)
			{
				DateTimeOffset value2 = now;
				dateTimeOffset = eightyAt;
				if (!(value2 - dateTimeOffset >= TimeSpan.FromSeconds(5.0)))
				{
					goto IL_046e;
				}
			}
			eightyAt = now;
			if (++eighties >= 5)
			{
				samples.Add(80);
				sampled = now;
			}
		}
		else
		{
			eighties = 0;
			eightyAt = null;
			int? num = Centre();
			if (valueOrDefault != 80 || !num.HasValue || Math.Abs(num.Value - 80) < 10)
			{
				DateTimeOffset? dateTimeOffset = sampled;
				if (dateTimeOffset.HasValue)
				{
					DateTimeOffset value2 = now;
					dateTimeOffset = sampled;
					if (!(value2 - dateTimeOffset >= TimeSpan.FromSeconds(5.0)))
					{
						(int, DateTimeOffset)? tuple = low;
						if (tuple.HasValue)
						{
							(int, DateTimeOffset) valueOrDefault2 = tuple.GetValueOrDefault();
							if (!num.HasValue || num - valueOrDefault <= 20 || Math.Abs(valueOrDefault2.Item1 - valueOrDefault) > 5)
							{
								low = null;
							}
						}
						goto IL_046e;
					}
				}
				if (num - valueOrDefault > 20)
				{
					(int, DateTimeOffset)? tuple = low;
					if (tuple.HasValue)
					{
						(int, DateTimeOffset) valueOrDefault3 = tuple.GetValueOrDefault();
						if (Math.Abs(valueOrDefault3.Item1 - valueOrDefault) <= 5 && now - valueOrDefault3.Item2 >= TimeSpan.FromSeconds(5.0))
						{
							samples.Clear();
							samples.Add(valueOrDefault3.Item1);
							low = null;
							goto IL_03af;
						}
					}
					low = (valueOrDefault, now);
				}
				else
				{
					low = null;
				}
				goto IL_03af;
			}
			low = null;
		}
		goto IL_046e;
		IL_046e:
		int? num2 = Centre();
		bool cached = eighties >= 5 || (num2.HasValue && num2 != valueOrDefault);
		return value with
		{
			Percent = num2,
			Cached = cached,
			Source = "MCHOSE G7 HID · 中位数估计",
			Status = ((!num2.HasValue) ? "G7 启动 80% 待确认" : ((eighties >= 5) ? "多次重复 80%；可能是设备缓存，非已确认实时电量" : "G7 电量经过抖动过滤（估计）"))
		};
	}
}
