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

    static VdfNode ParseBody(string t, ref int p)
    {
        var node = new VdfNode();
        while (true)
        {
            var key = Token(t, ref p);
            if (key == null || key == "}") return node;
            var val = Token(t, ref p);
            if (val == "{") node.Items.Add(new(key, ParseBody(t, ref p)));
            else if (val != null) node.Items.Add(new(key, val));
            else return node;
        }
    }

    static string Token(string t, ref int p)
    {
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
            var sb = new StringBuilder();
            while (p < t.Length && t[p] != '"')
            {
                if (t[p] == '\\' && p + 1 < t.Length) { sb.Append(t[p + 1]); p += 2; continue; }
                sb.Append(t[p++]);
            }
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
