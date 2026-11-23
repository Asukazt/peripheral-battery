namespace PeriDock;

internal record HidDevice(string Path, ushort Product, ushort UsagePage, ushort Usage, ushort InputBytes, ushort OutputBytes, ushort FeatureBytes, ushort Vendor = 5426, string Name = "", string Container = "", int? AccessError = null);
