namespace NovaSetlist.Models;

public sealed class SetItemDto
{
    public string Name { get; set; } = "";
    public string SelectedKey { get; set; } = "";
    public string Leader { get; set; } = "";

    /// <summary>Row colour-coding token from RowColors.Tokens; "" = none.</summary>
    public string Color { get; set; } = "";

    /// <summary>Song length from the sheet; "" = unknown.</summary>
    public string Length { get; set; } = "";

    /// <summary>True once the song has been played this service (row greys out).</summary>
    public bool Completed { get; set; }
}

public sealed class ServiceSet
{
    public List<SetItemDto> Items { get; set; } = new();
    public DateTime? ServiceDate { get; set; }
}
