using System.Text;

namespace FrameShift.LocalizationTool;

internal static class CsvFile
{
    internal static string[][] Read(string path)
    {
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), true);
        var text = reader.ReadToEnd();
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') field.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else { quoted = false; closed = true; }
                continue;
            }
            if (c == ';' || c is '\r' or '\n')
            {
                row.Add(field.ToString()); field.Clear(); closed = false;
                if (c != ';')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    rows.Add(row.ToArray()); row.Clear();
                }
            }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else if (closed || c == '"') throw new FormatException($"Invalid CSV quotation near character {i + 1}.");
            else field.Append(c);
        }
        if (quoted) throw new FormatException("Unclosed quoted CSV field.");
        if (row.Count > 0 || field.Length > 0 || closed) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows.ToArray();
    }

    internal static void Write(string path, IEnumerable<string[]> rows)
    {
        // A new export never replaces an existing contribution or a project file.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(true)) { NewLine = "\r\n" };
        foreach (var row in rows)
            writer.WriteLine(string.Join(';', row.Select(value => "\"" + value.Replace("\"", "\"\"") + "\"")));
    }
}
