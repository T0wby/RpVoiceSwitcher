# Macht Screenshots aller Tabs der laufenden App (aendert nichts an den Einstellungen).
param([string]$OutDir = "$env:TEMP\rpvs-screens")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace S -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
$p = Get-Process RpVoiceSwitcher -ErrorAction Stop | Select-Object -First 1
$win = $AE::RootElement.FindAll('Children', (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id))) |
    Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
$hwnd = [IntPtr]$win.Current.NativeWindowHandle
[S.U]::ShowWindow($hwnd, 9) | Out-Null   # SW_RESTORE (auch aus dem Tray)
$tabs = @($win.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem))))
$i = 0
foreach ($tab in $tabs) {
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -m 700
    [S.U]::SetForegroundWindow($hwnd) | Out-Null; Start-Sleep -m 400
    $r = New-Object S.U+RECT; [S.U]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose()
    $bmp.Save((Join-Path $OutDir ("{0}-{1}.png" -f $i, $tab.Current.Name))); $bmp.Dispose()
    $i++
}
$tabs[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Get-ChildItem $OutDir -Filter *.png | Select-Object -ExpandProperty FullName
