# Screenshots (PrintWindow, ohne Fokus) von Soundboard und Einstellungen -> Sprachtaste, mit einer Kopie der Einstellungen.
param([switch]$OpenMic)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$out = Join-Path $env:TEMP 'rpvs-shots'; New-Item -ItemType Directory -Force $out | Out-Null
$settings = Join-Path $env:TEMP "rpvs-shot-settings-$PID.json"
$j = Get-Content "$env:APPDATA\RpVoiceSwitcher\settings.json" -Raw | ConvertFrom-Json
$j | Add-Member PushToTalkKey 'Key|F13' -Force; $j | Add-Member PushToTalkEnabled $true -Force; $j | Add-Member GameOpenMic ([bool]$OpenMic) -Force
$j | Add-Member PttLatchHotkey 'Control, Alt|F10' -Force; $j | Add-Member CheckForUpdates $false -Force
$j | ConvertTo-Json -Depth 6 | Set-Content $settings
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace P -Name W -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function Shot($el, $name) {
    $h = [IntPtr]$el.Current.NativeWindowHandle
    $r = New-Object P.W+RECT; [P.W]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [P.W]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $out "$name.png")); $bmp.Dispose()
}
try {
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $tab = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 700
    Shot $win 'soundboard'
    $open = @($win.FindAll('Descendants', (Cond $AE::NameProperty 'Sprachtaste einstellen …'))) | Select-Object -First 1
    $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 1200
    $sw = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + @($win.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) |
          Where-Object { $_ -and $_.Current.Name -like 'Einstellungen*' } | Select-Object -First 1
    Shot $sw 'einstellungen'
}
finally {
    Get-Process -Id $p.Id -EA SilentlyContinue | Stop-Process
    Remove-Item $settings -EA SilentlyContinue
}
Get-ChildItem $out -Filter *.png | ForEach-Object FullName
