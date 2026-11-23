namespace PeriDock;

internal sealed record DeviceProfile(ushort Vendor, ushort Product, string Name, string Kind, string Reader, string Connection = "待确认", byte Transaction = 31, int Scale = 1, int BatteryOffset = 6, string? MatchName = null);
