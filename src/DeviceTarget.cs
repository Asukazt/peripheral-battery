using System;
using System.Text.Json.Serialization;

namespace PeriDock;

internal sealed record DeviceTarget(string Id, string Name, string Kind, bool Supported, string? BluetoothId = null,
    bool LowEnergy = false, string HardwareId = "", string Connection = "待确认", byte? LogitechSlot = null,
    string? ReceiverId = null, string Reader = "", byte Transaction = 31)
{
	[JsonIgnore]
	public HidDevice[] Interfaces { get; init; } = Array.Empty<HidDevice>();

	[JsonIgnore]
	public DeviceProfile? Profile { get; init; }

}
