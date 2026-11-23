#nullable disable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeriDock;

internal static class MultiBrandReader
{
	internal static bool Handles(string reader)
	{
		switch (reader)
		{
		case "AtkBitmouse":
		case "AtkNearlink":
		case "MchoseTurbo":
		case "AtkKeyboard":
		case "HyperXAlpha":
		case "HyperXHaste":
		case "MchoseMouse":
		case "HyperXFlight":
		case "AulaKeyboard":
		case "HyperXAlpha2":
		case "HyperXCloud2":
		case "HyperXCloud3":
		case "MchoseA5":
		case "MchoseG7":
		case "MchoseV9":
		case "HyperXKingston":
		case "MchoseKeyboard":
		case "Compx":
		case "AulaMouse":
		case "HyperXCloud3S":
		case "LogitechCenturion":
			return true;
		default:
			return false;
		}
	}

	internal static bool Accepts(HidDevice d, DeviceProfile p)
	{
		HidDevice d2 = d;
		if (d2.Vendor != p.Vendor || d2.Product != p.Product || (p.MatchName != null && !d2.Name.StartsWith(p.MatchName, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		switch (p.Reader)
		{
		case "Compx":
			return d2.UsagePage == 65282 && d2.Usage == 2 && Output(17);
		case "AtkNearlink":
			return d2.UsagePage == ((d2.Product == 4236) ? 65459 : 65458) && d2.Usage == 1 && Output(64);
		case "AtkBitmouse":
			return d2.UsagePage == 65285 && d2.Usage == 1 && Output(64);
		case "AtkKeyboard":
		case "AulaMouse":
			return d2.UsagePage == 65308 && d2.Usage == 146 && Output(32);
		case "AulaKeyboard":
			return d2.UsagePage == 65408 && d2.Usage == 1 && Output(33);
		case "MchoseMouse":
			return d2.UsagePage == 65281 && d2.Usage == 1 && d2.FeatureBytes >= 21;
		case "MchoseA5":
			return d2.UsagePage == ushort.MaxValue && d2.FeatureBytes >= 65;
		case "MchoseG7":
			return d2.UsagePage == 65281 && d2.Usage == 16 && Output(65);
		case "MchoseKeyboard":
			return d2.UsagePage == 65282 && Output(20);
		case "MchoseTurbo":
		case "MchoseV9":
			return d2.UsagePage >= 65280 && Output(64);
		case "HyperXCloud2":
			return d2.UsagePage == 65424 && d2.Usage == 771 && Output(52);
		case "HyperXKingston":
			return d2.UsagePage == 65299 && d2.Usage == 1 && Output(62);
		case "HyperXCloud3":
			return d2.UsagePage == 65299 && d2.Usage == 1 && (Output(62) || (d2.FeatureBytes >= 62 && d2.InputBytes > 0));
		case "HyperXCloud3S":
			return d2.Path.Contains("mi_03", StringComparison.OrdinalIgnoreCase) && (d2.UsagePage >= 65280 || d2.UsagePage == 12) && d2.InputBytes > 0;
		case "HyperXAlpha":
			return d2.UsagePage == 65347 && d2.Usage == 514 && Output(31);
		case "HyperXAlpha2":
			return d2.UsagePage == 65299 && d2.Usage == 65280 && Output(64);
		case "HyperXHaste":
			return d2.UsagePage == 65280 && d2.Usage == 1 && d2.Path.Contains("mi_02", StringComparison.OrdinalIgnoreCase) && Output(64);
		case "HyperXFlight":
		{
			ushort usagePage = d2.UsagePage;
			bool flag = ((usagePage == 65363 || usagePage == 65424) ? true : false);
			return flag && d2.Usage == 771 && Output(20);
		}
		case "LogitechCenturion":
			return d2.UsagePage == 65440 && d2.Usage == 1 && Output(64);
		default:
			return false;
		}
		bool Output(int length)
		{
			if (d2.OutputBytes >= length)
			{
				return d2.InputBytes > 0;
			}
			return false;
		}
	}

	internal static async Task<Reading> Read(HidDevice[] devices, DeviceProfile profile, Func<HidDevice[], IReportSession>? open = null)
	{
		DeviceProfile profile2 = profile;
		HidDevice[] selected = (from d in devices
			where Accepts(d, profile2)
			orderby d.UsagePage >= 65280 descending
			select d).ToArray();
		if (selected.Length == 0)
		{
			return new Reading(null, null, null, null, profile2.Reader, "已识别型号；未找到匹配的电量接口", Cached: false, "待确认", null, null, ReadFailure.ProtocolUnsupported);
		}
		string reader = profile2.Reader;
		IReportSession reportSession = open?.Invoke(selected);
		if (reportSession == null)
		{
			HidDevice[] devices2 = selected;
			string text = reader;
			bool featureOnly = ((text == "MchoseMouse" || text == "MchoseA5") ? true : false);
			reportSession = new HidReports(devices2, featureOnly);
		}
		IReportSession session = reportSession;
		try
		{
			ushort sequence = (ushort)Random.Shared.Next(1, 65536);
			byte report = (byte)((reader == "AtkNearlink") ? ((byte)((profile2.Product == 4236) ? 5u : 4u)) : ((reader == "AtkKeyboard") ? 4 : 0));
			string text = reader;
			if ((text == "HyperXAlpha" || text == "HyperXHaste") ? true : false)
			{
				bool alpha = reader == "HyperXAlpha";
				byte[] array = await Query(MultiBrandProtocols.Request(reader, (byte)(alpha ? 3 : 82), 1, 0), delegate(byte[] p)
				{
					if (p.Length >= (alpha ? 4 : 3))
					{
						if (!alpha)
						{
							if (p[0] == 83)
							{
								return p[1] == 1;
							}
							return false;
						}
						if (p[0] == 33 && p[1] == 187)
						{
							return p[2] == 3;
						}
						return false;
					}
					return false;
				});
				if (array == null)
				{
					return Empty();
				}
				if (alpha ? (array[3] == 1) : (array[2] == 0))
				{
					return Offline();
				}
				if (!alpha && array[2] != 1)
				{
					return Empty();
				}
			}
			bool aula = reader == "AulaMouse";
			Exception failure = null;
			Reading result2;
			try
			{
				try
				{
					if (aula && await Query(MultiBrandProtocols.Request(reader, 1, 1, 0), (byte[] p) => p.Length >= 4 && p[0] == 4 && p[1] == 0 && p[2] == 0 && p[3] == 1) == null)
					{
						result2 = Empty();
					}
					else
					{
						Reading result = null;
						byte[] array2 = ((!(reader == "MchoseMouse")) ? ((!(reader == "MchoseTurbo")) ? new byte[1] : new byte[2] { 0, 1 }) : new byte[2] { 17, 18 });
						bool featureOnly = default(bool);
						foreach (byte attempt in array2)
						{
							if (reader == "MchoseMouse" && selected.All((HidDevice d) => d.FeatureBytes < ((attempt == 17) ? 21 : 65)))
							{
								continue;
							}
							byte[] array3 = MultiBrandProtocols.Request(reader, (byte)((reader == "MchoseTurbo") ? attempt : 0), sequence, (reader == "MchoseMouse") ? attempt : report);
							byte[] array4;
							try
							{
								byte[] request2 = array3;
								Func<byte[], bool> predicate2 = (byte[] p) => (object)MultiBrandProtocols.Power(reader, p, sequence, report) != null;
								text = reader;
								featureOnly = ((text == "MchoseMouse" || text == "MchoseA5") ? true : false);
								array4 = await Query(request2, predicate2, featureOnly);
							}
							catch (Exception ex) when (((Func<bool>)delegate
							{
								// Could not convert BlockContainer to single expression
								featureOnly = reader == "MchoseMouse" && attempt == 17 && Diagnostics.NativeError(ex).GetValueOrDefault() != 5;
								if (featureOnly)
								{
									featureOnly = ((ex is IOException || ex is Win32Exception) ? true : false);
								}
								return featureOnly;
							}).Invoke())
							{
								continue;
							}
							if (array4 != null)
							{
								result = MultiBrandProtocols.Power(reader, array4, sequence, report);
								break;
							}
						}
						if ((object)result == null)
						{
							result2 = Empty();
						}
						else
						{
							byte b;
							switch (reader)
							{
							case "HyperXCloud2":
							case "HyperXKingston":
								b = 3;
								break;
							case "HyperXCloud3":
								b = 138;
								break;
							case "HyperXCloud3S":
								b = 72;
								break;
							case "HyperXAlpha":
								b = 12;
								break;
							case "AtkBitmouse":
								b = 15;
								break;
							default:
								b = 0;
								break;
							}
							byte b2 = b;
							if (b2 != 0)
							{
								try
								{
									byte[] array5 = await Query(MultiBrandProtocols.Request(reader, b2, 1, 0), (byte[] p) => MultiBrandProtocols.Charging(reader, p).HasValue);
									bool? charging = ((array5 == null) ? null : MultiBrandProtocols.Charging(reader, array5));
									result = result with
									{
										Charging = charging,
										ChargingNote = ((!charging.HasValue) ? "充电状态暂不可读" : null)
									};
								}
								catch (Exception ex2) when (((ex2 is IOException || ex2 is Win32Exception || ex2 is UnauthorizedAccessException || ex2 is OperationCanceledException) ? 1 : 0) != 0)
								{
									result = result with
									{
										Charging = null,
										ChargingNote = "充电状态暂不可读；电量查询已成功"
									};
								}
							}
							result2 = result;
						}
					}
				}
				catch (Exception ex3)
				{
					failure = ex3;
					throw;
				}
			}
			finally
			{
				if (aula)
				{
					try
					{
						await session.Query(MultiBrandProtocols.Request(reader, 2, 1, 0), (byte[] p) => p.Length >= 4 && p[0] == 4 && p[3] == 2, feature: false, featureFallback: false, 250);
					}
					catch (Exception ex4) when (((Func<bool>)delegate
					{
						// Could not convert BlockContainer to single expression
						bool flag = failure != null;
						if (flag)
						{
							flag = ((ex4 is IOException || ex4 is Win32Exception || ex4 is UnauthorizedAccessException || ex4 is OperationCanceledException) ? true : false);
						}
						return flag;
					}).Invoke())
					{
					}
				}
			}
			return result2;
		}
		finally
		{
			if (session != null)
			{
				session.Dispose();
			}
		}
		Reading Empty()
		{
			return new Reading(null, null, null, null, reader, "已识别设备；未收到有效的新鲜电量应答（可能休眠或协议变体）", Cached: false, "待确认", null, null, ReadFailure.ReadError);
		}
		Reading Offline()
		{
			return new Reading(null, null, null, null, reader, "接收器已连接；外设未连接", Cached: false, "待确认", false);
		}
		Task<byte[]?> Query(byte[] request, Func<byte[], bool> predicate, bool feature = false)
		{
			return session.Query(request, predicate, feature, reader == "HyperXCloud3", (reader == "HyperXFlight") ? 2000 : 1000);
		}
	}
}
