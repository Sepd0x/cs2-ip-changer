using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SRT.Core;

public sealed class VdfNode
{
    public List<KeyValuePair<string, object>> Items { get; } = new();

    public VdfNode Child(string key) => Items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value as VdfNode;

    /// <summary>The node down a path of keys (each found in any case), or null when one is missing.</summary>
    public VdfNode At(IEnumerable<string> keys)
    {
        var n = this;
        foreach (var k in keys) if ((n = n?.Child(k)) == null) return null;
        return n;
    }

    public string Get(string key) => Items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value as string;

    public bool Has(string key) => Items.Any(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public void Set(string key, string value)
    {
        var i = Items.FindIndex(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Items[i] = new(Items[i].Key, value); else Items.Add(new(key, value));
    }

    /// <summary>Steam's files (loginusers.vdf, localconfig.vdf, libraryfolders.vdf): see <see cref="KeyValues.Steam"/>.</summary>
    public static VdfNode Parse(string text) => KeyValues.Steam.Parse(text);

    /// <summary>
    /// True if writing the parsed file gives back the same tokens, quoted text as it was (escapes included).
    /// False on anything this parser doesn't keep: unquoted words (#include, [$WIN32] conditions), stray braces, escapes it would drop.
    /// </summary>
    public static bool RoundTrips(string text) => KeyValues.Steam.RoundTrips(text);

    public string Write() => KeyValues.Steam.Write(this);
}

/// <summary>
/// Valve's KeyValues text, the one reader and writer for Steam's .vdf files and CS2's config files. Written as Steam and CS2 write
/// it: "key"\t\t"value" and "key" then { } on their own lines, tabs, LF (the caller picks the line endings). // comments are skipped.
/// The two dialects differ in the quotes:
/// <list type="bullet">
/// <item><see cref="Steam"/>: a backslash escapes the next character (\" and \\ inside a name), and the writer escapes them back.
/// As the app always read it, a quoted "{" or "}" still opens or closes a section, and a bare word runs up to a space or a brace.
/// A file where either matters never round-trips, so it is never written.</item>
/// <item><see cref="Cs2"/>: no escapes. CS2 saves a bind on the \ key as "\" (seen on a real keys file), so a quote always ends the
/// text, a value can't hold a quote (the writer throws), and only bare braces count.</item>
/// </list>
/// </summary>
public sealed class KeyValues
{
    public static readonly KeyValues Steam = new(true, "VDF"), Cs2 = new(false, "config");

    readonly bool _escapes;
    readonly string _name;

    KeyValues(bool escapes, string name) { _escapes = escapes; _name = name; }

    /// <summary>One token. <paramref name="Raw"/>: a quoted text as it is in the file (escapes included).</summary>
    readonly record struct Token(string Text, bool Quoted, string Raw)
    {
        public bool Is(string brace, bool quotedCounts) => Text == brace && (!Quoted || quotedCounts);
    }

    List<Token> Tokens(string t)
    {
        var list = new List<Token>();
        var p = 0;
        while (p < t.Length)
        {
            var ch = t[p];
            if (char.IsWhiteSpace(ch)) { p++; continue; }
            if (ch == '/' && p + 1 < t.Length && t[p + 1] == '/') { while (p < t.Length && t[p] != '\n') p++; continue; }
            if (ch is '{' or '}') { list.Add(new(ch.ToString(), false, null)); p++; continue; }
            if (ch == '"')
            {
                var start = ++p;
                var sb = new StringBuilder();
                while (p < t.Length && t[p] != '"')
                {
                    if (_escapes && t[p] == '\\' && p + 1 < t.Length) { sb.Append(t[p + 1]); p += 2; continue; }
                    sb.Append(t[p++]);
                }
                list.Add(new(sb.ToString(), true, t[start..Math.Min(p, t.Length)]));
                p++;
                continue;
            }
            var s = p;
            while (p < t.Length && !char.IsWhiteSpace(t[p]) && t[p] is not ('{' or '}') && (_escapes || t[p] != '"')) p++;
            list.Add(new(t[s..p], false, null));
        }
        return list;
    }

    public VdfNode Parse(string text)
    {
        var tokens = Tokens(text);
        var i = 0;
        return Body(tokens, ref i, 0);
    }

    VdfNode Body(List<Token> k, ref int i, int depth)
    {
        // A crafted file with thousands of "{" would overflow the stack, and that can't be caught: it would close the app as admin
        // (an imported snapshot can come from anyone).
        if (depth > 32) throw new InvalidDataException(_name + " nested too deep");
        var node = new VdfNode();
        while (i < k.Count)
        {
            var key = k[i++];
            if (key.Is("}", _escapes)) return node;
            if (i >= k.Count) return node;
            var v = k[i++];
            if (v.Is("{", _escapes)) node.Items.Add(new(key.Text, Body(k, ref i, depth + 1)));
            else node.Items.Add(new(key.Text, v.Text));
        }
        return node;
    }

    public string Write(VdfNode n, int depth = 0)
    {
        var sb = new StringBuilder();
        var tab = new string('\t', depth);
        foreach (var (k, v) in n.Items)
        {
            if (v is VdfNode c) sb.Append(tab).Append('"').Append(Text(k)).Append("\"\n").Append(tab).Append("{\n").Append(Write(c, depth + 1)).Append(tab).Append("}\n");
            else sb.Append(tab).Append('"').Append(Text(k)).Append("\"\t\t\"").Append(Text((string)v)).Append("\"\n");
        }
        return sb.ToString();
    }

    string Text(string s) => _escapes ? s.Replace("\\", "\\\\").Replace("\"", "\\\"")
        : s.Contains('"') ? throw new InvalidDataException("a quote inside a config value") : s;

    /// <summary>True if writing the parsed file gives back the same tokens, quoted text as it was (nothing bare, nothing lost).</summary>
    public bool RoundTrips(string text)
    {
        try
        {
            var before = Tokens(text);
            if (before.Any(t => !t.Quoted && t.Text is not ("{" or "}"))) return false;
            return before.Select(Key).SequenceEqual(Tokens(Write(Parse(text))).Select(Key));
        }
        catch { return false; }
    }

    static (string, bool) Key(Token t) => (t.Quoted ? t.Raw : t.Text, t.Quoted);
}
