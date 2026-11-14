namespace PeriDock;

internal enum ReadFailure { None, AccessDenied, ProtocolUnsupported, ReadError }

internal record Reading(int? Percent, bool? Charging, int? DpiX, int? DpiY, string Source, string Status,
    bool Cached = false, string Connection = "待确认", bool? Connected = null, string? ChargingNote = null,
    ReadFailure Failure = ReadFailure.None, int? ErrorCode = null);

internal sealed class BatteryState(string id, string name, string shortName, string? kind = null)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string ShortName { get; } = shortName;
    public string Kind { get; } = kind ?? id;
    public string Connection { get; private set; } = "识别中";
    public bool? Connected { get; private set; }
    public string? ChargingNote { get; private set; }
    public int? Percent { get; private set; }
    public bool? Charging { get; private set; }
    public DateTimeOffset? LastSuccess { get; private set; }
    public string Source { get; private set; } = "";
    public string Status { get; private set; } = "等待首次读取";
    public bool Cached { get; private set; }
    public int? DpiX { get; private set; }
    public int? DpiY { get; private set; }
    public bool LastReadSucceeded { get; private set; }
    public ReadFailure Failure { get; private set; }
    public int? ErrorCode { get; private set; }
    public string PercentText => Percent.HasValue ? $"{Percent}%" : "--";
    public string DpiText => DpiX.HasValue && LastReadSucceeded && !IsStale(DateTimeOffset.UtcNow)
        ? DpiX == DpiY ? $"{DpiX} DPI" : $"{DpiX} × {DpiY} DPI" : "未知";
    public bool IsStale(DateTimeOffset now) => Cached || !LastReadSucceeded || !LastSuccess.HasValue ||
        now - LastSuccess.Value > TimeSpan.FromSeconds(90);
    public void Apply(Reading value, DateTimeOffset now)
    {
        var valid = value.Failure == ReadFailure.None && value.Percent is >= 0 and <= 100;
        LastReadSucceeded = valid && !value.Cached && value.Connected != false;
        Failure = value.Failure;
        ErrorCode = value.ErrorCode;
        Cached = value.Cached;
        Connected = value.Connected;
        if (value.Connection != "待确认" || value.Failure == ReadFailure.None || Connection is "识别中" or "未连接")
            Connection = value.Connection;
        ChargingNote = value.ChargingNote;
        Source = value.Source;
        Status = value.Status;
        Charging = value.Failure == ReadFailure.None && !value.Cached && (value.Connected == true || LastReadSucceeded) ? value.Charging : null;
        DpiX = LastReadSucceeded ? value.DpiX : null;
        DpiY = LastReadSucceeded ? value.DpiY : null;
        if (value.Failure == ReadFailure.AccessDenied) Percent = null;
        else if (valid) Percent = value.Percent;
        if (LastReadSucceeded) LastSuccess = now;
    }
}
