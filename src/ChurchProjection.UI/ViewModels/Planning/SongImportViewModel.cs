using System.Reactive;
using ChurchProjection.Core.Services;
using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Planning;

public class SongImportViewModel : ViewModelBase
{
    private readonly IContentLibraryService _contentLibrary;

    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _lyrics = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _showStatus;

    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        set => this.RaiseAndSetIfChanged(ref _artist, value);
    }

    public string Lyrics
    {
        get => _lyrics;
        set => this.RaiseAndSetIfChanged(ref _lyrics, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool ShowStatus
    {
        get => _showStatus;
        set => this.RaiseAndSetIfChanged(ref _showStatus, value);
    }

    public ReactiveCommand<Unit, Unit> ImportCommand { get; }

    public event Action? SongImported;

    public SongImportViewModel(IContentLibraryService contentLibrary)
    {
        _contentLibrary = contentLibrary;

        var canImport = this.WhenAnyValue(
            x => x.Title, x => x.Lyrics,
            (t, l) => !string.IsNullOrWhiteSpace(t) && !string.IsNullOrWhiteSpace(l));

        ImportCommand = ReactiveCommand.CreateFromTask(DoImportAsync, canImport);
    }

    private async Task DoImportAsync()
    {
        var song = await _contentLibrary.ImportSongAsync(Title, Lyrics, string.IsNullOrWhiteSpace(Artist) ? null : Artist);
        StatusMessage = $"Imported \"{song.Title}\" with {song.Sections.Count} sections";
        ShowStatus = true;
        Title = string.Empty;
        Artist = string.Empty;
        Lyrics = string.Empty;
        SongImported?.Invoke();
    }
}
