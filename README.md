# RP Voice Switcher

Stimmen verstellen fürs Roleplay – per Tastendruck, auch mitten im Spiel. Maske, Funkgerät, Megafon, Höhle, Metallhelm, Geist, Riese … oder eigene Stimmen. Dazu Charaktere mit eigener Tastenbelegung und ein Soundboard.

Windows-Desktop-App (WPF, .NET 10). Die Effekte macht [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) auf einem virtuellen Kabel ([VB-CABLE](https://vb-audio.com/Cable/)); die App schaltet zwischen den Stimmen um.

## Installieren

1. Den neuesten Installer unter **[Releases](https://github.com/T0wby/RpVoiceSwitcher/releases/latest)** herunterladen (`RpVoiceSwitcher-Setup-x.y.z.exe`) und starten. Keine Administratorrechte nötig, .NET muss nicht installiert sein.
2. Beim ersten Start öffnet sich der **Einrichtungs-Assistent**. Er führt Schritt für Schritt durch alles Weitere – auch wenn noch gar nichts eingerichtet ist:
   VB-CABLE installieren → Equalizer APO installieren und an „CABLE Output“ hängen → Zugriff freigeben → Neustart → Mikrofon per „Abhören“ ins Kabel leiten → Wiedergabegerät prüfen → ausprobieren → im Spiel und in Discord „CABLE Output“ als Mikrofon wählen.
   Jeder Schritt prüft selbst, ob er erledigt ist. Später jederzeit wieder unter *Hilfe → Einrichtungs-Assistent*.

Updates findet die App selbst (abschaltbar) – oder *Hilfe → Nach Updates suchen*.

## Funktionen

- **Stimmen** umschalten oder nur solange eine Taste gehalten wird; Shortcuts funktionieren global, auch im Spiel
- **Charaktere** mit eigener Stimmenauswahl und eigenen Shortcuts
- **Notfall-Taste** (Standard: Pause): sofort normale Stimme, alle Sounds aus
- **Soundboard** – Sounds wahlweise durch den Stimmeffekt oder unverändert
- **Probehören**: kurz sprechen, normal und mit Effekt vergleichen; misst den Lautstärke-Unterschied und gleicht ihn auf Wunsch aus
- **Lautstärke-Ausgleich** pro Stimme
- Stimmen anlegen, bearbeiten, kopieren, **exportieren/importieren** (.txt oder ZIP)
- **Status-Anzeige** auf dem Bildschirm (Position, Größe, Deckkraft, optional nur mit Effekt)
- **OBS-Textdatei** mit der aktiven Stimme für Streams
- Optional **Graillon** (kostenloses VST-Plugin) für Tonhöhe und Formanten

## Selbst bauen

```powershell
dotnet publish RpVoiceSwitcher.csproj -c Release -o .\publish     # App
.\tools\Build-Installer.ps1                                       # Installer nach dist\ (braucht Inno Setup 6)
```

Tests (UI-Automation und Messungen im Kabel, arbeiten mit einer Kopie der Einstellungen): `tools\Test-*.ps1`, Logik-Tests: `tools\FeatureProbe`.

## Wie es funktioniert

Das Mikrofon wird per Windows-„Abhören“ nach *CABLE Input* geleitet. Equalizer APO hängt an *CABLE Output* und bindet `voice.txt` ein, die die App beim Umschalten neu schreibt. Sounds ohne Effekt spielt das Soundboard gegenphasig ein; `voice.txt` trennt Mitte (Stimme) und Seite (Sound), damit nur die Stimme den Effekt bekommt.
