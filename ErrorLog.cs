using System;
using System.IO;
using System.Linq;

namespace RpVoiceSwitcher;

/// <summary>Kleine Fehlerprotokoll-Datei für die Diagnose (Hilfe → Problem melden).</summary>
public static class ErrorLog
{
    private const long MaxBytes = 200_000;
    private static readonly object Lock = new();

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RpVoiceSwitcher", "fehler.log");

    public static void Write(string context, Exception? ex)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    // Nur die zweite Hälfte behalten
                    var text = File.ReadAllText(FilePath);
                    File.WriteAllText(FilePath, text[(text.Length / 2)..]);
                }
                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context} (Version {UpdateChecker.CurrentVersionText}){Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { /* Protokollieren darf nie selbst einen Fehler auslösen */ }
    }

    /// <summary>Die letzten Einträge (für die Diagnose).</summary>
    public static string ReadRecent(int maxLines = 60)
    {
        try
        {
            if (!File.Exists(FilePath)) return "(keine Fehler protokolliert)";
            var lines = File.ReadAllLines(FilePath);
            return string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - maxLines)));
        }
        catch (Exception ex)
        {
            return "(Fehlerprotokoll nicht lesbar: " + ex.Message + ")";
        }
    }
}
