# Testet: Sounds mit/ohne Stimmeffekt (Mitte/Seite-Trick) sowie eigene Ordner fuer Stimmen und Sounds.
# Arbeitet mit einer Kopie der Einstellungen (--settings), sichert voice.txt, raeumt Testdateien wieder auf.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$probe = Join-Path $repo 'tools\AudioProbe\bin\out\AudioProbe.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"   # wird nur gelesen, nie veraendert
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"     # eigene Kopie fuer den Test
$cfg = 'C:\Program Files\EqualizerAPO\config'
$voices = Join-Path $cfg 'rp-voices'
$tmp = Join-Path $env:TEMP 'rpvs-soundtest'
$realHash = (Get-FileHash $realSettings).Hash
Copy-Item $realSettings $settings -Force
Copy-Item (Join-Path $cfg 'voice.txt') "$env:TEMP\voice.testbackup" -Force
New-Item -ItemType Directory -Force $tmp | Out-Null

Add-Type -Namespace T3 -Name U -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);'
function Key($vk, [switch]$Up) { [T3.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
function Combo([int[]]$vks) { foreach ($k in $vks) { Key $k }; Start-Sleep -m 60; [array]::Reverse($vks); foreach ($k in $vks) { Key $k -Up }; Start-Sleep -m 700 }
function Check($name, $ok) { "{0,-66} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Peak([int[]]$combo) {
    $log = Join-Path $tmp 'probe.txt'
    Start-Sleep 3   # Nachklang des vorherigen Sounds abwarten
    $pr = Start-Process $probe -ArgumentList '"CABLE Output"', '1800' -RedirectStandardOutput $log -PassThru -NoNewWindow
    Start-Sleep -m 500; Combo $combo; $pr.WaitForExit()
    # Die ersten 300 ms enthalten noch Restpuffer vom Aufnahmestart - erst danach wird der Sound ausgeloest
    (Get-Content $log | Where-Object { $_ -match '^\s*(\d+) ms\s+([\d,\.]+)' -and [int]$Matches[1] -gt 300 } | ForEach-Object { [double]($Matches[2] -replace ',', '.') } | Measure-Object -Maximum).Maximum
}
function Start-App { Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600; $p = Start-Process $exe -ArgumentList '--minimized', '--settings', "`"$settings`"" -PassThru; Start-Sleep 4; $p }
function Set-TestSettings($extra) {
    $j = Get-Content $settings -Raw | ConvertFrom-Json
    $p0 = $j.Profiles[0]
    $p0.IncludeAllVoices = $true
    $p0.Hotkeys = [pscustomobject]@{ '00-normal.txt' = 'Control, Alt|F8'; '06-metallhelm.txt' = 'Control, Alt|F9'; '99-teststumm.txt' = 'Control, Alt|F10' }
    $p0.SoundHotkeys = [pscustomobject]@{ 'Messingglocke.wav' = 'Control, Alt|F11'; 'Ringglocke.wav' = 'Control, Alt|F12' }
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    $j | Add-Member SoundsWithEffect @('Ringglocke.wav') -Force
    $j | Add-Member SoundMonitor $false -Force
    $j | Add-Member SoundVolume 0.8 -Force   # feste Lautstaerke, unabhaengig vom Regler des Nutzers
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
$C = 0x11; $A = 0x12; $F8 = 0x77; $F9 = 0x78; $F10 = 0x79; $F11 = 0x7A; $F12 = 0x7B
$stumm = "# Name: Teststumm`r`nPreamp: -60 dB`r`nChannel: all`r`nPreamp: -20 dB`r`n"

try {
    # ---------- Standard-Ordner ----------
    Set-Content (Join-Path $voices '99-teststumm.txt') $stumm -NoNewline
    $p = Start-App; Stop-Process -Id $p.Id; Start-Sleep -m 400     # Migration/Start-Sounds
    # Eigener Sound-Ordner mit frischen Start-Sounds – unabhaengig davon, was im echten Ordner liegt
    Set-TestSettings @{ VoicesFolder = $null; SoundFolder = (Join-Path $tmp 'StdSounds') }
    $p = Start-App

    "=== Stimme Normal ==="
    Combo @($C, $A, $F8)
    $dryN = Peak @($C, $A, $F11); $wetN = Peak @($C, $A, $F12)
    Check ("Glocke ohne Effekt hoerbar ({0:N2})" -f $dryN) ($dryN -gt 0.1)
    Check ("Ringglocke mit Effekt hoerbar ({0:N2})" -f $wetN) ($wetN -gt 0.1)

    "=== Teststimme: stumm, auch mit 'Channel: all' ==="
    Combo @($C, $A, $F10)
    $dry = Peak @($C, $A, $F11); $wet = Peak @($C, $A, $F12)
    Check ("Glocke OHNE Effekt bleibt hoerbar ({0:N2}, normal {1:N2})" -f $dry, $dryN) ($dry -gt 0.7 * $dryN)
    Check ("Ringglocke MIT Effekt wird stumm ({0:N3})" -f $wet) ($wet -lt 0.02)

    "=== Metallhelm (Filter nach 'Channel: all') ==="
    Combo @($C, $A, $F9)
    $dryM = Peak @($C, $A, $F11)
    Check ("Glocke ohne Effekt unveraendert ({0:N2}, normal {1:N2})" -f $dryM, $dryN) ([Math]::Abs($dryM - $dryN) -lt 0.1 * $dryN)
    $orig = Get-Content (Join-Path $voices '06-metallhelm.txt') -Raw
    Check 'Original-Stimmdatei unveraendert (Channel: all noch drin)' ($orig -match 'Channel: all')
    Check 'Aktive Kopie bereinigt (Channel: L R)' ((Get-Content (Join-Path $cfg 'voice-active.cfg') -Raw) -match 'Channel: L R')
    Stop-Process -Id $p.Id

    "=== Eigene Ordner ==="
    $myVoices = Join-Path $tmp 'Stimmen'; $mySounds = Join-Path $tmp 'Sounds'
    New-Item -ItemType Directory -Force $myVoices | Out-Null
    Copy-Item (Join-Path $voices '*.txt') $myVoices
    Remove-Item (Join-Path $voices '99-teststumm.txt')
    Set-TestSettings @{ VoicesFolder = $myVoices; SoundFolder = $mySounds }
    $p = Start-App
    Check 'Start-Sounds im eigenen Sound-Ordner angelegt' (Test-Path (Join-Path $mySounds 'Messingglocke.wav'))
    Combo @($C, $A, $F10)
    Check 'voice.txt zeigt auf Stimme aus eigenem Ordner' ((Get-Content (Join-Path $cfg 'voice-active.cfg') -Raw) -match [regex]::Escape($myVoices))
    $dryC = Peak @($C, $A, $F11); $wetC = Peak @($C, $A, $F12)
    Check ("Eigener Ordner: Effekt wirkt (Ringglocke stumm {0:N3}, Glocke {1:N2})" -f $wetC, $dryC) (($wetC -lt 0.02) -and ($dryC -gt 0.1))
    Combo @($C, $A, $F8)
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                     OK" }
    Remove-Item (Join-Path $voices '99-teststumm.txt') -EA SilentlyContinue
    Remove-Item $tmp -Recurse -Force -EA SilentlyContinue
    Copy-Item "$env:TEMP\voice.testbackup" (Join-Path $cfg 'voice.txt') -Force
}
