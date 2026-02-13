//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Reflection;

namespace NoZ.Editor;

/// <summary>
/// Provides Unicode character name lookup from a bundled UnicodeNames.txt embedded resource.
/// </summary>
internal static class UnicodeNames
{
    private static Dictionary<int, string>? _names;

    public static string? GetName(int codepoint)
    {
        _names ??= Load();
        return _names.TryGetValue(codepoint, out var name) ? name : null;
    }

    private static Dictionary<int, string> Load()
    {
        var dict = new Dictionary<int, string>();
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("UnicodeNames.txt");
        if (stream == null)
            return dict;

        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var sep = line.IndexOf(';');
            if (sep <= 0) continue;

            if (int.TryParse(line.AsSpan(0, sep), System.Globalization.NumberStyles.HexNumber, null, out var cp))
                dict[cp] = line.Substring(sep + 1);
        }

        return dict;
    }
}
