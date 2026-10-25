# Änderungen

Neueste Version oben. Die App zeigt diese Liste nach einem Update („Was ist neu“) und jederzeit unter *Hilfe → Was ist neu*.

## 1.14.0 – 25.10.2026
- **Eigenes Soundboard pro Charakter:** Im Soundboard gibt es jetzt „Sounds auswählen“ (auch im Menü Charakter) – genau wie bei den Stimmen. Alle Sounds erscheinen mit Haken; angehakte gehören zum aktiven Charakter, „Alle“ und „Keine“ helfen beim schnellen Umstellen. „Fertig“ zeigt nur noch die Sounds dieses Charakters.
- Nur die Shortcuts der Sounds des aktiven Charakters sind belegt – die anderen bleiben gespeichert und gelten wieder, sobald du den Sound zurück in den Charakter nimmst.
- Sounds, die du in der App hinzufügst (Knopf oder Hineinziehen), kommen automatisch zum aktiven Charakter. Neue Charaktere starten ohne Sounds. Bestehende Charaktere behalten zunächst alle Sounds.
- Die Statusleiste zeigt z. B. „Soundboard: 10 von 17 Sounds“. Ein Sound, der beim Charakterwechsel noch läuft, bleibt in der Status-Anzeige sichtbar.

## 1.13.0 – 24.10.2026
- **Sprachtaste nur, solange das Spiel vorne ist:** Dauersenden und das automatische Drücken bei Sounds halten die Taste nur noch, während nanos world im Vordergrund ist. Wechselst du per Alt+Tab in Discord oder den Browser, lässt die App die Taste los und drückt sie beim Zurückwechseln sofort wieder. So landet z. B. Maus 5 nie mehr im Browser, und Windows bleibt bedienbar.
- Schaltest du Dauersenden in der App selbst ein, zeigt der Knopf „wartet aufs Spiel“ – gedrückt wird, sobald du ins Spiel wechselst. Beim offenen Mikrofon wird das Antippen ebenso nachgeholt, und eigene Tastendrücke zählen nur noch, wenn sie im Spiel landen.
- Einstellungen → Sprachtaste im Spiel: Haken „Taste nur drücken, solange das Spiel im Vordergrund ist“ (Standard: an) und das Spiel als Programmname – „Erkennen …“ übernimmt es, wenn du innerhalb von 5 Sekunden ins Spiel wechselst. Daneben steht, ob das Spiel gerade läuft.
- **Warnung bei Administratorrechten:** Läuft das Spiel als Administrator, die App aber nicht, blockiert Windows die Sprachtaste. Die App erkennt das jetzt und sagt, dass sie dann ebenfalls als Administrator gestartet werden muss.

## 1.12.0 – 24.10.2026
- **Neu: Modus „Offenes Mikrofon im Spiel“** als zweiter Weg zum Dauersenden (Einstellungen → Sprachtaste im Spiel). Stellst du im Spiel den Sprachchat auf offenes Mikrofon (nanos world: Einstellungen → Audio → „Open Mic“), sendet das Spiel selbst dauerhaft – auch alle Sounds. Die App hält dann keine Taste mehr gedrückt: Die Umschalt-Taste tippt die Sprachtaste nur kurz an und schaltet dich damit im Spiel stumm bzw. wieder an.
- Soundboard, Status-Anzeige, Extras- und Tray-Menü zeigen in diesem Modus „Mikro im Spiel: AN“ bzw. „stumm“. Drückst du die Sprachtaste im Spiel selbst, zählt die App mit; stimmt die Anzeige trotzdem einmal nicht, dreht ⇄ sie um, ohne eine Taste zu drücken.
- Der bisherige Weg (Push-to-Talk, die App hält die Taste) bleibt Standard und funktioniert wie gehabt. Wechselst du während Dauersenden den Modus, wird die Taste sofort losgelassen.

## 1.11.0 – 23.10.2026
- **Einstellungen neu geordnet:** Links eine Navigation zu allen Bereichen (markiert, wo du gerade bist), rechts je Bereich eine eigene Karte mit Symbol, kurzer Erklärung und sauber ausgerichteten Feldern – Allgemein, Notfall-Taste, Sprachtaste im Spiel, Status-Anzeige, Geräte, Ordner, Stream (OBS) und Updates.
- Unter Updates gibt es jetzt auch „Was ist neu“.

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
