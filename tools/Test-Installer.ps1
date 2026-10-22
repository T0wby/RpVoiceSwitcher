# Testet den Installer: still in einen Testordner installieren, App starten (mit Test-Einstellungen), deinstallieren.
# Laeuft ueber explorer.exe, damit Installer und App ausserhalb einer App-Paket-Umgebung laufen (wie beim Nutzer).
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$setup = Get-ChildItem (Join-Path $repo 'dist') -Filter 'RpVoiceSwitcher-Setup-*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1
$work = Join-Path $env:TEMP 'rpvs-installtest'
$dir = Join-Path $work 'App'
$done = Join-Path $work 'fertig.txt'
$realSettings = "$env:APPDATA\RpVoiceSwitcher\settings.json"
$realHash = (Get-FileHash $realSettings).Hash
function Check($name, $ok) { "{0,-60} {1}" -f $name, $(if ($ok) { 'OK' } else { '!! FEHLER' }) }

Remove-Item $work -Recurse -Force -EA SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null
Copy-Item $realSettings (Join-Path $work 'settings.json')
$startMenu = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\RP Voice Switcher.lnk"
$hadShortcut = Test-Path $startMenu

function Run-ViaExplorer($cmdLines) {
    Remove-Item $done -EA SilentlyContinue
    $cmd = Join-Path $work 'run.cmd'
    Set-Content $cmd (@('@echo off') + $cmdLines + "echo ok> `"$done`"") -Encoding ASCII
    Start-Process explorer.exe $cmd
    for ($i = 0; $i -lt 240 -and -not (Test-Path $done); $i++) { Start-Sleep -m 500 }
    if (-not (Test-Path $done)) { throw 'Zeitueberschreitung' }
}

try {
    "Installer: $($setup.Name) ($([math]::Round($setup.Length / 1MB, 1)) MB)"
    Run-ViaExplorer @("start /wait `"`" `"$($setup.FullName)`" /VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER /DIR=`"$dir`" /TASKS=`"`" /NOLAUNCH=1 /LOG=`"$work\setup.log`"")
    Check 'Installiert: RpVoiceSwitcher.exe vorhanden' (Test-Path (Join-Path $dir 'RpVoiceSwitcher.exe'))
    Check 'Installiert: mitgelieferte Stimmen dabei' ((Get-ChildItem (Join-Path $dir 'Voices') -Filter *.txt).Count -ge 9)
    Check 'Installiert: eigenstaendig (.NET-Laufzeit dabei)' (Test-Path (Join-Path $dir 'coreclr.dll'))
    Check 'Startmenue-Eintrag angelegt' (Test-Path $startMenu)
    Check 'Kein Desktop-Symbol ohne Auswahl' (-not (Test-Path "$env:USERPROFILE\Desktop\RP Voice Switcher.lnk") -or (Test-Path "$env:USERPROFILE\Desktop\RP Voice Switcher.lnk") -and ((Get-Item "$env:USERPROFILE\Desktop\RP Voice Switcher.lnk").LastWriteTime -lt (Get-Date).AddMinutes(-5)))
    $uninst = Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | Where-Object { $_.PSChildName -like '*8F3C2A51*' } | Select-Object -First 1
    Check ("Unter 'Apps' eingetragen (Version {0})" -f $uninst.GetValue('DisplayVersion')) ($null -ne $uninst)

    # Installierte App kurz starten - mit Test-Einstellungen, damit die echten unberuehrt bleiben
    Get-Process RpVoiceSwitcher -EA SilentlyContinue | Stop-Process; Start-Sleep -m 500
    Run-ViaExplorer @("start `"`" `"$dir\RpVoiceSwitcher.exe`" --minimized --settings `"$work\settings.json`"")
    Start-Sleep 5
    $proc = Get-Process RpVoiceSwitcher -EA SilentlyContinue | Where-Object { $_.Path -like "$dir*" }
    Check 'Installierte App startet und laeuft' ($null -ne $proc)
    $proc | Stop-Process; Start-Sleep -m 800

    Run-ViaExplorer @("start /wait `"`" `"$dir\unins000.exe`" /VERYSILENT /SUPPRESSMSGBOXES")
    Start-Sleep 2
    Check 'Deinstalliert: Programmordner entfernt' (-not (Test-Path (Join-Path $dir 'RpVoiceSwitcher.exe')))
    Check 'Deinstalliert: Startmenue-Eintrag entfernt' (-not (Test-Path $startMenu))
    Check 'Deinstalliert: Eintrag unter Apps entfernt' (-not (Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | Where-Object { $_.PSChildName -like '*8F3C2A51*' }))
}
finally {
    if ((Get-FileHash $realSettings).Hash -ne $realHash) { "!! FEHLER: Die echte settings.json wurde veraendert!" } else { "Echte Einstellungen unveraendert                             OK" }
    Remove-Item $work -Recurse -Force -EA SilentlyContinue
}
