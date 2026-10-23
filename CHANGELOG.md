# Änderungen

Neueste Version oben. Die App zeigt diese Liste nach einem Update („Was ist neu“) und jederzeit unter *Hilfe → Was ist neu*.

## 1.10.1 – 23.10.2026
- Tasten werden ohne den Zusatz „(auch Num)“ bzw. „(auch 1)“ angezeigt. Ziffern-Shortcuts funktionieren weiterhin auf beiden Zahlenreihen.

## 1.10.0 – 23.10.2026
- **Einrichtung prüft das Windows-Standardmikrofon:** Programme ohne eigene Mikrofon-Auswahl – z. B. der Steam-Sprachchat und damit nanos world – nehmen einfach den Windows-Standard. Steht dort das echte Mikrofon, hören die anderen dich ohne Effekt. Die Einrichtung warnt jetzt und stellt per Klick „CABLE Output“ als Standard- und Kommunikationsgerät ein; der Einrichtungs-Assistent hat dafür einen Knopf im letzten Schritt.
- **„Kabel als Windows-Standardmikrofon halten“** (Einstellungen → Geräte): Stellt ein anderes Programm (z. B. Shure MOTIV Mix, NVIDIA Broadcast) sein Mikrofon wieder als Standard ein, setzt die App sofort das Kabel zurück. Bei einem ständigen Hin und Her hört sie nach drei Versuchen pro Minute auf.
- Die Einrichtung zeigt außerdem, welche Sprachtaste fürs Spiel eingestellt ist.
- Ein umgestelltes Standardmikrofon bricht keine laufenden Sounds mehr ab.
- Die Diagnose („Problem melden“) enthält Standardmikrofon und Sprachtaste.

## 1.9.1 – 23.10.2026
- **Sounds enden wieder von selbst:** Manche Sounds galten nach dem Abspielen weiter als „laufend“ – dann musste man sie einmal „stoppen“, bevor sie wieder starteten. Betroffen waren Sounds, deren Länge nicht genau in die Audio-Blöcke von Windows passt, und vor allem das Mithören auf den Kopfhörern.
- Damit blieb in solchen Fällen auch die **Sprachtaste** nach dem Sound gedrückt. Behoben.
- Notbremse: Ein Sound, der deutlich länger „läuft“, als er dauert, wird automatisch beendet.

## 1.9.0 – 23.10.2026
- **Was ist neu:** Nach einem Update zeigt die App einmal, was sich seit deiner letzten Version geändert hat – auch wenn du mehrere Versionen übersprungen hast.
- **Alle Änderungen nachlesen:** *Hilfe → Was ist neu (alle Versionen)* listet jede Version mit ihren Änderungen.
- Das Update-Angebot zeigt die Neuerungen ohne Formatierungszeichen.

## 1.8.4 – 22.10.2026
- **Richtige Länge bei „Stream“-WAV-Dateien:** Manche Konverter tragen im WAV-Kopf keine echte Größe ein. Solche Sounds wurden als 3:06:24 lang angezeigt – jetzt zählt die tatsächliche Länge (z. B. 12,5 s).

## 1.8.3 – 22.10.2026
- **Sprachtaste bleibt nicht mehr hängen:** Wurde die App beendet, während sie die Sprachtaste hielt (Absturz, Task-Manager, Update), blieb die Taste in Windows gedrückt – bei einer Maustaste ließ sich nichts mehr anklicken. Jetzt lässt die App sie beim Beenden, Abmelden und bei Fehlern los und spätestens beim nächsten Start.
- Testläufe drücken nie die echte Sprachtaste.

## 1.8.2 – 22.10.2026
- **Dauersenden reißt nicht mehr ab:** Drückst du die Sprachtaste zwischendurch selbst und lässt sie los, drückt die App sie sofort wieder.
- Gilt auch während eines Sounds mit automatischer Sprachtaste.

## 1.8.1 – 22.10.2026
- **Sprachtaste in den Einstellungen:** Unter *Einstellungen → Sprachtaste im Spiel* stellst du die Sprachtaste des Spiels (Taste oder Maus 4/5) und die Tastenkombination für Dauersenden an/aus ein.
- Im Soundboard: Dauersenden-Knopf mit Kurzinfo und „Sprachtaste einstellen …“.

## 1.8.0 – 22.10.2026
- **Dauersenden:** Für Spiele, die nur Push-to-Talk können (z. B. nanos world). Die App hält die Sprachtaste dauerhaft gedrückt – wie ein offenes Mikrofon. Umschalten per Knopf, Menü, Tray oder eigener Taste; die Notfall-Taste schaltet es aus.
- **Sprachtaste bleibt nach Sounds nicht mehr hängen:** Die App drückt sie erst, wenn Strg/Alt/Umschalt vom Shortcut losgelassen sind.

## 1.7.0 – 22.10.2026
- **Sprachtaste des Spiels beim Abspielen:** Die App drückt Push-to-Talk kurz vor dem Sound und lässt es danach wieder los – sonst überträgt der Sprachchat keine Sounds.
- **Lautstärke angleichen:** Jeder Sound wird automatisch auf volle Lautstärke gebracht.
- **Regler bis 200 %** mit weichem Begrenzer gegen Übersteuern.

## 1.6.0 – 22.10.2026
- **Jeder Sound lässt sich abbrechen:** Derselbe Shortcut oder „■ Stoppen“ beendet ihn – egal ob einmal, n-mal oder als Loop.
- **Fortschritt an der Karte** mit Balken und Zeit, z. B. „0:03 / 0:12“ bzw. „Loop 4 · …“.
- **Fortschritt in der Status-Anzeige** für laufende Sounds.

## 1.5.0 – 22.10.2026
- **Sounds wiederholen:** einmal, 2×, 3×, 5×, 10× oder endlos (Loop) – ohne Lücke.
- **Dieselbe Taste stoppt** einen laufenden Loop.
- Auswahllisten im dunklen Design.

## 1.4.0 – 22.10.2026
- **Exklusivmodus abschalten:** Öffnet ein Spiel das Kabel „exklusiv“, umgeht Windows den Stimmeffekt. Die Einrichtung erkennt das und schaltet es per Klick ab.
- **Problem melden:** *Hilfe → Problem melden* kopiert eine Diagnose und öffnet ein GitHub-Issue.
- Fehler werden protokolliert; bei unerwarteten Fehlern läuft die App weiter.

## 1.3.0 – 22.10.2026
- **Einrichtungs-Assistent:** führt von null bis ins Spiel – VB-CABLE, Equalizer APO, Zugriff, Neustart, Mikrofon ins Kabel, Wiedergabegerät, Ausprobieren, Discord/Spiel. Jeder Schritt prüft selbst, ob er erledigt ist.

## 1.2.0
- **Notfall-Taste** (Standard: Pause): sofort normale Stimme, alle Sounds aus.
- **Lautstärke-Ausgleich** pro Stimme und **Probehören** (normal und mit Effekt vergleichen).
- Stimmen **exportieren und importieren** (.txt oder ZIP).
- Status-Anzeige: Größe, Deckkraft, optional nur mit Effekt-Stimme.
- **OBS-Textdatei** mit der aktiven Stimme.
- Installer und automatische Update-Prüfung.

## 1.0 – 1.1
- Stimmen per Shortcut umschalten oder nur halten, **Charaktere** mit eigener Tastenbelegung.
- **Soundboard** mit Sounds wahlweise durch den Stimmeffekt oder unverändert.
- Stimmen in der App anlegen, bearbeiten, kopieren und löschen; Sounds umbenennen und löschen.
- Einrichtungs-Check, Status-Anzeige, Tray, Menüleiste und Tastenkürzel.
