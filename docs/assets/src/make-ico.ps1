# Makes app.ico (16 to 256 px) and icon.png from a 512x512 render of logo.svg. Called by render.cmd.
param($src, $outDir)
Add-Type -AssemblyName System.Drawing
$big = [System.Drawing.Bitmap]::FromFile($src)
$crop = $big.Clone((New-Object System.Drawing.Rectangle 0,0,512,512), $big.PixelFormat)
$pngs = @{}
foreach ($s in 256,64,48,40,32,24,20,16) {
  $bmp = New-Object System.Drawing.Bitmap $s,$s,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
  $g.DrawImage($crop, 0, 0, $s, $s); $g.Dispose()
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs[$s] = $ms.ToArray()
  if ($s -eq 256) { [IO.File]::WriteAllBytes("$outDir\icon.png", $pngs[$s]) }
}
$sizes = 16,20,24,32,40,48,64,256
$fs = [IO.File]::Create("$outDir\app.ico"); $w = New-Object IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
foreach ($s in $sizes) { $d = $pngs[$s]; $b = [byte]($s % 256)
  $w.Write($b); $w.Write($b); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$d.Length); $w.Write([uint32]$off); $off += $d.Length }
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Close(); 'ok'
