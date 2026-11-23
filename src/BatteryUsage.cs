using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PeriDock;

internal sealed class BatterySample
{
    public DateTimeOffset Time { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "device";
    public int Percent { get; set; }
    public bool? Charging { get; set; }
}

internal sealed record UsageDevice(string Id, string Name, string Kind, int? Percent, DateTimeOffset LastSeen, int Samples);

internal sealed record UsageSession(
    DateTimeOffset Start, DateTimeOffset End, int StartPercent, int EndPercent,
    TimeSpan Duration, double Drop, bool Ongoing);

internal sealed class BatteryUsageSummary
{
    internal DateTimeOffset RangeStart { get; init; }
    internal DateTimeOffset RangeEnd { get; init; }
    internal TimeSpan DischargeTime { get; init; }
    internal double PercentDropped { get; init; }
    internal double? DrainPerHour { get; init; }
    internal TimeSpan? EstimatedRemaining { get; init; }
    internal TimeSpan TodayDischarge { get; init; }
    internal double TodayDrop { get; init; }
    internal int ChargeCount { get; init; }
    internal DateTimeOffset? LastChargeEnd { get; init; }
    internal UsageSession? CurrentSession { get; init; }
    internal IReadOnlyList<UsageSession> Sessions { get; init; } = [];
    internal int SampleCount { get; init; }
}

internal static class BatteryUsage
{
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan GapLimit = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    internal static BatteryUsageSummary Compute(
        IReadOnlyList<BatterySample> source, DateTimeOffset now, TimeSpan range,
        int? currentPercent, TimeZoneInfo? zone = null)
    {
        var tz = zone ?? TimeZoneInfo.Local;
        var start = now - range;
        var samples = source
            .Where(s => s.Time >= start.AddHours(-2) && s.Time <= now.AddMinutes(1) && s.Percent is >= 0 and <= 100)
            .OrderBy(s => s.Time)
            .ToList();
        var inRange = samples.Where(s => s.Time >= start).ToList();

        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End, double Drop)>();
        var sessions = new List<UsageSession>();
        var charges = 0;
        DateTimeOffset? lastChargeEnd = null;

        DateTimeOffset? sessionStart = null;
        DateTimeOffset? sessionEnd = null;
        var sessionStartPercent = 0;
        var sessionEndPercent = 0;
        var sessionDrop = 0d;

        void CloseSession(bool ongoing)
        {
            if (sessionStart is null || sessionEnd is null) return;
            var duration = sessionEnd.Value - sessionStart.Value;
            if (duration >= TimeSpan.FromMinutes(1) && sessionEnd.Value >= start)
                sessions.Add(new UsageSession(sessionStart.Value, sessionEnd.Value,
                    sessionStartPercent, sessionEndPercent, duration, sessionDrop, ongoing));
            sessionStart = null; sessionEnd = null; sessionDrop = 0;
        }

        for (var i = 1; i < samples.Count; i++)
        {
            var a = samples[i - 1];
            var b = samples[i];
            var delta = b.Time - a.Time;
            if (b.Time >= start && a.Charging != true && b.Charging == true) charges++;
            if (b.Time >= start && a.Charging == true && b.Charging != true) lastChargeEnd = b.Time;

            var breakSession = delta > GapLimit || delta <= TimeSpan.Zero ||
                a.Charging == true || b.Charging == true || b.Percent > a.Percent;
            if (breakSession) { CloseSession(false); continue; }
            if (b.Time <= start) continue;

            var from = a.Time < start ? start : a.Time;
            var seconds = (b.Time - from).TotalSeconds;
            if (seconds <= 0) continue;
            var share = seconds / delta.TotalSeconds;
            var drop = (a.Percent - b.Percent) * share;
            intervals.Add((from, b.Time, drop));
            if (sessionStart is null)
            {
                sessionStart = from;
                sessionStartPercent = a.Percent;
            }
            sessionEnd = b.Time;
            sessionEndPercent = b.Percent;
            sessionDrop += drop;
        }
        var lastSample = samples.LastOrDefault();
        CloseSession(lastSample is not null && now - lastSample.Time <= GapLimit);

        var discharge = TimeSpan.FromSeconds(intervals.Sum(v => (v.End - v.Start).TotalSeconds));
        var dropped = intervals.Sum(v => v.Drop);
        var rate = discharge.TotalHours >= 0.75 && dropped >= 3 ? dropped / discharge.TotalHours : (double?)null;
        TimeSpan? remaining = null;
        if (rate is > 0 && currentPercent is > 0)
        {
            var hours = currentPercent.Value / rate.Value;
            if (hours is >= 0.2 and <= 500) remaining = TimeSpan.FromHours(hours);
        }

        var localNow = TimeZoneInfo.ConvertTime(now, tz);
        var localDate = localNow.Date;
        var todayStart = new DateTimeOffset(localDate, tz.GetUtcOffset(localDate));
        var todayEnd = todayStart.AddDays(1);
        var todaySeconds = intervals.Sum(v => Overlap(v.Start, v.End, todayStart, todayEnd).TotalSeconds);
        var todayDrop = intervals.Sum(v =>
        {
            var total = (v.End - v.Start).TotalSeconds;
            return total <= 0 ? 0 : v.Drop * Overlap(v.Start, v.End, todayStart, todayEnd).TotalSeconds / total;
        });

        return new BatteryUsageSummary
        {
            RangeStart = start,
            RangeEnd = now,
            DischargeTime = discharge,
            PercentDropped = dropped,
            DrainPerHour = rate,
            EstimatedRemaining = remaining,
            TodayDischarge = TimeSpan.FromSeconds(todaySeconds),
            TodayDrop = todayDrop,
            ChargeCount = charges,
            LastChargeEnd = lastChargeEnd,
            CurrentSession = sessions.LastOrDefault(),
            Sessions = sessions.OrderByDescending(s => s.End).Take(6).ToArray(),
            SampleCount = inRange.Count
        };
    }

    static TimeSpan Overlap(DateTimeOffset a, DateTimeOffset b, DateTimeOffset start, DateTimeOffset end)
    {
        var from = a > start ? a : start;
        var to = b < end ? b : end;
        return to > from ? to - from : TimeSpan.Zero;
    }

    internal static string Duration(TimeSpan? value)
    {
        if (value is null) return "样本不足";
        var span = value.Value;
        if (span <= TimeSpan.Zero) return "0 分钟";
        if (span.TotalMinutes < 1) return "不足 1 分钟";
        if (span.TotalHours < 1) return $"{Math.Round(span.TotalMinutes)} 分钟";
        return $"{(int)span.TotalHours} 小时 {span.Minutes} 分";
    }

    internal static string Remaining(TimeSpan? value)
    {
        if (value is null) return "样本不足";
        var span = value.Value;
        if (span.TotalDays >= 2) return $"约 {span.TotalDays:0.#} 天";
        if (span.TotalHours >= 1) return $"约 {span.TotalHours:0.#} 小时";
        return $"约 {Math.Max(1, Math.Round(span.TotalMinutes))} 分钟";
    }

    internal static string Rate(double? value)
        => value is null ? "样本不足" : value.Value.ToString("0.0", CultureInfo.CurrentCulture) + "% / 小时";
}

internal sealed class BatteryUsageStore
{
    readonly object gate = new();
    readonly string file;
    readonly Diagnostics log;
    readonly Dictionary<string, List<BatterySample>> devices = new(StringComparer.Ordinal);
    DateTimeOffset lastPrune = DateTimeOffset.MinValue;
    bool writeErrorLogged;

    internal BatteryUsageStore(string root, Diagnostics log)
    {
        this.log = log;
        file = Path.Combine(root, "data", "battery-history.jsonl");
        Load();
    }

    internal void Record(BatteryState state, DateTimeOffset now)
    {
        if (!state.Supported || !state.LastReadSucceeded || state.Connected == false ||
            state.Percent is not int percent) return;
        Record(state.Id, state.Name, state.Kind, percent, state.Charging, now);
    }

    internal void Record(string id, string name, string kind, int percent, bool? charging, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(id) || percent is < 0 or > 100) return;
        lock (gate)
        {
            if (!devices.TryGetValue(id, out var list))
            {
                list = [];
                devices[id] = list;
            }
            var last = list.LastOrDefault();
            if (last is not null && (now <= last.Time ||
                (now - last.Time < BatteryUsage.SampleInterval && percent == last.Percent && charging == last.Charging)))
                return;
            var sample = new BatterySample
            {
                Time = now, Id = id, Name = name, Kind = kind, Percent = percent, Charging = charging
            };
            list.Add(sample);
            Append(sample);
            if (now - lastPrune > TimeSpan.FromHours(12)) Prune(now);
        }
    }

    internal IReadOnlyList<BatterySample> SamplesFor(string id)
    {
        lock (gate) return devices.TryGetValue(id, out var list) ? list.ToArray() : [];
    }

    internal IReadOnlyList<UsageDevice> Devices()
    {
        lock (gate)
            return devices
                .Where(pair => pair.Value.Count > 0)
                .Select(pair =>
                {
                    var last = pair.Value[^1];
                    return new UsageDevice(pair.Key, last.Name, last.Kind, last.Percent, last.Time, pair.Value.Count);
                })
                .OrderByDescending(d => d.LastSeen)
                .ToArray();
    }

    internal void Delete(string id)
    {
        lock (gate)
        {
            if (devices.Remove(id)) Rewrite();
        }
    }

    void Load()
    {
        if (!File.Exists(file)) return;
        lock (gate)
        {
            var cutoff = DateTimeOffset.Now - BatteryUsage.Retention;
            var dropped = false;
            try
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var sample = JsonSerializer.Deserialize<BatterySample>(line);
                        if (sample is null || sample.Id.Length == 0 || sample.Percent is < 0 or > 100) continue;
                        if (sample.Time < cutoff) { dropped = true; continue; }
                        if (!devices.TryGetValue(sample.Id, out var list))
                        {
                            list = [];
                            devices[sample.Id] = list;
                        }
                        if (list.Count == 0 || sample.Time >= list[^1].Time) list.Add(sample);
                    }
                    catch (JsonException) { }
                }
                lastPrune = DateTimeOffset.Now;
                if (dropped) Rewrite();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Write("error", "usage", "load", ex.Message, ex);
            }
        }
    }

    void Append(BatterySample sample)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file, JsonSerializer.Serialize(sample) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (writeErrorLogged) return;
            writeErrorLogged = true;
            log.Write("error", "usage", "write", ex.Message, ex);
        }
    }

    void Prune(DateTimeOffset now)
    {
        var cutoff = now - BatteryUsage.Retention;
        var removed = false;
        foreach (var list in devices.Values)
        {
            var count = list.RemoveAll(s => s.Time < cutoff);
            removed |= count > 0;
        }
        lastPrune = now;
        if (removed) Rewrite();
    }

    void Rewrite()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + ".tmp";
            using (var writer = new StreamWriter(temp, false))
            {
                foreach (var sample in devices.Values.SelectMany(list => list).OrderBy(s => s.Time))
                    writer.WriteLine(JsonSerializer.Serialize(sample));
            }
            File.Move(temp, file, true);
            writeErrorLogged = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Write("error", "usage", "rewrite", ex.Message, ex);
        }
    }
}
