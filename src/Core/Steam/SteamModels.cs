using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SRT.Core;

// ------------------------------------------------------------ Contas guardadas no Steam deste PC

public sealed record LocalAccount(string SteamId, string AccountName, string PersonaName, bool MostRecent, long Timestamp);

public sealed class SteamProfile
{
    public string Persona { get; set; }
    public string AvatarUrl { get; set; }
    public string OnlineState { get; set; }
    public bool Private { get; set; }
    public bool Limited { get; set; }
    public int VacBans { get; set; }
    public int GameBans { get; set; }
    public int? DaysSinceLastBan { get; set; }
    public bool CommunityBan { get; set; }
    public string TradeBan { get; set; }
    public int? Level { get; set; }
    public double? Cs2Hours { get; set; }
    public string MemberSince { get; set; }
    public string Country { get; set; }
    public DateTime Checked { get; set; }
}
