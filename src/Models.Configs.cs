namespace SRT;

// Configs page: CS2's config copied between your accounts (Core/Steam/Cs2Configs.cs)
public sealed partial class Settings
{
    public string ConfigMode { get; set; } = "off";       // "off" · "main" (from one account) · "previous" (from the account used before) · "latest" (latest change wins)
    public string ConfigMainId { get; set; }              // SteamID64 of the main account ("main" mode)
    public List<string> ConfigCategories { get; set; } = new() { "binds", "crosshair", "game", "video", "hud" };
    public List<string> ConfigOptOut { get; set; } = new();   // SteamID64s whose config the sync never touches
    public bool ConfigOnNewAccount { get; set; } = true;  // a new account on this PC gets the config
    public bool ConfigOnLogin { get; set; } = true;       // before the app logs in to an account or opens CS2
}
