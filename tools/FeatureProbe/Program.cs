// Prueft die Logik hinter Lautstaerke-Ausgleich, Export/Import, Pegelmessung, Versionen und Tasten
// direkt gegen RpVoiceSwitcher.dll - ohne Oberflaeche und ohne die echte Equalizer-APO-Konfiguration anzufassen.
using System.IO;
using System.IO.Compression;
using System.Windows.Input;
using RpVoiceSwitcher;

int fails = 0;
void Check(string name, bool ok)
{
    Console.WriteLine($"{name,-64} {(ok ? "OK" : "!! FEHLER")}");
    if (!ok) fails++;
}

// ---------- Lautstaerke ----------
Check("ParseGain \"+3 dB\"", VoiceStore.ParseGain("+3 dB") == 3);
Check("ParseGain \"-2,5\"", VoiceStore.ParseGain("-2,5") == -2.5);
Check("ParseGain begrenzt auf +12", VoiceStore.ParseGain("40") == 12);
Check("ParseGain Unsinn = 0", VoiceStore.ParseGain("laut") == 0);
Check("FormatGain +2,5 dB", VoiceStore.FormatGain(2.5) == "+2,5 dB");

// ---------- Laenge von "Stream"-WAVs (Groesse 0xFFFFFFFF im Kopf, z. B. ffmpeg-Pipe) ----------
{
    string wav = Path.Combine(Path.GetTempPath(), "rpvs-streamwav-" + Guid.NewGuid().ToString("N") + ".wav");
    int rate = 48000, seconds = 3;
    using (var bw = new BinaryWriter(File.Create(wav)))
    {
        bw.Write("RIFF"u8); bw.Write(0xFFFFFFFFu); bw.Write("WAVE"u8);
        bw.Write("fmt "u8); bw.Write(18); bw.Write((short)3); bw.Write((short)2); bw.Write(rate); bw.Write(rate * 8); bw.Write((short)8); bw.Write((short)32); bw.Write((short)0);
        bw.Write("LIST"u8); bw.Write(26); bw.Write(new byte[26]);
        bw.Write("data"u8); bw.Write(0xFFFFFFFFu);
        for (int i = 0; i < rate * seconds; i++) { float s = 0.3f * MathF.Sin(i * 0.05f); bw.Write(s); bw.Write(s); }
    }
    TimeSpan reported;
    using (var r = new NAudio.Wave.AudioFileReader(wav)) reported = r.TotalTime;
    var real = SoundEngine.DurationOf(wav, reported);
    Check($"Stream-WAV: NAudio meldet {reported:h\\:mm\\:ss}, echte Laenge {real.TotalSeconds:F2} s", Math.Abs(real.TotalSeconds - seconds) < 0.01);
    Check("Normale WAV: Laenge unveraendert", SoundEngine.DurationOf(wav.Replace(".wav", ".mp3"), TimeSpan.FromSeconds(7)) == TimeSpan.FromSeconds(7));
    File.Delete(wav);
    // Nur zur Info: echte Sounds des Nutzers (werden nicht veraendert)
    string sounds = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RP Voice Switcher", "Sounds");
    if (Directory.Exists(sounds))
        foreach (var f in Directory.GetFiles(sounds, "*.wav"))
        {
            using var r = new NAudio.Wave.AudioFileReader(f);
            Console.WriteLine($"   {Path.GetFileName(f),-42} Kopf: {r.TotalTime:h\\:mm\\:ss}  ->  {SoundEngine.DurationOf(f, r.TotalTime).TotalSeconds:F2} s");
        }
}

string dir = Path.Combine(Path.GetTempPath(), "rpvs-probe-" + Guid.NewGuid().ToString("N"));
string dir2 = dir + "-import";
string zip = dir + ".zip";
string evilZip = dir + "-boese.zip";
try
{
    Directory.CreateDirectory(dir);
    Directory.CreateDirectory(dir2);
    var store = VoiceStore.TryCreate(dir) ?? throw new Exception("Equalizer APO nicht gefunden");

    string file = store.CreateVoice("Probe Gain", "Beschreibung", "", ["Preamp: -1 dB"], 4.5);
    var v = store.LoadVoices().Single(x => x.FileName == file);
    Check("Neue Stimme: Lautstaerke aus Kopfzeile gelesen", v.GainDb == 4.5);

    store.UpdateHeader(v, "Probe Gain", "B2", "Pitch -2", -3);
    v = store.LoadVoices().Single(x => x.FileName == file);
    Check("Bearbeiten: Lautstaerke geaendert, Effekte + Hinweis erhalten",
        v.GainDb == -3 && store.ReadBody(v).Contains("Preamp: -1 dB") && v.GraillonHint == "Pitch -2");
    Check("Bearbeiten: genau eine Lautstaerke-Zeile", File.ReadAllLines(v.FullPath).Count(l => l.Contains("Lautstaerke")) == 1);

    store.UpdateHeader(v, "Probe Gain", "B2", "Pitch -2", 0);
    v = store.LoadVoices().Single(x => x.FileName == file);
    Check("Lautstaerke 0 = keine Kopfzeile", v.GainDb == 0 && !File.ReadAllText(v.FullPath).Contains("Lautstaerke"));
    store.UpdateHeader(v, "Probe Gain", "B2", "Pitch -2", -3);

    // ---------- Export / Import ----------
    VoiceStore.ExportZip(store.LoadVoices(), zip);
    using (var check = ZipFile.OpenRead(zip)) Check("Export: ZIP angelegt", check.Entries.Count == 1);

    var store2 = VoiceStore.TryCreate(dir2)!;
    var imported = store2.ImportZip(zip);
    Check("Import: Stimme samt Lautstaerke uebernommen", imported.Count == 1 && store2.LoadVoices().Single().GainDb == -3);
    Check("Import: gleiche Stimme nicht doppelt", store2.ImportZip(zip).Count == 0);

    using (var z = ZipFile.Open(evilZip, ZipArchiveMode.Create))
    {
        using (var w = new StreamWriter(z.CreateEntry(@"..\..\boese.txt").Open())) w.Write("# Name: Boese");
        using (var w = new StreamWriter(z.CreateEntry("bild.png").Open())) w.Write("kein Bild");
    }
    var evil = store2.ImportZip(evilZip);
    Check("Import: Pfade aus der ZIP werden ignoriert (nur Dateiname)",
        evil.Count == 1 && File.Exists(Path.Combine(dir2, evil[0])) && !File.Exists(Path.Combine(Path.GetDirectoryName(dir2)!, "boese.txt")));
    Check("Import: Nicht-Stimmen werden uebersprungen", !Directory.GetFiles(dir2, "*.png").Any());
}
finally
{
    foreach (var folder in new[] { dir, dir2 }) try { Directory.Delete(folder, true); } catch { }
    foreach (var f in new[] { zip, evilZip }) try { File.Delete(f); } catch { }
}

// ---------- Pegelmessung (Probehoeren) ----------
static VoiceTester.Clip Sine(double amp, double seconds, bool pauses = false)
{
    int rate = 48000;
    var s = new float[(int)(rate * seconds)];
    for (int i = 0; i < s.Length; i++)
    {
        bool silent = pauses && (i / (rate / 4)) % 2 == 1; // jede zweite Viertelsekunde Pause
        s[i] = silent ? 0.0005f * (float)Math.Sin(i * 0.37) : (float)(amp * Math.Sin(2 * Math.PI * 300 * i / rate));
    }
    return new VoiceTester.Clip(s, rate);
}
double? loud = VoiceTester.SpeechLevelDb(Sine(0.1, 1));
double? quiet = VoiceTester.SpeechLevelDb(Sine(0.05, 1));
double? withPauses = VoiceTester.SpeechLevelDb(Sine(0.1, 2, pauses: true));
Check($"Pegel Sinus 0,1 = -23 dB ({loud:0.0})", loud is double l && Math.Abs(l + 23) < 0.5);
Check($"Halbe Amplitude = 6 dB leiser ({loud - quiet:0.0})", loud - quiet is double d && Math.Abs(d - 6.02) < 0.3);
Check($"Pausen zaehlen nicht mit ({withPauses:0.0})", withPauses is double p && Math.Abs(p - loud!.Value) < 0.5);
Check("Stille = nichts gehoert", VoiceTester.SpeechLevelDb(new VoiceTester.Clip(new float[48000], 48000)) == null);

// ---------- Versionen ----------
Check("Version v1.3.0 erkannt", UpdateChecker.TryParseVersion("v1.3.0", out var v13) && v13 == new Version(1, 3, 0));
Check("Version 1.2 = 1.2.0", UpdateChecker.TryParseVersion("1.2", out var v12) && v12 == new Version(1, 2, 0));
Check("Version v1.10.0-beta = 1.10.0", UpdateChecker.TryParseVersion("v1.10.0-beta", out var v110) && v110 == new Version(1, 10, 0));
var update = await UpdateChecker.CheckAsync();
Console.WriteLine($"Update-Pruefung gegen github.com/{UpdateChecker.Repository}: {update.State} {update.Error}");
Check("Update-Pruefung liefert ein Ergebnis ohne Absturz", update.State != UpdateChecker.State.Failed || update.Error != null);

// ---------- Tasten ----------
var pause = new Hotkey(ModifierKeys.None, Key.Pause);
Check("Pause ohne Strg/Alt erlaubt, heisst \"Pause\"", pause.IsSafeWithoutModifier && pause.ToString() == "Pause");
Check("Buchstabe ohne Strg/Alt nicht erlaubt", !new Hotkey(ModifierKeys.None, Key.W).IsSafeWithoutModifier);
Check("Standard-Notfall-Taste ist Pause", Hotkey.Parse(new AppSettings().PanicHotkey) == pause);

// ---------- Einrichtungs-Assistent: Zustand dieses PCs ----------
var status = SetupChecker.Probe(AppSettings.Load());
Console.WriteLine("Einrichtung hier: " + status);
Console.WriteLine("Assistent haelt die Einrichtung fuer vollstaendig: " + SetupWizardWindow.IsComplete(status));

// ---------- Diagnose ----------
var diagSettings = AppSettings.Load();
var diagStore = VoiceStore.TryCreate(diagSettings.VoicesFolder);
string diag = Diagnostics.Build(diagSettings, diagStore, SetupChecker.Run(diagStore, diagSettings, () => { }, _ => { }), "Test");
Check("Diagnose: enthaelt Pruefung, Geraete und voice.txt",
    diag.Contains("== Einrichtungs-Prüfung ==") && diag.Contains("== Geräte ==") && diag.Contains("== voice.txt =="));
Check("Diagnose: kein Windows-Benutzername in Pfaden", !diag.Contains(@"\" + Environment.UserName + @"\", StringComparison.OrdinalIgnoreCase));
Check("Diagnose: keine Charakternamen", diagSettings.Profiles.All(p => p.Name.Length < 3 || p.Name == "Standard" || !diag.Contains(p.Name, StringComparison.OrdinalIgnoreCase)));
Check($"Diagnose: handliche Laenge ({diag.Length} Zeichen)", diag.Length is > 500 and < 60000);
File.WriteAllText(Path.Combine(Path.GetTempPath(), "rpvs-diagnose.txt"), diag);

Console.WriteLine(fails == 0 ? "ALLE PRUEFUNGEN OK" : $"{fails} FEHLER");
return fails == 0 ? 0 : 1;
