# Testet den Wächter für Windows-„Abhören“ (Mikrofon → CABLE Input) mit den echten Geräten:
#  - läuft ohne Fehlalarm, solange alles in Ordnung ist
#  - Extras → Einrichtung → „Neu starten“ schaltet Abhören kurz aus und wieder an
#  - schaltest du Abhören selbst aus, schaltet die App es NICHT wieder ein
# Arbeitet mit einer Kopie der Einstellungen. Abhören ist am Ende in jedem Fall wieder an.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-listen-settings-$PID.json"
$log = "$env:LOCALAPPDATA\RpVoiceSwitcher\abhoeren-test.log"
$realHash = (Get-FileHash $realSettings).Hash

Add-Type -Path "$repo\publish\NAudio.Core.dll"; Add-Type -Path "$repo\publish\NAudio.Wasapi.dll"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TListen {
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig {
        [PreserveSig] int A([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr f);
        [PreserveSig] int B([MarshalAs(UnmanagedType.LPWStr)] string id, int d, IntPtr f);
        [PreserveSig] int C([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int D([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr a, IntPtr b);
        [PreserveSig] int E([MarshalAs(UnmanagedType.LPWStr)] string id, int d, IntPtr a, IntPtr b);
        [PreserveSig] int F([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr p);
        [PreserveSig] int G([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr m);
        [PreserveSig] int H([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr m);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fx, IntPtr key, IntPtr value);
    }
    public static int SetListen(string id, bool on) {
        var pc = (IPolicyConfig)new PolicyConfigClient();
        IntPtr key = Marshal.AllocHGlobal(20), val = Marshal.AllocHGlobal(24);
        try {
            Marshal.Copy(new Guid("24dbb0fc-9311-4b3d-9cf0-18ff155639d4").ToByteArray(), 0, key, 16);
            Marshal.WriteInt32(key, 16, 1);
            for (int i = 0; i < 24; i++) Marshal.WriteByte(val, i, 0);
            Marshal.WriteInt16(val, 0, 11);
            Marshal.WriteInt16(val, 8, (short)(on ? -1 : 0));
            return pc.SetPropertyValue(id, 0, key, val);
        } finally { Marshal.FreeHGlobal(key); Marshal.FreeHGlobal(val); Marshal.ReleaseComObject(pc); }
    }
}
'@
$AE = [Windows.Automation.AutomationElement]
function Check($name, $ok) { "{0,-80} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }

# Das Mikrofon, das per Abhören ins Kabel geht (wie die App es findet)
$base = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio'
$cinGuid = (Get-ChildItem "$base\Render" | Where-Object { (Get-ItemProperty $_.PSPath).DeviceState -eq 1 -and
            (Get-ItemProperty "$($_.PSPath)\Properties").'{a45c254e-df1c-4efd-8020-67d146a850e0},2' -eq 'CABLE Input' } | Select-Object -First 1).PSChildName
function ListenEnabled($guid) { $b = (Get-ItemProperty "$base\Capture\$guid\Properties").'{24dbb0fc-9311-4b3d-9cf0-18ff155639d4},1'; $b -and $b[8] -ne 0 }
$micGuid = (Get-ChildItem "$base\Capture" | Where-Object {
    $p = Get-ItemProperty "$($_.PSPath)\Properties"; (Get-ItemProperty $_.PSPath).DeviceState -eq 1 -and
    (ListenEnabled $_.PSChildName) -and "$($p.'{24dbb0fc-9311-4b3d-9cf0-18ff155639d4},0')" -like "*$cinGuid*" } | Select-Object -First 1).PSChildName
if (-not $micGuid) { "Kein Mikrofon wird per Abhören ins Kabel geleitet - Test uebersprungen"; return }
$micId = "{0.0.1.00000000}.$micGuid"
$en = New-Object NAudio.CoreAudioApi.MMDeviceEnumerator
$mic = $en.GetDevice($micId)
$cin = $en.EnumerateAudioEndPoints('Render', 'Active') | Where-Object { $_.FriendlyName -like 'CABLE Input*' } | Select-Object -First 1
function SessionState {
    $sm = $cin.AudioSessionManager; $sm.RefreshSessions(); $s = $sm.Sessions
    for ($i = 0; $i -lt $s.Count; $i++) { if ($s[$i].DisplayName -eq $mic.FriendlyName) { return "$($s[$i].State)" -replace 'AudioSessionState', '' } }
    'keine'
}
function LogLines { if (Test-Path $log) { @(Get-Content $log -Encoding UTF8) } else { @() } }
"Mikrofon: $($mic.FriendlyName)"

$j = Get-Content $realSettings -Raw | ConvertFrom-Json
$extra = @{ AutoFixListen = $true; ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false; ShowStatusOverlay = $false
            KeepCableDefaultMic = $false; SetupWizardDone = $true; TourSeen = $true; LastSeenVersion = '99.0.0'; PttLatchResume = $false }
foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
$j | ConvertTo-Json -Depth 6 | Set-Content $settings
Remove-Item $log -EA SilentlyContinue
$env:RPVS_TEST_LISTEN = '1'

try {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600
    $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1

    # 1) Kein Fehlalarm, solange Abhören läuft
    Check ("Vorher: Abhoeren laeuft (Sitzung '{0}')" -f (SessionState)) ((SessionState) -eq 'Active')
    Start-Sleep 8
    $restarts = @(LogLines | Where-Object { $_ -like '*Neustart*' })
    Check ("8 s lang kein unnoetiger Neustart ({0} Eintraege)" -f $restarts.Count) ($restarts.Count -eq 0)

    # 2) Einrichtung → „Neu starten“
    $extras = @($win.FindAll('Descendants', (Cond $AE::NameProperty 'Extras'))) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::MenuItem } | Select-Object -First 1
    $extras.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -m 500
    $item = @($AE::RootElement.FindAll('Descendants', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -like 'Einrichtung*' } | Select-Object -First 1
    $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep 2
    $sw = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) + @($win.FindAll('Children', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Window)))) |
          Where-Object { $_ -and $_.Current.Name -like 'Einrichtung*' } | Select-Object -First 1
    $btn = @($sw.FindAll('Descendants', (Cond $AE::NameProperty 'Neu starten'))) | Select-Object -First 1
    Check 'Einrichtung zeigt "Neu starten" beim Abhoeren' ($null -ne $btn)
    $seen = New-Object System.Collections.Generic.List[string]
    $job = [Diagnostics.Stopwatch]::StartNew()
    $btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    while ($job.ElapsedMilliseconds -lt 2500) { $s = SessionState; if ($seen.Count -eq 0 -or $seen[$seen.Count - 1] -ne $s) { $seen.Add($s) }; Start-Sleep -m 30 }
    $after = SessionState
    Check ("Neu starten: Sitzung {0}, danach '{1}', Abhoeren an ({2})" -f ($seen -join ' > '), $after, (ListenEnabled $micGuid)) ($after -eq 'Active' -and (ListenEnabled $micGuid))
    Check 'Protokoll: Neustart von Hand' (@(LogLines | Where-Object { $_ -like '*von Hand*' }).Count -eq 1)
    $sw.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -m 400

    # 3) Selbst ausgeschaltet → bleibt aus
    $before = (LogLines).Count
    [void][TListen]::SetListen($micId, $false); Start-Sleep 9
    $auto = @(LogLines | Select-Object -Skip $before | Where-Object { $_ -like 'Neustart*' -or $_ -like '*Neustart (*' })
    Check ("Selbst ausgeschaltet: bleibt aus (Abhoeren an: {0}, Sitzung '{1}', Neustarts: {2})" -f (ListenEnabled $micGuid), (SessionState), $auto.Count) (-not (ListenEnabled $micGuid) -and $auto.Count -eq 0)
    [void][TListen]::SetListen($micId, $true); Start-Sleep 2
    Check ("Wieder eingeschaltet: Sitzung '{0}'" -f (SessionState)) ((SessionState) -eq 'Active')
    Start-Sleep 6
    $restarts = @(LogLines | Where-Object { $_ -like '*Neustart (*' -and $_ -notlike '*von Hand*' })
    Check ("Danach wieder ruhig ({0} automatische Neustarts)" -f $restarts.Count) ($restarts.Count -eq 0)
}
finally {
    [void][TListen]::SetListen($micId, $true)
    Remove-Item Env:\RPVS_TEST_LISTEN -EA SilentlyContinue
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Remove-Item $settings -EA SilentlyContinue
    Start-Sleep 1
    Check ("Am Ende: Abhoeren an ({0}), Sitzung '{1}'" -f (ListenEnabled $micGuid), (SessionState)) ((ListenEnabled $micGuid) -and (SessionState) -eq 'Active')
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                                 OK" }
}
