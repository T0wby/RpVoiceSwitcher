# Testet den Modus "Offenes Mikrofon im Spiel" (Sprachtaste wird nur angetippt, nie gehalten):
#  - Umschalt-Taste (Strg+Alt+F10) tippt F13 kurz an und dreht die Anzeige (an <-> stumm)
#  - Sounds druecken F13 nicht
#  - eigenes Druecken von F13 (ausserhalb der App) wird mitgezaehlt, der Knopf "Anzeige umdrehen" drueckt nichts
#  - Notfall-Taste tippt nichts an
#  - Zurueck auf Push-to-Talk: Dauersenden haelt F13 wieder wie bisher
# Arbeitet mit einer Kopie der Einstellungen (--settings) und einem eigenen Sound-Ordner im TEMP.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-openmic-settings-$PID.json"
$soundDir = Join-Path $env:TEMP "rpvs-openmic-sounds-$PID"
$realHash = (Get-FileHash $realSettings).Hash
$cfg = 'C:\Program Files\EqualizerAPO\config'
Copy-Item (Join-Path $cfg 'voice.txt') "$env:TEMP\voice.openmicbackup" -Force
Copy-Item (Join-Path $cfg 'voice-active.cfg') "$env:TEMP\voice-active.openmicbackup" -Force
$mute = Join-Path $cfg 'rp-voices\99-teststumm.txt'
Set-Content $mute "# Name: Teststumm`r`nPreamp: -60 dB`r`n" -NoNewline

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T8 -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
'@
$AE = [Windows.Automation.AutomationElement]
function Key($vk, [switch]$Up) { [T8.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
function Combo([int]$fkey) { Key 0x11; Key 0x12; Key $fkey; Start-Sleep -m 60; Key $fkey -Up; Key 0x12 -Up; Key 0x11 -Up }
function F13Down { ([T8.U]::GetAsyncKeyState(0x7C) -band 0x8000) -ne 0 }
function Check($name, $ok) { "{0,-74} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function ById($root, $id) { @($root.FindAll('Descendants', (Cond $AE::AutomationIdProperty $id))) | Select-Object -First 1 }
# Beobachtet F13 fuer $ms Millisekunden: wie oft gedrueckt, ist es am Ende noch unten?
function Watch([int]$ms, [scriptblock]$action) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); $downs = 0; $was = F13Down; $maxHeld = 0; $since = $null
    if ($action) { & $action }
    while ($sw.ElapsedMilliseconds -lt $ms) {
        $d = F13Down
        if ($d -and -not $was) { $downs++; $since = $sw.ElapsedMilliseconds }
        if ($d -and $since -ne $null) { $maxHeld = [Math]::Max($maxHeld, $sw.ElapsedMilliseconds - $since) }
        $was = $d; Start-Sleep -m 3
    }
    [pscustomobject]@{ Downs = $downs; EndDown = (F13Down); MaxHeld = $maxHeld }
}

New-Item -ItemType Directory $soundDir -Force | Out-Null
$rate = 44100; $n = [int](1.5 * $rate)
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + 2 * $n)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int](2 * $rate)); $w.Write([int16]2); $w.Write([int16]16)
$w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int](2 * $n))
for ($i = 0; $i -lt $n; $i++) { $w.Write([int16]([Math]::Sin(2 * [Math]::PI * 440 * $i / $rate) * 0.2 * 32767)) }
$w.Flush(); [IO.File]::WriteAllBytes((Join-Path $soundDir 'Testton.wav'), $ms.ToArray())

function Write-TestSettings([bool]$openMic) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $p0 = $j.Profiles[0]
    $p0.IncludeAllVoices = $true
    $p0.Hotkeys = [pscustomobject]@{ '99-teststumm.txt' = 'Control, Alt|F8' }; $p0.HoldHotkeys = [pscustomobject]@{}
    $p0.SoundHotkeys = [pscustomobject]@{ 'Testton.wav' = 'Control, Alt|F11' }
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    $extra = @{ SoundMonitor = $false; SoundVolume = 1.0; SoundNormalize = $true; SoundFolder = $soundDir
                PushToTalkEnabled = $true; PushToTalkKey = 'Key|F13'; GameOpenMic = $openMic
                PttLatchHotkey = 'Control, Alt|F10'; PanicHotkey = 'None|Pause'
                ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false; ShowStatusOverlay = $false
                SetupWizardDone = $true; SoundsWithEffect = @(); SoundRepeat = [pscustomobject]@{}; LastSeenVersion = '99.0.0' }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
function Start-App {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    Combo 0x77; Start-Sleep 1   # Teststumm: im Kabel kommt nichts an
    $p
}
function MainWin($p) { @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1 }
function SoundTab($win) {
    $tab = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600
}
function SettingsWin($p, $win) {
    $open = @($win.FindAll('Descendants', (Cond $AE::NameProperty 'Sprachtaste einstellen …'))) | Select-Object -First 1
    $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 900
    @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + @($win.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) |
        Where-Object { $_ -and $_.Current.Name -like 'Einstellungen*' } | Select-Object -First 1
}
function ToggleName($win) { (ById $win 'PttLatchToggle').Current.Name }
function Minimize($el) { $el.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Minimized'); Start-Sleep -m 500 }
function AppInFront($p) { $fpid = 0; [void][T8.U]::GetWindowThreadProcessId([T8.U]::GetForegroundWindow(), [ref]$fpid); $fpid -eq $p.Id }

try {
    "=== Offenes Mikrofon im Spiel ==="
    Write-TestSettings $true
    $p = Start-App
    $win = MainWin $p
    SoundTab $win
    $sum = (ById $win 'PttSummary').Current.Name
    Check ("Soundboard zeigt den Modus ({0})" -f $sum) ($sum -like '*F13*offenes Mikro*')
    $t0 = ToggleName $win
    Check ("Start: Mikro im Spiel an ({0})" -f $t0) ($t0 -like '*Mikro im Spiel: AN*')
    $flip = ById $win 'PttStateFlip'
    Check 'Knopf "Anzeige umdrehen" ist da' ($flip -and -not $flip.Current.IsOffscreen)

    $r = Watch 900 { Combo 0x79 }
    $t1 = ToggleName $win
    Check ("Umschalt-Taste: F13 einmal angetippt ({0}x, {1} ms gehalten)" -f $r.Downs, $r.MaxHeld) ($r.Downs -eq 1 -and $r.MaxHeld -ge 40 -and $r.MaxHeld -lt 300)
    Check 'Danach ist F13 nicht mehr gedrueckt' (-not $r.EndDown)
    Check ("Anzeige jetzt stumm ({0})" -f $t1) ($t1 -like '*stumm*')

    $r = Watch 900 { Combo 0x79 }
    Check ("Nochmal: wieder angetippt und an ({0})" -f (ToggleName $win)) ($r.Downs -eq 1 -and -not $r.EndDown -and (ToggleName $win) -like '*AN*')

    # Sound: die App drueckt bei offenem Mikrofon nichts
    $r = Watch 1200 { Combo 0x7A }
    $playing = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | Where-Object { $_.Current.Name -like '*Stoppen*' -and $_.Current.Name -notlike '*Alle*' }
    Check ("Sound laeuft ({0} Stoppen-Knopf)" -f @($playing).Count) (@($playing).Count -ge 1)
    Check ("Waehrend des Sounds: F13 nie gedrueckt ({0}x)" -f $r.Downs) ($r.Downs -eq 0 -and -not $r.EndDown)
    Start-Sleep 2

    # Eigener Tastendruck (ausserhalb der App) schaltet im Spiel um -> App zaehlt mit
    Minimize $win
    $front = AppInFront $p
    Key 0x7C; Start-Sleep -m 120; Key 0x7C -Up; Start-Sleep -m 300
    $t2 = ToggleName $win
    Check ("Selbst F13 gedrueckt (App vorne: {0}): Anzeige stumm ({1})" -f $front, $t2) (-not $front -and $t2 -like '*stumm*')

    # "Anzeige umdrehen" dreht nur die Anzeige, ohne Taste
    $r = Watch 600 { $flip.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Check ("Anzeige umdrehen: wieder an ({0}), F13 nicht gedrueckt ({1}x)" -f (ToggleName $win), $r.Downs) ((ToggleName $win) -like '*AN*' -and $r.Downs -eq 0)

    # Notfall-Taste tippt nichts an und laesst den Zustand
    $r = Watch 800 { Key 0x13; Start-Sleep -m 60; Key 0x13 -Up }
    Check ("Notfall-Taste: kein Antippen ({0}x), Mikro bleibt an" -f $r.Downs) ($r.Downs -eq 0 -and (ToggleName $win) -like '*AN*')
    $win.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState('Normal'); Start-Sleep -m 500

    # Einstellungen: Modus-Auswahl, Haken fuer Sounds ist ausgegraut
    $sw = SettingsWin $p $win
    $SI = [Windows.Automation.SelectionItemPattern]
    $rbOpen = ById $sw 'PttModeOpenMic'; $rbHold = ById $sw 'PttModeHold'
    $isOpen = $rbOpen.GetCurrentPattern($SI::Pattern).Current.IsSelected
    $chk = @($sw.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::CheckBox)))) | Where-Object { $_.Current.Name -like 'Beim Abspielen*' } | Select-Object -First 1
    Check ("Einstellungen: 'Offenes Mikrofon' gewaehlt ({0}), Sound-Haken aus ({1})" -f $isOpen, $chk.Current.IsEnabled) ($isOpen -and -not $chk.Current.IsEnabled)

    # Zurueck auf Push-to-Talk: alter Weg funktioniert wie bisher
    $rbHold.GetCurrentPattern($SI::Pattern).Select(); Start-Sleep -m 600
    $saved = (Get-Content $settings -Raw | ConvertFrom-Json).GameOpenMic
    Check ("Auf Push-to-Talk umgestellt und gespeichert (GameOpenMic={0}), Sound-Haken an ({1})" -f $saved, $chk.Current.IsEnabled) (-not $saved -and $chk.Current.IsEnabled)
    $sw.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -m 400
    Combo 0x79; Start-Sleep -m 500
    $held = F13Down
    Check ("Push-to-Talk: Dauersenden haelt F13 ({0}), Knopf '{1}'" -f $held, (ToggleName $win)) ($held -and (ToggleName $win) -like '*Dauersenden: AN*')
    Combo 0x79; Start-Sleep -m 500
    Check 'Push-to-Talk: Dauersenden aus laesst F13 los' (-not (F13Down))
    $r = Watch 1200 { Combo 0x7A }
    Check ("Push-to-Talk: Sound drueckt F13 wieder ({0}x)" -f $r.Downs) ($r.Downs -ge 1)
    Start-Sleep 2
    Check 'Nach dem Sound wieder losgelassen' (-not (F13Down))

    # Wechsel waehrend Dauersenden: Taste darf nicht haengen bleiben
    Combo 0x79; Start-Sleep -m 500
    $heldBefore = F13Down
    $sw = SettingsWin $p $win
    (ById $sw 'PttModeOpenMic').GetCurrentPattern($SI::Pattern).Select(); Start-Sleep -m 600
    Check ("Wechsel auf offenes Mikro waehrend Dauersenden: F13 losgelassen (vorher {0})" -f $heldBefore) ($heldBefore -and -not (F13Down))
    $sw.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -m 400
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    if (F13Down) { Key 0x7C -Up }
    Remove-Item $settings, $mute -ErrorAction SilentlyContinue
    Remove-Item $soundDir -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$env:TEMP\voice.openmicbackup" (Join-Path $cfg 'voice.txt') -Force
    Copy-Item "$env:TEMP\voice-active.openmicbackup" (Join-Path $cfg 'voice-active.cfg') -Force
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                             OK" }
}
