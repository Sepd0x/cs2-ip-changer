namespace SRT.Core;

/// <summary>
/// How the phone is connected, said in one place for the Guard, Status, Checks, Phone setup and Change IP: through its hotspot or
/// a cable, whether its own Wi-Fi may be carrying the home network, whether the PC has a home connection next to it, and the mode
/// protection is really in.
/// </summary>
public static class PhoneLink
{
    /// <summary>The phone's connection in a report: the protected one, or the one still being checked (null = none).</summary>
    public static Adapter Exit(GuardReport report) => report?.Egress ?? report?.Candidate;

    /// <summary>
    /// The phone is used through its hotspot: a hotspot name is saved (only ever saved for a Wi-Fi adapter) and the phone's
    /// connection, when there is one, is Wi-Fi. With protection off or no connection yet the saved name decides.
    /// </summary>
    public static bool IsHotspot(string egressSsid, bool? exitIsWifi) => !string.IsNullOrEmpty(egressSsid) && exitIsWifi != false;

    /// <summary>
    /// The phone's own Wi-Fi is on a network, so USB tethering may be sharing the home network instead of mobile data: the phone
    /// isn't trusted. Wi-Fi on but on no network doesn't count (some phones turn it on for a second or two to scan, and blocking
    /// then moved Steam and the whole PC off the phone and back); unknown (Android 10 and older) still counts. On the hotspot the PC
    /// is on the phone's own network (<paramref name="exitIsWifi"/>), so the phone's Wi-Fi says nothing.
    /// </summary>
    public static bool WifiOn(PhoneInfo phone, bool? exitIsWifi) =>
        phone?.Ready == true && phone.WifiOn && phone.WifiConnected != false && exitIsWifi != true;

    /// <summary>
    /// The PC has a home connection: another active adapter with a gateway that isn't <paramref name="phone"/> (the phone's
    /// connection, null = none) and isn't a phone. Its IP can be compared with the phone's, and Save mobile data can fall back on it.
    /// </summary>
    public static bool HasHome(IEnumerable<Adapter> adapters, Adapter phone) =>
        adapters.Any(a => a.Up && a.Gateway != null && a.Index != phone?.Index && !a.IsPhone);

    /// <summary>
    /// The mode protection is really in: "off" while it is turned off (No phone stays "home": it is a choice of mode, not off),
    /// otherwise <see cref="Guard.ModeFor"/>.
    /// </summary>
    public static string EffectiveMode(bool on, string mode, bool gameMode, bool cs2Running) =>
        !on && mode != "home" ? "off" : Guard.ModeFor(mode, gameMode, cs2Running);
}
