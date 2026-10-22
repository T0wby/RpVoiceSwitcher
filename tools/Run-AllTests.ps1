# Fuehrt alle UI-/Audio-Tests nacheinander aus und schreibt das Ergebnis nach %TEMP%\rpvs-alltests.txt.
# Am Ende: Liste aller fehlgeschlagenen Pruefungen (leer = alles gruen).
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $env:TEMP 'rpvs-alltests.txt'
Set-Content $out "" -Encoding UTF8
$tests = 'Test-SoundLevel', 'Test-SoundLoop', 'Test-Soundboard', 'Test-SoundEdit', 'Test-Features', 'Test-App', 'Test-VoiceEdit', 'Test-Wizard'
foreach ($t in $tests) {
    Add-Content $out "##### $t" -Encoding UTF8
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "$t.ps1") 2>&1 | ForEach-Object { "$_" } | Add-Content $out -Encoding UTF8
}
Add-Content $out "##### FERTIG" -Encoding UTF8
