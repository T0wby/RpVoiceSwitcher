# Testet per UI-Automation: Stimme anlegen, bearbeiten, kopieren, loeschen; Charakterwechsel beendet die Stimmenauswahl.
# Arbeitet mit einer Kopie der Einstellungen (--settings) und raeumt angelegte Testdateien wieder auf.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"   # wird nur gelesen, nie veraendert
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"     # eigene Kopie fuer den Test
$voices = 'C:\Program Files\EqualizerAPO\config\rp-voices'
$realHash = (Get-FileHash $realSettings).Hash
Copy-Item $realSettings $settings -Force
$before = Get-ChildItem $voices -File | Select-Object -ExpandProperty Name

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace W -Name D -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetDlgItem(System.IntPtr h, int id); [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr SendMessage(System.IntPtr h, int msg, System.IntPtr w, System.IntPtr l);'
# Klassische Windows-MessageBox: Button per ID druecken (6 = Ja), deren Buttons sieht UI-Automation hier nicht
function PressYes($mb) { $h = [IntPtr]$mb.Current.NativeWindowHandle; [W.D]::SendMessage([W.D]::GetDlgItem($h, 6), 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -m 700 }
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function And($a, $b) { New-Object Windows.Automation.AndCondition($a, $b) }
function Btn($root, $pattern) { @($root.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | Where-Object { $_.Current.Name -like $pattern } | Select-Object -First 1 }
function BtnsByHelp($root, $pattern) { @(@($root.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | Where-Object { $_.Current.HelpText -like $pattern }) }
function Click($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 700 }
function Win($procId, $title) {
    for ($i = 0; $i -lt 20; $i++) {
        $w = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $procId))) + @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $procId)) | ForEach-Object { $_.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window))) }) | Where-Object { $_.Current.Name -like $title } | Select-Object -First 1
        if ($w -and $w.Current.ControlType -eq [Windows.Automation.ControlType]::Window) { return $w }
        Start-Sleep -m 200
    }
    throw "Fenster '$title' nicht gefunden"
}
function Fill($dlg, [string[]]$values) {
    $edits = @($dlg.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Edit))))
    for ($i = 0; $i -lt $values.Count; $i++) { $edits[$i].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($values[$i]) }
}
function Names($win) { @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) }
function Check($name, $ok) { "{0,-60} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }

try {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 4
    $win = Win $p.Id 'RP Voice Switcher'

    # --- Neue Stimme ---
    Click (Btn $win '+ Neue Stimme')
    $dlg = Win $p.Id 'Neue Stimme'
    Fill $dlg @('Testkobold', 'Nur ein Test', 'Pitch +5, Formant +4')
    Click (Btn $dlg 'Speichern'); Start-Sleep -m 600
    $file = Get-ChildItem $voices -Filter '*testkobold*.txt' | Select-Object -First 1
    Check 'Neue Stimme: Datei angelegt' ($null -ne $file)
    $content = Get-Content $file.FullName -Encoding UTF8
    Check 'Neue Stimme: Kopfzeilen richtig' (($content -contains '# Name: Testkobold') -and ($content -contains '# Graillon: Pitch +5, Formant +4'))
    Check 'Neue Stimme: in der Liste sichtbar' ((Names $win) -contains 'Testkobold')
    Check 'Neue Stimme: gelber Graillon-Hinweis' ((Names $win) -contains 'Mit Graillon: Pitch +5, Formant +4')

    # --- Bearbeiten (letzte Stimme = Testkobold) ---
    Click (BtnsByHelp $win 'Name, Beschreibung, Graillon-Hinweis und Lautst*rke bearbeiten')[-1]
    $dlg = Win $p.Id 'Stimme bearbeiten'
    Fill $dlg @('Testkobold', 'Geaenderte Beschreibung', 'Pitch +6')
    Click (Btn $dlg 'Speichern'); Start-Sleep -m 600
    $content = Get-Content $file.FullName -Encoding UTF8
    Check 'Bearbeiten: Beschreibung + Hinweis geaendert' (($content -contains '# Beschreibung: Geaenderte Beschreibung') -and ($content -contains '# Graillon: Pitch +6'))
    Check 'Bearbeiten: Effekt-Zeilen erhalten' (($content | Where-Object { $_ -like '# Noch keine Effekte*' }).Count -eq 1)

    # --- Kopieren ---
    Add-Content $file.FullName 'Preamp: -3 dB' -Encoding UTF8
    Start-Sleep -m 800
    Click (BtnsByHelp $win 'Kopieren*')[-1]
    $dlg = Win $p.Id 'Stimme kopieren'
    Click (Btn $dlg 'Speichern'); Start-Sleep -m 600
    $copy = Get-ChildItem $voices -Filter '*testkobold-kopie*.txt' | Select-Object -First 1
    Check 'Kopieren: Datei angelegt' ($null -ne $copy)
    Check 'Kopieren: Effekte uebernommen' ((Get-Content $copy.FullName -Encoding UTF8) -contains 'Preamp: -3 dB')

    # --- Loeschen (zweimal die letzte Stimme) ---
    foreach ($n in 1, 2) {
        Click (BtnsByHelp $win 'Stimme l*schen')[-1]
        $mb = Win $p.Id 'Stimme l*schen'
        PressYes $mb
    }
    Start-Sleep -m 600
    Check 'Loeschen: beide Testdateien weg' (-not (Get-ChildItem $voices -Filter '*testkobold*.txt'))
    Check 'Loeschen: aus der Liste verschwunden' (-not ((Names $win) -contains 'Testkobold'))

    # --- Charakterwechsel beendet die Stimmenauswahl ---
    Click (Btn $win '+ Neu')
    $dlg = Win $p.Id 'Neuer Charakter'
    Fill $dlg @('Testcharakter')
    Click (Btn $dlg 'OK'); Start-Sleep -m 600
    Check 'Neuer Charakter startet in der Stimmenauswahl' ($null -ne (Btn $win 'Fertig'))
    Click (Btn $win 'Standard')
    Check 'Wechsel zu Standard beendet die Auswahl' (($null -eq (Btn $win 'Fertig')) -and ($null -ne (Btn $win 'Stimmen ausw*')))

    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                     OK" }
    Get-ChildItem $voices -File | Where-Object { $before -notcontains $_.Name } | Remove-Item
    # Loesch-Merkliste von Testeintraegen befreien
    $del = Join-Path $voices '.geloeschte-stimmen'
    if (Test-Path $del) {
        $rest = Get-Content $del | Where-Object { $_ -notlike '*testkobold*' }
        if ($rest) { Set-Content $del $rest } else { Remove-Item $del }
    }
}
