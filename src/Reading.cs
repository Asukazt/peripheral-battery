namespace PeriDock;

internal record Reading(int? Percent, bool? Charging, int? DpiX, int? DpiY, string Source, string Status, bool Cached = false, string Connection = "待确认", bool? Connected = null, string? ChargingNote = null, ReadFailure Failure = ReadFailure.None, int? ErrorCode = null);
