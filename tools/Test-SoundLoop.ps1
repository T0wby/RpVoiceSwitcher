# Testet Wiederholen im Soundboard, gemessen im Kabel: einmal, 3x hintereinander, Endlos-Loop,
# und dass derselbe Shortcut einen laufenden Loop wieder stoppt (plus Button-Text "Stoppen").
# Arbeitet mit einer Kopie der Einstellungen (--settings); Mithoeren ist aus, damit nichts auf den Kopfhoerern landet.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $repo 'publish\RpVoiceSwitcher.exe'
$probe = Join-Path $repo 'tools\AudioProbe\bin\out\AudioProbe.exe'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$settings = Join-Path $env:TEMP "rpvs-test-settings-$PID.json"
$log = Join-Path $env:TEMP "rpvs-loop-$PID.txt"
$realHash = (Get-FileHash $realSettings).Hash

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace T6 -Name U -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);'
$AE = [Windows.Automation.AutomationElement]
function Key($vk, [switch]$Up) { [T6.U]::keybd_event($vk, 0, $(if ($Up) { 2 } else { 0 }), [UIntPtr]::Zero) }
# Nur global registrierte Kombination (Strg+Alt+F11) - die faengt Windows ab
function Press { Key 0x11; Key 0x12; Key 0x7A; Start-Sleep -m 60; Key 0x7A -Up; Key 0x12 -Up; Key 0x11 -Up }
function Check($name, $ok) { "{0,-66} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }
function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }

function Write-TestSettings([int]$repeat) {
    $j = Get-Content $realSettings -Raw | ConvertFrom-Json
    $p0 = $j.Profiles[0]
    $p0.Hotkeys = [pscustomobject]@{}; $p0.HoldHotkeys = [pscustomobject]@{}
    $p0.SoundHotkeys = [pscustomobject]@{ 'Messingglocke.wav' = 'Control, Alt|F11' }
    $j.Profiles = @($p0); $j.ActiveProfile = $p0.Name
    $extra = @{ SoundMonitor = $false; SoundVolume = 0.8; ShowOverlay = $false; PlaySound = $false; CheckForUpdates = $false
                SetupWizardDone = $true; SoundsWithEffect = @(); SoundRepeat = $(if ($repeat -eq 1) { [pscustomobject]@{} } else { [pscustomobject]@{ 'Messingglocke.wav' = $repeat } }) }
    foreach ($k in $extra.Keys) { $j | Add-Member $k $extra[$k] -Force }
    $j | ConvertTo-Json -Depth 6 | Set-Content $settings
}
function Start-App { Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 600; $p = Start-Process $exe -ArgumentList '--settings', "`"$settings`"" -PassThru; Start-Sleep 5; $p }

# Nimmt $ms lang am Kabel auf; $actions = @{ Zeitpunkt(ms) = Scriptblock }. Liefert Pegel je 100 ms (Index = Zeit/100).
function Record([int]$ms, [hashtable]$actions) {
    $pr = Start-Process $probe -ArgumentList '"CABLE Output"', "$ms" -RedirectStandardOutput $log -PassThru -NoNewWindow
    $sw = [Diagnostics.Stopwatch]::StartNew()
    foreach ($t in ($actions.Keys | Sort-Object)) {
        while ($sw.ElapsedMilliseconds -lt $t) { Start-Sleep -m 20 }
        & $actions[$t]
    }
    $pr.WaitForExit()
    $levels = @{}
    Get-Content $log | Where-Object { $_ -match '^\s*(\d+) ms\s+([\d,\.]+)' } | ForEach-Object { $levels[[int]$Matches[1] / 100] = [double]($Matches[2] -replace ',', '.') }
    $levels
}
function Loud($levels, [int]$fromMs, [int]$toMs, [double]$th = 0.02) {
    @($levels.Keys | Where-Object { $_ * 100 -ge $fromMs -and $_ * 100 -lt $toMs -and $levels[$_] -gt $th }).Count
}
# Wie lange nach dem Start noch Ton kam (letztes lautes Fenster)
function SoundEnd($levels, [int]$startMs, [double]$th = 0.02) {
    $hits = @($levels.Keys | Where-Object { $_ * 100 -gt $startMs + 200 -and $levels[$_] -gt $th } | Sort-Object)
    if ($hits.Count -eq 0) { 0 } else { $hits[-1] * 100 - $startMs }
}

try {
    "=== Einmal ==="
    Write-TestSettings 1
    $p = Start-App
    Start-Sleep 2
    $l = Record 8000 @{ 800 = { Press } }
    $single = SoundEnd $l 800
    Check ("Glocke einmal hoerbar, Dauer ca. {0} ms" -f $single) ($single -gt 300)
    Stop-Process -Id $p.Id

    "=== 3x hintereinander ==="
    Write-TestSettings 3
    $p = Start-App
    Start-Sleep 2
    $len = [Math]::Max(9000, [int](3 * $single + 4000))
    $l = Record $len @{ 800 = { Press } }
    $triple = SoundEnd $l 800
    Check ("3x dauert ca. dreimal so lange ({0} ms statt {1} ms)" -f $triple, $single) ($triple -gt 2.4 * $single -and $triple -lt 3.6 * $single + 600)
    Stop-Process -Id $p.Id

    "=== Endlos + derselbe Shortcut stoppt ==="
    Write-TestSettings 0
    $p = Start-App
    $win = @($AE::RootElement.FindAll('Children', (Cond $AE::ProcessIdProperty $p.Id))) | Where-Object { $_.Current.Name -eq 'RP Voice Switcher' } | Select-Object -First 1
    $tab = @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::TabItem)))) | Where-Object { $_.Current.Name -eq 'Soundboard' }
    $tab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -m 600
    function PlayButtons { @($win.FindAll('Descendants', (Cond $AE::ControlTypeProperty ([Windows.Automation.ControlType]::Button)))) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -like '*Loop starten*' -or $_ -like '*Stoppen*' } }
    Check 'Button zeigt "Loop starten"' ((PlayButtons) -contains '▶  Loop starten')
    $stopSeen = $false
    $loopEnd = [int](2 * $single + 3000)                       # mindestens zwei volle Durchlaeufe
    $l = Record ($loopEnd + 4000) @{ 800 = { Press }; ($loopEnd - 400) = { $script:stopSeen = (PlayButtons) -contains '■  Stoppen' }; $loopEnd = { Press } }
    $during = Loud $l ([int]($loopEnd - 1500)) $loopEnd 0.005
    $after = Loud $l ($loopEnd + 700) ($loopEnd + 4000) 0.005
    Check ("Loop laeuft nach {0:N1} s noch (laute Fenster kurz vor Stopp: {1})" -f ($loopEnd / 1000), $during) ($during -ge 3)
    Check 'Waehrend des Loops zeigt der Button "Stoppen"' $stopSeen
    Check ("Derselbe Shortcut stoppt den Loop (danach laute Fenster: {0})" -f $after) ($after -eq 0)
    Start-Sleep -m 600
    Check 'Button zeigt danach wieder "Loop starten"' ((PlayButtons) -contains '▶  Loop starten')
    Stop-Process -Id $p.Id
}
finally {
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process
    Start-Sleep -m 500
    Remove-Item $settings, $log -ErrorAction SilentlyContinue
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                                     OK" }
}
