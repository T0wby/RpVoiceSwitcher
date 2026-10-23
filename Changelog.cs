using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace RpVoiceSwitcher;

/// <summary>
/// Die Änderungen je Version – aus CHANGELOG.md, das in die App eingebettet ist.
/// Format: "## 1.9.0 – Datum", darunter Zeilen mit "- " (Unterpunkte mit zwei Leerzeichen davor).
/// </summary>
public static class Changelog
{
    public sealed record Entry(string Title, Version? Version, IReadOnlyList<string> Lines);

    private static IReadOnlyList<Entry>? _all;

    /// <summary>Alle Versionen, neueste zuerst.</summary>
    public static IReadOnlyList<Entry> All => _all ??= Parse(ReadEmbedded());

    private static string ReadEmbedded()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md");
            if (s == null) return "";
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }
        catch { return ""; }
    }

    public static IReadOnlyList<Entry> Parse(string markdown)
    {
        var list = new List<Entry>();
        string? title = null;
        var lines = new List<string>();
        void Flush()
        {
            if (title == null) return;
            while (lines.Count > 0 && lines[^1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            string first = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            list.Add(new Entry(title, UpdateChecker.TryParseVersion(first, out var v) ? v : null, lines.ToList()));
            lines.Clear();
        }
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            if (raw.StartsWith("## "))
            {
                Flush();
                title = raw[3..].Trim();
            }
            else if (title != null && !(lines.Count == 0 && raw.Trim().Length == 0))
                lines.Add(raw.TrimEnd());
        }
        Flush();
        return list;
    }

    /// <summary>
    /// Was seit <paramref name="lastSeen"/> dazugekommen ist (bis einschließlich <paramref name="current"/>).
    /// Ohne bekannte Vorversion nur die aktuelle.
    /// </summary>
    public static IReadOnlyList<Entry> Since(Version? lastSeen, Version current) =>
        All.Where(e => e.Version is Version v && v <= current && (lastSeen == null ? v == current : v > lastSeen)).ToList();

    /// <summary>Release-Text von GitHub ohne Markdown-Zeichen und ohne den Installationshinweis – für das Update-Angebot.</summary>
    public static string ToPlainText(string markdown)
    {
        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            string line = raw.TrimEnd();
            string t = line.TrimStart();
            if (t.StartsWith("**Installieren") || t.StartsWith(">")) continue;
            if (t.StartsWith("#")) line = t.TrimStart('#').Trim();
            if (t.StartsWith("- ")) { int i = line.IndexOf("- ", StringComparison.Ordinal); line = line[..i] + "• " + line[(i + 2)..]; }
            line = Regex.Replace(line, @"\*\*(.+?)\*\*", "$1");
            line = Regex.Replace(line, @"`(.+?)`", "$1");
            line = Regex.Replace(line, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "$1");
            if (line.Length == 0 && (sb.Length == 0 || sb.ToString().EndsWith("\n\n"))) continue;
            sb.Append(line).Append('\n');
        }
        return sb.ToString().Trim();
    }
}
