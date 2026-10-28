using System.Runtime.InteropServices;

namespace SRT.Core;

/// <summary>Short interface sounds inside the exe: they sound the same on every PC (the Windows sound scheme can be silent) and are heard in game.</summary>
public static class Tones
{
    public enum Kind { Start, Done, Failed, Busy, Ask }

    [DllImport("winmm.dll")]
    static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;

    // Kept for the life of the app: PlaySound reads the memory while it plays.
    static readonly Dictionary<Kind, IntPtr> Loaded = new();

    public static void Play(Kind kind)
    {
        try
        {
            IntPtr p;
            lock (Loaded)
            {
                if (!Loaded.TryGetValue(kind, out p))
                {
                    var wav = Make(kind);
                    p = Marshal.AllocHGlobal(wav.Length);
                    Marshal.Copy(wav, 0, p, wav.Length);
                    Loaded[kind] = p;
                }
            }
            PlaySound(p, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }
        catch { }
    }

    /// <summary>The WAV for each kind, embedded in the exe (Assets/Sounds).</summary>
    public static byte[] Make(Kind kind)
    {
        using var r = typeof(Tones).Assembly.GetManifestResourceStream($"Sounds.{kind.ToString().ToLowerInvariant()}.wav");
        using var m = new MemoryStream();
        r.CopyTo(m);
        return m.ToArray();
    }
}
