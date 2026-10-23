# Testet, was gegen "Sounds kommen im Sprachchat nicht an" hilft – gemessen im Kabel:
#  1. Lautstaerke angleichen: ein leiser Testton (Spitze 0,2) kommt deutlich lauter an
#  2. Lautstaerke 200 %: noch lauter, aber der Begrenzer laesst nichts uebersteuern
#  3. Sprachtaste: F13 ist gedrueckt, solange der Sound laeuft, und danach wieder los (auch beim Abbrechen)
# Arbeitet mit einer Kopie der Einstellungen (--settings) und einem eigenen Sound-Ordner im TEMP.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$probe = Join-Path $repo 'tools\AudioProbe\bin\out\AudioProbe.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"
$log = Join-Path $env:TEMP "rpvs-level-$PID.txt"
$soundDir = Join-Path $env:TEMP "rpvs-level-sounds-$PID"
$realHash = (Get-FileHash $realSettings).Hash
$cfg = 'C:\Program Files\EqualizerAPO\config'
Copy-Item (Join-Path $cfg 'voice.txt') "$env:TEMP\voice.levelbackup" -Force
Copy-Item (Join-Path $cfg 'voice-active.cfg') "$env:TEMP\voice-active.levelbackup" -Force
$mute = Join-Path $cfg 'rp-voices\99-teststumm.txt'
Set-Content $mute "# Name: Teststumm`r`nPreamp: -60 dB`r`n" -NoNewline

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T7 -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
'@
$AE = [Windows.Automation.AutomationElement]
function Key($vk, [switch]$Up) { [T7.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
function Combo([int]$fkey) { Key 0x11; Key 0x12; Key $fkey; Start-Sleep -m 60; Key $fkey -Up; Key 0x12 -Up; Key 0x11 -Up }
function Press { Combo 0x7A }                         # Strg+Alt+F11 = Testton
function Mute { Combo 0x77; Start-Sleep -m 800 }      # Strg+Alt+F8 = Teststumm (Mikrofon im Kabel aus)
function F13Down { ([T7.U]::GetAsyncKeyState(0x7C) -band 0x8000) -ne 0 }
function Check($name, $ok) { "{0,-70} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }

# Leiser Testton: 1,5 s, 440 Hz, Spitze 0,2 (-14 dBFS), 16 bit mono
New-Item -ItemType Directory $soundDir -Force | Out-Null
# Krumme Laenge (1,537 s) mit Absicht: Sounds, deren Laenge nicht genau in die Audio-Bloecke passt, blieben frueher "haengen"
$rate = 44100; $n = [int](1.537 * $rate)
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + 2 * $n)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int](2 * $rate)); $w.Write([int16]2); $w.Write([int16]16)
$w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int](2 * $n))
for ($i = 0; $i -lt $n; $i++) { $w.Write([int16]([Math]::Sin(2 * [Math]::PI * 440 * $i / $rate) * 0.2 * 32767)) }
$w.Flush(); [IO.File]::WriteAllBytes((Join-Path $soundDir 'Testton.wav'), $ms.ToArray())

function Write-TestSettings([bool]$normalize, [double]$volume, [string]$ptt) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $p0 = $j.Profiles[0]
    $p0.IncludeAllVoices = $true
    $p0.Hotkeys = [pscustomobject]@{ '99-teststumm.txt' = 'Control, Alt|F8' }; $p0.HoldHotkeys = [pscustomobject]@{}
    $p0.SoundHotkeys = [pscustomobject]@{ 'Testton.wav' = 'Control, Alt|F11' }
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    # Mithoeren an (aber stumm), damit auch der zweite Ausgang mitlaeuft
    $extra = @{ SoundMonitor = $true; SoundMonitorVolume = 0.0; SoundVolume = $volume; SoundNormalize = $normalize; SoundFolder = $soundDir
                PushToTalkEnabled = [bool]$ptt; PushToTalkKey = $(if ($ptt) { $ptt } else { $null })
                PttLatchHotkey = 'Control, Alt|F10'; PanicHotkey = 'None|Pause'
                ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false; ShowStatusOverlay = $false
                SetupWizardDone = $true; SoundsWithEffect = @(); SoundRepeat = [pscustomobject]@{} }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
function Start-App { Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600; $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5; Mute; Start-Sleep 1; $p }

function Record([int]$ms, [hashtable]$actions) {
    $pr = Start-Process $probe -ArgumentList '"CABLE Output"', "$ms" -RedirectStandardOutput $log -PassThru -NoNewWindow
    $sw = [Diagnostics.Stopwatch]::StartNew()
    foreach ($t in ($actions.Keys | Sort-Object)) {
        while ($sw.ElapsedMilliseconds -lt $t) { Start-Sleep -m 10 }
        & $actions[$t]
    }
    $pr.WaitForExit()
    $levels = @{}
    Get-Content $log | Where-Object { $_ -match '^\s*(\d+) ms\s+([\d,\.]+)' } | ForEach-Object { $levels[[int]$Matches[1] / 100] = [double]($Matches[2] -replace ',', '.') }
    $levels
}
function SoundTab($p) {
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $tab = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600
    $win
}
function MaxLevel($levels) { ($levels.Values | Measure-Object -Maximum).Maximum }

try {
    "=== Lautstaerke ==="
    Write-TestSettings $false 1.0 ''
    $p = Start-App
    $raw = MaxLevel (Record 3500 @{ 500 = { Press } })
    Stop-Process -Id $p.Id

    Write-TestSettings $true 1.0 ''
    $p = Start-App
    $norm = MaxLevel (Record 3500 @{ 500 = { Press } })
    Stop-Process -Id $p.Id

    Write-TestSettings $false 2.0 ''
    $p = Start-App
    $raw2 = MaxLevel (Record 3500 @{ 500 = { Press } })
    Stop-Process -Id $p.Id

    Write-TestSettings $true 2.0 ''
    $p = Start-App
    $boost = MaxLevel (Record 3500 @{ 500 = { Press } })
    $win = SoundTab $p
    $slider = @($win.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'SoundVolumeSlider'))) | Select-Object -First 1
    $RV = [Windows.Automation.RangeValuePattern]
    $range = [pscustomobject]@{ Value = [double]$slider.GetCurrentPropertyValue($RV::ValueProperty); Maximum = [double]$slider.GetCurrentPropertyValue($RV::MaximumProperty) }
    Stop-Process -Id $p.Id

    Check ("Ohne Angleichen kommt der leise Ton an (Pegel {0:N3})" -f $raw) ($raw -gt 0.02)
    Check ("Angeglichen deutlich lauter ({0:N3} -> {1:N3}, x{2:N1}; erwartet ca. x4,5)" -f $raw, $norm, ($norm / $raw)) ($norm / $raw -gt 3 -and $norm / $raw -lt 6)
    Check ("200 % verdoppelt einen leisen Sound ({0:N3} -> {1:N3})" -f $raw, $raw2) ($raw2 / $raw -gt 1.8 -and $raw2 / $raw -lt 2.2)
    Check ("200 % auf schon vollem Sound: Begrenzer, kein Uebersteuern ({0:N3})" -f $boost) ($boost -le 1.0 -and $boost -gt 0.7)
    Check ("Regler geht bis 200 % (steht auf {0:P0}, max {1:P0})" -f $range.Value, $range.Maximum) ($range.Maximum -eq 2 -and [Math]::Abs($range.Value - 2) -lt 0.01)

    "=== Sprachtaste (F13) ==="
    Write-TestSettings $true 1.0 'Key|F13'
    $p = Start-App
    $win = SoundTab $p
    $sum = @($win.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'PttSummary'))) | Select-Object -First 1
    Check ("Soundboard zeigt Sprachtaste und Umschalt-Taste ({0})" -f $sum.Current.Name) ($sum.Current.Name -eq 'Sprachtaste: F13 · Umschalten: Strg + Alt + F10')
    # "Sprachtaste einstellen …" oeffnet die Einstellungen mit beiden Tasten
    $open = @($win.FindAll('Descendants', (Cond $AE::NameProperty 'Sprachtaste einstellen …'))) | Select-Object -First 1
    $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 800
    # Eigene Fenster der App haengen in UIA mal unter dem Desktop, mal unter dem Hauptfenster
    $sw = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + @($win.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) |
          Where-Object { $_ -and $_.Current.Name -like 'Einstellungen*' } | Select-Object -First 1
    $k1 = (@($sw.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'PttKeyButton'))) | Select-Object -First 1).Current.Name
    $k2 = (@($sw.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'PttLatchButton'))) | Select-Object -First 1).Current.Name
    Check ("Einstellungen: Sprachtaste '{0}', Dauersenden-Taste '{1}'" -f $k1, $k2) ($k1 -eq 'F13' -and $k2 -like 'Strg + Alt + F10*')
    $sw.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -m 400

    $before = F13Down
    $script:during = $null; $script:after = $null
    $l = Record 4000 @{ 500 = { Press }; 1300 = { $script:during = F13Down }; 2900 = { $script:after = F13Down } }
    Check 'Vorher nicht gedrueckt' (-not $before)
    Check 'Waehrend der Sound laeuft: F13 gedrueckt' $script:during
    Check 'Nach dem Sound (+ Nachlauf) wieder losgelassen' (-not $script:after)
    $playBtn = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | Where-Object { $_.Current.Name -like '*Abspielen*' -or ($_.Current.Name -like '*Stoppen*' -and $_.Current.Name -notlike '*Alle*') } | Select-Object -First 1
    Check ("Sound mit krummer Laenge endet von selbst (Knopf: '{0}')" -f $playBtn.Current.Name) ($playBtn.Current.Name -like '*Abspielen*')
    Check ("Sound kam trotzdem an (Pegel {0:N3})" -f (MaxLevel $l)) ((MaxLevel $l) -gt 0.05)

    # Abbrechen mit demselben Shortcut -> Taste wieder los
    $script:during = $null; $script:after = $null
    $l = Record 3000 @{ 500 = { Press }; 900 = { $script:during = F13Down }; 1000 = { Press }; 1800 = { $script:after = F13Down } }
    Check 'Abbrechen: erst gedrueckt ...' $script:during
    Check '... und nach dem Abbrechen wieder losgelassen' (-not $script:after)

    "=== Dauersenden (Strg+Alt+F10) ==="
    Combo 0x79; Start-Sleep -m 500
    $on = F13Down
    $toggle = @($win.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'PttLatchToggle'))) | Select-Object -First 1
    Check ("An: F13 gedrueckt, Knopf zeigt '{0}'" -f $toggle.Current.Name) ($on -and $toggle.Current.Name -like '*AN*')
    Press; Start-Sleep 3   # Sound laeuft durch - Taste muss gedrueckt bleiben
    Check 'Nach einem Sound bleibt F13 bei Dauersenden gedrueckt' (F13Down)
    # Selbst kurz drücken und loslassen (echte Eingabe, nicht von der App) -> App drückt sofort wieder
    Key 0x7C; Start-Sleep -m 150; Key 0x7C -Up; Start-Sleep -m 250
    Check 'Selbst gedrueckt und losgelassen: Dauersenden laeuft weiter' (F13Down)
    Key 0x7C -Up; Start-Sleep -m 250
    Check '... auch beim zweiten Mal' (F13Down)
    Combo 0x79; Start-Sleep -m 500
    Check 'Aus: F13 wieder losgelassen' (-not (F13Down))
    Combo 0x79; Start-Sleep -m 500
    $onAgain = F13Down
    Key 0x13; Start-Sleep -m 60; Key 0x13 -Up; Start-Sleep -m 600   # Pause = Notfall-Taste
    Check 'Notfall-Taste beendet Dauersenden' ($onAgain -and -not (F13Down))

    # App wird hart beendet, waehrend sie die Taste haelt -> beim naechsten Start wird sie losgelassen
    Combo 0x79; Start-Sleep -m 500
    $heldBeforeKill = F13Down
    Stop-Process -Id $p.Id -Force; Start-Sleep -m 500
    $stuck = F13Down
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    Check ("Hart beendet: Taste hing ({0}), naechster Start laesst sie los" -f $stuck) ($heldBeforeKill -and $stuck -and -not (F13Down))
    Stop-Process -Id $p.Id

    # Test-Instanzen druecken nie die echte Sprachtaste (z. B. Maus 5) des Nutzers
    Write-TestSettings $true 1.0 'Mouse|2'
    $p = Start-App
    $win = SoundTab $p
    $sum = @($win.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'PttSummary'))) | Select-Object -First 1
    Check ("Test-Instanz ignoriert Maus 5 ({0})" -f $sum.Current.Name) ($sum.Current.Name -eq 'Keine Sprachtaste eingestellt')
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    if (F13Down) { Key 0x7C -Up }
    Remove-Item $settings, $log, $mute -ErrorAction SilentlyContinue
    Remove-Item $soundDir -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$env:TEMP\voice.levelbackup" (Join-Path $cfg 'voice.txt') -Force
    Copy-Item "$env:TEMP\voice-active.levelbackup" (Join-Path $cfg 'voice-active.cfg') -Force
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                         OK" }
}
