# Testet den Rundgang (Hilfe → Rundgang durch die App) und macht von jedem Schritt einen Screenshot
# (für die Anleitung): Schritte durchklicken, Tab-Wechsel, Zurück, Esc/Fertig, "schon gesehen" gespeichert.
# Arbeitet mit einer Kopie der Einstellungen (--settings), neutralen Beispiel-Charakteren und eigenem Sound-Ordner.
param([string]$ShotDir = (Join-Path $env:TEMP 'rpvs-tour-shots'))
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-tour-settings-$PID.json"
$soundDir = Join-Path $env:TEMP "rpvs-tour-sounds-$PID"
$realHash = (Get-FileHash $realSettings).Hash
New-Item -ItemType Directory -Force $ShotDir, $soundDir | Out-Null
Get-ChildItem $ShotDir -Filter 'tour-*.png' | Remove-Item

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace T11 -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
function Check($name, $ok) { "{0,-80} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function ById($root, $id) { @($root.FindAll('Descendants', (Cond $AE::AutomationIdProperty $id))) | Select-Object -First 1 }
function Shot($el, $file) {
    $h = [IntPtr]$el.Current.NativeWindowHandle
    $r = New-Object T11.U+RECT; [T11.U]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [T11.U]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($file); $bmp.Dispose()
}

# Beispiel-Sounds (aus deinem Sound-Ordner, falls vorhanden)
$mySounds = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'RP Voice Switcher\Sounds'
$pick = 'Glocke einmal.wav', 'Gong Kampfbeginn.wav', 'Applaus Tribüne.wav', 'Regen Amegakure (Loop).wav', 'Trommelwirbel und Tusch.wav', 'Windspiel.wav'
foreach ($s in $pick) { $src = Join-Path $mySounds $s; if (Test-Path $src) { Copy-Item $src $soundDir } }
Set-Content (Join-Path $soundDir '.starter-sounds') 'Test'

$j = Get-Content $realSettings -Raw | ConvertFrom-Json
$hot = [pscustomobject]@{}; for ($i = 0; $i -le 8; $i++) { $f = @('00-normal','01-maske','02-tief','03-funk','04-megafon','05-hoehle','06-metallhelm','07-geist','08-riese')[$i]; $hot | Add-Member "$f.txt" "Control, Alt|D$i" }
$snd = [pscustomobject]@{ 'Glocke einmal.wav' = 'Control|NumPad1'; 'Gong Kampfbeginn.wav' = 'Control|NumPad2'; 'Applaus Tribüne.wav' = 'Control|NumPad3'; 'Regen Amegakure (Loop).wav' = 'Control|NumPad0' }
$std = [pscustomobject]@{ Name = 'Standard'; IncludeAllVoices = $false; Voices = @('01-maske.txt','02-tief.txt','03-funk.txt','04-megafon.txt','05-hoehle.txt','06-metallhelm.txt','07-geist.txt','08-riese.txt')
    Hotkeys = $hot; HoldHotkeys = [pscustomobject]@{ '03-funk.txt' = 'None|F9' }; SoundHotkeys = $snd; IncludeAllSounds = $true; Sounds = @() }
$ninja = [pscustomobject]@{ Name = 'Ninja'; IncludeAllVoices = $false; Voices = @('01-maske.txt'); Hotkeys = [pscustomobject]@{}; HoldHotkeys = [pscustomobject]@{}; SoundHotkeys = [pscustomobject]@{}; IncludeAllSounds = $true; Sounds = @() }
$piraten = [pscustomobject]@{ Name = 'Piratin'; IncludeAllVoices = $false; Voices = @('02-tief.txt'); Hotkeys = [pscustomobject]@{}; HoldHotkeys = [pscustomobject]@{}; SoundHotkeys = [pscustomobject]@{}; IncludeAllSounds = $true; Sounds = @() }
$j.Profiles = @($std, $ninja, $piraten); $j.ActiveProfile = 'Standard'
$extra = @{ SoundFolder = $soundDir; SoundMonitor = $true; SoundMonitorVolume = 0.5; SoundVolume = 1.0; PushToTalkEnabled = $true; PushToTalkKey = $null; PttLatchHotkey = 'None|Multiply'
            ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false; ShowStatusOverlay = $false; SetupWizardDone = $true; LastSeenVersion = '99.0.0'
            TourSeen = $false; SoundsWithEffect = @(); SoundRepeat = [pscustomobject]@{ 'Regen Amegakure (Loop).wav' = 0 }
            WindowLeft = 40; WindowTop = 40; WindowWidth = 880; WindowHeight = 800; WindowMaximized = $false }
foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
$j | ConvertTo-Json -Depth 6 | Set-Content $settings -Encoding UTF8

try {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    Check 'Testlauf: kein Rundgang von selbst' ($null -eq (ById $win 'TourCard'))

    # Hilfe → Rundgang
    $help = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -eq 'Hilfe' } | Select-Object -First 1
    $help.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 600
    $item = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -like 'Rundgang*' } | Select-Object -First 1
    if (-not $item) { $item = @($AE::RootElement.FindAll('Descendants', (Cond $AE::NameProperty 'Rundgang durch die App'))) | Select-Object -First 1 }
    Check 'Menüpunkt "Rundgang durch die App" vorhanden' ($null -ne $item)
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 1200

    $titles = @(); $tabs = @(); $n = 0
    while ($n -lt 20) {
        $title = (ById $win 'TourTitle').Current.Name
        $counter = (ById $win 'TourCounter').Current.Name
        if (-not $title) { break }
        $sel = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) |
               Where-Object { $_.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } | Select-Object -First 1
        $titles += $title; $tabs += $sel.Current.Name
        Shot $win (Join-Path $ShotDir ('tour-{0:D2}.png' -f ($n + 1)))
        "   {0,-12} {1,-12} {2}" -f $counter, $sel.Current.Name, $title
        $n++
        if ($n -eq 3) {
            # einmal zurück und wieder vor
            (ById $win 'TourBack').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 500
            $backTitle = (ById $win 'TourTitle').Current.Name
            Check ("Zurück zeigt wieder '{0}'" -f $backTitle) ($backTitle -eq $titles[1])
            (ById $win 'TourNext').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 500
        }
        $next = ById $win 'TourNext'
        $last = $next.Current.Name -eq 'Fertig'
        $next.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 900
        if ($last) { break }
    }
    Check ("Rundgang hat {0} Schritte mit verschiedenen Titeln" -f $titles.Count) ($titles.Count -ge 12 -and (@($titles | Select-Object -Unique).Count -eq $titles.Count))
    Check ("Soundboard-Schritte zeigen den Soundboard-Tab ({0})" -f ($tabs -join ',')) ((@($tabs | Where-Object { $_ -eq 'Soundboard' }).Count -ge 4) -and $tabs[2] -eq 'Stimmen')
    Check 'Nach "Fertig" ist der Rundgang weg' ($null -eq (ById $win 'TourCard'))
    $seen = (Get-Content $settings -Raw | ConvertFrom-Json).TourSeen
    Check ("Als gesehen gespeichert (TourSeen={0})" -f $seen) ($seen -eq $true)

    Stop-Process -Id $p.Id; Start-Sleep -m 600

    # Neuer Start (UIA sieht einen zweiten Rundgang im selben Prozess nicht zuverlässig): mitten drin mit ✕ beenden
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $help = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -eq 'Hilfe' } | Select-Object -First 1
    $help.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 600
    $item = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -like 'Rundgang*' } | Select-Object -First 1
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 1000
    (ById $win 'TourNext').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 600
    $mid = (ById $win 'TourTitle').Current.Name
    (ById $win 'TourClose').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 600
    Check ("Mitten im Rundgang ('{0}') mit ✕ beendet" -f $mid) ($mid -eq 'Charaktere' -and $null -eq (ById $win 'TourCard'))
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings -ErrorAction SilentlyContinue
    Remove-Item $soundDir -Recurse -Force -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                                 OK" }
}
