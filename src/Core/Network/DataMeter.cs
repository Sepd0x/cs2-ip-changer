namespace SRT.Core;

/// <summary>
/// Mobile data used through the phone: the bytes in and out of the phone's adapter, read from Windows' counters every few seconds.
/// All of it is the phone's data (whatever went through that adapter), the app's or not.
/// </summary>
public sealed class DataMeter
{
    string _id;
    long _last = -1;

    /// <summary>Since the app started.</summary>
    public long Session { get; private set; }

    /// <summary>
    /// Takes the adapter's total (bytes in + out) and returns what was added since the last reading. The first reading of an adapter
    /// only sets the start. A total lower than before means Windows started the counter again (the phone was plugged in again):
    /// the new total is all new.
    /// </summary>
    public long Add(string adapterId, long total)
    {
        if (adapterId == null || total < 0) { _id = null; _last = -1; return 0; }
        if (adapterId != _id || _last < 0) { _id = adapterId; _last = total; return 0; }
        var d = total >= _last ? total - _last : total;
        _last = total;
        Session += d;
        return d;
    }

    /// <summary>"2026-11": the month the bytes count for (local time, the way a carrier counts).</summary>
    public static string Month(DateTime now) => now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The month's total after adding: a new month starts from zero.</summary>
    public static (string Month, long Bytes) AddToMonth(string month, long bytes, long added, DateTime now)
    {
        var m = Month(now);
        return (m, (month == m ? bytes : 0) + added);
    }

    /// <summary>The limit (GB a month, 0 = off) was passed by this addition.</summary>
    public static bool Crossed(long before, long after, double limitGb) =>
        limitGb > 0 && before < Bytes(limitGb) && after >= Bytes(limitGb);

    public static long Bytes(double gb) => (long)(gb * 1_000_000_000);

    /// <summary>"85 MB", "1.24 GB" (decimal, like carriers and phones count).</summary>
    public static string Text(long bytes) =>
        bytes < 1_000_000_000
            ? (bytes / 1_000_000.0).ToString(bytes < 10_000_000 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " MB"
            : (bytes / 1_000_000_000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " GB";

    /// <summary>Windows' byte counters for an adapter (in + out), -1 if it isn't there.</summary>
    public static long Read(string adapterId)
    {
        if (adapterId == null) return -1;
        try
        {
            var n = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(i => i.Id == adapterId);
            if (n == null) return -1;
            var s = n.GetIPStatistics();
            return s.BytesReceived + s.BytesSent;
        }
        catch { return -1; }
    }
}
