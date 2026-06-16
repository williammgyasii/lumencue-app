using System;
using System.Reactive;
using ChurchProjection.UI.ViewModels.Planning;
using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// Backs the lightweight "Quick edit" dialog: edit just one slide's section type and lyric text
/// without opening the full song editor. The caller persists the change after the dialog confirms.
/// </summary>
public sealed class QuickSlideEditViewModel : ReactiveObject
{
    private string _sectionType;
    private string _text;

    public QuickSlideEditViewModel(string songTitle, string sectionType, string text)
    {
        Heading = songTitle;
        _sectionType = string.IsNullOrWhiteSpace(sectionType) ? "verse" : sectionType;
        _text = text ?? "";

        SaveCommand = ReactiveCommand.Create(() =>
        {
            Confirmed = true;
            CloseRequested?.Invoke();
        });
        CancelCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke());
    }

    public string Heading { get; }
    public IReadOnlyList<string> SectionTypes => SongSectionVm.Types;

    public string SectionType { get => _sectionType; set => this.RaiseAndSetIfChanged(ref _sectionType, value); }
    public string Text { get => _text; set => this.RaiseAndSetIfChanged(ref _text, value); }

    /// <summary>True only when the user pressed Save (not Cancel / window close).</summary>
    public bool Confirmed { get; private set; }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public event Action? CloseRequested;
}
