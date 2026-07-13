using CommunityToolkit.Mvvm.ComponentModel;

namespace NovaSetlist.ViewModels;

/// <summary>One row of the current service order.</summary>
public partial class SetItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int index;

    [ObservableProperty]
    private string name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EquivalentText))]
    private string selectedKey = "";

    [ObservableProperty]
    private string leader = "";

    /// <summary>Colour-coding token from RowColors.Tokens; "" = none.</summary>
    [ObservableProperty]
    private string color = "";

    /// <summary>Song length from the sheet ("3:45"); "" = unknown.</summary>
    [ObservableProperty]
    private string length = "";

    /// <summary>Tempo from the sheet ("72"); "" = unknown.</summary>
    [ObservableProperty]
    private string bpm = "";

    /// <summary>True while the row shows its inline editor (pencil toggled).</summary>
    [ObservableProperty]
    private bool isEditing;

    /// <summary>True while this row is the "now playing" song (session-only).</summary>
    [ObservableProperty]
    private bool isPlaying;

    /// <summary>True once the song has been played this service — the row greys out.
    /// Toggled by clicking the row number; persisted so it survives a restart.</summary>
    [ObservableProperty]
    private bool isCompleted;

    /// <summary>Enharmonic spelling of the selected key, e.g. "(= Gb)", or "" for naturals.</summary>
    public string EquivalentText =>
        Music.Keys.EquivalentOf(SelectedKey) is { } eq ? $"(= {eq})" : "";
}
