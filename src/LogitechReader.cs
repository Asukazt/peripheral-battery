#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace PeriDock;

internal static class LogitechReader
{
	private sealed class Channel
	{
		[CompilerGenerated]
		private IReportSession _003Csession_003EP;

		[CompilerGenerated]
		private int _003CtimeoutMs_003EP;

		private int swid;

		internal byte Error { get; private set; }

		public Channel(IReportSession session, int timeoutMs)
		{
			_003Csession_003EP = session;
			_003CtimeoutMs_003EP = timeoutMs;
			swid = Random.Shared.Next(6);
		}

		internal async Task<byte[]?> Ask(byte slot, byte feature, byte function, params byte[] parameters)
		{
			byte[] array = new byte[20]
			{
				17,
				slot,
				feature,
				(byte)((function << 4) | (10 + swid++ % 6)),
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0,
				0
			};
			parameters.CopyTo(array, 4);
			return await Exchange(array);
		}

		internal async Task<byte[]?> Exchange(byte[] q)
		{
			byte[] q2 = q;
			Error = 0;
			byte[] array = await _003Csession_003EP.Query(q2, (byte[] p) => Matches(p, q2), feature: false, featureFallback: false, _003CtimeoutMs_003EP);
			if (array == null)
			{
				return null;
			}
			byte b = array[2];
			if ((b == 143 || b == byte.MaxValue) ? true : false)
			{
				Error = array[5];
				return null;
			}
			return array[4..];
		}

		internal async Task<byte> Feature(byte slot, ushort feature)
		{
			return (await Ask(slot, 0, 0, (byte)(feature >> 8), (byte)feature))?.FirstOrDefault() ?? 0;
		}
	}

	private static readonly (int Mv, int Level)[] Curve = new(int, int)[13]
	{
		(4186, 100),
		(4067, 90),
		(3989, 80),
		(3922, 70),
		(3859, 60),
		(3811, 50),
		(3778, 40),
		(3751, 30),
		(3717, 20),
		(3671, 10),
		(3646, 5),
		(3579, 2),
		(3500, 0)
	};

	internal static bool Accepts(HidDevice d)
	{
		bool flag = d.Vendor == 1133;
		if (flag)
		{
			bool flag2 = d.UsagePage == 65280;
			if (flag2)
			{
				ushort usage = d.Usage;
				bool flag3 = (uint)(usage - 1) <= 1u;
				flag2 = flag3;
			}
			flag = flag2 || (d.UsagePage == 65347 && d.Usage == 514) || (d.Product == 2756 && d.UsagePage == 12 && d.Usage == 1);
		}
		if (flag)
		{
			return d.InputBytes >= 7;
		}
		return false;
	}

	internal static string SlotId(DeviceTarget receiver, byte slot)
	{
		return receiver.Id + "-s" + slot;
	}

	internal static DeviceTarget Slot(DeviceTarget receiver, byte slot, string name, string kind)
	{
		return receiver with
		{
			Id = SlotId(receiver, slot),
			Name = name,
			Kind = kind,
			LogitechSlot = slot,
			ReceiverId = receiver.Id
		};
	}

	internal static bool Matches(byte[] p, byte[] q)
	{
		bool flag = q.Length < 4 || p.Length < 4;
		byte b;
		if (!flag)
		{
			b = p[0];
			bool flag2 = (uint)(b - 16) <= 1u;
			flag = !flag2;
		}
		if (flag || p[1] != q[1])
		{
			return false;
		}
		b = p[2];
		if ((b == 143 || b == byte.MaxValue) ? true : false)
		{
			if (p.Length >= 6 && p[3] == q[2])
			{
				return p[4] == q[3];
			}
			return false;
		}
		if (p[2] == q[2])
		{
			return p[3] == q[3];
		}
		return false;
	}

	private static int Estimate(int mv, (int Mv, int Level)[] curve)
	{
		for (int i = 1; i < curve.Length; i++)
		{
			if (mv >= curve[i].Mv)
			{
				return Math.Clamp((int)Math.Round((double)curve[i].Level + (double)(mv - curve[i].Mv) * (double)(curve[i - 1].Level - curve[i].Level) / (double)(curve[i - 1].Mv - curve[i].Mv)), 0, 100);
			}
		}
		return 0;
	}

	internal static Reading? Battery(ushort feature, byte[] p)
	{
		if (p.Length < 3)
		{
			return null;
		}
		bool flag = false;
		int? num;
		bool? charging;
		if ((feature == 4096 || feature == 4100) ? true : false)
		{
			if (p[0] > 100)
			{
				return null;
			}
			num = p[0];
			bool? flag2;
			switch (p[2])
			{
			case 0:
				flag2 = false;
				break;
			case 1:
			case 2:
			case 3:
			case 4:
				flag2 = true;
				break;
			default:
				flag2 = null;
				break;
			}
			charging = flag2;
			if (feature == 4100 && num == 0 && p[1] != 0)
			{
				num = p[1] switch
				{
					8 => 90, 
					4 => 50, 
					2 => 20, 
					1 => 5, 
					_ => null, 
				};
				flag = true;
			}
			if (num == 0 || !num.HasValue)
			{
				return null;
			}
		}
		else
		{
			if ((feature != 4097 && feature != 7968) || 1 == 0)
			{
				return null;
			}
			if (feature == 7968 && (p[2] & 1) == 0)
			{
				return null;
			}
			int num2 = (p[0] << 8) | p[1];
			if ((num2 < 2500 || num2 > 5000) ? true : false)
			{
				return null;
			}
			charging = ((feature == 4097) ? ((p[2] & 0x80) != 0) : ((p[2] & 2) != 0));
			num = ((!charging.GetValueOrDefault()) ? new int?(Estimate(num2, Curve)) : ((feature == 4097 && (p[2] & 3) == 1) ? new int?(100) : null));
			flag = num.HasValue && !charging.GetValueOrDefault();
		}
		return new Reading(num, charging, null, null, $"Logitech HID++ {feature:x4}", flag ? "电量为档位或电压估计" : ((!num.HasValue) ? "充电中；不使用电压伪造百分比" : "已读取"), Cached: false, "待确认", true, (!charging.HasValue) ? "充电状态未知" : null);
	}

	internal static async Task<List<DeviceTarget>> Expand(DeviceTarget receiver, IEnumerable<DeviceTarget> known, Func<HidDevice[], IReportSession>? open = null)
	{
		DeviceTarget receiver2 = receiver;
		Dictionary<string, DeviceTarget> result = (from t in known.Where(delegate(DeviceTarget t)
			{
				if (t.ReceiverId == receiver2.Id)
				{
					byte? logitechSlot = t.LogitechSlot;
					if (logitechSlot.HasValue)
					{
						byte valueOrDefault2 = logitechSlot.GetValueOrDefault();
						if (valueOrDefault2 >= 1)
						{
							return valueOrDefault2 <= 6;
						}
					}
					return false;
				}
				return false;
			})
			select Slot(receiver2, t.LogitechSlot.Value, t.Name, t.Kind)).ToDictionary((DeviceTarget t) => t.Id);
		HidDevice[] array = receiver2.Interfaces.Where(Accepts).ToArray();
		if (array.Length == 0)
		{
			return (result.Count > 0) ? result.Values.ToList() : new List<DeviceTarget>(1) { receiver2 };
		}
		try
		{
			using IReportSession session = open?.Invoke(array) ?? new HidReports(array);
			Channel ch = new Channel(session, 150);
			Stopwatch elapsed = Stopwatch.StartNew();
			for (byte slot = 1; slot <= 6 && elapsed.ElapsedMilliseconds < 7000; slot++)
			{
				byte[] array2 = await ch.Ask(slot, 0, 1, 0, 0, 90);
				if (array2 == null)
				{
					if (ch.Error == 8)
					{
						result.Remove(SlotId(receiver2, slot));
					}
					bool flag = ch.Error == 1;
					if (flag)
					{
						flag = (object)(await Legacy(ch, slot)) != null;
					}
					if (flag)
					{
						result[SlotId(receiver2, slot)] = Slot(receiver2, slot, $"Logitech HID++ 1.0 外设 · 槽位 {slot}", "device");
					}
					continue;
				}
				if (array2.Length < 3 || array2[2] != 90)
				{
					continue;
				}
				string name = $"Logitech 外设 · 槽位 {slot}";
				string kind = "device";
				byte fi = await ch.Feature(slot, 5);
				string text;
				bool flag2;
				if (fi != 0)
				{
					byte length = (await ch.Ask(slot, fi, 0))?.FirstOrDefault() ?? 0;
					List<byte> bytes = new List<byte>();
					while (bytes.Count < Math.Min((int)length, 64) && elapsed.ElapsedMilliseconds < 7000)
					{
						byte[] array3 = await ch.Ask(slot, fi, 1, (byte)bytes.Count);
						if (array3 == null || array3.Length == 0)
						{
							break;
						}
						bytes.AddRange(array3.Take(16));
					}
					if (length > 0 && bytes.Count >= length)
					{
						string @string = Encoding.UTF8.GetString(bytes.Take(length).ToArray());
						@string = new string(@string.Where((char c) => !char.IsControl(c)).ToArray()).Trim();
						if (@string.Length > 0)
						{
							name = (@string.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? @string : ("Logitech " + @string));
						}
					}
					byte? b = (await ch.Ask(slot, fi, 2))?.FirstOrDefault();
					bool flag;
					switch (b)
					{
					case 0:
					case 2:
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					if (flag)
					{
						text = "keyboard";
						goto IL_06f3;
					}
					if (b.HasValue)
					{
						byte valueOrDefault = b.GetValueOrDefault();
						if ((uint)(valueOrDefault - 3) <= 2u)
						{
							flag2 = true;
							goto IL_06db;
						}
					}
					flag2 = false;
					goto IL_06db;
				}
				goto IL_0702;
				IL_0702:
				result[SlotId(receiver2, slot)] = Slot(receiver2, slot, name, kind);
				continue;
				IL_06db:
				text = (flag2 ? "mouse" : DeviceCatalog.Kind(name));
				goto IL_06f3;
				IL_06f3:
				kind = text;
				goto IL_0702;
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is Win32Exception || ex is UnauthorizedAccessException || ex is OperationCanceledException) ? 1 : 0) != 0)
		{
			new Diagnostics(AppContext.BaseDirectory).HidFailure(receiver2.Interfaces[0], "slots", ex);
		}
		return (result.Count > 0) ? result.Values.ToList() : new List<DeviceTarget>(1) { receiver2 };
	}

	private static async Task<Reading?> Legacy(Channel ch, byte slot)
	{
		byte[] array = new byte[2] { 13, 7 };
		foreach (byte reg in array)
		{
			byte[] array2 = await ch.Exchange(new byte[7] { 16, slot, 129, reg, 0, 0, 0 });
			if (array2 == null || array2.Length < 3)
			{
				continue;
			}
			int? num = ((reg != 13) ? (array2[0] switch
			{
				7 => 90, 
				5 => 50, 
				3 => 20, 
				1 => 5, 
				_ => null, 
			}) : ((array2[0] <= 100) ? new int?(array2[0]) : null));
			int? percent = num;
			if (percent.HasValue)
			{
				bool flag2;
				if (reg == 13)
				{
					int num2 = array2[2] & 0xF0;
					bool flag = ((num2 == 80 || num2 == 144) ? true : false);
					flag2 = flag;
				}
				else
				{
					flag2 = (array2[1] & 0x21) == 33 || (array2[1] & 0x22) == 34;
				}
				bool value = flag2;
				return new Reading(percent, value, null, null, "Logitech HID++ 1.0", (reg == 7) ? "档位估计" : "已读取", Cached: false, "待确认", true);
			}
		}
		return null;
	}

	internal static async Task<Reading> Read(HidDevice[] devices, DeviceProfile profile, byte slot = byte.MaxValue, Func<HidDevice[], IReportSession>? open = null)
	{
		HidDevice[] selected = devices.Where(Accepts).ToArray();
		if (selected.Length == 0)
		{
			return new Reading(null, null, null, null, "Logitech HID++", "已识别；未找到 HID++ 电量 collection", Cached: false, "待确认", null, null, ReadFailure.ProtocolUnsupported);
		}
		if (profile.Reader == "LogitechReceiver" && slot == byte.MaxValue)
		{
			return new Reading(null, null, null, null, "Logitech HID++", "接收器已识别；未发现可响应的配对槽位", Cached: false, "待确认", null, null, ReadFailure.ReadError);
		}
		using IReportSession session = open?.Invoke(selected) ?? new HidReports(selected);
		Channel ch = new Channel(session, 450);
		if (profile.Product == 2756)
		{
			byte[] obj = new byte[20]
			{
				17, 0, 5, 13, 0, 0, 0, 0, 0, 0,
				0, 0, 0, 0, 0, 0, 0, 0, 0, 0
			};
			obj[1] = slot;
			byte[] array = await ch.Exchange(obj);
			if (array != null && array.Length >= 3)
			{
				int num = (array[0] << 8) | array[1];
				if (num >= 2500 && num <= 5000)
				{
					return new Reading((array[2] == 3) ? null : new int?(Estimate(num, new(int, int)[6]
					{
						(4175, 100),
						(3817, 50),
						(3766, 30),
						(3730, 20),
						(3664, 5),
						(3310, 0)
					})), array[2] == 3, null, null, "Logitech G535 HID++", "电压估计；充电时不换算百分比", Cached: false, "待确认", true);
				}
			}
		}
		ushort[] array2 = new ushort[4] { 4100, 4096, 4097, 7968 };
		foreach (ushort feature in array2)
		{
			byte b = await ch.Feature(slot, feature);
			if (b == 0)
			{
				continue;
			}
			byte[] array3 = await ch.Ask(slot, b, (feature == 4100) ? ((byte)1) : ((byte)0));
			if (array3 != null)
			{
				if (feature == 7968 && array3.Length >= 3 && (array3[2] & 1) == 0)
				{
					return new Reading(null, null, null, null, "Logitech HID++", "接收器已连接；耳机未连接", Cached: false, "待确认", false);
				}
				Reading reading = Battery(feature, array3);
				if ((object)reading != null)
				{
					return reading;
				}
			}
		}
		Reading reading2 = ((!selected.Any((HidDevice d) => d.UsagePage == 65280 && d.Usage == 1 && d.OutputBytes >= 7)) ? null : (await Legacy(ch, slot)));
		return reading2 ?? new Reading(null, null, null, null, "Logitech HID++", "已识别配对设备；休眠、未响应或电量功能未适配", Cached: false, "待确认", null, null, ReadFailure.ReadError);
	}
}
