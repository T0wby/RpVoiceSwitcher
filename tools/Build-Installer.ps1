# Baut den Installer: App eigenstaendig veroeffentlichen (ohne .NET-Installation lauffaehig) und mit Inno Setup verpacken.
# Ergebnis: dist\RpVoiceSwitcher-Setup-<Version>.exe
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 nicht gefunden (winget install JRSoftware.InnoSetup --scope user)' }

[xml]$proj = Get-Content (Join-Path $repo 'RpVoiceSwitcher.csproj')
$version = @($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
"Version $version"

$out = Join-Path $repo 'publish-installer'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $repo 'RpVoiceSwitcher.csproj') -c Release -r win-x64 --self-contained true -o $out -nologo -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen' }

& $iscc /Qp "/DAppVersion=$version" (Join-Path $repo 'installer\RpVoiceSwitcher.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup fehlgeschlagen' }
Get-Item (Join-Path $repo "dist\RpVoiceSwitcher-Setup-$version.exe") | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
