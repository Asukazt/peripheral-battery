#nullable disable
using System;
using System.Linq;

namespace PeriDock;

internal static class MultiBrandProtocols
{
	internal static byte[] Request(string reader, byte command = 0, ushort sequence = 1, byte report = 0)
	{
		switch (reader)
		{
		case "Compx":
			return Convert.FromHexString("0804000000000000000000000000000049");
		case "AtkNearlink":
		case "AtkKeyboard":
			return Frame((reader == "AtkKeyboard") ? 32 : 64, new byte[4]
			{
				report,
				(byte)sequence,
				(byte)(sequence >> 8),
				26
			});
		case "AtkBitmouse":
			return Bitmouse((byte)((command == 0) ? 7 : command));
		case "AulaKeyboard":
			return Frame(33, new byte[3] { 0, 170, 16 });
		case "AulaMouse":
			return Frame(32, new byte[4]
			{
				4,
				0,
				0,
				(byte)((command == 0) ? 26 : command)
			});
		case "MchoseMouse":
			return Inverted((byte)((report == 0) ? 17 : report));
		case "MchoseG7":
			return Frame(65, new byte[9] { 0, 85, 48, 165, 11, 46, 1, 1, 1 });
		case "MchoseA5":
			return Frame(65, new byte[7] { 0, 0, 0, 2, 2, 0, 131 });
		case "MchoseKeyboard":
			return Frame(20, new byte[6] { 19, 74, 1, 0, 0, 2 });
		case "MchoseV9":
			return Frame(64, new byte[3] { 85, 101, 1 });
		case "MchoseTurbo":
			return (command == 1) ? Frame(64, new byte[3] { 85, 101, 1 }) : Frame(64, new byte[9] { 85, 170, 1, 0, 1, 0, 1, 11, 0 });
		case "HyperXCloud2":
		{
			byte[] obj2 = new byte[4] { 6, 255, 187, 0 };
			obj2[3] = (byte)((command == 0) ? 2 : command);
			return Frame(52, obj2);
		}
		case "HyperXKingston":
		{
			byte[] obj = new byte[16]
			{
				6, 0, 2, 0, 154, 0, 0, 104, 74, 142,
				10, 0, 0, 0, 187, 0
			};
			obj[15] = (byte)((command == 0) ? 2 : command);
			return Frame(62, obj);
		}
		case "HyperXCloud3":
			return Frame(62, new byte[2]
			{
				102,
				(byte)((command == 0) ? 137 : command)
			});
		case "HyperXCloud3S":
		{
			byte[] obj3 = new byte[6] { 12, 2, 3, 1, 0, 0 };
			obj3[5] = (byte)((command == 0) ? 6 : command);
			return Frame(64, obj3);
		}
		case "HyperXAlpha":
			return Frame(31, new byte[3]
			{
				33,
				187,
				(byte)((command == 0) ? 11 : command)
			});
		case "HyperXHaste":
		case "HyperXAlpha2":
			return Frame(64, new byte[2]
			{
				(byte)((command == 0) ? 80 : command),
				(byte)((command == 82) ? 1 : 2)
			});
		case "HyperXFlight":
			return Frame(20, new byte[3] { 33, 255, 5 });
		case "LogitechCenturion":
			return Frame(64, new byte[10] { 81, 8, 0, 3, 26, 0, 3, 0, 4, 10 });
		default:
			throw new ArgumentException("Unsupported battery request");
		}
		static byte[] Frame(int length, byte[] prefix)
		{
			byte[] array = new byte[length];
			prefix.CopyTo(array, 0);
			return array;
		}
	}

	private static byte[] Inverted(byte report)
	{
		byte[] array = Enumerable.Repeat(byte.MaxValue, (report == 17) ? 21 : 65).ToArray();
		array[0] = report;
		array[1] = 249;
		return array;
	}

	private static byte[] Bitmouse(byte command)
	{
		byte[] array = new byte[64];
		array[0] = 8;
		array[2] = 114;
		array[3] = 2;
		array[5] = 1;
		array[6] = command;
		array[7] = 1;
		array[1] = (byte)array.Skip(2).Sum((byte b) => b);
		return array;
	}

	private static Reading Value(string source, int? percent, bool? charging = null, string status = "已读取")
	{
		return new Reading(percent, charging, null, null, source, status, Cached: false, "待确认", true, (!charging.HasValue) ? "此协议未提供可确认的充电状态" : null);
	}

	internal static Reading? Power(string reader, byte[] p, ushort sequence = 1, byte report = 0)
	{
		byte[] p2 = p;
		switch (reader)
		{
		case "Compx":
			if (p2.Length < 17 || !Header(new byte[2] { 8, 4 }) || p2.Take(17).Sum((byte b) => b) % 256 != 85 || p2[6] > 100)
			{
				return null;
			}
			return Value("CompX HID", p2[6], p2[7] != 0);
		case "AtkNearlink":
		case "AtkKeyboard":
			if (p2.Length < ((reader == "AtkKeyboard") ? 10 : 64) || !Header(new byte[4]
			{
				report,
				(byte)sequence,
				(byte)(sequence >> 8),
				26
			}) || p2[8] > 100)
			{
				return null;
			}
			if (reader == "AtkKeyboard" && (p2[5] != 0 || p2[6] != 0 || p2[9] > 2))
			{
				return null;
			}
			return Value("ATK Evision HID", p2[8], p2[9] != 0);
		case "AtkBitmouse":
		{
			bool flag = p2.Length < 8 || !Header(new byte[2] { 8, 114 }) || p2[2] == byte.MaxValue || p2[5] != 7 || p2[6] < 1;
			if (!flag)
			{
				byte b2 = p2[7];
				bool flag2 = ((b2 > 100 || b2 == 0) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				return null;
			}
			return Value("ATK BITMOUSE HID", p2[7]);
		}
		case "AulaKeyboard":
		{
			int num4 = (Header(new byte[3] { 0, 85, 16 }) ? 1 : ((!Header(new byte[2] { 85, 16 })) ? (-1) : 0));
			bool flag = num4 < 0 || p2.Length <= num4 + 25;
			if (!flag)
			{
				byte b2 = p2[num4 + 25];
				bool flag2 = ((b2 > 100 || b2 == 0) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				return null;
			}
			return Value("AULA 键盘 HID", p2[num4 + 25]);
		}
		case "AulaMouse":
		{
			bool flag = p2.Length < 10 || !Header(new byte[4] { 4, 0, 0, 26 }) || p2[5] != 0 || p2[6] != 0;
			byte b2;
			if (!flag)
			{
				b2 = p2[8];
				bool flag2 = ((b2 > 100 || b2 == 0) ? true : false);
				flag = flag2;
			}
			if (flag || p2[9] > 2)
			{
				return null;
			}
			int? percent = p2[8];
			b2 = p2[9];
			flag = (uint)(b2 - 1) <= 1u;
			return Value("AULA Evision HID", percent, flag, "接收器电量；无线在线状态待确认")with
			{
				Connected = null,
				Cached = true
			};
		}
		case "MchoseMouse":
		{
			bool flag = p2.Length < 13;
			if (!flag)
			{
				byte b2 = p2[0];
				bool flag2 = (uint)(b2 - 17) <= 1u;
				flag = !flag2;
			}
			if (flag || p2[1] != 249)
			{
				return null;
			}
			byte[] array = (from b in p2.Skip(2)
				select (byte)(b ^ 0xFFu)).ToArray();
			int num = array[0] | (array[1] << 8);
			flag = ((num == 14391 || num == 21075) ? true : false);
			if (!flag || array[9] > 100 || array[10] > 1)
			{
				return null;
			}
			return Value("MCHOSE Realtek HID", array[9], array[10] == 1);
		}
		case "MchoseG7":
			if (p2.Length < 10 || !Header(new byte[2] { 170, 48 }) || p2[8] > 100 || p2[9] > 1)
			{
				return null;
			}
			return Value("MCHOSE G7 HID", p2[8], p2[9] == 1, "设备原始读数（可能抖动）");
		case "MchoseA5":
		{
			int num4 = ((!Header(new byte[1] { 161 })) ? (Header(new byte[2] { 0, 161 }) ? 1 : (-1)) : 0);
			if (num4 < 0 || p2.Length < num4 + 8 || p2[num4 + 4] != 0 || p2[num4 + 5] != 131 || p2[num4 + 7] > 100)
			{
				return null;
			}
			return Value("MCHOSE A5 HID", p2[num4 + 7], p2[num4 + 6] != 0);
		}
		case "MchoseKeyboard":
			if (p2.Length < 20 || !Header(new byte[3] { 19, 74, 1 }) || p2[5] > 100 || (byte)p2.Take(19).Sum((byte b) => b) != p2[19])
			{
				return null;
			}
			return Value("MCHOSE K87 HID", p2[5]);
		case "MchoseV9":
			if (p2.Length < 4 || !Header(new byte[2] { 85, 101 }) || p2[2] > 100)
			{
				return null;
			}
			return Value("MCHOSE V9 HID", p2[2], (p2[3] == 0) ? new bool?(true) : ((p2[3] == 2) ? new bool?(false) : null));
		case "MchoseTurbo":
		{
			bool flag = Header(new byte[2] { 85, 101 }) && p2.Length >= 4 && p2[2] <= 100;
			if (flag)
			{
				byte b2 = p2[3];
				bool flag2 = (uint)(b2 - 2) <= 2u;
				flag = flag2;
			}
			if (flag)
			{
				return Value("MCHOSE Turbo HID", p2[2], (p2[3] == 2) ? new bool?(false) : null);
			}
			if (p2.Length < 11 || !Header(new byte[3] { 85, 170, 1 }) || p2[3] > 1 || p2[4] < 3 || p2.Length < 8 + p2[4] || p2[5] != 0 || p2[6] != 1 || p2[7] != 11 || p2[8] == 0 || (p2[9] & 0x7F) > 100)
			{
				return null;
			}
			int num5 = p2[3] ^ p2[4] ^ p2[5] ^ p2[6] ^ p2[7];
			for (int i = 8; i < 7 + p2[4]; i++)
			{
				num5 ^= p2[i];
			}
			if (p2[7 + p2[4]] != ((p2[3] == 1) ? num5 : 0))
			{
				return null;
			}
			return Value("MCHOSE Turbo HID", p2[9] & 0x7F);
		}
		case "HyperXCloud2":
		case "HyperXKingston":
			if (p2.Length < 8 || !((reader == "HyperXCloud2") ? Header(new byte[4] { 6, 255, 187, 2 }) : Header(new byte[4] { 11, 0, 187, 2 })) || p2[7] > 100)
			{
				return null;
			}
			return Value("HyperX Cloud II HID", p2[7]);
		case "HyperXCloud3":
		{
			bool flag = p2.Length < 5 || p2[0] != 102;
			if (!flag)
			{
				byte b2 = p2[1];
				bool flag2 = ((b2 == 13 || b2 == 137) ? true : false);
				flag = !flag2;
			}
			if (flag || (p2[2] == 0 && p2[3] == 0) || p2[4] > 100)
			{
				return null;
			}
			return Value("HyperX Cloud III HID", p2[4]);
		}
		case "HyperXCloud3S":
			if (p2.Length < 7 || !Header(new byte[1] { 12 }) || p2[5] != 6 || p2[6] > 100)
			{
				return null;
			}
			return Value("HyperX Cloud III S HID", p2[6]);
		case "HyperXAlpha":
			if (p2.Length < 4 || !Header(new byte[3] { 33, 187, 11 }) || p2[3] > 100)
			{
				return null;
			}
			return Value("HyperX Cloud Alpha HID", p2[3]);
		case "HyperXHaste":
		case "HyperXAlpha2":
			if (p2.Length < ((reader == "HyperXHaste") ? 10 : 7) || !Header(new byte[2] { 81, 2 }) || p2[2] > 100)
			{
				return null;
			}
			return Value("HyperX NGENUITY HID", p2[2], (reader == "HyperXAlpha2") ? new bool?((p2[6] & 0x80) != 0) : Charge(p2[3]));
		case "HyperXFlight":
		{
			int num2 = p2.Length;
			if ((num2 != 15 && num2 != 20) || 1 == 0)
			{
				return null;
			}
			int num3 = (p2[3] << 8) | p2[4];
			if ((num3 < 2500 || num3 > 5000) ? true : false)
			{
				return null;
			}
			if (num3 > 4107)
			{
				return Value("HyperX Cloud Flight HID", null, true, "充电中；电压不能换算实时百分比");
			}
			double a = ((num3 <= 3648) ? (1.0 / 800.0 * (double)num3) : ((num3 > 3975) ? 100.0 : (2.547505E-08 * Math.Pow(num3, 4.0) - 0.0003900299 * Math.Pow(num3, 3.0) + 2.238321 * (double)num3 * (double)num3 - 5706.256 * (double)num3 + 5452299.0)));
			return Value("HyperX Cloud Flight 电压估计", Math.Clamp((int)Math.Round(a), 0, 100), false, "电压换算估计，非设备直报百分比");
		}
		case "LogitechCenturion":
			if (p2.Length < 13 || !Header(new byte[2] { 81, 11 }) || p2[8] != 4 || p2[10] > 100)
			{
				return null;
			}
			return Value("Logitech Centurion HID", p2[10], p2[12] == 2);
		default:
			return null;
		}
		bool Header(byte[] prefix)
		{
			return p2.AsSpan().StartsWith(prefix);
		}
	}

	internal static bool? Charge(byte state)
	{
		switch (state)
		{
		case 0:
			return false;
		case 1:
		case 2:
			return true;
		default:
			return null;
		}
	}

	internal static bool? Charging(string reader, byte[] p)
	{
		bool flag = reader == "HyperXCloud3" && p.Length >= 3 && p[0] == 102;
		if (flag)
		{
			byte b = p[1];
			bool flag2 = ((b == 12 || b == 138) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return Charge(p[2]);
		}
		if (reader == "HyperXCloud3S" && p.Length >= 7 && p[0] == 12 && p[5] == 72)
		{
			return Charge(p[6]);
		}
		flag = ((reader == "HyperXCloud2" || reader == "HyperXKingston") ? true : false);
		bool num;
		if (flag && p.Length >= 5 && p[2] == 187 && p[3] == 3)
		{
			if (!(reader == "HyperXCloud2"))
			{
				if (p[0] == 11)
				{
					num = p[1] == 0;
					goto IL_00db;
				}
			}
			else if (p[0] == 6)
			{
				num = p[1] == byte.MaxValue;
				goto IL_00db;
			}
		}
		goto IL_00e6;
		IL_00db:
		if (num)
		{
			return Charge(p[4]);
		}
		goto IL_00e6;
		IL_00e6:
		if (reader == "HyperXAlpha" && p.Length >= 4 && p.AsSpan().StartsWith(new byte[3] { 33, 187, 12 }) && p[3] <= 1)
		{
			return p[3] == 1;
		}
		if (reader == "AtkBitmouse" && p.Length >= 8 && p[0] == 8 && p[1] == 114 && p[2] != byte.MaxValue && p[5] == 15 && p[6] >= 1 && p[7] <= 1)
		{
			return p[7] == 1;
		}
		return null;
	}
}
