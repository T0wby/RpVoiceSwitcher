# RP Voice Switcher

Stimmen verstellen fürs Roleplay – per Tastendruck, auch mitten im Spiel. Maske, Funkgerät, Megafon, Höhle, Metallhelm, Geist, Riese … oder eigene Stimmen. Dazu Charaktere mit eigener Tastenbelegung und ein Soundboard.

Windows-Desktop-App (WPF, .NET 10). Die Effekte macht [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) auf einem virtuellen Kabel ([VB-CABLE](https://vb-audio.com/Cable/)); die App schaltet zwischen den Stimmen um.

## Installieren

1. Den neuesten Installer unter **[Releases](https://github.com/T0wby/RpVoiceSwitcher/releases/latest)** herunterladen (`RpVoiceSwitcher-Setup-x.y.z.exe`) und starten. Keine Administratorrechte nötig, .NET muss nicht installiert sein.

   > **„Der Computer wurde durch Windows geschützt“?** Der Installer ist (noch) nicht digital signiert, deshalb warnt Windows SmartScreen bei neuen Programmen. Auf **„Weitere Informationen“** klicken und dann **„Trotzdem ausführen“**. Wer sichergehen will, kann den Quellcode hier selbst prüfen und bauen (siehe unten).
2. Beim ersten Start öffnet sich der **Einrichtungs-Assistent**. Er führt Schritt für Schritt durch alles Weitere – auch wenn noch gar nichts eingerichtet ist:
   VB-CABLE installieren → Equalizer APO installieren und an „CABLE Output“ hängen → Zugriff freigeben → Neustart → Mikrofon per „Abhören“ ins Kabel leiten → Wiedergabegerät prüfen → ausprobieren → im Spiel und in Discord „CABLE Output“ als Mikrofon wählen.
   Jeder Schritt prüft selbst, ob er erledigt ist. Später jederzeit wieder unter *Hilfe → Einrichtungs-Assistent*.

Updates findet die App selbst (abschaltbar) – oder *Hilfe → Nach Updates suchen*. Nach einem Update zeigt sie einmal, was neu ist; alle Änderungen stehen unter *Hilfe → Was ist neu* und in der [CHANGELOG.md](CHANGELOG.md).

## Hilfe, wenn etwas nicht klappt

- *Extras → Einrichtung* prüft die ganze Kette und behebt vieles per Klick.
- Kein Effekt im Spiel, obwohl die Stimme aktiv ist? Meist hat das Spiel das Mikrofon „exklusiv“ geöffnet – die Einrichtung schaltet den Exklusivmodus an „CABLE Output“ ab.
- Kein Effekt im Steam-Sprachchat (z. B. nanos world)? Steam nimmt das Windows-Standardmikrofon – die Einrichtung setzt „CABLE Output“ per Klick als Standard, und *Einstellungen → Geräte → Kabel als Windows-Standardmikrofon halten* verhindert, dass andere Programme es zurückstellen.
- Sounds kommen im Sprachchat nicht an? Lautstärke für andere hochdrehen (bis 200 %) – und bei Spielen mit Push-to-Talk (z. B. nanos world) unter *Einstellungen → Sprachtaste im Spiel* dieselbe Taste wie im Spiel wählen und „Beim Abspielen von Sounds automatisch drücken“ anhaken (oder „Dauersenden“ nutzen).
- Sonst: *Hilfe → Problem melden* kopiert eine Diagnose und öffnet ein [GitHub-Issue](https://github.com/T0wby/RpVoiceSwitcher/issues/new?template=problem.yml) zum Einfügen.

## Funktionen

- **Stimmen** umschalten oder nur solange eine Taste gehalten wird; Shortcuts funktionieren global, auch im Spiel
- **Charaktere** mit eigener Stimmenauswahl und eigenen Shortcuts
- **Notfall-Taste** (Standard: Pause): sofort normale Stimme, alle Sounds aus
- **Soundboard** – Sounds wahlweise durch den Stimmeffekt oder unverändert; einmal, n-mal oder als Endlos-Loop, den derselbe Shortcut wieder stoppt; gleicht die Lautstärke automatisch an (Regler bis 200 % mit Begrenzer) und drückt auf Wunsch die Push-to-Talk-Taste des Spiels, solange ein Sound läuft (Taste oder Maus 4/5)
- **Dauersenden**: hält die Push-to-Talk-Taste des Spiels per Shortcut dauerhaft gedrückt – offenes Mikrofon auch in Spielen, die nur Push-to-Talk können
- **Offenes Mikrofon im Spiel** (zweiter Weg, z. B. nanos world: Audio → „Open Mic“): Das Spiel sendet selbst; die App hält keine Taste, sondern tippt die Sprachtaste zum Stummschalten/Anschalten nur kurz an und zeigt an, ob du gerade sendest
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

## Lizenz

[MIT](LICENSE) – frei nutzbar, veränderbar und weitergebbar, solange der Lizenzhinweis erhalten bleibt.
Equalizer APO, VB-CABLE und Graillon sind eigenständige Programme ihrer jeweiligen Hersteller und werden nicht mitgeliefert.

## Wie es funktioniert

Das Mikrofon wird per Windows-„Abhören“ nach *CABLE Input* geleitet. Equalizer APO hängt an *CABLE Output* und bindet `voice.txt` ein, die die App beim Umschalten neu schreibt. Sounds ohne Effekt spielt das Soundboard gegenphasig ein; `voice.txt` trennt Mitte (Stimme) und Seite (Sound), damit nur die Stimme den Effekt bekommt.
