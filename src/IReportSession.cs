using System;
using System.Threading.Tasks;

namespace PeriDock;

internal interface IReportSession : IDisposable
{
	Task<byte[]?> Query(byte[] request, Func<byte[], bool> matches, bool feature = false, bool featureFallback = false, int timeoutMs = 900);
}
