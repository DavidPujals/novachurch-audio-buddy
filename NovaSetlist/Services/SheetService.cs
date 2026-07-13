using System.Globalization;
using System.IO;
using System.Net.Http;
using CsvHelper;
using CsvHelper.Configuration;
using NovaSetlist.Models;

namespace NovaSetlist.Services;

/// <summary>Reads the master Songs and Leaders lists from a shared Google Sheet via the gviz CSV endpoint.</summary>
public sealed class SheetService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<(List<Song> Songs, List<string> Leaders)> FetchAsync(AppConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SpreadsheetId) || config.SpreadsheetId == "PUT_ID_HERE")
            throw new InvalidOperationException("No Spreadsheet ID set — open Settings and paste your sheet's ID or URL.");

        // Both tabs in flight at once — halves refresh latency. WhenAll observes
        // both outcomes, so one tab failing doesn't leave the other's exception dangling.
        var songsTask = FetchTabAsync(config.SpreadsheetId, config.SongsTab);
        var leadersTask = FetchTabAsync(config.SpreadsheetId, config.LeadersTab);
        await Task.WhenAll(songsTask, leadersTask);
        var songsCsv = songsTask.Result;
        var leadersCsv = leadersTask.Result;

        var songs = ParseRows(songsCsv)
            .Select(r => new Song
            {
                Name = Cell(r, 0),
                DefaultKey = Music.Keys.Normalize(Cell(r, 1)),
                Length = Cell(r, 2),
                Bpm = Cell(r, 3),
            })
            .Where(s => s.Name.Length > 0)
            .ToList();

        var leaders = ParseRows(leadersCsv)
            .Select(r => Cell(r, 0))
            .Where(n => n.Length > 0)
            .ToList();

        return (songs, leaders);
    }

    private static async Task<string> FetchTabAsync(string spreadsheetId, string tab)
    {
        // headers=1: without it Google GUESSES how many rows are headers, and a
        // mostly-empty column (e.g. a fresh BPM column) can make it swallow the
        // whole sheet as one giant multi-row header, returning almost no songs.
        var url = $"https://docs.google.com/spreadsheets/d/{Uri.EscapeDataString(spreadsheetId)}" +
                  $"/gviz/tq?tqx=out:csv&headers=1&sheet={Uri.EscapeDataString(tab)}";
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();

        // If the sheet isn't shared "Anyone with the link", Google returns an HTML sign-in page.
        if (text.TrimStart().StartsWith("<", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Tab '{tab}' returned a web page, not CSV — check the sheet is shared \"Anyone with the link: Viewer\" and the tab name is right.");

        return text;
    }

    /// <summary>Parses CSV into rows of trimmed cells, skipping the header row.</summary>
    private static List<string[]> ParseRows(string csv)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        var rows = new List<string[]>();
        using var reader = new CsvReader(new StringReader(csv), config);
        var first = true;
        while (reader.Read())
        {
            if (first) { first = false; continue; } // header row
            rows.Add(reader.Parser.Record ?? Array.Empty<string>());
        }
        return rows;
    }

    private static string Cell(string[] row, int index) =>
        index < row.Length ? (row[index] ?? "").Trim() : "";
}
