using System;
using System.Reactive;
using ReactiveUI;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// Backs the small "add / edit note" dialog: a title and a free-text body. The caller persists the
/// note after the dialog confirms.
/// </summary>
public sealed class NoteEditViewModel : ReactiveObject
{
    private string _noteTitle;
    private string _body;

    public NoteEditViewModel(string heading, string title, string body)
    {
        Heading = heading;
        _noteTitle = title ?? "";
        _body = body ?? "";

        SaveCommand = ReactiveCommand.Create(() =>
        {
            Confirmed = true;
            CloseRequested?.Invoke();
        });
        CancelCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke());
    }

    public string Heading { get; }

    public string NoteTitle { get => _noteTitle; set => this.RaiseAndSetIfChanged(ref _noteTitle, value); }
    public string Body { get => _body; set => this.RaiseAndSetIfChanged(ref _body, value); }

    /// <summary>True only when the user pressed Save (not Cancel / window close).</summary>
    public bool Confirmed { get; private set; }

    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public event Action? CloseRequested;
}
