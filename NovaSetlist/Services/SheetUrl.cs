namespace NovaSetlist.Services;

public static class SheetUrl
{
    /// <summary>Accepts either a bare spreadsheet ID or a full pasted sheet URL.</summary>
    public static string ExtractSpreadsheetId(string input)
    {
        input = input.Trim();
        var marker = input.IndexOf("/d/", StringComparison.Ordinal);
        if (marker < 0)
            return input;
        var id = input[(marker + 3)..];
        var end = id.IndexOfAny(new[] { '/', '?', '#' });
        return end < 0 ? id : id[..end];
    }
}
