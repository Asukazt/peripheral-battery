#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PeriDock;

internal static class DeviceCatalog
{
	internal static IReadOnlyList<DeviceProfile> Profiles { get; } = Create();


	internal static IReadOnlySet<ushort> Vendors { get; } = new HashSet<ushort>
	{
		5426, 2821, 1133, 1008, 2385, 21075, 14391, 43173, 8227, 10525,
		16868, 14139, 13652, 12815, 3141, 14502
	};


	internal static DeviceProfile? Find(ushort vendor, ushort product)
	{
		return Profiles.FirstOrDefault((DeviceProfile p) => p.Vendor == vendor && p.Product == product);
	}

	internal static string Key(string value)
	{
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()))).Substring(0, 24).ToLowerInvariant();
	}

	internal static int? RazerInterface(ushort product)
	{
		switch (product)
		{
		case 192:
			return 0;
		case 602:
		case 604:
		case 656:
		case 662:
		case 725:
			return 2;
		case 600:
		case 625:
		case 658:
		case 664:
		case 697:
		case 698:
		case 727:
			return 3;
		default:
			return null;
		}
	}

	internal static List<DeviceTarget> FromHid(IEnumerable<HidDevice> devices)
	{
		return (from t in (from d in devices.Where((HidDevice d) => Vendors.Contains(d.Vendor)).ToArray()
				group d by $"{d.Vendor:x4}:{d.Product:x4}:{((d.Container.Length > 0) ? d.Container : d.Path)}" into @group
				where @group.Any(delegate(HidDevice d)
				{
					ushort vendor = d.Vendor;
					bool flag = ((vendor == 1133 || vendor == 5426) ? true : false);
					return flag || (object)ProfileFor(d) != null || IsBranded(d.Name);
				})
				select @group).Select(delegate(IGrouping<string, HidDevice> @group)
			{
				HidDevice hidDevice = @group.First();
				DeviceProfile deviceProfile = @group.Select(ProfileFor).FirstOrDefault((DeviceProfile p) => (object)p != null);
				string name = deviceProfile?.Name ?? ((hidDevice.Name.Length > 0) ? hidDevice.Name : $"外设 {hidDevice.Vendor:x4}:{hidDevice.Product:x4}");
				return new DeviceTarget("hid-" + Key(@group.Key), name, deviceProfile?.Kind ?? Kind(name),
					(object)deviceProfile != null && deviceProfile.Reader != "Unsupported", null, LowEnergy: false,
					$"{hidDevice.Vendor:x4}:{hidDevice.Product:x4}", deviceProfile?.Connection ?? "待确认",
					Reader: deviceProfile?.Reader ?? "", Transaction: deviceProfile?.Transaction ?? 31)
				{
					Interfaces = @group.ToArray(),
					Profile = deviceProfile
				};
			})
			orderby (!(t.Kind == "keyboard")) ? ((t.Kind == "mouse") ? 1 : 2) : 0, t.Name
			select t).ToList();
	}

	internal static bool IsBranded(string name)
	{
		return Regex.IsMatch(name, "(?i)(?:^|[^a-z0-9])(?:razer|rog|logitech|logi|hyperx|mchose|atk|aula|vgn|vxe)(?:$|[^a-z0-9])|罗技|极度未知|迈从|狼蛛");
	}

	internal static string ModelKey(string name)
	{
		return string.Concat(name.ToLowerInvariant().Where(char.IsLetterOrDigit)).Replace("razer", "").Replace("rog", "");
	}

	internal static string Kind(string name)
	{
		string name2 = name;
		object obj = Profiles.OrderByDescending((DeviceProfile p) => p.Name.Length).FirstOrDefault((DeviceProfile p) => ModelKey(name2).Contains(ModelKey(p.Name), StringComparison.Ordinal))?.Kind;
		if (obj == null)
		{
			if (!name2.Contains("keyboard", StringComparison.OrdinalIgnoreCase))
			{
				if (!name2.Contains("mouse", StringComparison.OrdinalIgnoreCase))
				{
					if (!name2.Contains("headset", StringComparison.OrdinalIgnoreCase) && !name2.Contains("headphone", StringComparison.OrdinalIgnoreCase))
					{
						return "device";
					}
					return "headset";
				}
				return "mouse";
			}
			obj = "keyboard";
		}
		return (string)obj;
	}

	private static IReadOnlyList<DeviceProfile> Create()
	{
		List<DeviceProfile> list = new List<DeviceProfile>(MultiBrandProfiles());
		RazerPair("Mamba (2012)", 36, 37, byte.MaxValue);
		RazerPair("Naga Epic Chroma", 62, 63, byte.MaxValue);
		RazerPair("Mamba", 68, 69, byte.MaxValue);
		RazerPair("Lancehead", 89, 90, 63);
		RazerPair("Lancehead Wireless", 112, 111, 31);
		RazerPair("Mamba Wireless", 115, 114, 63);
		RazerPair("Pro Click", 128, 119, 31);
		RazerPair("Viper Ultimate", 122, 123, byte.MaxValue);
		RazerPair("DeathAdder V2 Pro", 124, 125, 63);
		RazerPair("Basilisk Ultimate", 134, 136, 31);
		RazerPair("Naga Pro", 143, 144, 31);
		RazerPair("Viper Mini SE", 158, 159, 31);
		RazerPair("Viper V2 Pro", 165, 166, 31);
		RazerPair("Naga V2 Pro", 167, 168, 31);
		RazerPair("Basilisk V3 Pro", 170, 171, 31);
		RazerPair("Cobra Pro", 175, 176, 31);
		RazerPair("DeathAdder V3 Pro", 182, 183, 31);
		RazerPair("DeathAdder V4 Pro", 190, 191, 31);
		RazerPair("Viper V3 Pro", 192, 193, 31);
		RazerPair("DeathAdder V3 Pro", 194, 195, 31);
		RazerPair("DeathAdder V3 HyperSpeed", 196, 197, 31);
		RazerPair("Pro Click V2 Vertical Edition", 199, 200, 31);
		RazerPair("Basilisk V3 Pro 35K", 204, 205, 31);
		RazerPair("Pro Click V2", 208, 209, 31);
		RazerPair("Basilisk Mobile", 211, 212, 31);
		RazerPair("Basilisk V3 Pro 35K Phantom Green", 214, 215, 31);
		RazerSingle("Naga Epic", 31, byte.MaxValue);
		RazerSingle("Ouroboros", 50, byte.MaxValue);
		RazerSingle("Atheris", 98, 31);
		RazerSingle("Basilisk X HyperSpeed", 131, byte.MaxValue);
		RazerSingle("Orochi V2", 148, 31);
		RazerSingle("Pro Click Mini", 154, 31);
		RazerSingle("DeathAdder V2 X HyperSpeed", 156, 31);
		RazerSingle("HyperPolling Dongle", 179, 31);
		RazerSingle("Naga V2 HyperSpeed", 180, 31);
		RazerSingle("Viper V3 HyperSpeed", 184, 31);
		RazerSingle("Basilisk V3 X HyperSpeed", 185, 31);
		RazerPair("DeathStalker V2 Pro", 658, 656, 159, "keyboard", (byte)31);
		RazerPair("DeathStalker V2 Pro TKL", 664, 662, 159, "keyboard", (byte)31);
		RazerPair("BlackWidow V3 Mini HyperSpeed", 600, 625, 159, "keyboard", (byte)31);
		RazerPair("BlackWidow V4 Mini HyperSpeed", 697, 698, 159, "keyboard", (byte)31);
		RazerPair("BlackWidow V4 Tenkeyless HyperSpeed", 727, 725, 159, "keyboard", (byte)31);
		RazerPair("BlackWidow V3 Pro", 602, 604, 159, "keyboard", (byte)63);
		list.Add(new DeviceProfile(5426, 1398, "Razer BlackShark V3 Pro", "headset", "Scythe", "USB", 31));
		list.Add(new DeviceProfile(5426, 1399, "Razer BlackShark V3 Pro", "headset", "Scythe", "2.4G", 31));
		list.Add(new DeviceProfile(5426, 1320, "Razer BlackShark V2 Pro (2020)", "headset", "Razer2020", "2.4G", 31));
		(ushort, string)[] array = new(ushort, string)[4]
		{
			(1365, "BlackShark V2 Pro (2023)"),
			(1366, "BlackShark V2 Pro (2023)"),
			(1367, "BlackShark V2 Pro (2023)"),
			(1338, "Barracuda Pro")
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (num, text) = array[i];
			list.Add(new DeviceProfile(5426, num, "Razer " + text, "headset", (num == 1367) ? "Unsupported" : "RazerPA", "2.4G", 31));
		}
		AsusMouse("ROG Gladius III Aimpoint", 6768, 6770);
		AsusMouse("ROG Gladius III Eva 2", 6922, 6924);
		AsusMouse("ROG Gladius III Wireless", 6525, 6527);
		AsusMouse("ROG Chakram X", 6680, 6682);
		AsusMouse("ROG Harpe Ace Aim Lab Edition", 6802, 6804);
		AsusMouse("ROG Keris Wireless Aimpoint", 6758, 6760);
		AsusMouse("ROG Spatha X", 6519, 6521);
		AsusMouse("TX Gaming Mouse Mini", 6899, 6901);
		AsusMouse("TUF Gaming Mini Miku Edition", 7254, 7255);
		AsusMouse("ROG Chakram", 6371, 6373, 25);
		AsusMouse("ROG Keris Wireless", 6494, 6496, 25);
		AsusMouse("ROG Keris EVA Edition", 6743, 6745, 25);
		AsusMouse("ROG Pugio II", 6406, 6408, 25);
		AsusMouse("ROG Strix Impact II Wireless", 6471, 6473, 25);
		list.Add(new DeviceProfile(2821, 6644, "TUF Gaming M4 Wireless", "mouse", "AsusMouse", "2.4G", 31));
		list.Add(new DeviceProfile(2821, 6797, "TX Gaming Mouse", "mouse", "AsusMouse", "待确认", 31));
		(ushort, string, int)[] array2 = new(ushort, string, int)[13]
		{
			(6787, "ROG Azoth", 6),
			(6789, "ROG Azoth", 6),
			(6975, "ROG Azoth Extreme", 6),
			(7407, "ROG Azoth Extreme SE", 6),
			(7204, "ROG Azoth X", 6),
			(7205, "ROG Azoth X", 6),
			(6460, "ROG Falchion", 11),
			(6462, "ROG Falchion", 11),
			(6916, "ROG Falchion RX", 11),
			(6507, "ROG Claymore II", 6),
			(6830, "ROG Strix Scope II 96", 6),
			(7032, "ROG Strix Scope II 96 RX", 6),
			(6663, "ROG Strix Scope RX TKL", 6)
		};
		for (int i = 0; i < array2.Length; i++)
		{
			var (product2, name2, batteryOffset) = array2[i];
			list.Add(new DeviceProfile(2821, product2, name2, "keyboard", "AsusKeyboard", "待确认", 31, 1, batteryOffset));
		}
		array = new(ushort, string)[4]
		{
			(6906, "ROG Delta II"),
			(7489, "ROG Delta II KJP"),
			(7044, "ROG Pelta"),
			(6867, "ROG Cetra SpeedNova")
		};
		for (int i = 0; i < array.Length; i++)
		{
			var (product3, name3) = array[i];
			list.Add(new DeviceProfile(2821, product3, name3, "headset", "AsusHeadset", "2.4G", 31));
		}
		list.Add(new DeviceProfile(2821, 6862, "ROG OMNI Receiver", "keyboard", "Omni", "待确认", 31));
		return list.AsReadOnly();
		void AsusMouse(string name, ushort wired, ushort wireless, int scale = 1)
		{
			list.Add(new DeviceProfile(2821, wired, name, "mouse", "AsusMouse", "USB", 31, scale));
			list.Add(new DeviceProfile(2821, wireless, name, "mouse", "AsusMouse", "2.4G", 31, scale));
		}
		void RazerPair(string name, ushort wired, ushort wireless, byte transaction = 31, string kind = "mouse", byte? wiredTransaction = null)
		{
			list.Add(new DeviceProfile(5426, wired, "Razer " + name, kind, "RazerFeature", "USB", wiredTransaction.GetValueOrDefault(transaction)));
			list.Add(new DeviceProfile(5426, wireless, "Razer " + name, kind, "RazerFeature", "2.4G", transaction));
		}
		void RazerSingle(string name, ushort product, byte transaction = 31)
		{
			list.Add(new DeviceProfile(5426, product, "Razer " + name, "mouse", "RazerFeature", "2.4G", transaction));
		}
	}

	internal static DeviceProfile? ProfileFor(HidDevice d)
	{
		DeviceProfile deviceProfile = Find(d.Vendor, d.Product);
		if ((object)deviceProfile != null)
		{
			if (deviceProfile.MatchName != null && !d.Name.StartsWith(deviceProfile.MatchName, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			return deviceProfile;
		}
		bool flag = d.Vendor == 1133 && d.UsagePage == 65280;
		if (flag)
		{
			ushort usage = d.Usage;
			bool flag2 = (uint)(usage - 1) <= 1u;
			flag = flag2;
		}
		if (flag)
		{
			return new DeviceProfile(d.Vendor, d.Product, (d.Name.Length > 0) ? d.Name : "Logitech HID++ 设备", Kind(d.Name), (d.Name.Contains("receiver", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("dongle", StringComparison.OrdinalIgnoreCase)) ? "LogitechReceiver" : "Logitech", "待确认", 31);
		}
		return null;
	}

	private static IEnumerable<DeviceProfile> MultiBrandProfiles()
	{
		List<DeviceProfile> list = new List<DeviceProfile>();
		ushort[] array = new ushort[23]
		{
			50504, 50475, 50482, 50479, 50456, 50458, 50459, 50465, 50469, 50470,
			50478, 50481, 50484, 50485, 50487, 50489, 50490, 50493, 50495, 50497,
			50501, 50503, 50509
		};
		foreach (ushort pid2 in array)
		{
			Add(1133, pid2, "Logitech HID++ 接收器", "device", "LogitechReceiver");
		}
		(ushort, string)[] array2 = new(ushort, string)[14]
		{
			(2662, "G533"),
			(2756, "G535"),
			(2652, "G633"),
			(2697, "G635"),
			(2651, "G933"),
			(2695, "G935"),
			(2741, "G733"),
			(2814, "G733"),
			(2847, "G733"),
			(2727, "G PRO"),
			(2730, "G PRO X"),
			(2746, "G PRO X"),
			(2811, "G PRO X 2"),
			(2812, "G PRO X 2")
		};
		for (int i = 0; i < array2.Length; i++)
		{
			var (pid3, text) = array2[i];
			Add(1133, pid3, "Logitech " + text, "headset", "Logitech");
		}
		Add(1133, 2807, "Logitech G PRO X 2 LIGHTSPEED", "headset", "LogitechCenturion");
		Add(1133, 2885, "Logitech PRO X 3 LIGHTSPEED", "headset", "Unsupported");
		array = new ushort[2] { 1686, 395 };
		foreach (ushort pid4 in array)
		{
			Add(1008, pid4, "HyperX Cloud II Wireless", "headset", "HyperXCloud2");
		}
		Add(2385, 5912, "HyperX Cloud II Wireless", "headset", "HyperXKingston");
		array = new ushort[2] { 1463, 3229 };
		foreach (ushort pid5 in array)
		{
			Add(1008, pid5, "HyperX Cloud III Wireless", "headset", "HyperXCloud3");
		}
		array = new ushort[2] { 1941, 2453 };
		foreach (ushort pid6 in array)
		{
			Add(1008, pid6, "HyperX Cloud II Core Wireless", "headset", "HyperXCloud3");
		}
		array = new ushort[2] { 716, 1726 };
		foreach (ushort pid7 in array)
		{
			Add(1008, pid7, "HyperX Cloud III S Wireless", "headset", "HyperXCloud3S");
		}
		Add(1008, 2445, "HyperX Cloud Alpha Wireless", "headset", "HyperXAlpha");
		Add(1008, 2238, "HyperX Cloud Alpha 2", "headset", "HyperXAlpha2");
		array = new ushort[2] { 5828, 5923 };
		foreach (ushort pid8 in array)
		{
			Add(2385, pid8, "HyperX Cloud Flight Wireless", "headset", "HyperXFlight");
		}
		array = new ushort[2] { 3479, 3992 };
		foreach (ushort pid9 in array)
		{
			Add(1008, pid9, "HyperX Pulsefire Haste 2 Wireless", "mouse", "HyperXHaste");
		}
		Add(21075, 4128, "MCHOSE M7 / L7 接收器", "mouse", "MchoseMouse");
		Add(21075, 49, "MCHOSE M7 Ultra", "mouse", "MchoseMouse", "USB");
		Add(14391, 4107, "MCHOSE A7 V2 Ultra", "mouse", "MchoseMouse");
		Add(14391, 4117, "MCHOSE G98 V3", "keyboard", "Unsupported");
		Add(43173, 8789, "MCHOSE G7", "mouse", "MchoseG7", "USB / 2.4G（待确认）");
		array = new ushort[3] { 61459, 61461, 61465 };
		foreach (ushort num in array)
		{
			Add(8227, num, "MCHOSE A5 Pro Max", "mouse", "MchoseA5", (num == 61465) ? "USB" : "2.4G");
		}
		Add(10525, 14429, "MCHOSE V9 Pro", "headset", "MchoseV9", "USB / 2.4G（待确认）");
		array = new ushort[2] { 24584, 24586 };
		foreach (ushort num2 in array)
		{
			Add(14391, num2, "MCHOSE V9 Turbo+", "headset", "MchoseTurbo", (num2 == 24586) ? "USB" : "2.4G");
		}
		Add(16868, 8193, "MCHOSE K87", "keyboard", "MchoseKeyboard");
		(ushort, ushort, string, string)[] array3 = new(ushort, ushort, string, string)[11]
		{
			(13652, 62863, "VXE R1 SE+", "USB"),
			(14139, 4229, "VXE R1 SE+", "2.4G"),
			(13652, 62858, "VXE R1 Pro / R1 Pro Max", "2.4G"),
			(13652, 62860, "VXE R1 Pro Max", "USB"),
			(13652, 62725, "VGN Dragonfly 4K", "2.4G"),
			(13652, 62726, "VGN Dragonfly 4K", "USB"),
			(14139, 4534, "ATK A9 Ultimate", "USB"),
			(14139, 4569, "ATK A9 Ultimate", "2.4G"),
			(14139, 4297, "ATK A9 SE", "2.4G"),
			(14139, 4122, "ATK X1 Pro", "2.4G"),
			(14139, 4123, "ATK Fire X 8K", "2.4G")
		};
		for (int i = 0; i < array3.Length; i++)
		{
			var (vid2, pid10, name2, connection2) = array3[i];
			Add(vid2, pid10, name2, "mouse", "Compx", connection2);
		}
		Add(14139, 4236, "ATK X1 Ultimate", "mouse", "AtkNearlink");
		Add(14139, 4235, "ATK X1 Ultimate", "mouse", "AtkNearlink", "USB");
		Add(14139, 4630, "ATK / VXE NK 接收器", "mouse", "AtkBitmouse");
		Add(12815, 20616, "ATK V75X", "keyboard", "Unsupported", "2.4G", "ATK V75X");
		Add(12815, 20565, "ATK V75X", "keyboard", "AtkKeyboard", "USB", "ATK V75X");
		Add(3141, 65276, "AULA F87 Pro V2C", "keyboard", "AulaKeyboard", "2.4G", "AULA");
		Add(14502, 10002, "AULA F87 Pro V2C", "keyboard", "Unsupported", "USB", "AULA");
		Add(13652, 64009, "AULA F75", "keyboard", "Compx", "2.4G", "AULA");
		Add(12815, 8864, "AULA V9 Max+", "mouse", "AulaMouse", "2.4G", "AULA");
		Add(12815, 8887, "AULA V9 Max+", "mouse", "AulaMouse", "USB", "AULA");
		return list;
		void Add(ushort vid, ushort pid, string name, string kind, string reader, string connection = "2.4G", string? match = null)
		{
			list.Add(new DeviceProfile(vid, pid, name, kind, reader, connection, 31, 1, 6, match));
		}
	}
}
