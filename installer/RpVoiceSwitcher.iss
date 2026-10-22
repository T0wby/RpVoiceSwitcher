; Installer für RP Voice Switcher (Inno Setup 6).
; Bauen: tools\Build-Installer.ps1 – setzt die Version aus der .csproj und veröffentlicht die App vorher eigenständig (ohne .NET-Installation).
; Installiert pro Benutzer (keine Administratorrechte nötig); Updates laufen über denselben Installer.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "RP Voice Switcher"
#define AppExe "RpVoiceSwitcher.exe"

[Setup]
AppId={{8F3C2A51-6D4E-4B7A-9C11-2E5A7D0B9F42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=T0wby
AppPublisherURL=https://github.com/T0wby/RpVoiceSwitcher
AppSupportURL=https://github.com/T0wby/RpVoiceSwitcher/issues
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=RpVoiceSwitcher-Setup-{#AppVersion}
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
; Läuft die App noch, wird sie geschlossen (bei Updates beendet sie sich ohnehin selbst).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Messages]
de.FinishedLabel=[name] ist installiert.%n%nWichtig: Für die Stimmen braucht es außerdem Equalizer APO und VB-CABLE. Die App zeigt unter Extras → Einrichtung, was noch fehlt, und hilft beim Einrichten.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Mit Windows starten (im Hintergrund, Shortcuts sofort aktiv)"; GroupDescription: "Start:"; Flags: unchecked

[Files]
Source: "..\publish-installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Reste älterer Versionen (z. B. umbenannte DLLs) entfernen – Einstellungen liegen woanders und bleiben erhalten.
Type: filesandordirs; Name: "{app}\*.dll"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RpVoiceSwitcher"; \
  ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; Ohne "skipifsilent": nach einem Update aus der App (/SILENT) startet sie automatisch wieder.
; /NOLAUNCH=1 (für Tests) startet die App nicht.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall; Check: ShouldLaunch

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopApp"

[Code]
// Bei einem Update aus der App heraus: kurz warten, bis sie sich beendet hat.
function InitializeSetup(): Boolean;
var
  i: Integer;
begin
  i := 0;
  while CheckForMutexes('RpVoiceSwitcher_SingleInstance') and (i < 40) do
  begin
    Sleep(250);
    i := i + 1;
  end;
  Result := True;
end;

function ShouldLaunch(): Boolean;
begin
  Result := ExpandConstant('{param:NOLAUNCH|0}') <> '1';
end;
