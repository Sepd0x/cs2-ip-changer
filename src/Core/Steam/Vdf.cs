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

    public string Get(string key) => Items.FirstOrDefault(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value as string;

    public bool Has(string key) => Items.Any(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public void Set(string key, string value)
    {
        var i = Items.FindIndex(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) Items[i] = new(Items[i].Key, value); else Items.Add(new(key, value));
    }

    public static VdfNode Parse(string text)
    {
        var pos = 0;
        return ParseBody(text, ref pos);
    }

    static VdfNode ParseBody(string t, ref int p, int depth = 0)
    {
        // A crafted file with thousands of "{" would overflow the stack, and that can't be caught: it would close the app as admin.
        if (depth > 32) throw new InvalidDataException("VDF nested too deep");
        var node = new VdfNode();
        while (true)
        {
            var key = Token(t, ref p);
            if (key == null || key == "}") return node;
            var val = Token(t, ref p);
            if (val == "{") node.Items.Add(new(key, ParseBody(t, ref p, depth + 1)));
            else if (val != null) node.Items.Add(new(key, val));
            else return node;
        }
    }

    static string Token(string t, ref int p) => Token(t, ref p, out _, out _);

    /// <summary>
    /// True if writing the parsed file gives back the same tokens, quoted text as it was (escapes included).
    /// False on anything this parser doesn't keep: unquoted words (#include, [$WIN32] conditions), stray braces, escapes it would drop.
    /// </summary>
    public static bool RoundTrips(string text)
    {
        try
        {
            var before = Tokens(text);
            if (before == null) return false;
            var after = Tokens(Parse(text).Write());
            return after != null && before.SequenceEqual(after);
        }
        catch { return false; }
    }

    /// <summary>Raw tokens; null if one is a bare word (Steam quotes every key and value).</summary>
    static List<string> Tokens(string t)
    {
        var list = new List<string>();
        var p = 0;
        while (Token(t, ref p, out var quoted, out var raw) is string tok)
        {
            if (!quoted && tok != "{" && tok != "}") return null;
            list.Add(quoted ? "\"" + raw + "\"" : tok);
        }
        return list;
    }

    static string Token(string t, ref int p, out bool quoted, out string raw)
    {
        quoted = false;
        raw = null;
        while (p < t.Length)
        {
            if (char.IsWhiteSpace(t[p])) { p++; continue; }
            if (t[p] == '/' && p + 1 < t.Length && t[p + 1] == '/') { while (p < t.Length && t[p] != '\n') p++; continue; }
            break;
        }
        if (p >= t.Length) return null;
        if (t[p] == '{' || t[p] == '}') return t[p++].ToString();
        if (t[p] == '"')
        {
            p++;
            var start = p;
            var sb = new StringBuilder();
            while (p < t.Length && t[p] != '"')
            {
                if (t[p] == '\\' && p + 1 < t.Length) { sb.Append(t[p + 1]); p += 2; continue; }
                sb.Append(t[p++]);
            }
            quoted = true;
            raw = t[start..Math.Min(p, t.Length)];
            p++;
            return sb.ToString();
        }
        var s = p;
        while (p < t.Length && !char.IsWhiteSpace(t[p]) && t[p] != '{' && t[p] != '}') p++;
        return t[s..p];
    }

    public string Write(int depth = 0)
    {
        var sb = new StringBuilder();
        var tab = new string('\t', depth);
        foreach (var (k, v) in Items)
        {
            if (v is VdfNode n)
            {
                sb.Append(tab).Append('"').Append(Esc(k)).Append("\"\n");
                sb.Append(tab).Append("{\n").Append(n.Write(depth + 1)).Append(tab).Append("}\n");
            }
            else sb.Append(tab).Append('"').Append(Esc(k)).Append("\"\t\t\"").Append(Esc((string)v)).Append("\"\n");
        }
        return sb.ToString();
    }

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
