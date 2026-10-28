# Toca os 5 sons dos comandos, por ordem, com o nome de cada um
$dir = Join-Path $PSScriptRoot '..\src\Assets\Sounds'
foreach ($n in 'start', 'done', 'failed', 'busy', 'ask') {
    Write-Host $n
    (New-Object System.Media.SoundPlayer (Join-Path $dir "$n.wav")).PlaySync()
    Start-Sleep -Milliseconds 700
}
