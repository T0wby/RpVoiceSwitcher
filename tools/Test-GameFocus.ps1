# Testet "Taste nur drücken, solange das Spiel im Vordergrund ist":
#  - Ein Ersatz-Spiel (Kopie von powershell.exe als FakeGame.exe mit eigenem Fenster) wird in den Vordergrund geholt
#  - Dauersenden: F13 gedrückt, solange FakeGame vorne ist; App-Fenster vorne -> losgelassen; zurück -> wieder gedrückt
#  - Sound, während das Spiel nicht vorne ist: kein F13; Wechsel ins Spiel während des Sounds -> F13
#  - Offenes Mikrofon: Umschalten außerhalb des Spiels wird vorgemerkt und beim Wechsel ins Spiel angetippt;
#    eigene Drücke zählen nur, wenn das Spiel vorne ist
#  - Haken aus: altes Verhalten (drückt auch ohne Spiel vorne)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-focus-settings-$PID.json"
$soundDir = Join-Path $env:TEMP "rpvs-focus-sounds-$PID"
$fakeDir = Join-Path $env:TEMP "rpvs-fakegame-$PID"
$realHash = (Get-FileHash $realSettings).Hash
$cfg = 'C:\Program Files\EqualizerAPO\config'
Copy-Item (Join-Path $cfg 'voice.txt') "$env:TEMP\voice.focusbackup" -Force
Copy-Item (Join-Path $cfg 'voice-active.cfg') "$env:TEMP\voice-active.focusbackup" -Force
$mute = Join-Path $cfg 'rp-voices\99-teststumm.txt'
Set-Content $mute "# Name: Teststumm`r`nPreamp: -60 dB`r`n" -NoNewline

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T9 -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool BringWindowToTop(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
'@
$AE = [Windows.Automation.AutomationElement]
function Key($vk, [switch]$Up) { [T9.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
function Combo([int]$fkey) { Key 0x11; Key 0x12; Key $fkey; Start-Sleep -m 60; Key $fkey -Up; Key 0x12 -Up; Key 0x11 -Up }
function F13Down { ([T9.U]::GetAsyncKeyState(0x7C) -band 0x8000) -ne 0 }
function Check($name, $ok) { "{0,-80} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function ById($root, $id) { @($root.FindAll('Descendants', (Cond $AE::AutomationIdProperty $id))) | Select-Object -First 1 }
function FrontPid { $fp = 0; [void][T9.U]::GetWindowThreadProcessId([T9.U]::GetForegroundWindow(), [ref]$fp); $fp }
# Fenster nach vorne holen (Windows erlaubt das nur mit kleinem Trick: Eingabe-Threads kurz verbinden)
function Front([IntPtr]$h) {
    for ($i = 0; $i -lt 5; $i++) {
        $fgThread = [T9.U]::GetWindowThreadProcessId([T9.U]::GetForegroundWindow(), [ref]([uint32]0))
        $me = [T9.U]::GetCurrentThreadId()
        [void][T9.U]::AttachThreadInput($me, $fgThread, $true)
        [void][T9.U]::ShowWindow($h, 9); [void][T9.U]::BringWindowToTop($h); [void][T9.U]::SetForegroundWindow($h)
        [void][T9.U]::AttachThreadInput($me, $fgThread, $false)
        $want = 0; [void][T9.U]::GetWindowThreadProcessId($h, [ref]$want)
        for ($k = 0; $k -lt 40; $k++) { if ((FrontPid) -eq $want) { return $true }; Start-Sleep -m 10 }
    }
    $false
}
function Watch([int]$ms, [scriptblock]$action) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); $downs = 0; $was = F13Down
    if ($action) { & $action }
    while ($sw.ElapsedMilliseconds -lt $ms) { $d = F13Down; if ($d -and -not $was) { $downs++ }; $was = $d; Start-Sleep -m 3 }
    [pscustomobject]@{ Downs = $downs; EndDown = (F13Down) }
}

New-Item -ItemType Directory $soundDir, $fakeDir -Force | Out-Null
$rate = 44100; $n = [int](1.5 * $rate)
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + 2 * $n)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int](2 * $rate)); $w.Write([int16]2); $w.Write([int16]16)
$w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int](2 * $n))
for ($i = 0; $i -lt $n; $i++) { $w.Write([int16]([Math]::Sin(2 * [Math]::PI * 440 * $i / $rate) * 0.2 * 32767)) }
$w.Flush(); [IO.File]::WriteAllBytes((Join-Path $soundDir 'Testton.wav'), $ms.ToArray())

# Ersatz-Spiel: eigenes Fenster in einem Prozess namens "FakeGame"
Copy-Item "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" (Join-Path $fakeDir 'FakeGame.exe') -Force
Set-Content (Join-Path $fakeDir 'game.ps1') @'
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text = 'RPVS Fake Game'; $f.Width = 420; $f.Height = 260; $f.StartPosition = 'CenterScreen'
[void]$f.ShowDialog()
'@

function Write-TestSettings([bool]$onlyInGame, [bool]$openMic) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $p0 = $j.Profiles[0]
    $p0.IncludeAllVoices = $true
    $p0.Hotkeys = [pscustomobject]@{ '99-teststumm.txt' = 'Control, Alt|F8' }; $p0.HoldHotkeys = [pscustomobject]@{}
    $p0.SoundHotkeys = [pscustomobject]@{ 'Testton.wav' = 'Control, Alt|F11' }
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    $extra = @{ SoundMonitor = $false; SoundVolume = 1.0; SoundNormalize = $true; SoundFolder = $soundDir
                PushToTalkEnabled = $true; PushToTalkKey = 'Key|F13'; GameOpenMic = $openMic
                PttOnlyInGame = $onlyInGame; PttGameProcess = 'FakeGame'
                PttLatchHotkey = 'Control, Alt|F10'; PanicHotkey = 'None|Pause'
                ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false; ShowStatusOverlay = $false
                SetupWizardDone = $true; SoundsWithEffect = @(); SoundRepeat = [pscustomobject]@{}; LastSeenVersion = '99.0.0' }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
function Start-App {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    Combo 0x77; Start-Sleep 1   # Teststumm
    $p
}
function MainWin($p) {
    $w = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $tab = @($w.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600
    $w
}
function ToggleName($win) { (ById $win 'PttLatchToggle').Current.Name }

$game = $null
try {
    $game = Start-Process (Join-Path $fakeDir 'FakeGame.exe') -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $fakeDir 'game.ps1')`"" -PassThru
    for ($i = 0; $i -lt 40 -and (Get-Process -Id $game.Id).MainWindowHandle -eq 0; $i++) { Start-Sleep -m 250 }
    $gameWnd = (Get-Process -Id $game.Id).MainWindowHandle

    "=== Push-to-Talk: nur im Spiel ==="
    Write-TestSettings $true $false
    $p = Start-App
    $win = MainWin $p
    $appWnd = [IntPtr]$win.Current.NativeWindowHandle

    $okFront = Front $gameWnd
    Combo 0x79; Start-Sleep -m 500
    Check ("Spiel vorne ({0}): Dauersenden haelt F13 ({1})" -f $okFront, (F13Down)) ($okFront -and (F13Down))
    $okApp = Front $appWnd; Start-Sleep -m 200
    $t = ToggleName $win
    Check ("App vorne ({0}): F13 losgelassen, Knopf '{1}'" -f $okApp, $t) ($okApp -and -not (F13Down) -and $t -like '*wartet aufs Spiel*')
    [void](Front $gameWnd); Start-Sleep -m 200
    Check ("Zurueck ins Spiel: F13 wieder gedrueckt, Knopf '{0}'" -f (ToggleName $win)) ((F13Down) -and (ToggleName $win) -notlike '*wartet*')
    Combo 0x79; Start-Sleep -m 500
    Check 'Dauersenden aus: F13 los' (-not (F13Down))

    # Sound, waehrend die App vorne ist -> kein F13; dann ins Spiel -> F13 fuer den Rest des Sounds
    [void](Front $appWnd)
    $r = Watch 500 { Combo 0x7A }
    Check ("Sound ausserhalb des Spiels: F13 nicht gedrueckt ({0}x)" -f $r.Downs) ($r.Downs -eq 0 -and -not $r.EndDown)
    [void](Front $gameWnd); Start-Sleep -m 150
    $mid = F13Down
    Start-Sleep 2
    Check ("Ins Spiel gewechselt waehrend des Sounds: F13 gedrueckt ({0}), danach los ({1})" -f $mid, -not (F13Down)) ($mid -and -not (F13Down))

    # Dauersenden in der App eingeschaltet -> erst beim Wechsel ins Spiel gedrueckt
    [void](Front $appWnd)
    Combo 0x79; Start-Sleep -m 500
    $before = F13Down
    [void](Front $gameWnd); Start-Sleep -m 200
    Check ("In der App eingeschaltet: erst nicht ({0}), im Spiel dann gedrueckt ({1})" -f $before, (F13Down)) (-not $before -and (F13Down))
    Key 0x13; Start-Sleep -m 60; Key 0x13 -Up; Start-Sleep -m 500
    Check 'Notfall-Taste beendet es auch hier' (-not (F13Down))

    # Einstellungen zeigen Spiel und Status
    $open = @($win.FindAll('Descendants', (Cond $AE::NameProperty 'Sprachtaste einstellen …'))) | Select-Object -First 1
    $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 1500
    $sw = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + @($win.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) |
          Where-Object { $_ -and $_.Current.Name -like 'Einstellungen*' } | Select-Object -First 1
    $chk = ById $sw 'PttOnlyInGameCheck'
    $box = (ById $sw 'PttGameBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    $status = (ById $sw 'PttGameStatus').Current.Name
    $TP = [Windows.Automation.TogglePattern]
    $isOn = $chk.GetCurrentPattern($TP::Pattern).Current.ToggleState -eq 'On'
    Check ("Einstellungen: Haken an ({0}), Spiel '{1}', Status '{2}'" -f $isOn, $box, $status) ($isOn -and $box -eq 'FakeGame' -and $status -like '*läuft*')

    # "Erkennen …": 5 s Zeit, ins Spiel zu wechseln
    (ById $sw 'PttGameBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Irgendwas')
    (ById $sw 'PttGameDetect').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -m 800; [void](Front $gameWnd); Start-Sleep -m 5500
    $box2 = (ById $sw 'PttGameBox').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    $saved2 = (Get-Content $settings -Raw | ConvertFrom-Json).PttGameProcess
    Check ("Erkennen: Spiel uebernommen ('{0}', gespeichert '{1}')" -f $box2, $saved2) ($box2 -eq 'FakeGame' -and $saved2 -eq 'FakeGame')

    # Haken aus -> altes Verhalten
    $chk.GetCurrentPattern($TP::Pattern).Toggle(); Start-Sleep -m 400
    $saved = (Get-Content $settings -Raw | ConvertFrom-Json).PttOnlyInGame
    $sw.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -m 400
    [void](Front $appWnd)
    Combo 0x79; Start-Sleep -m 500
    Check ("Haken aus (gespeichert: {0}): drueckt auch mit App vorne ({1})" -f $saved, (F13Down)) (-not $saved -and (F13Down))
    Combo 0x79; Start-Sleep -m 500
    Check 'Und laesst wieder los' (-not (F13Down))
    Stop-Process -Id $p.Id; Start-Sleep -m 500

    "=== Offenes Mikrofon: nur im Spiel ==="
    Write-TestSettings $true $true
    $p = Start-App
    $win = MainWin $p
    $appWnd = [IntPtr]$win.Current.NativeWindowHandle
    [void](Front $appWnd)
    $r = Watch 700 { Combo 0x79 }
    $t = ToggleName $win
    Check ("Umschalten mit App vorne: nicht angetippt ({0}x), Anzeige '{1}'" -f $r.Downs, $t) ($r.Downs -eq 0 -and $t -like '*stumm*')
    [void](Front $gameWnd)
    $r = Watch 900 $null
    Check ("Wechsel ins Spiel: vorgemerktes Antippen ({0}x), danach los" -f $r.Downs) ($r.Downs -eq 1 -and -not $r.EndDown)
    $r = Watch 600 { Combo 0x79 }
    Check ("Umschalten im Spiel: sofort angetippt ({0}x), Anzeige '{1}'" -f $r.Downs, (ToggleName $win)) ($r.Downs -eq 1 -and (ToggleName $win) -like '*AN*')
    Key 0x7C; Start-Sleep -m 120; Key 0x7C -Up; Start-Sleep -m 300
    Check ("Selbst gedrueckt im Spiel: zaehlt mit ('{0}')" -f (ToggleName $win)) ((ToggleName $win) -like '*stumm*')
    [void](Front $appWnd)
    Key 0x7C; Start-Sleep -m 120; Key 0x7C -Up; Start-Sleep -m 300
    Check ("Selbst gedrueckt mit App vorne: zaehlt nicht ('{0}')" -f (ToggleName $win)) ((ToggleName $win) -like '*stumm*')
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    if ($game) { Stop-Process -Id $game.Id -EA SilentlyContinue }
    Start-Sleep -m 500
    if (F13Down) { Key 0x7C -Up }
    Remove-Item $settings, $mute -ErrorAction SilentlyContinue
    Remove-Item $soundDir, $fakeDir -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$env:TEMP\voice.focusbackup" (Join-Path $cfg 'voice.txt') -Force
    Copy-Item "$env:TEMP\voice-active.focusbackup" (Join-Path $cfg 'voice-active.cfg') -Force
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                                 OK" }
}
