# Oeffnet/schliesst Menues und die Fenster Einstellungen/Einrichtung ueber UI-Automation (keine echten Klicks
# oder Tastendruecke - die koennten sonst in einem anderen Fenster landen) und macht Fenster-Screenshots per PrintWindow.
param([string]$OutDir = "$env:TEMP\rpvs-menu")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Namespace P -Name W -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PrintWindow(System.IntPtr h, System.IntPtr hdc, uint flags);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@
$AE = [Windows.Automation.AutomationElement]
function Shot($el, $name) {
    $h = [IntPtr]$el.Current.NativeWindowHandle
    $r = New-Object P.W+RECT; [P.W]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc(); [P.W]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $OutDir "$name.png")); $bmp.Dispose()
}
function Windows($procId) {
    $tops = @($AE::RootElement.FindAll('Children', (New-Object Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $procId))))
    $tops + @($tops | ForEach-Object { $_.FindAll('Children', (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::Window))) })
}
function Win($procId, $title) { Windows $procId | Where-Object { $_.Current.Name -like $title -and -not $_.Current.IsOffscreen } | Select-Object -First 1 }
function MenuItems($root) { @($root.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::MenuItem)))) }

$p = Get-Process RpVoiceSwitcher | Select-Object -First 1
$win = Win $p.Id 'RP Voice Switcher'

# Menues auf- und zuklappen
foreach ($name in 'Extras', 'Hilfe', 'Charakter', 'Ansicht') {
    $m = MenuItems $win | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
    $ec = $m.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $ec.Expand(); Start-Sleep -m 400; $ec.Collapse(); Start-Sleep -m 300
}
Start-Sleep -m 300
Shot $win '1-nach-menues'
$focused = $AE::FocusedElement
"Fokus nach den Menues: '$($focused.Current.Name)' ($($focused.Current.ControlType.ProgrammaticName))"

# Extras -> Einstellungen / Einrichtung
foreach ($pair in @(@('Einstellungen*', 'Einstellungen', '2-einstellungen'), @('Einrichtung*', 'Einrichtung', '3-einrichtung'))) {
    $extras = MenuItems $win | Where-Object { $_.Current.Name -eq 'Extras' } | Select-Object -First 1
    $extras.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 400
    $item = MenuItems $AE::RootElement | Where-Object { $_.Current.Name -like "$($pair[1])*" -and $_.Current.ProcessId -eq $p.Id } | Select-Object -First 1
    if (-not $item) { $item = MenuItems $extras | Where-Object { $_.Current.Name -like "$($pair[1])*" } | Select-Object -First 1 }
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep 1
    $w = Win $p.Id $pair[0]
    if ($w) { Shot $w $pair[2]; "$($pair[1])-Fenster: offen"; $w.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -m 500; "$($pair[1]) nach Schliessen sichtbar: $([bool](Win $p.Id $pair[0]))" }
    else { "$($pair[1])-Fenster: NICHT gefunden" }
}
Get-ChildItem $OutDir -Filter *.png | Select-Object -ExpandProperty FullName
