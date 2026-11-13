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

    internal static (int Percent, bool? Charging)? KeyboardPower(byte[] packet, byte report, int batteryOffset = 6)
        => batteryOffset is 6 or 11 && KeyboardMatches(packet, report, 0x12, 1) &&
           packet.Length >= Math.Max(11, batteryOffset + 1) && packet[batteryOffset] <= 100
            ? (packet[batteryOffset], packet[9] == 1 ? true : packet[9] == 0 ? false : null) : null;

    internal static (int Percent, bool? Charging)? AsusMousePower(byte[] packet, int scale)
    {
        if (packet.Length < 11 || packet[0] != 0 || packet[1] != 0x12 || packet[2] != 7 || scale is not (1 or 25)) return null;
        var raw = packet[5];
        if (raw > 100 / scale || raw == 0 && packet[10] == 0) return null;
        return (raw * scale, packet[10] > 0);
    }

    internal static string? KeyboardConnection(byte[] packet, byte report)
        => KeyboardMatches(packet, report, 0x12, 3) && packet.Length >= 6 ? packet[5] switch
        {
            0 => "USB", 1 => "2.4G", 2 => "蓝牙", _ => null
        } : null;

    internal static (int Percent, bool? Charging)? AsusHeadsetPower(byte[] packet, ushort product)
    {
        if (packet.Length < 9 || packet[0] != 0xcc || packet[1] != 0x12 || packet[2] != 7 || packet[6] > 100) return null;
        if (product != 0x1ad3) return (packet[6], null);
        return packet[7] <= 100 && packet[8] <= 100 ? (Math.Min(packet[6], packet[7]), null) : null;
    }

    internal static bool? AsusHeadsetCharging(byte[] packet, ushort product)
        => packet.Length >= 6 && packet[0] == 0xcc && packet[1] == 0x12 && packet[2] == 8
           ? product == 0x1ad3 ? (packet[5] & 3) != 0 : packet[5] switch { 0 => false, 1 => true, _ => null } : null;

    internal static (int? Percent, bool? Charging)? Razer2020Power(byte[] packet)
    {
        if (packet.Length != 64 || !packet.AsSpan(0, 9).SequenceEqual(Convert.FromHexString("FF0F05FE12041F0805")) ||
            (packet[12] << 8 | packet[13]) == 0) return null;
        bool? charging = packet[11] switch { 9 => true, 1 or 2 or 3 or 6 => false, _ => null };
        return (charging == true || packet[14] > 100 ? null : packet[14], charging);
    }

    internal static byte[] PaRequest(ushort product, byte command, bool? remote = null)
    {
        if (product is not (0x0555 or 0x0556 or 0x053a) ||
            (remote.HasValue ? command != 0xe1 : command is not (0x21 or 0x2a))) throw new ArgumentException("Unknown PA read query");
        var packet = new byte[64];
        packet[0] = product == 0x053a ? (byte)1 : (byte)2;
        packet[1] = 0x80; packet[2] = remote.HasValue ? (byte)7 : (byte)8;
        var marker = product == 0x053a ? 3 : 5;
        packet[marker] = 0x50; packet[marker + 1] = 0x41; packet[marker + 2] = remote.HasValue ? (byte)0x0e : (byte)8;
        if (product == 0x053a) packet[6] = 8;
        var type = product == 0x053a ? 7 : 9;
        packet[type] = remote.HasValue ? (byte)2 : (byte)3;
        packet[type + 1] = command;
        if (remote.HasValue) packet[type + 2] = remote.Value ? (byte)1 : (byte)0;
        return packet;
    }

    internal static int? PaValue(byte[] packet, ushort product, byte command)
    {
        if (product is not (0x0555 or 0x0556 or 0x053a) || packet.Length != 64) return null;
        var barracuda = product == 0x053a;
        var echo = barracuda ? 13 : 12;
        if (packet[0] != (barracuda ? 1 : 2) || packet[echo] != command ||
            (packet[echo + 1] != 1 && !(barracuda && packet[echo + 1] == 2)) ||
            packet[echo + 2] < 1 || packet[echo + 2] > 64 - echo - 3) return null;
        if (barracuda && (packet[3] != 0x50 || packet[4] != 0x49)) return null;
        var value = packet[echo + 3];
        return command == 0x21 && value <= 100 || command == 0x2a && value <= 1 ? value : null;
    }

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

    internal static byte[] MouseRequest(byte commandClass, byte command, byte size, byte transaction = 0x1f)
    {
        if (!((commandClass == 7 && command is 0x80 or 0x84 && size == 2) ||
              (commandClass == 4 && command == 0x85 && size == 7)))
            throw new ArgumentException("Only battery, charging and DPI read queries are allowed");
        if (transaction is not (0x1f or 0x3f or 0x9f or 0xff)) throw new ArgumentException("Unsupported transaction id");
        var packet = new byte[91];
        packet[2] = transaction; packet[6] = size; packet[7] = commandClass; packet[8] = command;
        for (var i = 3; i <= 88; i++) packet[89] ^= packet[i];
        return packet;
    }
    internal static bool MouseMatches(byte[] packet, byte commandClass, byte command, byte transaction = 0x1f)
    {
        if (packet.Length != 91 || packet[0] != 0 || packet[1] != 2 ||
            packet[2] != transaction || packet[7] != commandClass || packet[8] != command ||
            packet[6] > 80) return false;
        byte sum = 0;
        for (var i = 3; i <= 88; i++) sum ^= packet[i];
        return sum == packet[89];
    }
    internal static int? MouseValue(byte[] packet, byte commandClass, byte command, byte transaction = 0x1f)
        => MouseMatches(packet, commandClass, command, transaction) && packet[6] >= 2 ? packet[10] : null;
    internal static (int X, int Y)? MouseDpi(byte[] packet, byte transaction = 0x1f)
    {
        if (!MouseMatches(packet, 4, 0x85, transaction) || packet[6] < 5) return null;
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
