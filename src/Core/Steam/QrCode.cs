using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SRT.Core;

/// <summary>
/// A QR code for the Steam app's "scan to sign in" link (byte mode, error correction M, versions 1 to 10, about 200
/// characters at most). Small on purpose, so the app doesn't ship a QR library and System.Drawing for one picture.
/// Follows the steps of the QR standard as in Nayuki's reference implementation.
/// </summary>
public sealed class QrCode
{
    // Error correction level M, versions 1..10: codewords per block and number of blocks.
    static readonly int[] EccPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
    static readonly int[] Blocks = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
    const int MaxVersion = 10;

    public int Version { get; }
    public int Size { get; }
    readonly bool[,] _dark, _func;

    public bool this[int x, int y] => _dark[y, x];

    QrCode(int version)
    {
        Version = version;
        Size = version * 4 + 17;
        _dark = new bool[Size, Size];
        _func = new bool[Size, Size];
    }

    static int RawModules(int ver)
    {
        var r = (16 * ver + 128) * ver + 64;
        if (ver >= 2) { var n = ver / 7 + 2; r -= (25 * n - 10) * n - 55; if (ver >= 7) r -= 36; }
        return r;
    }

    static int DataCodewords(int ver) => RawModules(ver) / 8 - EccPerBlock[ver] * Blocks[ver];

    public static QrCode Encode(string text)
    {
        var data = Encoding.UTF8.GetBytes(text ?? "");
        int ver = 1;
        for (; ver <= MaxVersion; ver++)
            if (4 + (ver <= 9 ? 8 : 16) + data.Length * 8 <= DataCodewords(ver) * 8) break;
        if (ver > MaxVersion) throw new ArgumentException("too long for a QR code here");

        var bits = new List<bool>();
        void Put(int v, int n) { for (var i = n - 1; i >= 0; i--) bits.Add(((v >> i) & 1) != 0); }
        Put(0b0100, 4);
        Put(data.Length, ver <= 9 ? 8 : 16);
        foreach (var b in data) Put(b, 8);
        var cap = DataCodewords(ver) * 8;
        Put(0, Math.Min(4, cap - bits.Count));
        Put(0, (8 - bits.Count % 8) % 8);
        for (var pad = 0xEC; bits.Count < cap; pad ^= 0xEC ^ 0x11) Put(pad, 8);
        var codewords = new byte[bits.Count / 8];
        for (var i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        var q = new QrCode(ver);
        q.DrawFunctionPatterns();
        q.DrawCodewords(q.AddEccAndInterleave(codewords));
        var best = 0; var bestPenalty = int.MaxValue;
        for (var m = 0; m < 8; m++)
        {
            q.ApplyMask(m);
            q.DrawFormatBits(m);
            var p = q.Penalty();
            if (p < bestPenalty) { best = m; bestPenalty = p; }
            q.ApplyMask(m);   // XOR again = undo
        }
        q.ApplyMask(best);
        q.DrawFormatBits(best);
        return q;
    }

    void SetFunc(int x, int y, bool dark) { _dark[y, x] = dark; _func[y, x] = true; }

    void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++) { SetFunc(6, i, i % 2 == 0); SetFunc(i, 6, i % 2 == 0); }
        DrawFinder(3, 3); DrawFinder(Size - 4, 3); DrawFinder(3, Size - 4);
        var pos = AlignmentPositions();
        for (var i = 0; i < pos.Length; i++)
            for (var j = 0; j < pos.Length; j++)
                if (!((i == 0 && j == 0) || (i == 0 && j == pos.Length - 1) || (i == pos.Length - 1 && j == 0)))
                    for (var dy = -2; dy <= 2; dy++)
                        for (var dx = -2; dx <= 2; dx++)
                            SetFunc(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
        DrawFormatBits(0);
        DrawVersion();
    }

    void DrawFinder(int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
            for (var dx = -4; dx <= 4; dx++)
            {
                int xx = x + dx, yy = y + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) SetFunc(xx, yy, d != 2 && d != 4);
            }
    }

    int[] AlignmentPositions()
    {
        if (Version == 1) return Array.Empty<int>();
        var n = Version / 7 + 2;
        var step = (Version * 8 + n * 3 + 5) / (n * 4 - 4) * 2;
        var r = new int[n];
        r[0] = 6;
        for (int i = n - 1, p = Size - 7; i >= 1; i--, p -= step) r[i] = p;
        return r;
    }

    void DrawFormatBits(int mask)
    {
        var data = (0 << 3) | mask;   // level M = 00
        var rem = data;
        for (var i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var b = ((data << 10) | rem) ^ 0x5412;
        bool Bit(int i) => ((b >> i) & 1) != 0;
        for (var i = 0; i <= 5; i++) SetFunc(8, i, Bit(i));
        SetFunc(8, 7, Bit(6)); SetFunc(8, 8, Bit(7)); SetFunc(7, 8, Bit(8));
        for (var i = 9; i < 15; i++) SetFunc(14 - i, 8, Bit(i));
        for (var i = 0; i < 8; i++) SetFunc(Size - 1 - i, 8, Bit(i));
        for (var i = 8; i < 15; i++) SetFunc(8, Size - 15 + i, Bit(i));
        SetFunc(8, Size - 8, true);
    }

    void DrawVersion()
    {
        if (Version < 7) return;
        var rem = Version;
        for (var i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
        var b = (Version << 12) | rem;
        for (var i = 0; i < 18; i++)
        {
            var bit = ((b >> i) & 1) != 0;
            int a = Size - 11 + i % 3, c = i / 3;
            SetFunc(a, c, bit); SetFunc(c, a, bit);
        }
    }

    byte[] AddEccAndInterleave(byte[] data)
    {
        int nb = Blocks[Version], ecc = EccPerBlock[Version], raw = RawModules(Version) / 8;
        int shortBlocks = nb - raw % nb, shortLen = raw / nb;
        var div = Divisor(ecc);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < nb; i++)
        {
            var len = shortLen - ecc + (i < shortBlocks ? 0 : 1);
            var dat = data[k..(k + len)];
            k += len;
            var e = Remainder(dat, div);
            var block = new byte[shortLen + 1];
            dat.CopyTo(block, 0);
            e.CopyTo(block, shortLen + 1 - ecc);
            blocks.Add(block);
        }
        var result = new List<byte>(raw);
        for (var i = 0; i < blocks[0].Length; i++)
            for (var j = 0; j < blocks.Count; j++)
                if (i != shortLen - ecc || j >= shortBlocks) result.Add(blocks[j][i]);
        return result.ToArray();
    }

    static int Mul(int x, int y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--) { z = (z << 1) ^ ((z >> 7) * 0x11D); z ^= ((y >> i) & 1) * x; }
        return z;
    }

    static byte[] Divisor(int degree)
    {
        var r = new byte[degree];
        r[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < r.Length; j++) { r[j] = (byte)Mul(r[j], root); if (j + 1 < r.Length) r[j] ^= r[j + 1]; }
            root = Mul(root, 0x02);
        }
        return r;
    }

    static byte[] Remainder(byte[] data, byte[] div)
    {
        var r = new byte[div.Length];
        foreach (var b in data)
        {
            var f = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1);
            r[^1] = 0;
            for (var i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], f);
        }
        return r;
    }

    void DrawCodewords(byte[] data)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < Size; vert++)
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var up = ((right + 1) & 2) == 0;
                    var y = up ? Size - 1 - vert : vert;
                    if (!_func[y, x] && i < data.Length * 8)
                    {
                        _dark[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
        }
    }

    void ApplyMask(int m)
    {
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var inv = m switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (inv && !_func[y, x]) _dark[y, x] = !_dark[y, x];
            }
    }

    /// <summary>The standard's penalty (runs, 2×2 blocks, finder-like patterns, balance): the lowest mask scans best.</summary>
    int Penalty()
    {
        var p = 0;
        for (var pass = 0; pass < 2; pass++)
            for (var a = 0; a < Size; a++)
            {
                var run = 1;
                for (var b = 1; b < Size; b++)
                {
                    bool cur = pass == 0 ? _dark[a, b] : _dark[b, a], prev = pass == 0 ? _dark[a, b - 1] : _dark[b - 1, a];
                    if (cur == prev) { run++; if (run == 5) p += 3; else if (run > 5) p++; }
                    else run = 1;
                }
                for (var b = 0; b + 10 < Size; b++)
                {
                    bool M(int k) => pass == 0 ? _dark[a, b + k] : _dark[b + k, a];
                    bool core = M(4) && !M(5) && M(6) && M(7) && M(8) && !M(9) && M(10);
                    bool coreR = M(0) && !M(1) && M(2) && M(3) && M(4) && !M(5) && M(6);
                    if (core && !M(0) && !M(1) && !M(2) && !M(3)) p += 40;
                    if (coreR && !M(7) && !M(8) && !M(9) && !M(10)) p += 40;
                }
            }
        for (var y = 0; y < Size - 1; y++)
            for (var x = 0; x < Size - 1; x++)
            {
                var c = _dark[y, x];
                if (c == _dark[y, x + 1] && c == _dark[y + 1, x] && c == _dark[y + 1, x + 1]) p += 3;
            }
        var dark = 0;
        foreach (var d in _dark) if (d) dark++;
        var total = Size * Size;
        var k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return p + Math.Max(0, k2) * 10;
    }

    /// <summary>Black on white with the 4-module quiet zone, <paramref name="scale"/> pixels per module.</summary>
    public BitmapSource ToImage(int scale = 6)
    {
        var n = (Size + 8) * scale;
        var px = new byte[n * n];
        Array.Fill(px, (byte)255);
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (_dark[y, x])
                    for (var dy = 0; dy < scale; dy++)
                        Array.Fill(px, (byte)0, ((y + 4) * scale + dy) * n + (x + 4) * scale, scale);
        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Gray8, null, px, n);
        bmp.Freeze();
        return bmp;
    }
}
