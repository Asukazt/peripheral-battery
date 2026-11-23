#nullable disable
using System;
using System.Text.Json.Serialization;

namespace PeriDock;

internal sealed class BatteryState
{
	private MchoseG7Filter? g7Filter;

	public string Id { get; }

	public string Name { get; }

	public string ShortName { get; }

	public string Kind { get; }

	public bool Supported { get; init; } = true;

	[JsonIgnore]
	internal string? ProductImagePath { get; set; }

	[JsonIgnore]
	internal bool GenericImage { get; set; }

	public string Connection { get; private set; }

	public bool? Connected { get; private set; }

	public string? ChargingNote { get; private set; }

	public int? Percent { get; private set; }

	public bool? Charging { get; private set; }

	public DateTimeOffset? LastSuccess { get; private set; }

	public string Source { get; private set; }

	public string Status { get; private set; }

	public bool Cached { get; private set; }

	public int? DpiX { get; private set; }

	public int? DpiY { get; private set; }

	public int LowThreshold { get; set; } = 20;

	public bool IsLow => Percent.HasValue && Percent <= LowThreshold;

	public bool LastReadSucceeded { get; private set; }

	public ReadFailure Failure { get; private set; }

	public int? ErrorCode { get; private set; }

	public string PercentText
	{
		get
		{
			if (!Percent.HasValue)
			{
				return "--";
			}
			return $"{Percent}%";
		}
	}

	internal string TrayText => Kind switch
	{
		"keyboard" => "键盘", 
		"mouse" => "鼠标", 
		"headset" => "耳机", 
		_ => "设备", 
	} + "：" + PercentText;

	public string DpiText
	{
		get
		{
			if (!DpiX.HasValue || !LastReadSucceeded || IsStale(DateTimeOffset.UtcNow))
			{
				return "未知";
			}
			if (DpiX == DpiY)
			{
				return $"{DpiX} DPI";
			}
			return $"{DpiX} × {DpiY} DPI";
		}
	}

	public BatteryState(string id, string name, string shortName, string? kind = null)
	{
		Id = id;
		Name = name;
		ShortName = shortName;
		Kind = kind ?? id;
		Connection = "识别中";
		Source = "";
		Status = "等待首次读取";
	}

	public bool IsStale(DateTimeOffset now)
	{
		if (!Cached && LastReadSucceeded && LastSuccess.HasValue)
		{
			return now - LastSuccess.Value > TimeSpan.FromSeconds(90.0);
		}
		return true;
	}

	public void Apply(Reading value, DateTimeOffset now)
	{
		if (value.Failure != 0 || value.Connected == false)
		{
			g7Filter = null;
		}
		if (value.Source == "MCHOSE G7 HID")
		{
			value = (g7Filter ?? (g7Filter = new MchoseG7Filter())).Apply(value, now);
		}
		int num;
		if (value.Failure == ReadFailure.None)
		{
			int? percent = value.Percent;
			if (percent.HasValue)
			{
				int valueOrDefault = percent.GetValueOrDefault();
				if (valueOrDefault >= 0)
				{
					num = ((valueOrDefault <= 100) ? 1 : 0);
					goto IL_0095;
				}
			}
			num = 0;
		}
		else
		{
			num = 0;
		}
		goto IL_0095;
		IL_0095:
		bool flag = (byte)num != 0;
		LastReadSucceeded = flag && !value.Cached && value.Connected != false;
		Failure = value.Failure;
		ErrorCode = value.ErrorCode;
		Cached = value.Cached;
		Connected = value.Connected;
		bool flag2 = value.Connection != "待确认" || value.Failure == ReadFailure.None;
		if (!flag2)
		{
			string connection = Connection;
			bool flag3 = ((connection == "识别中" || connection == "未连接") ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			Connection = value.Connection;
		}
		ChargingNote = value.ChargingNote;
		Source = value.Source;
		Status = value.Status;
		Charging = ((value.Failure == ReadFailure.None && !value.Cached && (value.Connected.GetValueOrDefault() || LastReadSucceeded)) ? value.Charging : null);
		DpiX = (LastReadSucceeded ? value.DpiX : null);
		DpiY = (LastReadSucceeded ? value.DpiY : null);
		if (value.Failure == ReadFailure.AccessDenied)
		{
			Percent = null;
		}
		else if (flag)
		{
			Percent = value.Percent;
		}
		if (LastReadSucceeded)
		{
			LastSuccess = now;
		}
	}

	public void ApplyDpi(int x, int y)
	{
		if (x is < 50 or > 35000 || y is < 50 or > 35000) return;
		DpiX = x;
		DpiY = y;
	}
}
