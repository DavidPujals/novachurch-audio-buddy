namespace NovaSetlist.Models;

public sealed class Song
{
    public string Name { get; set; } = "";
    public string DefaultKey { get; set; } = "";

    /// <summary>Song length as written in the sheet ("3:45", "1:02:30", "0:03:45:12"); "" = unknown.</summary>
    public string Length { get; set; } = "";

    /// <summary>Tempo as written in the sheet ("72"); "" = unknown.</summary>
    public string Bpm { get; set; } = "";
}
