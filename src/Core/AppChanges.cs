namespace SRT.Core;

/// <summary>
/// Log > What the app changed (beta.11, simulated users: "the Checks say 2 IPs blocked and never which ones"): the Firewall rules
/// with what they block, the routes, the adapter metric, and the command that undoes all of it. Only read, never changes anything.
/// </summary>
public static class AppChanges
{
    const int MaxIps = 12;

    public static string RestoreCommand(string exe) => $"\"{exe}\" {AppIds.Args.Restore}";

    /// <summary>The addresses of a rule, short: a few IPs, or how many ranges ("*" is everything).</summary>
    public static string Blocks(string remote)
    {
        if (string.IsNullOrWhiteSpace(remote) || remote == "*") return "everything outside the phone";
        var parts = remote.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith("/255.255.255.255") ? p[..^16] : p).ToList();
        if (parts.Any(p => p.Contains('-') || p.Contains('/'))) return Fmt.Count(parts.Count, "address range");
        return string.Join(", ", parts.Take(MaxIps)) + (parts.Count > MaxIps ? $" and {parts.Count - MaxIps} more" : "");
    }

    public static string Text(IReadOnlyList<Firewall.RuleInfo> rules, IReadOnlyList<string> routes, int metricIf, Func<int, string> adapterName, string exe,
        IReadOnlyList<string> other = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(rules.Count == 0 ? "Firewall: no rules from the app." : "Firewall rules (Windows Defender Firewall, outbound block):");
        foreach (var r in rules.OrderBy(r => r.Name))
            sb.AppendLine($"• {Firewall.Meaning(r.Name)}. Blocks: {Blocks(r.Remote)}.\n   Rule name: {r.Name}");
        // 2nd round (Hugo, Ines): the old names looked like leftovers
        if (rules.Count > 0) sb.AppendLine("The rule names are the same since the first version, so every version finds and removes them.");
        if (other is { Count: > 0 })
        {
            sb.AppendLine();
            foreach (var o in other) sb.AppendLine("• " + o);
        }
        sb.AppendLine();
        sb.AppendLine(routes.Count == 0 ? "Routes: none from the app." : "Routes (gone after a restart):");
        foreach (var key in routes.OrderBy(k => k))
        {
            var p = key.Split('|');
            if (p.Length != 3 || !int.TryParse(p[1], out var idx)) continue;
            sb.AppendLine($"• {p[0]} through {adapterName(idx) ?? "interface " + idx}" + (p[2] is "0.0.0.0" ? "" : $" ({p[2]})"));
        }
        if (metricIf >= 0) sb.AppendLine($"• Metric of {adapterName(metricIf) ?? "interface " + metricIf} raised, so the rest of the PC stays at home (gone after a restart).");
        sb.AppendLine();
        sb.Append("To undo all of it, close the app and run as admin:\n" + RestoreCommand(exe));
        return sb.ToString();
    }
}
