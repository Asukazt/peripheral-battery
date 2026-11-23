#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace PeriDock;

internal sealed class HidReports : IReportSession, IDisposable
{
	private readonly List<(HidDevice Device, SafeFileHandle Handle, FileStream? Stream)> channels = new List<(HidDevice, SafeFileHandle, FileStream)>();

	internal HidReports(HidDevice[] devices, bool featureOnly = false)
	{
		Exception ex = null;
		foreach (HidDevice hidDevice in devices)
		{
			try
			{
				SafeFileHandle safeFileHandle = NativeDevices.Open(hidDevice.Path, !featureOnly, featureOnly);
				FileStream item = null;
				try
				{
					if (!featureOnly)
					{
						item = new FileStream(safeFileHandle, FileAccess.ReadWrite, 1, isAsync: true);
					}
				}
				catch
				{
					safeFileHandle.Dispose();
					throw;
				}
				channels.Add((hidDevice, safeFileHandle, item));
			}
			catch (Exception ex2) when (((ex2 is Win32Exception || ex2 is IOException || ex2 is UnauthorizedAccessException) ? 1 : 0) != 0)
			{
				if (ex2 == null || Diagnostics.NativeError(ex2).GetValueOrDefault() != 5)
				{
					ex = ex2;
				}
			}
		}
		if (channels.Count == 0)
		{
			throw ex ?? new IOException("未找到可用 HID 电量接口");
		}
	}

	public async Task<byte[]?> Query(byte[] request, Func<byte[], bool> matches, bool feature = false, bool featureFallback = false, int timeoutMs = 900)
	{
		byte[] request2 = request;
		Func<byte[], bool> matches2 = matches;
		bool flag = request2.Length < 1 || request2.Length > 1024;
		if (!flag)
		{
			bool flag2 = ((timeoutMs < 1 || timeoutMs > 2500) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			throw new ArgumentException("Invalid battery exchange");
		}
		if (feature)
		{
			return await Feature(request2, matches2, timeoutMs);
		}
		foreach (var channel in channels)
		{
			NativeDevices.HidD_FlushQueue(channel.Handle);
		}
		CancellationTokenSource timeout = new CancellationTokenSource(timeoutMs);
		try
		{
			Exception last = null;
			bool written = false;
			bool headsetRegister = request2.Length == 20 && request2[0] == 17 && request2[2] == 5 && request2[3] == 13 && channels.All<(HidDevice, SafeFileHandle, FileStream)>(((HidDevice Device, SafeFileHandle Handle, FileStream Stream) c) => c.Device.Vendor == 1133 && c.Device.Product == 2756);
			foreach (var c2 in from c in channels
				where c.Stream != null && c.Device.OutputBytes >= request2.Length
				orderby c.Device.OutputBytes
				select c)
			{
				byte[] array = new byte[c2.Item1.OutputBytes];
				request2.CopyTo(array, 0);
				try
				{
					await c2.Item3.WriteAsync(array, timeout.Token);
					written = true;
					if (!headsetRegister)
					{
						break;
					}
				}
				catch (IOException ex) when (featureFallback && (ex.HResult & 0xFFFF) == 1 && c2.Item1.FeatureBytes >= request2.Length)
				{
					SetFeature(c2.Item2, request2, c2.Item1.FeatureBytes);
					written = true;
					break;
				}
				catch (Exception ex2) when (((ex2 is IOException || ex2 is Win32Exception) ? 1 : 0) != 0)
				{
					last = ex2;
				}
			}
			if (!written && featureFallback && last == null)
			{
				(HidDevice, SafeFileHandle, FileStream) tuple = channels.FirstOrDefault<(HidDevice, SafeFileHandle, FileStream)>(((HidDevice Device, SafeFileHandle Handle, FileStream Stream) c) => c.Device.FeatureBytes >= request2.Length);
				if (tuple.Item2 != null)
				{
					SetFeature(tuple.Item2, request2, tuple.Item1.FeatureBytes);
					written = true;
				}
			}
			if (!written)
			{
				throw last ?? new IOException("此 HID collection 不支持该电量报告长度");
			}
			List<Task<byte[]>> reads = (from c in channels
				where c.Stream != null && c.Device.InputBytes > 0
				select Read(c.Stream, c.Device.InputBytes, matches2, timeout.Token)).ToList();
			byte[] result;
			try
			{
				while (true)
				{
					if (reads.Count > 0)
					{
						Task<byte[]> task = await Task.WhenAny(reads);
						reads.Remove(task);
						try
						{
							byte[] array2 = await task;
							if (array2 != null)
							{
								result = array2;
								break;
							}
						}
						catch (Exception ex3) when (((ex3 is IOException || ex3 is Win32Exception || ex3 is UnauthorizedAccessException) ? 1 : 0) != 0)
						{
							if (ex3 == null || Diagnostics.NativeError(ex3).GetValueOrDefault() != 5)
							{
								last = ex3;
							}
						}
						continue;
					}
					if (last != null)
					{
						throw last;
					}
					result = null;
					break;
				}
			}
			finally
			{
				timeout.Cancel();
				try
				{
					await Task.WhenAll(reads);
				}
				catch (Exception ex4) when (((ex4 is IOException || ex4 is Win32Exception || ex4 is UnauthorizedAccessException) ? 1 : 0) != 0)
				{
				}
			}
			return result;
		}
		finally
		{
			if (timeout != null)
			{
				((IDisposable)timeout).Dispose();
			}
		}
	}

	private static async Task<byte[]?> Read(FileStream stream, int length, Func<byte[], bool> matches, CancellationToken token)
	{
		try
		{
			for (int i = 0; i < 128; i++)
			{
				byte[] bytes = new byte[length];
				int num = await stream.ReadAsync(bytes, token);
				if (num == 0)
				{
					return null;
				}
				byte[] subArray = bytes[..num];
				if (matches(subArray))
				{
					return subArray;
				}
			}
			return null;
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			return null;
		}
	}

	private static void SetFeature(SafeFileHandle handle, byte[] request, int size)
	{
		if (size < request.Length)
		{
			throw new IOException("Feature report is too short");
		}
		byte[] array = request.ToArray();
		if (!NativeDevices.HidD_SetFeature(handle, array, array.Length))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "无法发送电量查询 feature report");
		}
	}

	private async Task<byte[]?> Feature(byte[] request, Func<byte[], bool> matches, int timeoutMs)
	{
		byte[] request2 = request;
		(HidDevice Device, SafeFileHandle Handle, FileStream Stream) c2 = channels.FirstOrDefault<(HidDevice, SafeFileHandle, FileStream)>(((HidDevice Device, SafeFileHandle Handle, FileStream Stream) c) => c.Device.FeatureBytes >= request2.Length);
		if (c2.Handle == null)
		{
			throw new IOException("未找到对应 feature report");
		}
		DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		do
		{
			SetFeature(c2.Handle, request2, c2.Device.FeatureBytes);
			await Task.Delay(60);
			byte[] array = new byte[request2.Length];
			array[0] = request2[0];
			if (!NativeDevices.HidD_GetFeature(c2.Handle, array, array.Length))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取电量 feature report");
			}
			if (matches(array))
			{
				return array;
			}
		}
		while (DateTime.UtcNow < deadline);
		return null;
	}

	public void Dispose()
	{
		foreach (var channel in channels)
		{
			channel.Stream?.Dispose();
			channel.Handle.Dispose();
		}
	}
}
