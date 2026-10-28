using System.Net.NetworkInformation;

namespace SRT.Core;

public enum QualityLevel { Unknown, Good, Fair, Bad }

/// <summary>One measurement of the phone connection: a few pings to the IP test address, which only goes out through the phone.</summary>
public sealed record LinkQuality(int Sent, int Lost, int AvgMs, int JitterMs, string NetType)
{
    public int LossPct => Sent == 0 ? 100 : Lost * 100 / Sent;
    public QualityLevel Level => Quality.Rate(this);
    public string Text => Lost == Sent ? $"no answer · {NetType ?? "?"}"
        : $"{AvgMs} ms · jitter {JitterMs} ms · {LossPct}% loss" + (NetType != null ? $" · {NetType}" : "");
}

public static class Quality
{
    // Slow mobile generations: CS2 over them lags whatever the ping says.
    static readonly string[] SlowNets = { "2G", "3G", "EDGE", "GPRS", "UMTS", "HSPA", "HSPA+", "HSDPA", "HSUPA", "H", "H+" };

    /// <summary>
    /// For CS2 over mobile data. Bad: loss of 20% or more (2 of 8 pings), 150 ms or more, jitter of 40 ms or more, or a 2G/3G network.
    /// Fair: any loss, 80 ms or more, or jitter of 20 ms or more. Good otherwise.
    /// </summary>
    public static QualityLevel Rate(LinkQuality q)
    {
        if (q == null || q.Sent == 0) return QualityLevel.Unknown;
        if (q.LossPct >= 20 || q.AvgMs >= 150 || q.JitterMs >= 40 || SlowNets.Contains(q.NetType ?? "", StringComparer.OrdinalIgnoreCase)) return QualityLevel.Bad;
        if (q.Lost > 0 || q.AvgMs >= 80 || q.JitterMs >= 20) return QualityLevel.Fair;
        return QualityLevel.Good;
    }

    /// <summary>
    /// The level that counts: one bad sample is a hiccup, so Bad needs the last two to be bad,
    /// and coming back from Bad needs the last two to be Good or Fair.
    /// </summary>
    public static QualityLevel Settled(IReadOnlyList<LinkQuality> recent, QualityLevel current)
    {
        if (recent.Count == 0) return QualityLevel.Unknown;
        var last = recent[^1].Level;
        if (recent.Count < 2) return current == QualityLevel.Unknown && last != QualityLevel.Bad ? last : current;
        var prev = recent[^2].Level;
        if (last == QualityLevel.Bad && prev == QualityLevel.Bad) return QualityLevel.Bad;
        if (current == QualityLevel.Bad && (last == QualityLevel.Bad || prev == QualityLevel.Bad)) return QualityLevel.Bad;
        return last == QualityLevel.Bad ? (current == QualityLevel.Unknown ? QualityLevel.Fair : current) : last;
    }

    /// <summary>Pings <paramref name="ip"/> <paramref name="count"/> times, 200 ms apart. Average and jitter come from the answers only.</summary>
    public static async Task<LinkQuality> Measure(string ip, string netType, int count = 8, int timeoutMs = 1000)
    {
        var times = new List<long>();
        var lost = 0;
        using var ping = new Ping();
        for (var i = 0; i < count; i++)
        {
            try
            {
                var r = await ping.SendPingAsync(ip, timeoutMs);
                if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime); else lost++;
            }
            catch { lost++; }
            if (i < count - 1) await Task.Delay(200);
        }
        var avg = times.Count == 0 ? 0 : (int)Math.Round(times.Average());
        // Jitter as games show it: the average change between two answers in a row.
        var jitter = times.Count < 2 ? 0 : (int)Math.Round(times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average());
        return new LinkQuality(count, lost, avg, jitter, netType);
    }
}
