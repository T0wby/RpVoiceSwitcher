# Testet, dass Dauersenden robust bleibt und nur bewusst ausgeht:
#  - Menü im Spiel auf/zu (Maus wird gefangen/freigegeben) -> einmal aufgefrischt
#  - anderes Fenster des Spiels kommt nach vorne -> einmal aufgefrischt
#  - nach dem Zurückwechseln ins Spiel kein zusätzliches "regelmäßig" direkt hinterher
#  - "Alle stoppen" lässt Dauersenden an
#  - Absturz (Prozess hart beendet) -> beim nächsten Start läuft Dauersenden weiter
#  - Umschalt-Taste aus bzw. bewusst beenden -> beim nächsten Start aus
# Ersatz-Spiel: Kopie von powershell.exe als FakeGame.exe mit zwei Fenstern. Gedrückt wird nur F13 (Testmodus).
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-robust-settings-$PID.json"
$soundDir = Join-Path $env:TEMP "rpvs-robust-sounds-$PID"
$fakeDir = Join-Path $env:TEMP "rpvs-robustgame-$PID"
$testLog = "$env:LOCALAPPDATA\RpVoiceSwitcher\sprachtaste-test.log"
$realHash = (Get-FileHash $realSettings).Hash
$cfg = 'C:\Program Files\EqualizerAPO\config'
Copy-Item (Join-Path $cfg 'voice.txt') "$env:TEMP\voice.robustbackup" -Force
Copy-Item (Join-Path $cfg 'voice-active.cfg') "$env:TEMP\voice-active.robustbackup" -Force
$mute = Join-Path $cfg 'rp-voices\99-teststumm.txt'
Set-Content $mute "# Name: Teststumm`r`nPreamp: -60 dB`r`n" -NoNewline
$env:RPVS_TEST_RESUME = '1'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T12 -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool BringWindowToTop(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ClipCursor(ref RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ClipCursor")] public static extern bool UnclipCursor(System.IntPtr none);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
function Key($vk, [switch]$Up) { [T12.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
function Combo([int]$fkey) { Key 0x11; Key 0x12; Key $fkey; Start-Sleep -m 60; Key $fkey -Up; Key 0x12 -Up; Key 0x11 -Up }
function F13Down { ([T12.U]::GetAsyncKeyState(0x7C) -band 0x8000) -ne 0 }
function Check($name, $ok) { "{0,-80} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function ById($root, $id) { @($root.FindAll('Descendants', (Cond $AE::AutomationIdProperty $id))) | Select-Object -First 1 }
function FrontWnd { [T12.U]::GetForegroundWindow() }
function Front([IntPtr]$h) {
    for ($i = 0; $i -lt 5; $i++) {
        $fgThread = [T12.U]::GetWindowThreadProcessId([T12.U]::GetForegroundWindow(), [ref]([uint32]0))
        $me = [T12.U]::GetCurrentThreadId()
        [void][T12.U]::AttachThreadInput($me, $fgThread, $true)
        [void][T12.U]::ShowWindow($h, 9); [void][T12.U]::BringWindowToTop($h); [void][T12.U]::SetForegroundWindow($h)
        [void][T12.U]::AttachThreadInput($me, $fgThread, $false)
        for ($k = 0; $k -lt 40; $k++) { if ((FrontWnd) -eq $h) { return $true }; Start-Sleep -m 10 }
    }
    $false
}
function FrontPid { $fp = 0; [void][T12.U]::GetWindowThreadProcessId([T12.U]::GetForegroundWindow(), [ref]$fp); $fp }
function Watch([int]$ms, [scriptblock]$action) {
    $sw = [Diagnostics.Stopwatch]::StartNew(); $downs = 0; $was = F13Down; $left = $false
    if ($action) { & $action }
    while ($sw.ElapsedMilliseconds -lt $ms) {
        $d = F13Down; if ($d -and -not $was) { $downs++ }; $was = $d
        if ($script:gamePid -and (FrontPid) -ne $script:gamePid) { $left = $true }
        Start-Sleep -m 3
    }
    [pscustomobject]@{ Downs = $downs; EndDown = (F13Down); Disturbed = $left }
}
# Wie Watch, aber im Spiel: holt das Spiel vorher nach vorne und wiederholt den Schritt, wenn ein anderes Programm
# (du arbeitest gerade am PC) zwischendurch in den Vordergrund kommt.
function WatchInGame([int]$ms, [scriptblock]$action, [scriptblock]$before) {
    for ($try = 1; $try -le 4; $try++) {
        [void](Front $script:gameWnd)   # erst nach vorne – ein Fensterwechsel hebt eine Maussperre auf
        if ($before) { & $before }
        Start-Sleep -m 600
        if (-not (F13Down)) { continue }
        $r = Watch $ms $action
        if (-not $r.Disturbed) { $r | Add-Member Tries $try; return $r }
    }
    $r | Add-Member Tries 'gestoert' -Force; $r
}
# „Menü zu“ wie in Unreal: Maus ins Fenster sperren (hier: fast der ganze Bildschirm, nur 1 Pixel weniger –
# deine Maus bleibt praktisch frei). „Menü auf“: wieder freigeben.
function Clip {
    $x = [T12.U]::GetSystemMetrics(76); $y = [T12.U]::GetSystemMetrics(77); $w = [T12.U]::GetSystemMetrics(78); $h = [T12.U]::GetSystemMetrics(79)
    $r = New-Object T12.U+RECT; $r.L = $x; $r.T = $y; $r.R = $x + $w - 1; $r.B = $y + $h
    [void][T12.U]::ClipCursor([ref]$r)
}
function Unclip { [void][T12.U]::UnclipCursor([IntPtr]::Zero) }
function SavedResume { (Get-Content $settings -Raw | ConvertFrom-Json).PttLatchResume }
function LogTail { Get-Content $testLog -Encoding UTF8 -EA SilentlyContinue | Select-Object -Last 12 }

New-Item -ItemType Directory $soundDir, $fakeDir -Force | Out-Null
$rate = 44100; $n = [int](20 * $rate)
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + 2 * $n)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int](2 * $rate)); $w.Write([int16]2); $w.Write([int16]16)
$w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int](2 * $n))
for ($i = 0; $i -lt $n; $i++) { $w.Write([int16]([Math]::Sin(2 * [Math]::PI * 440 * $i / $rate) * 0.05 * 32767)) }
$w.Flush(); [IO.File]::WriteAllBytes((Join-Path $soundDir 'Langer Ton.wav'), $ms.ToArray())

Copy-Item "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" (Join-Path $fakeDir 'FakeGame.exe') -Force
Set-Content (Join-Path $fakeDir 'game.ps1') @'
Add-Type -AssemblyName System.Windows.Forms
$f = New-Object Windows.Forms.Form; $f.Text = 'RPVS Fake Game'; $f.Width = 420; $f.Height = 260; $f.StartPosition = 'CenterScreen'
$d = New-Object Windows.Forms.Form; $d.Text = 'RPVS Fake Dialog'; $d.Width = 300; $d.Height = 160; $d.StartPosition = 'Manual'; $d.Left = 40; $d.Top = 40
$f.Add_Shown({ $d.Show() })
[Windows.Forms.Application]::Run($f)
'@

function Write-TestSettings([int]$refresh = 0, [bool]$resume = $false) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $p0 = $j.Profiles[0]
    $p0.IncludeAllVoices = $true; $p0.IncludeAllSounds = $true
    $p0.Hotkeys = [pscustomobject]@{ '99-teststumm.txt' = 'Control, Alt|F8' }; $p0.HoldHotkeys = [pscustomobject]@{}
    $p0.SoundHotkeys = [pscustomobject]@{ 'Langer Ton.wav' = 'Control, Alt|F11' }
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    $extra = @{ SoundMonitor = $false; SoundVolume = 1.0; SoundNormalize = $false; SoundFolder = $soundDir
                PushToTalkEnabled = $true; PushToTalkKey = 'Key|F13'; GameOpenMic = $false
                PttOnlyInGame = $true; PttGameProcess = 'FakeGame'; PttRefreshSeconds = $refresh; PttLatchResume = $resume
                PttLatchHotkey = 'Control, Alt|F10'; PanicHotkey = 'None|Pause'
                ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false; ShowStatusOverlay = $false; KeepCableDefaultMic = $false
                SetupWizardDone = $true; TourSeen = $true; SoundsWithEffect = @(); SoundRepeat = [pscustomobject]@{}; LastSeenVersion = '99.0.0' }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
function Start-App([switch]$NoMute) {
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    if (-not $NoMute) { Combo 0x77; Start-Sleep 1 }
    $p
}
function MainWin($p) {
    $w = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $tab = @($w.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600
    $w
}
function ToggleName($win) { (ById $win 'PttLatchToggle').Current.Name }
function Quit($p) {
    $quit = $null
    for ($try = 0; $try -lt 10 -and -not $quit; $try++) {
        try {
            $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
            $datei = @($win.FindAll('Descendants', (Cond $AE::NameProperty 'Datei'))) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::MenuItem } | Select-Object -First 1
            $datei.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 500
            $quit = @($win.FindAll('Descendants', (Cond $AE::NameProperty 'Beenden'))) + @($AE::RootElement.FindAll('Descendants', (Cond $AE::NameProperty 'Beenden'))) |
                    Where-Object { $_ -and $_.Current.ProcessId -eq $p.Id -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::MenuItem } | Select-Object -First 1
        } catch { }
        if (-not $quit) { Start-Sleep -m 500 }
    }
    if ($quit) { $quit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    $p.WaitForExit(6000)
}

$game = $null
try {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $game = Start-Process (Join-Path $fakeDir 'FakeGame.exe') -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $fakeDir 'game.ps1')`"" -PassThru
    for ($i = 0; $i -lt 40 -and (Get-Process -Id $game.Id).MainWindowHandle -eq 0; $i++) { Start-Sleep -m 250 }
    Start-Sleep 1
    $gameWin = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $game.Id)))
    $gameWnd = [IntPtr](($gameWin | Where-Object { $_.Current.Name -eq 'RPVS Fake Game' } | Select-Object -First 1).Current.NativeWindowHandle)
    $script:gameWnd = $gameWnd; $script:gamePid = $game.Id
    $dlgWnd = [IntPtr](($gameWin | Where-Object { $_.Current.Name -eq 'RPVS Fake Dialog' } | Select-Object -First 1).Current.NativeWindowHandle)
    Check ("Ersatz-Spiel mit zwei Fenstern laeuft ({0}, {1})" -f $gameWnd, $dlgWnd) ($gameWnd -ne 0 -and $dlgWnd -ne 0)

    "=== Menue im Spiel, zweites Spielfenster, Alle stoppen ==="
    Write-TestSettings 0
    $p = Start-App
    $win = MainWin $p
    $appWnd = [IntPtr]$win.Current.NativeWindowHandle
    Unclip
    [void](Front $gameWnd); Start-Sleep -m 500
    Combo 0x79; Start-Sleep -m 700
    $held = F13Down
    for ($try = 1; $try -le 4 -and -not $held; $try++) { [void](Front $gameWnd); Start-Sleep -m 700; $held = F13Down }
    Check ("Dauersenden an, F13 gehalten ({0}), gemerkt fuer Neustart ({1})" -f $held, (SavedResume)) ($held -and (SavedResume) -eq $true)

    $r = WatchInGame 1000 { Clip } { Unclip }
    Check ("Maus vom Spiel gefangen (Menue zu): einmal aufgefrischt ({0}x, gedrueckt: {1}, Versuch {2})" -f $r.Downs, $r.EndDown, $r.Tries) ($r.Downs -eq 1 -and $r.EndDown)
    $r = WatchInGame 1000 { Unclip } { Clip }
    Check ("Maus wieder frei (Menue auf): einmal aufgefrischt ({0}x, gedrueckt: {1}, Versuch {2})" -f $r.Downs, $r.EndDown, $r.Tries) ($r.Downs -eq 1 -and $r.EndDown)
    $r = WatchInGame 1200 { Clip; Start-Sleep -m 60; Unclip; Start-Sleep -m 60; Clip; Start-Sleep -m 60; Unclip } { Unclip }
    Check ("Schnelles Hin und Her: nur einmal aufgefrischt ({0}x, Versuch {1})" -f $r.Downs, $r.Tries) ($r.Downs -eq 1 -and $r.EndDown)
    Check 'Protokoll nennt den Grund (Mauszeiger/Menue)' (@(LogTail | Where-Object { $_ -like '*aufgefrischt (Mauszeiger*' }).Count -ge 2)

    $r = WatchInGame 1000 { [void](Front $dlgWnd) }   # zweites Fenster desselben Spielprozesses
    Check ("Anderes Fenster des Spiels vorne: einmal aufgefrischt ({0}x, gedrueckt: {1}, Versuch {2})" -f $r.Downs, $r.EndDown, $r.Tries) ($r.Downs -eq 1 -and $r.EndDown)

    [void](Front $gameWnd); Start-Sleep -m 700
    Combo 0x7A; Start-Sleep -m 800   # langer Ton laeuft
    $stop = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | Where-Object { $_.Current.Name -like '*Alle stoppen*' } | Select-Object -First 1
    $stop.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 800
    $knopf = ToggleName $win
    $held = $false   # der Klick kann das App-Fenster nach vorne holen – zurück ins Spiel, dann muss F13 wieder gedrückt sein
    for ($try = 1; $try -le 4 -and -not $held; $try++) { [void](Front $gameWnd); Start-Sleep -m 700; $held = F13Down }
    Check ("'Alle stoppen' laesst Dauersenden an (Knopf '{0}', im Spiel gedrueckt: {1})" -f $knopf, $held) ($knopf -like '*AN*' -and $held)

    "=== Absturz: Dauersenden laeuft nach dem Neustart weiter ==="
    Stop-Process -Id $p.Id -Force; Start-Sleep -m 800
    if (F13Down) { Key 0x7C -Up }   # ein hart beendeter Prozess kann keine Taste mehr loslassen
    Check ("Hart beendet, Merker steht noch ({0})" -f (SavedResume)) ((SavedResume) -eq $true)
    [void](Front $gameWnd)
    $p = Start-App -NoMute
    [void](Front $gameWnd); Start-Sleep -m 900
    $win = MainWin $p
    $held = $false
    for ($try = 1; $try -le 4 -and -not $held; $try++) { [void](Front $gameWnd); Start-Sleep -m 900; $held = F13Down }
    Check ("Nach Neustart: Dauersenden wieder an (F13: {0}, Knopf '{1}')" -f (F13Down), (ToggleName $win)) ((F13Down) -and (ToggleName $win) -like '*AN*')
    Check 'Protokoll: nach Neustart fortgesetzt' (@(LogTail | Where-Object { $_ -like '*nach Neustart fortgesetzt*' }).Count -ge 1)

    "=== Bewusst ausschalten ==="
    Combo 0x79; Start-Sleep -m 600
    Check ("Umschalt-Taste aus: F13 los ({0}), Merker geloescht ({1})" -f (-not (F13Down)), (SavedResume)) (-not (F13Down) -and (SavedResume) -eq $false)
    Combo 0x79; Start-Sleep -m 600
    Check ("Wieder an: Merker gesetzt ({0})" -f (SavedResume)) ((SavedResume) -eq $true)
    $quitOk = Quit $p
    Start-Sleep -m 300
    Check ("App bewusst beendet ({0}): F13 los ({1}), Merker geloescht ({2})" -f $quitOk, (-not (F13Down)), (SavedResume)) ($quitOk -and -not (F13Down) -and (SavedResume) -eq $false)
    $p = Start-App -NoMute
    [void](Front $gameWnd); Start-Sleep -m 900
    $win = MainWin $p
    [void](Front $gameWnd); Start-Sleep -m 900
    Check ("Nach bewusstem Beenden: beim Start aus (F13: {0}, Knopf '{1}')" -f (F13Down), (ToggleName $win)) (-not (F13Down) -and (ToggleName $win) -like '*aus*')
    Stop-Process -Id $p.Id; Start-Sleep -m 500

    "=== Nach dem Zurueckwechseln kein Auffrischen direkt hinterher (Test: regelmaessig alle 2 s) ==="
    Write-TestSettings 2
    $p = Start-App
    $win = MainWin $p
    $appWnd = [IntPtr]$win.Current.NativeWindowHandle
    [void](Front $gameWnd); Start-Sleep -m 500
    Combo 0x79; Start-Sleep -m 600
    [void](Front $appWnd); Start-Sleep -m 2600   # regelmaessiges Auffrischen waere jetzt faellig
    $r = Watch 1500 { [void](Front $gameWnd) }
    Check ("Zurueck ins Spiel: genau einmal gedrueckt, nicht gleich aufgefrischt ({0}x)" -f $r.Downs) ($r.Downs -eq 1 -and $r.EndDown)
    $r = Watch 2600 $null
    for ($try = 1; $try -le 3 -and $r.Disturbed; $try++) { [void](Front $gameWnd); Start-Sleep -m 600; $r = Watch 2600 $null }
    Check ("Danach wieder regelmaessig aufgefrischt ({0}x)" -f $r.Downs) ($r.Downs -ge 1 -and $r.EndDown)
    Key 0x13; Start-Sleep -m 60; Key 0x13 -Up; Start-Sleep -m 500
    Check ("Notfall-Taste: F13 los ({0}), Merker geloescht ({1})" -f (-not (F13Down)), (SavedResume)) (-not (F13Down) -and (SavedResume) -eq $false)
    Stop-Process -Id $p.Id
}
finally {
    Unclip
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    if ($game) { Stop-Process -Id $game.Id -EA SilentlyContinue }
    Start-Sleep -m 500
    if (F13Down) { Key 0x7C -Up }
    Remove-Item Env:\RPVS_TEST_RESUME -EA SilentlyContinue
    Remove-Item $settings, $mute -ErrorAction SilentlyContinue
    Remove-Item $soundDir, $fakeDir -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$env:TEMP\voice.robustbackup" (Join-Path $cfg 'voice.txt') -Force
    Copy-Item "$env:TEMP\voice-active.robustbackup" (Join-Path $cfg 'voice-active.cfg') -Force
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                                 OK" }
}
