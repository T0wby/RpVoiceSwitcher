# Testet Sound umbenennen/loeschen per UI-Automation (keine echten Klicks).
# Eigene Einstellungsdatei (--settings) und eigener Sound-Ordner - die echten Daten werden nicht angefasst.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$realHash = (Get-FileHash $realSettings).Hash
$tmp = Join-Path $env:TEMP "rpvs-soundedit-$PID"
$sounds = Join-Path $tmp 'Sounds'
$settings = Join-Path $tmp 'settings.json'
New-Item -ItemType Directory -Force $sounds | Out-Null
Set-Content (Join-Path $sounds '.starter-sounds') 'Test'   # keine Start-Sounds erzeugen
$realSoundDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'RP Voice Switcher\Sounds'
Copy-Item (Join-Path $realSoundDir 'Messingglocke.wav') (Join-Path $sounds 'Testglocke.wav')

$j = Get-Content $realSettings -Raw | ConvertFrom-Json
$p0 = $j.Profiles[0]; $p0.SoundHotkeys = [pscustomobject]@{ 'Testglocke.wav' = 'Control, Alt|F11' }
$j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
$j | Add-Member SoundFolder $sounds -Force
$j | Add-Member SoundsWithEffect @('Testglocke.wav') -Force
$j | ConvertTo-Json -Depth 6 | Set-Content $settings

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace W2 -Name D -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetDlgItem(System.IntPtr h, int id); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr SendMessage(System.IntPtr h, int msg, System.IntPtr w, System.IntPtr l);'
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function Buttons($root) { @($root.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) }
function Click($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 700 }
function Win($procId, $title) {
    for ($i = 0; $i -lt 20; $i++) {
        $tops = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $procId)))
        $w = ($tops + @($tops | ForEach-Object { $_.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window))) })) |
             Where-Object { $_.Current.Name -like $title } | Select-Object -First 1
        if ($w) { return $w }; Start-Sleep -m 200
    }
    throw "Fenster '$title' nicht gefunden"
}
function Check($name, $ok) { "{0,-60} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Settings { Get-Content $settings -Raw | ConvertFrom-Json }

try {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 4
    $win = Win $p.Id 'RP Voice Switcher'
    $tab = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600

    # --- Umbenennen ---
    Click (Buttons $win | Where-Object { $_.Current.Name -eq 'Sound umbenennen' } | Select-Object -First 1)
    $dlg = Win $p.Id 'Sound umbenennen'
    $dlg.FindFirst('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Edit))).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Klingel')
    Click (Buttons $dlg | Where-Object { $_.Current.Name -eq 'OK' } | Select-Object -First 1); Start-Sleep -m 600
    Check 'Umbenennen: Datei heisst jetzt Klingel.wav' ((Test-Path (Join-Path $sounds 'Klingel.wav')) -and -not (Test-Path (Join-Path $sounds 'Testglocke.wav')))
    $s = Settings
    Check 'Umbenennen: Shortcut wandert mit' ($s.Profiles[0].SoundHotkeys.'Klingel.wav' -eq 'Control, Alt|F11' -and -not $s.Profiles[0].SoundHotkeys.'Testglocke.wav')
    Check 'Umbenennen: "Durch Stimmeffekt" wandert mit' (($s.SoundsWithEffect -contains 'Klingel.wav') -and -not ($s.SoundsWithEffect -contains 'Testglocke.wav'))
    $texts = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name })
    Check 'Umbenennen: Karte zeigt neuen Namen' ($texts -contains 'Klingel')

    # --- Loeschen ---
    Click (Buttons $win | Where-Object { $_.Current.Name -eq 'Sound löschen' } | Select-Object -First 1)
    $mb = Win $p.Id 'Sound l*schen'
    [W2.D]::SendMessage([W2.D]::GetDlgItem([IntPtr]$mb.Current.NativeWindowHandle, 6), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    Start-Sleep 1
    Check 'Loeschen: Datei aus dem Sound-Ordner verschwunden' (-not (Test-Path (Join-Path $sounds 'Klingel.wav')))
    $s = Settings
    Check 'Loeschen: Shortcut und Effekt-Eintrag entfernt' ((-not $s.Profiles[0].SoundHotkeys.'Klingel.wav') -and -not ($s.SoundsWithEffect -contains 'Klingel.wav'))
    $bin = (New-Object -ComObject Shell.Application).NameSpace(10)
    $recycled = @($bin.Items() | Where-Object { $_.Name -like 'Klingel*' -and $_.Path -like '*$Recycle.Bin*' })
    Check 'Loeschen: liegt im Papierkorb' ($recycled.Count -gt 0)
    foreach ($r in $recycled) { Remove-Item -LiteralPath $r.Path -Force -EA SilentlyContinue }   # Testdatei aus dem Papierkorb entfernen
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $tmp -Recurse -Force -EA SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                             OK" }
}
