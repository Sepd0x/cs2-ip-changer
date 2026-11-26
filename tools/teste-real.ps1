# The automatic test run on the PC that plays CS2 (publish/plano-1-0-0/20-testes-automaticos.md): the code, the exe of this checkout,
# the admin tests and the real app driven by UI Automation, with the phone plugged in. One UAC prompt. Everything goes to
# publish/testes-auto/<date>/ (relatorio.md, relatorio.html, capturas/, the logs and the exe).
#   powershell -ExecutionPolicy Bypass -File tools\teste-real.ps1             phases 0 to 3, without the game
#   powershell -ExecutionPolicy Bypass -File tools\teste-real.ps1 -Jogo       also Join a server in CS2 (left after 20 s)
#   powershell -ExecutionPolicy Bypass -File tools\teste-real.ps1 -SoCodigo   phases 0 and 1 only, no UAC
# It never types or reads sign-in secrets, changes no Steam profiles, CS2 configs or accounts, sends no stats (Turn off at the first start), uses its own data
# folder in C:\Program Files\Pickroute teste, and ends with --restore and a check that no Firewall rule of the app is left.
param([switch]$Jogo, [switch]$SoCodigo, [switch]$Site, [string]$Out, [string]$Exe, [switch]$Elevado)
$ErrorActionPreference = 'Stop'
$Repo = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $Repo ('publish\testes-auto\' + (Get-Date -Format 'yyyy-MM-dd_HHmm')) }
New-Item -ItemType Directory -Force -Path $Out, (Join-Path $Out 'capturas') | Out-Null
$Res = Join-Path $Out 'resultados.jsonl'
$Prefixes = 'Pickroute - *', 'CS2 IP Changer - *', 'ServerRouteTool v9*'

function Note([string]$fase, [string]$passo, [string]$estado, [string]$detalhe = '', [string]$captura = '') {
  $o = [ordered]@{ fase = $fase; passo = $passo; estado = $estado; detalhe = $detalhe; captura = $captura; hora = (Get-Date -Format 'HH:mm:ss') }
  Add-Content -Path $Res -Value (ConvertTo-Json $o -Compress) -Encoding UTF8
  Write-Host ('{0,-8} {1}  {2}' -f $estado, $passo, $detalhe)
}

# A process with its output in files and no wait on its children (the test suite leaves adb running, and adb holds a pipe)
function Run-Logged([string]$name, [string]$file, [string]$argline, [hashtable]$envs = @{}) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo $file, $argline
  $psi.WorkingDirectory = $Repo; $psi.UseShellExecute = $false
  foreach ($k in $envs.Keys) { $psi.EnvironmentVariables[$k] = $envs[$k] }
  $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
  $p = New-Object System.Diagnostics.Process; $p.StartInfo = $psi
  $log = Join-Path $Out "$name.log"
  $sb = New-Object System.Text.StringBuilder
  $handler = { if ($EventArgs.Data -ne $null) { [void]$Event.MessageData.AppendLine($EventArgs.Data) } }
  $e1 = Register-ObjectEvent $p OutputDataReceived -Action $handler -MessageData $sb
  $e2 = Register-ObjectEvent $p ErrorDataReceived -Action $handler -MessageData $sb
  [void]$p.Start(); $p.BeginOutputReadLine(); $p.BeginErrorReadLine(); $p.WaitForExit()
  Start-Sleep -Milliseconds 300
  Unregister-Event -SourceIdentifier $e1.Name; Unregister-Event -SourceIdentifier $e2.Name
  [System.IO.File]::WriteAllText($log, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
  return @{ Code = $p.ExitCode; Text = $sb.ToString(); Log = $log }
}

function Phase-Code {
  $b = Run-Logged 'build' 'dotnet' 'build src/ServerRouteTool.csproj -c Release'
  Note '0 Codigo' 'Build Release com 0 avisos' $(if ($b.Code -eq 0 -and $b.Text -match '\b0 Warning\(s\)') { 'OK' } else { 'FALHA' }) (Split-Path -Leaf $b.Log)
  $t = Run-Logged 'testes' 'dotnet' 'run --project tests' @{ CI = 'true' }
  $last = ($t.Text.Trim() -split "`n")[-1].Trim()
  Note '0 Codigo' 'Testes da app (dotnet run --project tests)' $(if ($last -eq 'TUDO OK') { 'OK' } else { 'FALHA' }) $last
  $w = Run-Logged 'worker' 'node' '--test "worker/stats/test/*.test.mjs"'
  $wf = [regex]::Match($w.Text, 'fail (\d+)').Groups[1].Value; $wp = [regex]::Match($w.Text, 'pass (\d+)').Groups[1].Value
  Note '0 Codigo' 'Testes do Worker' $(if ($w.Code -eq 0 -and $wf -eq '0') { 'OK' } else { 'FALHA' }) "$wp passam, $wf falham"
  if ($Site) {
    $s = Run-Logged 'site' 'node' '--test "tools/*.test.mjs"'
    $sf = [regex]::Match($s.Text, 'fail (\d+)').Groups[1].Value; $sp = [regex]::Match($s.Text, 'pass (\d+)').Groups[1].Value
    Note '0 Codigo' 'Testes do site' $(if ($s.Code -eq 0 -and $sf -eq '0') { 'OK' } else { 'FALHA' }) "$sp passam, $sf falham"
  }
  $p = Run-Logged 'publish' 'dotnet' "publish src/ServerRouteTool.csproj -c Release -o `"$Out\exe`""
  $exe = Join-Path $Out 'exe\CS2IPChanger.exe'
  if ($p.Code -eq 0 -and (Test-Path $exe)) {
    $sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    Set-Content -Path (Join-Path $Out 'exe\SHA256.txt') -Value "$sha  CS2IPChanger.exe" -Encoding ASCII
    Note '1 Exe' 'Exe final para testar' 'OK' ("exe\CS2IPChanger.exe, {0:N1} MB, SHA-256 {1}" -f ((Get-Item $exe).Length / 1MB), $sha)
  } else { Note '1 Exe' 'Exe final para testar' 'FALHA' (Split-Path -Leaf $p.Log) }
  return $exe
}

# ---------- UI Automation ----------
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class W32 { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); }
'@
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Cond($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }
function ById($root, [string]$id) { if ($root) { $root.FindFirst($TS::Descendants, (Cond $AE::AutomationIdProperty $id)) } }
function ByName($root, [string]$name) { if ($root) { $root.FindFirst($TS::Descendants, (Cond $AE::NameProperty $name)) } }
function Text($el) { if ($el) { try { $el.Current.Name } catch { '' } } else { '' } }
function Click($el) {
  if (-not $el) { return $false }
  $pat = $null
  if ($el.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pat)) { $pat.Invoke(); return $true }
  if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) { $pat.Select(); return $true }
  if ($el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { $pat.Toggle(); return $true }
  return $false
}
function Toggled($el) { $pat = $null; if ($el -and $el.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pat)) { return $pat.Current.ToggleState -eq 'On' }; return $false }
function Wait-For([scriptblock]$test, [int]$seconds) {
  $t0 = Get-Date
  while (((Get-Date) - $t0).TotalSeconds -lt $seconds) { $r = & $test; if ($r) { return $r }; Start-Sleep -Milliseconds 400 }
  return $null
}
function Windows-Of([int]$procId) { $AE::RootElement.FindAll($TS::Children, (Cond $AE::ProcessIdProperty $procId)) }
$script:Shot = 0
function Shot($win, [string]$name) {
  if (-not $win) { return '' }
  try {
    $h = [IntPtr]$win.Current.NativeWindowHandle; [void][W32]::ShowWindow($h, 9); [void][W32]::SetForegroundWindow($h); Start-Sleep -Milliseconds 500
    $r = $win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size); $g.Dispose()
    $script:Shot++; $file = 'capturas\{0:D2}-{1}.png' -f $script:Shot, $name
    $bmp.Save((Join-Path $Out $file), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); return $file
  } catch { return '' }
}

$AppDir = Join-Path $env:ProgramFiles 'Pickroute teste'
$script:App = $null; $script:Win = $null

function Start-App([hashtable]$envs = @{}) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo (Join-Path $AppDir 'CS2IPChanger.exe')
  $psi.UseShellExecute = $false; $psi.WorkingDirectory = $AppDir
  foreach ($k in $envs.Keys) { $psi.EnvironmentVariables[$k] = $envs[$k] }
  $script:App = [System.Diagnostics.Process]::Start($psi)
  $script:Win = Wait-For {
    foreach ($w in (Windows-Of $script:App.Id)) {
      $off = ByName $w 'Turn off'; $acc = ByName $w 'Accept'
      if ($off -and $acc) { [void](Click $off); continue }   # the stats notice: nothing is sent
      if (ById $w 'NavRegions') { return $w }
    }
    $null
  } 90
  if ($script:Win) {
    Start-Sleep 2
    foreach ($id in 'DoorLaterBtn', 'TourSkipBtn') { $b = ById $script:Win $id; if ($b -and -not $b.Current.IsOffscreen) { [void](Click $b) } }
  }
  return $script:Win
}

function Close-App([string]$answer) {
  if (-not $script:App -or $script:App.HasExited) { return $true }
  $pat = $null
  if ($script:Win.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$pat)) { $pat.Close() }
  if ($answer) { $b = Wait-For { foreach ($w in (Windows-Of $script:App.Id)) { $x = ByName $w $answer; if ($x) { return $x } }; $null } 20; [void](Click $b) }
  $gone = Wait-For { $script:App.Refresh(); $script:App.HasExited } 90
  return [bool]$gone
}

function State { Text (ById $script:Win 'SideState') }
function Rules { @(Get-NetFirewallRule -DisplayName $Prefixes -ErrorAction SilentlyContinue).Count }
function Page([string]$nav) { [void](Click (ById $script:Win $nav)); Start-Sleep -Milliseconds 900 }

function Phase-Admin {
  $a = Run-Logged 'testes-admin' 'dotnet' "run --project tests -- --admin `"$Out\testes-admin.txt`""
  $last = if (Test-Path "$Out\testes-admin.txt") { (Get-Content "$Out\testes-admin.txt" -Encoding UTF8 | Select-Object -Last 1) } else { 'sem ficheiro' }
  Note '2 Admin' 'Testes --admin (regras e rotas de teste)' $(if ($last -eq 'TUDO OK') { 'OK' } else { 'FALHA' }) $last
}

function Phase-App([string]$exe) {
  if (Get-Process CS2IPChanger -ErrorAction SilentlyContinue) { Note '3 App' 'Arranque' 'SALTADO' 'a app ja esta aberta (instancia unica): fecha-a e corre outra vez'; return }
  New-Item -ItemType Directory -Force -Path $AppDir | Out-Null
  $data = Join-Path $AppDir 'SRT_Dados'
  if (Test-Path $data) { Rename-Item $data ('SRT_Dados.' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }   # kept, never deleted
  Copy-Item $exe (Join-Path $AppDir 'CS2IPChanger.exe') -Force
  $mine = Join-Path $env:ProgramFiles 'CS2 IP Changer\SRT_Dados\servers.json'
  if (Test-Path $mine) { New-Item -ItemType Directory -Force -Path $data | Out-Null; Copy-Item $mine $data }   # his server list, a copy
  $rulesBefore = Rules

  try {
    # SMOKE 1
    if (-not (Start-App)) { Note '3 App' 'SMOKE 1: arranque' 'FALHA' 'a janela principal nao apareceu em 90 s'; return }
    $s = Wait-For { $x = State; if ($x -and $x -notmatch 'Checking') { $x } } 30
    Note '3 App' 'SMOKE 1: arranque com a protecao desligada' $(if ($s -and $s -notmatch '^Protected') { 'OK' } else { 'FALHA' }) "topo: $s" (Shot $script:Win 'arranque')
    $phone = Wait-For { $x = Text (ById $script:Win 'PhoneState'); if ($x -and $x -notmatch 'Searching') { $x } } 30
    $noPhone = (-not $phone) -or $phone -match 'No phone|not found|Plug'

    # SMOKE 5 (privacy, no account touched)
    Page 'NavSettings'
    $opt = ById $script:Win 'OptStreamer'
    if ($opt) {
      $before = Toggled $opt; [void](Click $opt); Start-Sleep 1; $mid = Toggled $opt; Page 'NavAccounts'; $cap = Shot $script:Win 'contas-nomes-escondidos'
      Page 'NavSettings'; [void](Click (ById $script:Win 'OptStreamer')); Start-Sleep 1; $after = Toggled (ById $script:Win 'OptStreamer')
      Note '3 App' 'SMOKE 5: Hide usernames liga e desliga' $(if ($mid -ne $before -and $after -eq $before) { 'OK' } else { 'FALHA' }) "antes $before, ligado $mid, depois $after" $cap
    } else { Note '3 App' 'SMOKE 5: Hide usernames' 'FALHA' 'OptStreamer nao encontrado' }

    if ($noPhone) {
      Note '3 App' 'SMOKE 2, 3, 8, 9 e 17' 'SALTADO' "sem telemovel ligado ($phone)" (Shot $script:Win 'sem-telemovel')
    } else {
      # SMOKE 2
      Page 'NavNet'
      [void](Click (ById $script:Win 'TurnOnBtn'))
      $on = Wait-For { $x = State; if ($x -match '^Protected') { $x } } 90
      Note '3 App' 'SMOKE 2: ligar a protecao com o telemovel' $(if ($on) { 'OK' } else { 'FALHA' }) "topo: $(State) / telemovel: $phone" (Shot $script:Win 'protegido')
      if ($on) {
        # SMOKE 3
        $ip0 = Wait-For { $x = Text (ById $script:Win 'EgressIp'); if ($x -match '^\d+\.\d+\.\d+\.\d+$') { $x } } 30
        $t0 = Get-Date; [void](Click (ById $script:Win 'ChangeIpBtn'))
        $ip1 = Wait-For { $x = Text (ById $script:Win 'EgressIp'); if ($x -match '^\d+\.\d+\.\d+\.\d+$' -and $x -ne $ip0) { $x } } 150
        Note '3 App' 'SMOKE 3: Change IP da um IP novo' $(if ($ip1) { 'OK' } else { 'FALHA' }) ("{0} > {1} em {2:N0} s" -f $ip0, $ip1, ((Get-Date) - $t0).TotalSeconds) (Shot $script:Win 'ip-novo')
        # SMOKE 4
        Page 'NavServers'
        if (Test-Path (Join-Path $data 'servers.json')) {
          [void](Click (ById $script:Win 'QueryBtn')); Start-Sleep 20
          $list = ById $script:Win 'ServerList'
          $rows = if ($list) { $list.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition).Count } else { 0 }
          Note '3 App' 'SMOKE 4: Servers > Refresh all' $(if ($rows -gt 0) { 'OK' } else { 'FALHA' }) "$rows linhas" (Shot $script:Win 'servidores')
          if ($Jogo -and $rows -gt 0) {
            $join = ByName $list 'Join'
            if (Click $join) {
              $cs2 = Wait-For { Get-Process cs2 -ErrorAction SilentlyContinue } 150
              if ($cs2) { Start-Sleep 40; $full = Join-Path $Out ('capturas\{0:D2}-cs2.png' -f (++$script:Shot))
                $sb = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp = New-Object System.Drawing.Bitmap $sb.Width, $sb.Height
                $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size); $g.Dispose(); $bmp.Save($full); $bmp.Dispose()
                [void]$cs2.CloseMainWindow(); if (-not (Wait-For { $cs2.Refresh(); $cs2.HasExited } 30)) { Stop-Process -Id $cs2.Id } }
              Note '3 App' 'SMOKE 4: Join abre o CS2 e entra pelo telemovel' $(if ($cs2) { 'OK' } else { 'FALHA' }) 'o CS2 fechado ao fim de 40 s' ($(if ($cs2) { 'capturas\' + (Split-Path -Leaf $full) } else { '' }))
            } else { Note '3 App' 'SMOKE 4: Join' 'FALHA' 'botao Join nao encontrado' }
          }
        } else { Note '3 App' 'SMOKE 4: Servers' 'SALTADO' 'sem servers.json na app instalada para copiar' }
        # SMOKE 8
        Page 'NavNet'; [void](Click (ById $script:Win 'TurnOffBtn'))
        $off = Wait-For { $x = State; if ($x -and $x -notmatch '^Protected') { $x } } 60
        Note '3 App' 'SMOKE 8: desligar a protecao' $(if ($off) { 'OK' } else { 'FALHA' }) "topo: $(State)" (Shot $script:Win 'desligado')
        # SMOKE 9
        [void](Click (ById $script:Win 'TurnOnBtn')); [void](Wait-For { (State) -match '^Protected' } 90)
        $closed = Close-App 'Turn off and close'
        Start-Sleep 3; $left = Rules
        Note '3 App' 'SMOKE 9: fechar com a protecao ligada e tirar tudo' $(if ($closed -and $left -le $rulesBefore) { 'OK' } else { 'FALHA' }) "fechou: $closed, regras da app na Firewall: $left (antes: $rulesBefore)"
        # SMOKE 17
        if (Start-App) {
          [void](Click (ById $script:Win 'TurnOnBtn')); $on2 = Wait-For { (State) -match '^Protected' } 90
          $kept = Close-App 'Keep and close'; $during = Rules
          $r = Start-Process -FilePath (Join-Path $AppDir 'CS2IPChanger.exe') -ArgumentList '--restore' -PassThru -WindowStyle Hidden
          [void]$r.WaitForExit(120000); Start-Sleep 2; $after = Rules
          Note '3 App' 'SMOKE 17: --restore depois de fechar com Keep' $(if ($on2 -and $during -gt 0 -and $after -le $rulesBefore) { 'OK' } else { 'FALHA' }) "regras com Keep: $during, depois do --restore: $after"
        }
      }
    }
    if ($script:App -and -not $script:App.HasExited) { [void](Close-App 'Turn off and close') }

    # SMOKE 14: every page in each language, for cut text
    foreach ($lang in 'en', 'pt-BR', 'es', 'ru', 'tr') {
      if (-not (Start-App @{ SRT_LANG = $lang })) { Note '3 App' "SMOKE 14: $lang" 'FALHA' 'a janela nao abriu'; continue }
      $caps = foreach ($nav in 'NavNet', 'NavServers', 'NavRegions', 'NavAccounts', 'NavConfigs', 'NavProfiles', 'NavLog', 'NavSettings') { Page $nav; Shot $script:Win "$lang-$($nav.Substring(3).ToLower())" }
      [void](Close-App 'Turn off and close')
      Note '3 App' "SMOKE 14: as 8 paginas em $lang" 'VER' 'capturas para procurar texto cortado ou ingles' ($caps -join ';')
    }
  } catch {
    Note '3 App' 'Erro' 'FALHA' $_.Exception.Message
  } finally {
    if ($script:App -and -not $script:App.HasExited) { try { $script:App.Kill() } catch {} }
    $r = Start-Process -FilePath (Join-Path $AppDir 'CS2IPChanger.exe') -ArgumentList '--restore' -PassThru -WindowStyle Hidden
    [void]$r.WaitForExit(120000); Start-Sleep 2
    $end = Rules
    Note '3 App' 'Fim: --restore e Firewall sem regras da app' $(if ($end -le $rulesBefore) { 'OK' } else { 'FALHA' }) "regras da app: $end (antes do teste: $rulesBefore)"
  }
}

function Report {
  $rows = @(Get-Content $Res -Encoding UTF8 | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
  $n = @{}; foreach ($r in $rows) { $n[$r.estado] = 1 + [int]$n[$r.estado] }
  $sum = ($n.Keys | Sort-Object | ForEach-Object { "$($n[$_]) $_" }) -join ', '
  $md = New-Object System.Collections.Generic.List[string]
  $md.Add("# Teste automatico, $(Get-Date -Format 'yyyy-MM-dd HH:mm')"); $md.Add('')
  $md.Add("Repo em $(git -C $Repo rev-parse --short HEAD). Resultado: $sum."); $md.Add('')
  $md.Add('| Fase | Passo | Estado | Detalhe |'); $md.Add('|---|---|---|---|')
  foreach ($r in $rows) { $md.Add("| $($r.fase) | $($r.passo) | **$($r.estado)** | $(($r.detalhe -replace '\|', '/')) |") }
  $md.Add(''); $md.Add('## Fica a mao (docs/SMOKE.md)')
  $md.Add('10 e 18 atualizacao a partir de uma versao instalada; 11 e 16 primeiro arranque em Program Files e SmartScreen; 12 e 13 os passos do telemovel no ecra dele; 15 um ban a serio; 19 crash; 20 e 21 site e README depois do push. Com -Jogo nao: 5 (Log in noutra conta) e 6 (atalho New IP + next account).')
  [System.IO.File]::WriteAllLines((Join-Path $Out 'relatorio.md'), $md, (New-Object System.Text.UTF8Encoding $false))
  $esc = { param($s) [System.Net.WebUtility]::HtmlEncode([string]$s) }
  $html = "<!doctype html><meta charset=utf-8><title>Teste automatico</title><style>body{font:14px Segoe UI,sans-serif;background:#0e0c14;color:#eeecf3;margin:24px}td,th{padding:6px 10px;border-bottom:1px solid #302e35;text-align:left;vertical-align:top}.OK{color:#46c58a}.FALHA{color:#ef6461}.SALTADO,.VER{color:#f0a948}img{max-width:620px;display:block;margin:6px 0;border:1px solid #302e35}</style><h1>Teste automatico</h1><p>$(& $esc $sum)</p><table>"
  foreach ($r in $rows) {
    $imgs = ($r.captura -split ';' | Where-Object { $_ } | ForEach-Object { "<img src='$($_ -replace '\\', '/')' loading=lazy>" }) -join ''
    $html += "<tr><td>$(& $esc $r.fase)</td><td>$(& $esc $r.passo)</td><td class='$($r.estado)'>$($r.estado)</td><td>$(& $esc $r.detalhe)$imgs</td></tr>"
  }
  $html += '</table>'
  [System.IO.File]::WriteAllText((Join-Path $Out 'relatorio.html'), $html, (New-Object System.Text.UTF8Encoding $false))
  Write-Host "`nRelatorio: $(Join-Path $Out 'relatorio.md')  ($sum)"
}

if ($Elevado) {
  try { Phase-Admin; Phase-App $Exe } catch { Note 'admin' 'Erro' 'FALHA' $_.Exception.Message }
  exit 0
}
$exe = Phase-Code
if (-not $SoCodigo -and (Test-Path $exe)) {
  Write-Host 'A pedir o UAC para os testes --admin e a app real...'
  $a = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Elevado -Out `"$Out`" -Exe `"$exe`""
  if ($Jogo) { $a += ' -Jogo' }
  try { $p = Start-Process powershell -Verb RunAs -ArgumentList $a -PassThru; $p.WaitForExit() }
  catch { Note '2 Admin' 'UAC' 'SALTADO' 'o UAC foi recusado' }
}
Report
