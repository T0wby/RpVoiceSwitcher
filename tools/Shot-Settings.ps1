# Screenshots (PrintWindow, ohne Fokus) des Einstellungsfensters oben, mittig und unten - mit einer Kopie der Einstellungen.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$out = Join-Path $env:TEMP 'rpvs-shots'; New-Item -ItemType Directory -Force $out | Out-Null
$settings = Join-Path $env:TEMP "rpvs-shot-settings-$PID.json"
Copy-Item "$env:APPDATA\RpVoiceSwitcher\settings.json" $settings
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace P4 -Name W -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
function Shot($el, $name) {
    $h = [IntPtr]$el.Current.NativeWindowHandle
    $r = New-Object P4.W+RECT; [P4.W]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [P4.W]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $out "$name.png")); $bmp.Dispose()
}
try {
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $extras = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::MenuItem)))) | Where-Object { $_.Current.Name -eq 'Extras' } | Select-Object -First 1
    $extras.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 500
    $item = @($AE::RootElement.FindAll('Descendants', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -like 'Einstellungen*' } | Select-Object -First 1
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 1200
    $sw = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + @($win.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) |
          Where-Object { $_ -and $_.Current.Name -like 'Einstellungen*' } | Select-Object -First 1
    Shot $sw 'settings-1'
    $scroll = @($sw.FindAll('Descendants', (Cond $AE::AutomationIdProperty 'SettingsScroll'))) | Select-Object -First 1
    $sp = $scroll.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    foreach ($pct in 50, 100) { $sp.SetScrollPercent(-1, $pct); Start-Sleep -m 500; Shot $sw "settings-$pct" }
}
finally {
    Get-Process -Id $p.Id -EA SilentlyContinue | Stop-Process
    Remove-Item $settings -EA SilentlyContinue
}
Get-ChildItem $out -Filter 'settings-*.png' | ForEach-Object FullName
