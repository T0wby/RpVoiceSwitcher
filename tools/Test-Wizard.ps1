# Testet den Einrichtungs-Assistenten per UI-Automation (ohne Aktions-Buttons zu druecken - die oeffnen Webseiten bzw. starten neu).
# 1) Unvollstaendige Einrichtung (Zielgeraet gibt es nicht) -> Assistent oeffnet sich beim Start, offene Schritte werden erkannt.
# 2) Vollstaendige Einrichtung -> Assistent bleibt zu, wird als erledigt gemerkt; ueber das Menue alle Schritte durchgehen.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"
$shots = Join-Path $env:TEMP 'rpvs-screens'
$cfg = 'C:\Program Files\EqualizerAPO\config'
$realHash = (Get-FileHash $realSettings).Hash
New-Item -ItemType Directory -Force $shots | Out-Null
Copy-Item (Join-Path $cfg 'voice.txt') "$env:TEMP\voice.wizardbackup" -Force
Copy-Item (Join-Path $cfg 'voice-active.cfg') "$env:TEMP\voice-active.wizardbackup" -Force

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace T5 -Name P -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr dc, uint flags);'
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function All($root, $type) { @($root.FindAll('Descendants', (Cond $AE::ControlTypeProperty $type))) }
function Btn($root, $pattern) { All $root ([Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like $pattern } | Select-Object -First 1 }
function Texts($root) { All $root ([Windows.Automation.ControlType]::Text) | ForEach-Object { $_.Current.Name } }
function Click($el) { $el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 600 }
function Check($name, $ok) { "{0,-66} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function FindWin($procId, $title, [int]$tries = 25) {
    for ($i = 0; $i -lt $tries; $i++) {
        $top = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $procId)))
        $w = @($top) + @($top | ForEach-Object { $_.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window))) }) |
             Where-Object { $_.Current.Name -like $title -and $_.Current.ControlType -eq [Windows.Automation.ControlType]::Window } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -m 200
    }
    $null
}
function Shot($el, $path) {
    $r = $el.Current.BoundingRectangle
    $bmp = New-Object Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
    [T5.P]::PrintWindow([IntPtr]$el.Current.NativeWindowHandle, $dc, 2) | Out-Null
    $g.ReleaseHdc($dc); $g.Dispose(); $bmp.Save($path); $bmp.Dispose()
}
function Settings { Get-Content $settings -Raw | ConvertFrom-Json }
function Write-TestSettings($extra) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $base = @{ SetupWizardDone = $false; SetupWizardStep = 0; CheckForUpdates = $false; ShowStatusOverlay = $false; PlaySound = $false }
    foreach ($k in $base.Keys) { $j | Add-Member $k $base[$k] -Force }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
function Start-App { Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600; $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5; $p }
function BadgeOf($wiz) { (Texts $wiz) | Where-Object { $_ -in @('✓ Erledigt', 'Noch offen', 'Optional') } | Select-Object -First 1 }

try {
    "=== Unvollstaendig: Assistent oeffnet sich beim Start ==="
    Write-TestSettings @{ TargetDevice = 'Gibt es nicht 123' }
    $p = Start-App
    $wiz = FindWin $p.Id 'Einrichtungs-Assistent*'
    Check 'Assistent beim ersten Start geoeffnet' ($null -ne $wiz)
    if ($wiz) {
        Check 'Startet bei Schritt 1 (Willkommen)' ((Texts $wiz) -contains 'Schritt 1 von 10')
        Click (Btn $wiz 'Schritt 3:*')
        Check ("Schritt 3 (Equalizer APO) noch offen ({0})" -f (BadgeOf $wiz)) ((BadgeOf $wiz) -eq 'Noch offen')
        Click (Btn $wiz 'Schritt 5:*')
        Check 'Schritt 5 (Neustart) wartet auf Schritt 3' ((Texts $wiz) -contains 'Erst Schritt 3 abschließen.')
        Click (Btn $wiz 'Schritt 2:*')
        Check 'Schritt 2 (VB-CABLE) erledigt' ((BadgeOf $wiz) -eq '✓ Erledigt')
        Shot $wiz (Join-Path $shots 'assistent-2.png')
        Click (Btn $wiz 'Sp*ter weitermachen')
        Start-Sleep -m 500
        Check ("Position gemerkt (Schritt {0})" -f ((Settings).SetupWizardStep + 1)) ((Settings).SetupWizardStep -eq 1)
        Check 'Nicht als fertig markiert' (-not (Settings).SetupWizardDone)
    }
    Stop-Process -Id $p.Id

    "=== Vollstaendig: Assistent bleibt zu, alle Schritte durchgehen ==="
    Write-TestSettings @{}
    $p = Start-App
    Check 'Assistent oeffnet sich NICHT, wenn alles eingerichtet ist' ($null -eq (FindWin $p.Id 'Einrichtungs-Assistent*' 5))
    Check 'Als erledigt gemerkt' ((Settings).SetupWizardDone)
    $win = FindWin $p.Id 'RP Voice Switcher'
    $help = All $win ([Windows.Automation.ControlType]::MenuItem) | Where-Object { $_.Current.Name -eq 'Hilfe' } | Select-Object -First 1
    $help.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 500
    Click (@($AE::RootElement.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -like 'Einrichtungs-Assistent*' } | Select-Object -First 1)
    $wiz = FindWin $p.Id 'Einrichtungs-Assistent*'
    Check 'Ueber Hilfe -> Einrichtungs-Assistent geoeffnet' ($null -ne $wiz)
    $expected = @{ 2 = '✓ Erledigt'; 3 = '✓ Erledigt'; 4 = '✓ Erledigt'; 5 = '✓ Erledigt'; 6 = '✓ Erledigt'; 7 = '✓ Erledigt' }
    for ($i = 1; $i -le 10; $i++) {
        $badge = BadgeOf $wiz
        $title = (Texts $wiz) | Where-Object { $_ -like "Schritt $i von 10" }
        if ($expected.ContainsKey($i)) { Check ("Schritt {0}: {1}" -f $i, $badge) ($badge -eq $expected[$i]) }
        Shot $wiz (Join-Path $shots ("assistent-{0:00}.png" -f $i))
        if ($i -lt 10) { Click (Btn $wiz 'Weiter') }
    }
    Check 'Letzter Schritt hat "Fertig"' ($null -ne (Btn $wiz 'Fertig'))
    Click (Btn $wiz 'Fertig')
    Check 'Fertig schliesst den Assistenten' ($null -eq (FindWin $p.Id 'Einrichtungs-Assistent*' 5))
    Check 'Danach erledigt, Position zurueckgesetzt' ((Settings).SetupWizardDone -and (Settings).SetupWizardStep -eq 0)
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                     OK" }
    Copy-Item "$env:TEMP\voice.wizardbackup" (Join-Path $cfg 'voice.txt') -Force
    Copy-Item "$env:TEMP\voice-active.wizardbackup" (Join-Path $cfg 'voice-active.cfg') -Force
}
