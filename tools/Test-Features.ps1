# Testet die Extras: Lautstaerke-Ausgleich (gemessen im Kabel), Notfall-Taste, OBS-Textdatei,
# Status-Anzeige (Groesse, bei Normal ausblenden), Probehoeren und die Einstellungen dazu.
# Arbeitet mit einer Kopie der Einstellungen (--settings), sichert voice.txt/voice-active.cfg und raeumt auf.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$probe = Join-Path $repo 'tools\AudioProbe\bin\out\AudioProbe.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"   # wird nur gelesen, nie veraendert
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"
$cfg = 'C:\Program Files\EqualizerAPO\config'
$voices = Join-Path $cfg 'rp-voices'
$voiceTxt = Join-Path $cfg 'voice.txt'
$tmp = Join-Path $env:TEMP 'rpvs-featuretest'
$obs = Join-Path $tmp 'obs.txt'
$realHash = (Get-FileHash $realSettings).Hash
New-Item -ItemType Directory -Force $tmp | Out-Null
Copy-Item $realSettings $settings -Force
Copy-Item $voiceTxt (Join-Path $tmp 'voice.backup') -Force
Copy-Item (Join-Path $cfg 'voice-active.cfg') (Join-Path $tmp 'voice-active.backup') -Force

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T4 -Name U -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);'
$AE = [Windows.Automation.AutomationElement]
function Key($vk, [switch]$Up) { [T4.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
# Nur global registrierte Kombinationen druecken - die faengt Windows ab, sie landen in keinem anderen Fenster.
function Combo([int[]]$vks) { foreach ($k in $vks) { Key $k }; Start-Sleep -m 60; [array]::Reverse($vks); foreach ($k in $vks) { Key $k -Up }; Start-Sleep -m 700 }
function Check($name, $ok) { "{0,-66} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Active { ((Get-Content $voiceTxt) | Where-Object { $_ -like '# Aktiv:*' }) -replace '# Aktiv: ', '' }
function Tone($mode) {
    $log = Join-Path $tmp 'probe.txt'
    & $probe 'CABLE Output' 1200 $mode 'CABLE Input' > $log
    # Die ersten 300 ms nicht werten: Dort steckt oft noch der vorige Ton im Kabel-Puffer (vor allem, wenn ein Spiel
    # oder Steam das Kabel gerade offen hat) - sonst misst "Seite" direkt nach "Mitte" noch den Mitte-Ton.
    (Get-Content $log | Where-Object { $_ -match '^\s*(\d+) ms\s+([\d,\.]+)' -and [int]$Matches[1] -gt 300 } |
        ForEach-Object { [double]($Matches[2] -replace ',', '.') } | Measure-Object -Maximum).Maximum
}
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function All($root, $type) { @($root.FindAll('Descendants', (Cond $AE::ControlTypeProperty $type))) }
function Click($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 700 }
function TopWindows($procId) { @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $procId))) }
function Win($procId, $title) {
    for ($i = 0; $i -lt 25; $i++) {
        $w = @(TopWindows $procId) + @(TopWindows $procId | ForEach-Object { $_.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window))) }) |
             Where-Object { $_.Current.Name -like $title -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Window } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -m 200
    }
    throw "Fenster '$title' nicht gefunden"
}
# Status-Anzeige = namenloses, kleines Fenster der App
function StatusOverlay($procId) { TopWindows $procId | Where-Object { $_.Current.Name -eq '' -and $_.Current.BoundingRectangle.Width -lt 800 -and -not $_.Current.IsOffscreen } | Select-Object -First 1 }
function Start-App([switch]$Visible) {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $argList = @('--settings', "`"$settings`""); if (-not $Visible) { $argList = @('--minimized') + $argList }
    $p = Start-Process $exe -ArgumentList $argList -PassThru; Start-Sleep 4; $p
}
function Settings { Get-Content $settings -Raw | ConvertFrom-Json }
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace T4 -Name P -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr dc, uint flags);'
# Screenshot nur ueber PrintWindow (kein Fokuswechsel, keine Eingaben)
function Shot($el, $path) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    $r = $el.Current.BoundingRectangle
    $bmp = New-Object Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    [T4.P]::PrintWindow([IntPtr]$el.Current.NativeWindowHandle, $dc, 2) | Out-Null
    $g.ReleaseHdc($dc); $g.Dispose(); $bmp.Save($path); $bmp.Dispose()
    "   Screenshot: $path"
}

$C = 0x11; $A = 0x12; $F8 = 0x77; $F9 = 0x78; $F10 = 0x79
$gainVoice = Join-Path $voices '98-testgain.txt'

try {
    Set-Content $gainVoice "# Name: Testgain`r`n# Lautstaerke: +6 dB`r`n# (keine Effekte)`r`n" -NoNewline
    $j = Settings
    $p0 = $j.Profiles[0]
    $p0.IncludeAllVoices = $true
    $p0.Hotkeys = [pscustomobject]@{ '00-normal.txt' = 'Control, Alt|F8'; '98-testgain.txt' = 'Control, Alt|F9' }
    $p0.HoldHotkeys = [pscustomobject]@{}
    $p0.SoundHotkeys = [pscustomobject]@{}
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    $extra = @{ ShowOverlay = $false; PlaySound = $false; PanicHotkey = 'Control, Alt|F10'; CheckForUpdates = $false
                ObsFileEnabled = $true; ObsFilePath = $obs; ObsTemplate = '{charakter}: {effekt}'
                ShowStatusOverlay = $true; StatusOverlayHideNormal = $true; StatusOverlayScale = 1.5; StatusOverlayOpacity = 0.8 }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
    $name = $p0.Name

    $p = Start-App

    "=== Lautstaerke-Ausgleich (Testton im Kabel) ==="
    Combo @($C, $A, $F8)
    $midN = Tone 'mid'; $sideN = Tone 'side'
    Combo @($C, $A, $F9)
    $midG = Tone 'mid'; $sideG = Tone 'side'
    Check 'voice.txt enthaelt den Ausgleich (+6 dB = Faktor 1,995)' ((Get-Content $voiceTxt -Raw) -match 'Copy: L=1\.99526\*L R=1\.99526\*R')
    Check ("Stimme +6 dB: doppelt so laut ({0:N2} statt {1:N2})" -f $midG, $midN) ($midG -gt 1.8 * $midN -and $midG -lt 2.2 * $midN)
    Check ("Sounds ohne Effekt bleiben gleich laut ({0:N2} / {1:N2})" -f $sideG, $sideN) ([Math]::Abs($sideG - $sideN) -lt 0.1 * $sideN)

    "=== OBS-Textdatei ==="
    Check "OBS: '$name`: Testgain'" ((Get-Content $obs -Raw) -eq "$name`: Testgain")

    "=== Status-Anzeige ==="
    $ov = StatusOverlay $p.Id
    Check 'Mit Effekt-Stimme sichtbar' ($null -ne $ov)
    if ($ov) { $h = $ov.Current.BoundingRectangle.Height; Check ("Groesse 150 % (Hoehe {0:N0} px)" -f $h) ($h -gt 40) }

    "=== Notfall-Taste ==="
    Combo @($C, $A, $F10)
    Start-Sleep -m 400
    Check ("Notfall: zurueck auf Normal (ist: {0})" -f (Active)) ((Get-Content $voiceTxt -Raw) -match '# Datei: 00-')
    Check 'Notfall: Status-Anzeige bei Normal ausgeblendet' ($null -eq (StatusOverlay $p.Id))
    Check "OBS bei Normal: '$name`: '" ((Get-Content $obs -Raw) -eq "$name`: ")
    Check 'Ausgleich bei Normal nicht mehr aktiv' (-not ((Get-Content $voiceTxt -Raw) -match 'Lautstaerke-Ausgleich'))
    Stop-Process -Id $p.Id

    "=== Probehoeren + Einstellungen (sichtbares Fenster) ==="
    $p = Start-App -Visible
    $win = Win $p.Id 'RP Voice Switcher'
    Combo @($C, $A, $F9)
    Click (All $win ([Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like 'Probeh*ren' } | Select-Object -First 1)
    $test = Win $p.Id 'Probeh*'
    $combo = All $test ([Windows.Automation.ControlType]::ComboBox) | Select-Object -First 1
    Check ("Probehoeren: aktive Stimme vorausgewaehlt ({0})" -f $combo.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name) `
          ($combo.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name -eq 'Testgain')
    Click (All $test ([Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like '*Aufnehmen*' } | Select-Object -First 1)
    & $probe 'CABLE Output' 5500 'mid' 'CABLE Input' | Out-Null   # Testton ins Kabel, damit "mit Effekt" etwas zu hoeren ist
    Start-Sleep 2
    $texts = All $test ([Windows.Automation.ControlType]::Text) | ForEach-Object { $_.Current.Name }
    $status = $texts | Where-Object { $_ -like 'Aufgenommen*' -or $_ -like 'Aufnahme fehlgeschlagen*' } | Select-Object -First 1
    "   Status:   $status"
    "   Ergebnis: " + (($texts | Where-Object { $_ -like '*Mikrofon*nichts*' -or $_ -like '*kommt*' -or $_ -like '*kam nichts*' }) -join ' / ')
    Check 'Probehoeren: Aufnahme abgeschlossen' ($status -like 'Aufgenommen*')
    $play = All $test ([Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like '*Mit Effekt*' } | Select-Object -First 1
    Check 'Probehoeren: Abspielen freigeschaltet' ($play.Current.IsEnabled)
    Shot $test (Join-Path $env:TEMP 'rpvs-screens\probehoeren.png')
    Click (All $test ([Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like 'Schlie*en' } | Select-Object -First 1)

    # Einstellungen ueber das Menue Extras oeffnen
    $extras = All $win ([Windows.Automation.ControlType]::MenuItem) | Where-Object { $_.Current.Name -eq 'Extras' } | Select-Object -First 1
    $extras.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 500
    $menuItems = @($AE::RootElement.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem))))
    $panicItem = $menuItems | Where-Object { $_.Current.Name -like 'Notfall*' } | Select-Object -First 1
    Check 'Menue Extras: Notfall- und Probehoeren-Eintrag vorhanden' (($null -ne $panicItem) -and ($menuItems | Where-Object { $_.Current.Name -like 'Probeh*' }))
    # (Kuerzeltexte zeigt WPF der UI-Automation nicht - die prueft der Screenshot des Menues)
    $popup = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.ClassName -like '*Popup*' -or ($_.Current.Name -eq '' -and $_.Current.BoundingRectangle.Height -gt 150) } | Select-Object -First 1
    if ($popup) { Shot $popup (Join-Path $env:TEMP 'rpvs-screens\menue-extras.png') }
    Click ($menuItems | Where-Object { $_.Current.Name -like 'Einstellungen*' } | Select-Object -First 1)
    $set = Win $p.Id 'Einstellungen*'
    $pb = All $set ([Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -eq 'Notfall-Taste' } | Select-Object -First 1
    $pbText = (All $pb ([Windows.Automation.ControlType]::Text) | Select-Object -First 1).Current.Name
    Check ("Einstellungen: Notfall-Taste angezeigt ({0})" -f $pbText) ($pbText -eq 'Strg + Alt + F10')
    $slider = All $set ([Windows.Automation.ControlType]::Slider) | Where-Object { $_.Current.Name -like 'Gr*e der Status-Anzeige' } | Select-Object -First 1
    $slider.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(1.2); Start-Sleep -m 600
    Check ("Regler Groesse speichert ({0})" -f (Settings).StatusOverlayScale) ((Settings).StatusOverlayScale -eq 1.2)
    $tb = All $set ([Windows.Automation.ControlType]::Edit) | Where-Object { $_.Current.Name -eq 'Text f*r OBS' -or $_.Current.Name -like 'Text f*' } | Select-Object -First 1
    Check 'OBS-Vorlage im Feld' ($tb.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '{charakter}: {effekt}')
    Shot $set (Join-Path $env:TEMP 'rpvs-screens\einstellungen.png')
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                     OK" }
    Remove-Item $gainVoice -EA SilentlyContinue
    Copy-Item (Join-Path $tmp 'voice.backup') $voiceTxt -Force
    Copy-Item (Join-Path $tmp 'voice-active.backup') (Join-Path $cfg 'voice-active.cfg') -Force
    Remove-Item $tmp -Recurse -Force -EA SilentlyContinue
}
