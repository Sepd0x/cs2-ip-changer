using System.Windows.Input;

namespace SRT.Core;

/// <summary>
/// Global hotkeys as text ("Ctrl+Shift+F9"). Ctrl+Alt together is refused: on many keyboards that is AltGr,
/// which types @, € and other characters, so a hotkey on it would fire while someone writes in Discord.
/// </summary>
public static class Hotkey
{
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    static readonly KeyConverter Keys = new();

    /// <summary>Reads "Ctrl+Shift+F9". False if it can't be a hotkey (see <see cref="Problem"/>).</summary>
    public static bool TryParse(string text, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (!TryRead(text, out var m, out var key)) return false;
        if (Problem(m, key) != null) return false;
        mods = (m.HasFlag(ModifierKeys.Control) ? MOD_CONTROL : 0) | (m.HasFlag(ModifierKeys.Alt) ? MOD_ALT : 0)
             | (m.HasFlag(ModifierKeys.Shift) ? MOD_SHIFT : 0) | (m.HasFlag(ModifierKeys.Windows) ? MOD_WIN : 0);
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    static bool TryRead(string text, out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None; key = Key.None;
        var parts = (text ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        foreach (var p in parts[..^1])
        {
            var m = p.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "shift" => ModifierKeys.Shift,
                "alt" => ModifierKeys.Alt,
                "win" or "windows" => ModifierKeys.Windows,
                _ => (ModifierKeys?)null,
            };
            if (m == null || mods.HasFlag(m.Value)) return false;
            mods |= m.Value;
        }
        try { key = (Key)Keys.ConvertFromInvariantString(parts[^1]); }
        catch { return false; }
        return key != Key.None;
    }

    /// <summary>Why this combination can't be a hotkey (null = it can).</summary>
    public static string Problem(ModifierKeys mods, Key key)
    {
        if (key == Key.None || IsModifier(key)) return "Press a key together with Ctrl, Alt, Shift or Win.";
        if (mods.HasFlag(ModifierKeys.Control) && mods.HasFlag(ModifierKeys.Alt)) return "Ctrl+Alt is AltGr on many keyboards (it types @ and €). Pick another combination.";
        var fKey = key >= Key.F1 && key <= Key.F24;
        if (mods == ModifierKeys.None) return "A key on its own would fire while you type or play. Add Ctrl, Alt, Shift or Win.";
        if (mods == ModifierKeys.Shift && !fKey) return "Shift with a letter or number types it. Use Shift only with F1 to F24.";
        return null;
    }

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    /// <summary>"Ctrl+Shift+F9" (the same text <see cref="TryParse"/> reads).</summary>
    public static string Format(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Keys.ConvertToInvariantString(key));
        return string.Join("+", parts);
    }
}
