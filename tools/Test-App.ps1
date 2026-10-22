# Rauchtest fuer RP Voice Switcher: Umschalten, Halten, Profile, Soundboard, Einrichtungs-Check + Screenshots.
# Arbeitet mit einer Kopie der Einstellungen (--settings) - die echte settings.json wird nie veraendert.
param([string]$OutDir = "$env:TEMP\rpvs-test")

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"   # wird nur gelesen, nie veraendert
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"     # eigene Kopie fuer den Test
$voiceTxt = 'C:\Program Files\EqualizerAPO\config\voice.txt'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$realHash = (Get-FileHash $realSettings).Hash
Copy-Item $realSettings $settings -Force

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type -Path (Join-Path $repo 'publish\NAudio.Core.dll')
Add-Type -Path (Join-Path $repo 'publish\NAudio.Wasapi.dll')
Add-Type -Namespace T -Name U -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
public struct RECT { public int L, T, R, B; }
'@

function Key($vk, [switch]$Up) { [T.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
function Combo([int[]]$vks) { foreach ($k in $vks) { Key $k }; Start-Sleep -m 60; [array]::Reverse($vks); foreach ($k in $vks) { Key $k -Up }; Start-Sleep -m 600 }
function Voice { ((Get-Content $voiceTxt) | Where-Object { $_ -like '# Aktiv:*' }) -replace '# Aktiv: ', '' }
function Check($name, $ok) { "{0,-62} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER (ist: ' + (Voice) + ')' }) }

function Start-App([switch]$Visible) {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    if ($Visible) { $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru } else { $p = Start-Process $exe -ArgumentList '--minimized', '--settings', "`"$settings`"" -PassThru }
    Start-Sleep 4; $p
}

function Peak($deviceName, $ms) {
    $en = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
    $dev = $en.EnumerateAudioEndPoints('Render', 'Active') | Where-Object { $_.FriendlyName -like "*$deviceName*" } | Select-Object -First 1
    $max = 0; $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { $max = [Math]::Max($max, $dev.AudioMeterInformation.MasterPeakValue); Start-Sleep -m 20 }
    $max
}

$VK_CTRL = 0x11; $VK_ALT = 0x12; $F9 = 0x78; $F11 = 0x7A; $NUM7 = 0x67; $NUM8 = 0x68

try {
    # ---------- Testbelegung ----------
    $p = Start-App; Stop-Process -Id $p.Id; Start-Sleep -m 500      # Migration ins Profil-Format
    $j = Get-Content $settings -Raw | ConvertFrom-Json
    $std = $j.Profiles[0]
    $std.IncludeAllVoices = $true   # Test braucht alle Stimmen im Standard-Profil
    $std.HoldHotkeys | Add-Member '11-herr-mondgesicht.txt' 'None|NumPad8' -Force
    $std.SoundHotkeys | Add-Member 'Messingglocke.wav' 'Control, Alt|F11' -Force
    $jinpei = [pscustomobject]@{
        Name = 'Jinpei'; IncludeAllVoices = $false; Voices = @('10-jinpei-ausrufer.txt', '11-herr-mondgesicht.txt')
        Hotkeys = [pscustomobject]@{ '10-jinpei-ausrufer.txt' = 'Control, Alt|F9' }
        HoldHotkeys = [pscustomobject]@{ '11-herr-mondgesicht.txt' = 'None|NumPad7' }
        SoundHotkeys = [pscustomobject]@{}
    }
    $j.Profiles = @($std, $jinpei); $j.ActiveProfile = 'Standard'
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings

    "=== Profil Standard ==="
    $p = Start-App
    Combo @($VK_CTRL, $VK_ALT, 0x35);                     Check 'Strg+Alt+5 -> Hoehle' ((Voice) -eq 'Hoehle')
    Key $NUM8; Start-Sleep -m 500;                         Check 'Num 8 gedrueckt -> Herr Mondgesicht' ((Voice) -eq 'Herr Mondgesicht')
    Key $NUM8 -Up; Start-Sleep -m 500;                     Check 'Num 8 losgelassen -> zurueck zu Hoehle' ((Voice) -eq 'Hoehle')

    # Was die anderen hoeren: direkt am CABLE Output aufnehmen (der Ausgabe-Pegelmesser von VB-CABLE zeigt immer 0)
    $probe = Join-Path $repo 'tools\AudioProbe\bin\out\AudioProbe.exe'
    $log = Join-Path $env:TEMP 'rpvs-probe.txt'
    $pr = Start-Process $probe -ArgumentList '"CABLE Output"', '2000' -RedirectStandardOutput $log -PassThru -NoNewWindow
    Start-Sleep -m 600
    $quiet = 0.0
    Combo @($VK_CTRL, $VK_ALT, $F11)
    $pr.WaitForExit()
    $lines = Get-Content $log
    $quiet = [double](($lines | Where-Object { $_ -match '^\s+(100|200|300|400) ms' } | ForEach-Object { ($_ -split '\s+')[-1] -replace ',', '.' } | Measure-Object -Maximum).Maximum)
    $peak = [double](($lines | Select-Object -Last 1) -replace '^MAX ([\d,\.]+).*', '$1' -replace ',', '.')
    Check ("Strg+Alt+F11 -> Glocke im Kabel (Pegel {0:N2}, vorher {1:N2})" -f $peak, $quiet) ($peak -gt [Math]::Max(0.05, $quiet * 2))
    Stop-Process -Id $p.Id

    "=== Profil Jinpei ==="
    $j = Get-Content $settings -Raw | ConvertFrom-Json; $j.ActiveProfile = 'Jinpei'; $j | ConvertTo-Json -Depth 6 | Set-Content $settings
    $p = Start-App
    Check 'Profilwechsel: Hoehle gehoert nicht dazu -> Normal' ((Voice) -eq 'Normal')
    Combo @($VK_CTRL, $VK_ALT, 0x35);                     Check 'Strg+Alt+5 (nur Standard) wirkt hier nicht' ((Voice) -eq 'Normal')
    Combo @($VK_CTRL, $VK_ALT, $F9);                      Check 'Strg+Alt+F9 -> Jinpei - Ausrufer' ((Voice) -eq 'Jinpei - Ausrufer')
    Key $NUM7; Start-Sleep -m 400;                         Check 'Num 7 gedrueckt -> Herr Mondgesicht' ((Voice) -eq 'Herr Mondgesicht')
    Key $NUM7 -Up; Start-Sleep -m 400;                     Check 'Num 7 losgelassen -> Jinpei - Ausrufer' ((Voice) -eq 'Jinpei - Ausrufer')
    Key $NUM8; Start-Sleep -m 400;                         Check 'Num 8 (nur Standard) wirkt hier nicht' ((Voice) -eq 'Jinpei - Ausrufer'); Key $NUM8 -Up
    Stop-Process -Id $p.Id

    "=== Oberflaeche ==="
    $p = Start-App -Visible
    $root = [Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    $win = $root.FindAll('Children', $cond) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    foreach ($tab in 'Stimmen', 'Soundboard') {
        $tc = New-Object Windows.Automation.AndCondition(
            (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)),
            (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $tab)))
        $item = $win.FindFirst('Descendants', $tc)
        $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -m 900
        [T.U]::SetForegroundWindow($hwnd) | Out-Null; Start-Sleep -m 400
        $r = New-Object T.U+RECT; [T.U]::GetWindowRect($hwnd, [ref]$r) | Out-Null
        $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
        $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size); $g.Dispose()
        $bmp.Save((Join-Path $OutDir "$tab.png")); $bmp.Dispose()
        if ($tab -eq 'Einrichtung') {
            "--- Einrichtungs-Check ---"
            $texts = $win.FindAll('Descendants', (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)))
            $texts | ForEach-Object { $_.Current.Name } | Where-Object { $_ -and $_.Length -gt 1 }
        }
    }
    Stop-Process -Id $p.Id
    "Screenshots: $OutDir"
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                     OK" }
}
