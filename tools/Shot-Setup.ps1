# Screenshot (PrintWindow, ohne Fokus) des Einrichtungsfensters und die Texte aller Pruefungen - mit einer Kopie der Einstellungen.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$out = Join-Path $env:TEMP 'rpvs-shots'; New-Item -ItemType Directory -Force $out | Out-Null
$settings = Join-Path $env:TEMP "rpvs-shot-settings-$PID.json"
Copy-Item "$env:APPDATA\RpVoiceSwitcher\settings.json" $settings
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace P3 -Name W -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }
try {
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $link = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Hyperlink)))) | Select-Object -First 1
    $link.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -m 1500
    $sw = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + @($win.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) |
          Where-Object { $_ -and $_.Current.Name -like 'Einrichtung*' } | Select-Object -First 1
    @($sw.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Text)))) | ForEach-Object { $_.Current.Name } | Where-Object { $_ }
    $h = [IntPtr]$sw.Current.NativeWindowHandle
    $r = New-Object P3.W+RECT; [P3.W]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [P3.W]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $out 'einrichtung.png')); $bmp.Dispose()
}
finally {
    Get-Process -Id $p.Id -EA SilentlyContinue | Stop-Process
    Remove-Item $settings -EA SilentlyContinue
}
