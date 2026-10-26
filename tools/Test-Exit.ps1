# Testet das Beenden über Datei → Beenden (wie Strg+Q, Tray „Beenden“ und Update-Installation):
# Die App muss ohne Fehlermeldung und ohne neuen Eintrag in fehler.log beendet werden –
# mit Status-Anzeige an, und mit „nur bei Effekt zeigen“ (Anzeige versteckt).
# Arbeitet mit einer Kopie der Einstellungen (--settings).
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$errorLog = "$env:LOCALAPPDATA\RpVoiceSwitcher\fehler.log"
$realHash = (Get-FileHash $realSettings).Hash

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [Windows.Automation.AutomationElement]
function Check($name, $ok) { "{0,-80} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function LogSize { if (Test-Path $errorLog) { (Get-Item $errorLog).Length } else { 0 } }

function Run-Exit($label, [bool]$hideNormal) {
    $settings = Join-Path $env:TEMP "rpvs-exit-settings-$PID.json"
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $extra = @{ ShowStatusOverlay = $true; StatusOverlayHideNormal = $hideNormal; ShowOverlay = $true; PlaySound = $false
                CheckForUpdates = $false; SetupWizardDone = $true; LastSeenVersion = '99.0.0'; TourSeen = $true; KeepCableDefaultMic = $false }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings -Encoding UTF8

    $before = LogSize
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
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
    Check "$label`: Menüpunkt Beenden gefunden" ($null -ne $quit)
    if ($quit) { $quit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

    $exited = $p.WaitForExit(6000)
    $box = $null
    if (-not $exited) {
        $box = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.ClassName -eq '#32770' } | Select-Object -First 1
        Stop-Process -Id $p.Id -EA SilentlyContinue
    }
    Check "$label`: App beendet sich innerhalb von 6 s" $exited
    Check "$label`: keine Fehlermeldung" ($null -eq $box)
    Check "$label`: kein neuer Eintrag in fehler.log" ((LogSize) -eq $before)
    Remove-Item $settings -EA SilentlyContinue
}

try {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    Run-Exit 'Status-Anzeige sichtbar' $false
    Run-Exit 'Status-Anzeige nur bei Effekt' $true
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Check 'Echte Einstellungen unveraendert' ((Get-FileHash $realSettings).Hash -eq $realHash)
}
