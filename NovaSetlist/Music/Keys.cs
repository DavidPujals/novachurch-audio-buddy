namespace NovaSetlist.Music;

/// <summary>
/// Musical key helpers: the dropdown key list and enharmonic-equivalent lookup.
/// "Equivalent" is strictly enharmonic (same pitch, different spelling) — no transposition.
/// </summary>
public static class Keys
{
    private static readonly Dictionary<string, string> Enharmonic = new()
    {
        ["C#"] = "Db",
        ["Db"] = "C#",
        ["D#"] = "Eb",
        ["Eb"] = "D#",
        ["F#"] = "Gb",
        ["Gb"] = "F#",
        ["G#"] = "Ab",
        ["Ab"] = "G#",
        ["A#"] = "Bb",
        ["Bb"] = "A#",
        ["B"] = "Cb",
        ["Cb"] = "B",
        ["E#"] = "F",
        ["Fb"] = "E",
        ["B#"] = "C",
    };

    private static readonly string[] Roots =
        { "C", "C#", "Db", "D", "D#", "Eb", "E", "F", "F#", "Gb", "G", "G#", "Ab", "A", "A#", "Bb", "B" };

    /// <summary>All majors followed by all minors, for the key dropdowns.</summary>
    public static readonly IReadOnlyList<string> StandardKeys =
        Roots.Concat(Roots.Select(r => r + "m")).ToArray();

    /// <summary>
    /// Cleans up a key string: trims, uppercases the root letter, normalizes the
    /// accidental (♯/♭ to #/b) and lowercases a minor suffix. Returns "" for blank input.
    /// Unrecognized text is returned trimmed rather than rejected.
    /// </summary>
    public static string Normalize(string? raw)
    {
        var (root, minor, ok) = Parse(raw);
        if (!ok)
            return raw?.Trim() ?? "";
        return root + (minor ? "m" : "");
    }

    /// <summary>
    /// Returns the enharmonically equivalent spelling of a key, preserving a minor
    /// suffix (e.g. "Ebm" → "D#m"), or null if there is none (naturals) or the
    /// input isn't a recognizable key.
    /// </summary>
    public static string? EquivalentOf(string? key)
    {
        var (root, minor, ok) = Parse(key);
        if (!ok || !Enharmonic.TryGetValue(root, out var eq))
            return null;
        return eq + (minor ? "m" : "");
    }

    private static (string Root, bool Minor, bool Ok) Parse(string? raw)
    {
        var k = (raw ?? "").Trim().Replace('♯', '#').Replace('♭', 'b');
        if (k.Length == 0)
            return ("", false, false);

        var minor = k.Length > 1 && k[^1] == 'm';
        var root = minor ? k[..^1] : k;
        if (root.Length is < 1 or > 2)
            return ("", false, false);

        var letter = char.ToUpperInvariant(root[0]);
        if (letter is < 'A' or > 'G')
            return ("", false, false);

        var norm = letter.ToString();
        if (root.Length == 2)
        {
            if (root[1] == '#')
                norm += '#';
            else if (root[1] is 'b' or 'B')
                norm += 'b';
            else
                return ("", false, false);
        }
        return (norm, minor, true);
    }
}
