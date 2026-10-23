# Testet "Was ist neu": nach einem Update (zuletzt gesehen 1.8.2) erscheinen genau die neueren Versionen,
# beim naechsten Start nicht mehr; Hilfe -> Was ist neu zeigt alle Versionen. Arbeitet mit einer Kopie der Einstellungen.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$realHash = (Get-FileHash $realSettings).Hash
$settings = Join-Path $env:TEMP "rpvs-whatsnew-$PID.json"
$shots = Join-Path $env:TEMP 'rpvs-shots'; New-Item -ItemType Directory -Force $shots | Out-Null
$version = ([xml](Get-Content (Join-Path $repo 'RpVoiceSwitcher.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace P2 -Name W -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function Check($name, $ok) { "{0,-70} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Shot($el, $name) {
    $h = [IntPtr]$el.Current.NativeWindowHandle
    $r = New-Object P2.W+RECT; [P2.W]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [P2.W]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $shots "$name.png")); $bmp.Dispose()
}
function AppWindows($p) {
    $main = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + $(if ($main) { @($main.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) })
}
function WhatsNewWindow($p) { AppWindows $p | Where-Object { $_ -and $_.Current.Name -like 'Was ist neu*' } | Select-Object -First 1 }
function Texts($w) { @($w.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) }
function Write-TestSettings($lastSeen) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $extra = @{ LastSeenVersion = $lastSeen; SetupWizardDone = $true; CheckForUpdates = $false; ShowOverlay = $false; PlaySound = $false }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
function Start-App { Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600; $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5; $p }

try {
    "=== Nach Update von 1.8.2 ==="
    Write-TestSettings '1.8.2'
    $p = Start-App
    $w = WhatsNewWindow $p
    Check 'Fenster "Was ist neu" erscheint' ($null -ne $w)
    $t = Texts $w
    $heads = @($t | Where-Object { $_ -like 'Version *' })
    Check ("Ueberschrift: Update auf Version $version") ($t -contains "Update auf Version $version")
    Check ("Zeigt nur neuere Versionen ({0})" -f ($heads -join ', ')) (($heads | Where-Object { $_ -like 'Version 1.8.3*' }) -and ($heads | Where-Object { $_ -like "Version $version*" }) -and -not ($heads | Where-Object { $_ -like 'Version 1.8.2*' }))
    Check 'Seit-Hinweis nennt 1.8.2' (($t | Where-Object { $_ -like '*(1.8.2)*' }).Count -ge 1)
    Shot $w 'whatsnew-update'
    $saved = (Get-Content $settings -Raw | ConvertFrom-Json).LastSeenVersion
    Check "Gemerkt: zuletzt gesehen = $saved" ($saved -eq $version)
    Stop-Process -Id $p.Id

    "=== Naechster Start ==="
    $p = Start-App
    Check 'Kein "Was ist neu" mehr' ($null -eq (WhatsNewWindow $p))

    "=== Hilfe -> Was ist neu ==="
    $main = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $help = @($main.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -eq 'Hilfe' } | Select-Object -First 1
    $help.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 500
    $item = @($AE::RootElement.FindAll('Descendants', (Cond $AE::ProcessIdProperty $p.Id))) | Out-Null
    $item = @($main.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -like 'Was ist neu*' } | Select-Object -First 1
    if (-not $item) { $item = @($AE::RootElement.FindAll('Descendants', (Cond $AE::NameProperty 'Was ist neu (alle Versionen) …'))) | Select-Object -First 1 }
    Check 'Menüpunkt "Was ist neu (alle Versionen)" vorhanden' ($null -ne $item)
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 1200
    $w = WhatsNewWindow $p
    $t = Texts $w
    $heads = @($t | Where-Object { $_ -like 'Version *' })
    Check ("Alle Versionen ({0} Stueck, von {1} bis {2})" -f $heads.Count, $heads[0], $heads[-1]) ($heads.Count -ge 12 -and $heads[0] -like "Version $version*" -and $heads[-1] -like 'Version 1.0*')
    Check 'Aktuelle Version als "installiert" markiert' (($t | Where-Object { $_ -like "Version $version*installiert*" }).Count -eq 1)
    Shot $w 'whatsnew-all'
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Remove-Item $settings -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                         OK" }
}
