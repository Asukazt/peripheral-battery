namespace PeriDock;

internal static class Protocols
{
    internal static string RazerConnection(ushort product) => product switch
    {
        0x00c0 or 0x0576 => "USB",
        0x00c1 or 0x0577 => "2.4G",
        _ => "待确认"
    };

    internal static bool? HeadsetConnected(ushort product, int? wirelessState)
        // The 20:00 reply describes the wireless link even on the USB headset.
        => product switch
        {
            0x0576 => true,
            0x0577 => wirelessState switch { 0 => false, 1 => true, _ => null },
            _ => null
        };

    internal static byte[] KeyboardRequest(byte report, byte command, byte sub, int length)
    {
        if (!((report == 0 && length == 65 || report == 2 && length == 64) && command == 0x12 && sub is 1 or 3 ||
              report == 1 && length == 64 && command == 0xa0 && sub == 0))
            throw new ArgumentException("Only Azoth X power, connection and OMNI slot reads are allowed");
        var packet = new byte[length];
        packet[0] = report; packet[1] = command; packet[2] = sub;
        return packet;
    }

    internal static bool KeyboardMatches(byte[] packet, byte report, byte command, byte sub)
        => packet.Length >= 5 && packet[0] == report && packet[1] == command && packet[2] == sub &&
           (command == 0xa0 || packet[3] == 0 && packet[4] == 0);

    internal static (int Percent, bool? Charging)? KeyboardPower(byte[] packet, byte report)
        => KeyboardMatches(packet, report, 0x12, 1) && packet.Length >= 11 && packet[6] <= 100
            ? (packet[6], packet[9] == 1 ? true : packet[9] == 0 ? false : null) : null;

    internal static string? KeyboardConnection(byte[] packet, byte report)
        => KeyboardMatches(packet, report, 0x12, 3) && packet.Length >= 6 ? packet[5] switch
        {
            0 => "USB", 1 => "2.4G", 2 => "蓝牙", _ => null
        } : null;

    internal static bool OmniHasAzothX(byte[] packet)
    {
        if (!KeyboardMatches(packet, 1, 0xa0, 0)) return false;
        var count = packet[3] | packet[4] << 8;
        if (count > 14 || packet.Length < 5 + count * 4) return false;
        for (var i = 0; i < count; i++)
        {
            var offset = 5 + i * 4;
            if (packet[offset] == 0x25 && packet[offset + 1] == 0x1c &&
                packet[offset + 2] == 2 && packet[offset + 3] == 4) return true;
        }
        return false;
    }

    internal static bool? BluetoothCharging(ushort characteristic, byte[] value)
    {
        if (characteristic == 0x2a1a && value.Length == 1 && (value[0] & 3) == 3)
            return ((value[0] >> 4) & 3) switch { 2 => false, 3 => true, _ => null };
        if (characteristic != 0x2bed || value.Length < 3 || (value[0] & 0xf8) != 0) return null;
        var flags = value[0];
        var length = 3 + ((flags & 1) != 0 ? 2 : 0) + ((flags & 2) != 0 ? 1 : 0) + ((flags & 4) != 0 ? 1 : 0);
        if (value.Length != length || (value[1] & 1) == 0 || (value[2] & 0x80) != 0) return null;
        var levelOffset = 3 + ((flags & 1) != 0 ? 2 : 0);
        if ((flags & 2) != 0 && value[levelOffset] > 100) return null;
        return ((value[1] >> 5) & 3) switch { 1 => true, 2 or 3 => false, _ => null };
    }

    internal static byte[] MouseRequest(byte commandClass, byte command, byte size)
    {
        if (!((commandClass == 7 && command is 0x80 or 0x84 && size == 2) ||
              (commandClass == 4 && command == 0x85 && size == 7)))
            throw new ArgumentException("Only battery, charging and DPI read queries are allowed");
        var packet = new byte[91];
        packet[2] = 0x1f; packet[6] = size; packet[7] = commandClass; packet[8] = command;
        for (var i = 3; i <= 88; i++) packet[89] ^= packet[i];
        return packet;
    }
    internal static bool MouseMatches(byte[] packet, byte commandClass, byte command)
    {
        if (packet.Length != 91 || packet[0] != 0 || packet[1] != 2 ||
            packet[2] != 0x1f || packet[7] != commandClass || packet[8] != command ||
            packet[6] > 80) return false;
        byte sum = 0;
        for (var i = 3; i <= 88; i++) sum ^= packet[i];
        return sum == packet[89];
    }
    internal static int? MouseValue(byte[] packet, byte commandClass, byte command)
        => MouseMatches(packet, commandClass, command) && packet[6] >= 2 ? packet[10] : null;
    internal static (int X, int Y)? MouseDpi(byte[] packet)
    {
        if (!MouseMatches(packet, 4, 0x85) || packet[6] < 5) return null;
        var x = (packet[10] << 8) | packet[11];
        var y = (packet[12] << 8) | packet[13];
        return x is >= 50 and <= 35000 && y is >= 50 and <= 35000 ? (x, y) : null;
    }
    internal static byte[] HeadsetRequest(byte command, byte route)
    {
        if (!((command == 0x20 && route == 0) || (command is 0x21 or 0x2a && route == 0x80)))
            throw new ArgumentException("Only connection, battery and charging read queries are allowed");
        var packet = new byte[64];
        packet[0] = 2; packet[2] = 0x60; packet[6] = 4;
        packet[9] = route; packet[10] = command;
        for (var i = 0; i < 62; i++) packet[62] ^= packet[i];
        return packet;
    }
    internal static int? HeadsetValue(byte[] packet, byte command, byte route)
    {
        if (packet.Length != 64 || packet[0] != 2 || packet[1] != 2 || packet[2] != 0x60 ||
            packet[9] != route || packet[10] != command || packet[11] != 1 ||
            (packet[12] != 1 && !(command == 0x20 && packet[12] == 2))) return null;
        byte sum = 0;
        for (var i = 0; i < 62; i++) sum ^= packet[i];
        if (sum != packet[62]) return null;
        var value = packet[13];
        return command == 0x21 ? value <= 100 ? value : null : value <= 1 ? value : null;
    }
}
