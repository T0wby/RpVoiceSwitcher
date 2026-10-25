# Testet "Sounds auswählen" pro Charakter (wie bei den Stimmen):
#  - Charakter mit fester Auswahl zeigt nur seine Sounds, Statusleiste "x von y"
#  - Shortcuts von Sounds außerhalb des Charakters sind nicht belegt (Taste bleibt frei)
#  - Auswahlmodus: alle Sounds mit Haken, Haken setzen speichert, "Fertig" zeigt die neue Auswahl
#  - Charakter ohne Auswahl ("alle") zeigt alles; neu in den Ordner kopierte Sounds landen nur dort
#  - Alte Einstellungen ohne Sound-Auswahl: alle Sounds wie bisher
# Arbeitet mit einer Kopie der Einstellungen (--settings) und einem eigenen Sound-Ordner im TEMP.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-soundsel-settings-$PID.json"
$soundDir = Join-Path $env:TEMP "rpvs-soundsel-sounds-$PID"
$realHash = (Get-FileHash $realSettings).Hash

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T10 -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool RegisterHotKey(System.IntPtr h, int id, uint mods, uint vk);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool UnregisterHotKey(System.IntPtr h, int id);
'@
$AE = [Windows.Automation.AutomationElement]
function Check($name, $ok) { "{0,-80} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function ById($root, $id) { @($root.FindAll('Descendants', (Cond $AE::AutomationIdProperty $id))) | Select-Object -First 1 }
function Invoke($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 700 }
# Ist die Tastenkombination frei? (Strg+Alt = 0x2 | 0x1)
function KeyFree([int]$vk) { $ok = [T10.U]::RegisterHotKey([IntPtr]::Zero, 77, 3, $vk); if ($ok) { [void][T10.U]::UnregisterHotKey([IntPtr]::Zero, 77) }; $ok }

function Tone([string]$name, [double]$f) {
    $rate = 44100; $n = [int](0.6 * $rate)
    $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter($ms)
    $w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + 2 * $n)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$rate); $w.Write([int](2 * $rate)); $w.Write([int16]2); $w.Write([int16]16)
    $w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int](2 * $n))
    for ($i = 0; $i -lt $n; $i++) { $w.Write([int16]([Math]::Sin(2 * [Math]::PI * $f * $i / $rate) * 0.1 * 32767)) }
    $w.Flush(); [IO.File]::WriteAllBytes((Join-Path $soundDir $name), $ms.ToArray())
}
New-Item -ItemType Directory $soundDir -Force | Out-Null
Tone 'Eins.wav' 440; Tone 'Zwei.wav' 550; Tone 'Drei.wav' 660
Set-Content (Join-Path $soundDir '.starter-sounds') 'Test: keine Start-Sounds'

$j = Get-Content $realSettings -Raw | ConvertFrom-Json
$p0 = $j.Profiles[0]
$alle = [pscustomobject]@{ Name = 'Alle'; IncludeAllVoices = $true; Voices = @(); Hotkeys = [pscustomobject]@{}; HoldHotkeys = [pscustomobject]@{}; SoundHotkeys = [pscustomobject]@{} }
$auswahl = [pscustomobject]@{ Name = 'Auswahl'; IncludeAllVoices = $true; Voices = @(); Hotkeys = [pscustomobject]@{}; HoldHotkeys = [pscustomobject]@{}
    SoundHotkeys = [pscustomobject]@{ 'Eins.wav' = 'Control, Alt|F9'; 'Zwei.wav' = 'Control, Alt|F11' }
    IncludeAllSounds = $false; Sounds = @('Eins.wav') }
$j.Profiles = @($alle, $auswahl); $j.ActiveProfile = 'Auswahl'
$extra = @{ SoundFolder = $soundDir; SoundMonitor = $false; PushToTalkEnabled = $false; ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false
            ShowStatusOverlay = $false; SetupWizardDone = $true; LastSeenVersion = '99.0.0'; SoundsWithEffect = @(); SoundRepeat = [pscustomobject]@{} }
foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
$j | ConvertTo-Json -Depth 6 | Set-Content $settings

function Start-App {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    $w = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $tab = @($w.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600
    $p, $w
}
function Cards($win) {
    @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text)))) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -in 'Eins', 'Zwei', 'Drei', 'Vier' }
}
function StatusText($win) {
    @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text)))) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like 'Soundboard:*' } | Select-Object -First 1
}
function Boxes($win) { @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::CheckBox)))) | Where-Object { $_.Current.Name -like 'Gehört zum Charakter:*' } }
function BoxState($b) { $b.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq 'On' }
function Saved { (Get-Content $settings -Raw | ConvertFrom-Json).Profiles | Where-Object Name -eq 'Auswahl' }

try {
    "=== Charakter mit fester Sound-Auswahl ==="
    $p, $win = Start-App
    $c = @(Cards $win)
    Check ("Nur 'Eins' sichtbar ({0})" -f ($c -join ', ')) ($c.Count -eq 1 -and $c[0] -eq 'Eins')
    Check ("Statusleiste: '{0}'" -f (StatusText $win)) ((StatusText $win) -eq 'Soundboard: 1 von 3 Sounds')
    Check 'Shortcut von Eins (Strg+Alt+F9) ist belegt' (-not (KeyFree 0x78))
    Check 'Shortcut von Zwei (Strg+Alt+F11, nicht im Charakter) bleibt frei' (KeyFree 0x7A)

    "=== Sounds auswaehlen ==="
    Invoke (ById $win 'SelectSoundsButton')
    $c = @(Cards $win); $boxes = @(Boxes $win)
    $states = ($boxes | ForEach-Object { "$($_.Current.Name.Split(':')[1].Trim())=$(BoxState $_)" }) -join ', '
    Check ("Auswahlmodus: alle drei mit Haken ({0})" -f $states) ($c.Count -eq 3 -and $boxes.Count -eq 3 -and $states -like '*Eins=True*' -and $states -like '*Zwei=False*')
    Check ("Knopf heisst jetzt 'Fertig' ({0})" -f (ById $win 'SelectSoundsButton').Current.Name) ((ById $win 'SelectSoundsButton').Current.Name -eq 'Fertig')
    $drei = $boxes | Where-Object { $_.Current.Name -like '*Drei' }
    $drei.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -m 500
    $s = Saved
    Check ("Haken bei Drei gespeichert ({0})" -f ($s.Sounds -join ', ')) (($s.Sounds -contains 'Drei.wav') -and ($s.Sounds -contains 'Eins.wav') -and -not ($s.Sounds -contains 'Zwei.wav'))
    Invoke (ById $win 'SelectSoundsButton')
    $c = @(Cards $win)
    Check ("Fertig: Eins und Drei sichtbar ({0}), '{1}'" -f ($c -join ', '), (StatusText $win)) ($c.Count -eq 2 -and $c -contains 'Drei' -and (StatusText $win) -eq 'Soundboard: 2 von 3 Sounds')

    # "Keine" und "Alle"
    Invoke (ById $win 'SelectSoundsButton')
    Invoke (ById $win 'SelectNoSounds')
    $none = @((Saved).Sounds).Count
    Invoke (ById $win 'SelectAllSounds')
    $all = @((Saved).Sounds).Count
    Invoke (ById $win 'SelectSoundsButton')
    Check ("'Keine' leert ({0}), 'Alle' nimmt alle ({1}), sichtbar {2}" -f $none, $all, @(Cards $win).Count) ($none -eq 0 -and $all -eq 3 -and @(Cards $win).Count -eq 3)
    Check 'Jetzt ist auch Zwei (Strg+Alt+F11) belegt' (-not (KeyFree 0x7A))
    # zurueck auf Eins + Drei fuer den Rest
    Invoke (ById $win 'SelectSoundsButton')
    (Boxes $win | Where-Object { $_.Current.Name -like '*Zwei' }).GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -m 400
    Invoke (ById $win 'SelectSoundsButton')

    "=== Charakter ohne Auswahl / neuer Sound im Ordner ==="
    Tone 'Vier.wav' 770; Start-Sleep 2
    $c = @(Cards $win)
    Check ("Neuer Sound im Ordner: im Auswahl-Charakter nicht dabei ({0})" -f ($c -join ', ')) ($c -notcontains 'Vier' -and (StatusText $win) -eq 'Soundboard: 2 von 4 Sounds')
    $chip = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | Where-Object { $_.Current.Name -eq 'Alle' } | Select-Object -First 1
    Invoke $chip
    $c = @(Cards $win)
    Check ("Charakter 'Alle': alle vier ({0}), '{1}'" -f ($c -join ', '), (StatusText $win)) ($c.Count -eq 4 -and (StatusText $win) -eq 'Soundboard: 4 Sounds')
    Check 'Im Charakter Alle ist Strg+Alt+F9 frei (Eins hat dort keinen Shortcut)' (KeyFree 0x78)
    $alleSaved = (Get-Content $settings -Raw | ConvertFrom-Json).Profiles | Where-Object Name -eq 'Alle'
    Check ("Charakter 'Alle' bleibt bei 'alle Sounds' (IncludeAllSounds={0})" -f $alleSaved.IncludeAllSounds) ($alleSaved.IncludeAllSounds -eq $true)
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings -ErrorAction SilentlyContinue
    Remove-Item $soundDir -Recurse -Force -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                                 OK" }
}
