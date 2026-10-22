# Testet Wiederholen im Soundboard, gemessen im Kabel: einmal, 3x hintereinander, Endlos-Loop,
# und dass derselbe Shortcut einen laufenden Loop wieder stoppt (plus Button-Text "Stoppen").
# Arbeitet mit einer Kopie der Einstellungen (--settings); Mithoeren ist aus, damit nichts auf den Kopfhoerern landet.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$probe = Join-Path $repo 'tools\AudioProbe\bin\out\AudioProbe.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"
$log = Join-Path $env:TEMP "rpvs-loop-$PID.txt"
$soundDir = Join-Path $env:TEMP "rpvs-loop-sounds-$PID"   # frische Start-Sounds, unabhaengig vom echten Ordner
$realHash = (Get-FileHash $realSettings).Hash
$cfg = 'C:\Program Files\EqualizerAPO\config'
Copy-Item (Join-Path $cfg 'voice.txt') "$env:TEMP\voice.loopbackup" -Force
Copy-Item (Join-Path $cfg 'voice-active.cfg') "$env:TEMP\voice-active.loopbackup" -Force
$mute = Join-Path $cfg 'rp-voices\99-teststumm.txt'
Set-Content $mute "# Name: Teststumm`r`nPreamp: -60 dB`r`n" -NoNewline

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T6 -Name U -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);'
$AE = [Windows.Automation.AutomationElement]
function Key($vk, [switch]$Up) { [T6.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
# Nur global registrierte Kombination (Strg+Alt+F11) - die faengt Windows ab
function Press { Key 0x11; Key 0x12; Key 0x7A; Start-Sleep -m 60; Key 0x7A -Up; Key 0x12 -Up; Key 0x11 -Up }
function Check($name, $ok) { "{0,-66} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }

function Write-TestSettings([int]$repeat) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $p0 = $j.Profiles[0]
    $p0.IncludeAllVoices = $true
    $p0.Hotkeys = [pscustomobject]@{ '00-normal.txt' = 'Control, Alt|F9'; '99-teststumm.txt' = 'Control, Alt|F8' }; $p0.HoldHotkeys = [pscustomobject]@{}
    $p0.SoundHotkeys = [pscustomobject]@{ 'Messingglocke.wav' = 'Control, Alt|F11' }
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    $extra = @{ SoundMonitor = $false; SoundVolume = 0.8; SoundFolder = $soundDir; ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false
                ShowStatusOverlay = $true; StatusOverlayHideNormal = $true; StatusOverlayShowSounds = $true
                SetupWizardDone = $true; SoundsWithEffect = @(); SoundRepeat = $(if ($repeat -eq 1) { [pscustomobject]@{} } else { [pscustomobject]@{ 'Messingglocke.wav' = $repeat } }) }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
# Status-Anzeige = namenloses, kleines Fenster der App
function StatusOverlay { @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq '' -and $_.Current.BoundingRectangle.Width -lt 800 -and -not $_.Current.IsOffscreen } | Select-Object -First 1 }
function Combo([int]$fkey) { Key 0x11; Key 0x12; Key $fkey; Start-Sleep -m 60; Key $fkey -Up; Key 0x12 -Up; Key 0x11 -Up; Start-Sleep -m 800 }
# Teststimme "stumm" schaltet das Mikrofon im Kabel ab – so stoert es die Messung nicht, auch wenn gerade jemand spricht.
# Sounds ohne Stimmeffekt laufen daran vorbei und bleiben voll hoerbar.
function Mute { Combo 0x77 }     # Strg+Alt+F8
function Normal { Combo 0x78 }   # Strg+Alt+F9
function Start-App { Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600; $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5; Mute; $p }

# Nimmt $ms lang am Kabel auf; $actions = @{ Zeitpunkt(ms) = Scriptblock }. Liefert Pegel je 100 ms (Index = Zeit/100).
function Record([int]$ms, [hashtable]$actions) {
    $pr = Start-Process $probe -ArgumentList '"CABLE Output"', "$ms" -RedirectStandardOutput $log -PassThru -NoNewWindow
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $script:ranAt = @{}
    foreach ($t in ($actions.Keys | Sort-Object)) {
        while ($sw.ElapsedMilliseconds -lt $t) { Start-Sleep -m 20 }
        $script:ranAt[$t] = $sw.ElapsedMilliseconds   # tatsaechlicher Zeitpunkt (UI-Abfragen davor koennen verzoegern)
        & $actions[$t]
    }
    $pr.WaitForExit()
    $levels = @{}
    Get-Content $log | Where-Object { $_ -match '^\s*(\d+) ms\s+([\d,\.]+)' } | ForEach-Object { $levels[[int]$Matches[1] / 100] = [double]($Matches[2] -replace ',', '.') }
    $levels
}
function Loud($levels, [int]$fromMs, [int]$toMs, [double]$th = 0.02) {
    @($levels.Keys | Where-Object { $_ * 100 -ge $fromMs -and $_ * 100 -lt $toMs -and $levels[$_] -gt $th }).Count
}
# Wie lange nach dem Start noch Ton kam (letztes lautes Fenster)
function SoundEnd($levels, [int]$startMs, [double]$th = 0.02) {
    $hits = @($levels.Keys | Where-Object { $_ * 100 -gt $startMs + 200 -and $levels[$_] -gt $th } | Sort-Object)
    if ($hits.Count -eq 0) { 0 } else { $hits[-1] * 100 - $startMs }
}

try {
    "=== Einmal ==="
    Write-TestSettings 1
    $p = Start-App
    Start-Sleep 2
    $l = Record 8000 @{ 800 = { Press } }
    $single = SoundEnd $l 800
    Check ("Glocke einmal hoerbar, Dauer ca. {0} ms" -f $single) ($single -gt 300)
    Start-Sleep 1
    $l = Record 5000 @{ 800 = { Press }; 1400 = { Press } }
    $cut = SoundEnd $l 800
    Check ("Einmal-Sound: derselbe Shortcut bricht ab (nach {0} ms statt {1} ms)" -f $cut, $single) ($cut -lt 1100 -and $cut -lt $single - 500)
    Stop-Process -Id $p.Id

    "=== 3x hintereinander ==="
    Write-TestSettings 3
    $p = Start-App
    Start-Sleep 2
    $len = [Math]::Max(9000, [int](3 * $single + 4000))
    $l = Record $len @{ 800 = { Press } }
    $triple = SoundEnd $l 800
    Check ("3x dauert ca. dreimal so lange ({0} ms statt {1} ms)" -f $triple, $single) ($triple -gt 2.4 * $single -and $triple -lt 3.6 * $single + 600)
    Stop-Process -Id $p.Id

    "=== Endlos + derselbe Shortcut stoppt ==="
    Write-TestSettings 0
    $p = Start-App
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $tab = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600
    Normal
    $overlayBefore = StatusOverlay   # "nur mit Effekt zeigen" + kein Sound -> ausgeblendet
    Mute
    function PlayButtons { @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -like '*Loop starten*' -or $_ -like '*Stoppen*' } }
    Check 'Button zeigt "Loop starten"' ((PlayButtons) -contains '▶  Loop starten')
    $stopSeen = $false
    $loopEnd = [int](2 * $single + 3000)                       # mindestens zwei volle Durchlaeufe
    $script:timeText = ''; $script:overlayTexts = @()
    $l = Record ($loopEnd + 4000) @{
        800 = { Press }
        ($loopEnd - 1800) = {
            $script:timeText = (@($win.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'SoundTime'))) | Where-Object { $_.Current.Name } | Select-Object -First 1).Current.Name
            $ov = StatusOverlay
            if ($ov) { $script:overlayTexts = @($ov.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) }
        }
        ($loopEnd - 1000) = { $script:stopSeen = (PlayButtons) -contains '■  Stoppen' }   # UI-Abfragen mit Abstand zum Stopp
        $loopEnd = { Press }
    }
    $during = Loud $l ([int]($loopEnd - 2500)) ([int]($loopEnd - 1000)) 0.005
    $stopAt = [int]$script:ranAt[$loopEnd]
    $after = Loud $l ($stopAt + 700) ($loopEnd + 4000) 0.005
    if ($after -gt 0) { "   Pegel ab Stopp ($stopAt ms): " + ((([int]($stopAt / 100) - 3)..([int]($stopAt / 100) + 30) | ForEach-Object { '{0:N3}' -f $l[$_] }) -join ' ') }
    Check ("Loop laeuft nach {0:N1} s noch (laute Fenster kurz vor Stopp: {1})" -f ($loopEnd / 1000), $during) ($during -ge 3)
    Check 'Waehrend des Loops zeigt der Button "Stoppen"' $stopSeen
    Check ("Karte zeigt Durchgang und Zeit ({0})" -f $timeText) ($timeText -match '^Loop [2-9] · 0:0\d / 0:0\d$')
    Check 'Status-Anzeige: bei Normal ohne Sound ausgeblendet' ($null -eq $overlayBefore)
    Check ("Status-Anzeige zeigt den Sound mit Zeit ({0})" -f (($overlayTexts | Where-Object { $_ -like 'Loop*' }) -join '')) (($overlayTexts -contains 'Messingglocke') -and ($overlayTexts | Where-Object { $_ -like 'Loop * · *' }))
    Check ("Derselbe Shortcut stoppt den Loop (danach laute Fenster: {0})" -f $after) ($after -eq 0)
    Start-Sleep -m 600
    Check 'Button zeigt danach wieder "Loop starten"' ((PlayButtons) -contains '▶  Loop starten')
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings, $log, $mute -ErrorAction SilentlyContinue
    Remove-Item $soundDir -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$env:TEMP\voice.loopbackup" (Join-Path $cfg 'voice.txt') -Force
    Copy-Item "$env:TEMP\voice-active.loopbackup" (Join-Path $cfg 'voice-active.cfg') -Force
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                     OK" }
}
