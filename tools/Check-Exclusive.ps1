# Prueft am Zielgeraet (Standard: CABLE Output), ob ein Programm es exklusiv oeffnen koennte.
# Gibt aus: Eintrag in den Geraeteeigenschaften + echter Versuch, das Geraet exklusiv zu oeffnen (sofort wieder geschlossen).
param([string]$Device = 'CABLE Output')
$d = Join-Path (Split-Path $PSScriptRoot -Parent) 'publish'
Add-Type -Path (Join-Path $d 'NAudio.Core.dll'); Add-Type -Path (Join-Path $d 'NAudio.Wasapi.dll')
$en = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
$dev = $en.EnumerateAudioEndPoints([NAudio.CoreAudioApi.DataFlow]::Capture, [NAudio.CoreAudioApi.DeviceState]::Active) | Where-Object { $_.FriendlyName -like "*$Device*" } | Select-Object -First 1
if (-not $dev) { "Geraet nicht gefunden"; exit 1 }
$key = New-Object NAudio.CoreAudioApi.PropertyKey ([Guid]'b3f8fa53-0004-438e-9003-51a46e139bfc'), 3
$prop = if ($dev.Properties.Contains($key)) { $dev.Properties[$key].Value } else { '(nicht gesetzt = erlaubt)' }
"Geraet: $($dev.FriendlyName)"
"ID: $($dev.ID)"
"Eigenschaft Exklusiv erlaubt: $prop"
$client = $dev.AudioClient
# Ein Format suchen, das das Geraet exklusiv kann (die Pruefung klappt auch, wenn Exklusiv verboten ist)
$fmt = $null
foreach ($rate in 48000, 44100, 96000) { foreach ($bits in 16, 24, 32) { foreach ($ch in 2, 1) {
    $f = New-Object NAudio.Wave.WaveFormatExtensible $rate, $bits, $ch
    try { if (-not $fmt -and $client.IsFormatSupported([NAudio.CoreAudioApi.AudioClientShareMode]::Exclusive, $f)) { $fmt = $f } } catch { }
} } }
if (-not $fmt) { $fmt = New-Object NAudio.Wave.WaveFormat 48000, 16, 2 }
"Testformat: $fmt"
try {
    $client.Initialize([NAudio.CoreAudioApi.AudioClientShareMode]::Exclusive, [NAudio.CoreAudioApi.AudioClientStreamFlags]::None, 1000000, 1000000, $fmt, [Guid]::Empty)
    "Exklusiv oeffnen: MOEGLICH"
} catch {
    $hr = $_.Exception.InnerException.HResult; if (-not $hr) { $hr = $_.Exception.HResult }
    "Exklusiv oeffnen: abgelehnt (0x{0:X8}{1})" -f $hr, $(if ($hr -eq -2004287474) { ' = vom Nutzer verboten' } else { '' })
}
$client.Dispose()
