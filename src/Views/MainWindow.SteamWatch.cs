using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using SRT.Core;

namespace SRT;

// O Steam e o CS2 vistos de fora: sessão na Valve (pelo registo do Steam) e como o CS2 fecha
public partial class MainWindow
{
    readonly SteamLogWatcher _steamLog = new();
    /// <summary>Hora (ao segundo) da última troca de IP: até o Steam voltar a iniciar sessão depois dela, não se entra.</summary>
    DateTime? _ipChangedAt;
    readonly List<Process> _cs2Watched = new();
    /// <summary>
    /// Depois de um crash dentro de um servidor, esse servidor fica com a sessão antiga. Uns 3 a 4 min depois o Steam
    /// diz-lhe que ela acabou e ele expulsa a conta com "No Steam logon", mesmo que já tenhas voltado a entrar
    /// (27 out.: voltar 2 min depois deu kick; 3 min 25 s depois, não). Até esta hora não se volta ao mesmo servidor.
    /// </summary>
    static readonly TimeSpan AfterCrashWait = TimeSpan.FromMinutes(5);
    DateTime? _cs2CrashAt;
    string _cs2CrashServer;

    /// <summary>O Steam tem sessão na Valve e, se o IP mudou, já a refez pelo IP novo.</summary>
    bool SteamReady => !Steam.IsRunning || (_steamLog.Online != false && (_ipChangedAt == null || _steamLog.LastLogon >= _ipChangedAt));

    void WatchSteamLog()
    {
        foreach (var ev in _steamLog.Poll())
        {
            Diag.Write("steam-log", new { hora = ev.Time.ToString("HH:mm:ss"), tipo = ev.Kind, linha = ev.Line });
            switch (ev.Kind)
            {
                case "caiu":
                    var why = Regex.Match(ev.Line, @"ConnectionDisconnected\('([^']*)'\)").Groups[1].Value;
                    Log.Warn($"O Steam caiu da Valve às {ev.Time:HH:mm:ss} ({why})."
                        + (Steam.Cs2Running ? " Se estavas num servidor, é isto que dá kick com \"No Steam logon\"." : ""));
                    break;
                case "ligou":
                    Log.Ok($"O Steam tem sessão na Valve ({ev.Time:HH:mm:ss}).");
                    break;
                case "recusado":
                    Log.Warn("O Steam não conseguiu iniciar sessão: " + ev.Line);
                    break;
            }
        }
    }

    /// <summary>Espera até <paramref name="seconds"/> s que o Steam fique pronto. false = não ficou.</summary>
    async Task<bool> WaitSteamReady(int seconds)
    {
        WatchSteamLog();
        if (SteamReady) return true;
        ShowProgress("À espera que o Steam volte a ter sessão na Valve…");
        try
        {
            for (var i = seconds; i > 0; i--)
            {
                await Task.Delay(1000);
                WatchSteamLog();
                if (SteamReady) { Log.Ok("O Steam está pronto: já podes entrar sem levar kick."); return true; }
                ProgressText.Text = $"À espera que o Steam volte a ter sessão na Valve… {i} s";
            }
            return false;
        }
        finally { HideProgress(); }
    }

    /// <summary>Depois de o IP mudar: corta as ligações antigas do Steam para ele se religar já, e não daqui a 30 s.</summary>
    void ResetSteamAfterIpChange()
    {
        if (!Steam.IsRunning) return;
        var link = Net.ReadSteamLink();
        var n = Net.ResetLink(link);
        Diag.Write("steam-cortado", new { ligacoes = n, de = link.Keys.Count });
        if (n > 0) Log.Info($"Cortei {n} ligação(ões) antigas do Steam: religa-se já pelo IP novo.");
    }

    // ------------------------------------------------------------ Como o CS2 fecha

    static readonly Dictionary<uint, string> CrashCodes = new()
    {
        [0xC0000005] = "acesso inválido à memória",
        [0xC0000409] = "pilha corrompida",
        [0xC0000374] = "memória (heap) corrompida",
        [0xC000001D] = "instrução inválida",
        [0xC00000FD] = "pilha esgotada",
        [0xC0000094] = "divisão por zero",
        [0x80000003] = "paragem forçada (breakpoint)",
        [0xE06D7363] = "exceção não tratada",
    };

    void WatchCs2Exit()
    {
        foreach (var p in Process.GetProcessesByName("cs2"))
        {
            if (_cs2Watched.Any(w => w.Id == p.Id)) { p.Dispose(); continue; }
            try
            {
                var start = p.StartTime;
                p.EnableRaisingEvents = true;
                p.Exited += (_, _) =>
                {
                    int? code = null;
                    try { code = p.ExitCode; } catch { }
                    Dispatcher.BeginInvoke(() => OnCs2Exit(p, code, start));
                };
                _cs2Watched.Add(p);
            }
            catch (Exception ex) { Log.Warn("Não consigo vigiar o CS2: " + ex.Message); p.Dispose(); }
        }
    }

    void OnCs2Exit(Process p, int? code, DateTime start)
    {
        _cs2Watched.Remove(p);
        p.Dispose();
        var min = (int)(DateTime.Now - start).TotalMinutes;
        var hex = code == null ? "?" : $"0x{(uint)code.Value:X8}";
        var name = code is int c && CrashCodes.TryGetValue((uint)c, out var n) ? n : null;
        Diag.Write("cs2-saiu", new { codigo = code, hex, motivo = name, minutos = min });
        if (code == 0) Log.Info($"O CS2 fechou normalmente ao fim de {min} min.");
        else
        {
            Log.Warn($"O CS2 fechou com erro {hex}{(name != null ? $" ({name})" : "")} ao fim de {min} min. Se foi um crash, exporta o diagnóstico em Registo.");
            if (min >= 1 && _s.LastServer != null)
            {
                _cs2CrashAt = DateTime.Now;
                _cs2CrashServer = _s.LastServer;
                var until = _cs2CrashAt.Value + AfterCrashWait;
                Log.Warn($"O CS2 crashou. Não voltes ao {_cs2CrashServer} antes das {until:HH:mm}: o servidor ainda tem a sessão antiga e dá kick com \"No Steam logon\". Outro servidor pode ser já.");
                Toast($"O CS2 crashou. Volta ao mesmo servidor só depois das {until:HH:mm} (outro pode ser já).", false);
            }
            else Toast($"O CS2 fechou com erro {hex}. Ficou no diagnóstico.", false);
        }
    }

    /// <summary>Se o CS2 crashou há pouco neste servidor, pergunta e (se quiseres) espera. false = não entrar.</summary>
    async Task<bool> WaitAfterCrash(ServerItem s)
    {
        if (_cs2CrashAt == null || s.Key != _cs2CrashServer) return true;
        var until = _cs2CrashAt.Value + AfterCrashWait;
        if (DateTime.Now >= until) return true;
        var wait = await Ask("O CS2 crashou há pouco neste servidor",
            $"O CS2 crashou às {_cs2CrashAt:HH:mm} dentro do {s.Title}. O servidor ainda tem essa sessão e, uns minutos depois, expulsa a conta com \"No Steam logon\", mesmo que já tenhas voltado.\n\n" +
            $"Espero até às {until:HH:mm:ss} e entro sozinho? Outro servidor pode ser já.", "Esperar e entrar", "Entrar já");
        Diag.Write("espera-pos-crash", new { servidor = s.Key, segundos = (int)(until - DateTime.Now).TotalSeconds, esperou = wait });
        if (!wait) return true;
        ShowProgress("À espera que o servidor largue a sessão antiga…");
        try
        {
            while (DateTime.Now < until)
            {
                ProgressText.Text = $"À espera que o servidor largue a sessão antiga… {Fmt.Left(until - DateTime.Now)}";
                await Task.Delay(1000);
            }
        }
        finally { HideProgress(); }
        return true;
    }
}
