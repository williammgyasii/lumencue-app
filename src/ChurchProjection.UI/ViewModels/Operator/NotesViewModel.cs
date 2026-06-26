using System.Collections.ObjectModel;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Infrastructure.Data;
using ReactiveUI;
using Serilog;

namespace ChurchProjection.UI.ViewModels.Operator;

/// <summary>
/// Backs the "Notes" tab: a small library of free-text notes (e.g. prayer points) shown as projectable
/// slide cards. Double-click a card to send it live; add/edit via the note dialog. Projection itself is
/// driven by <see cref="OperatorViewModel.SendNoteLive"/> so notes are themed exactly like scripture/songs.
/// </summary>
public class NotesViewModel : ViewModelBase
{
    private readonly NotesRepository _repo;

    private NoteSlideItem? _selectedCard;
    private string _statusText = "Add a note, then double-click it to show it.";

    public NotesViewModel(NotesRepository repo) => _repo = repo;

    /// <summary>Saved notes as projectable cards, most recently updated first.</summary>
    public ObservableCollection<NoteSlideItem> Cards { get; } = [];

    public bool HasNotes => Cards.Count > 0;

    public NoteSlideItem? SelectedCard
    {
        get => _selectedCard;
        set => this.RaiseAndSetIfChanged(ref _selectedCard, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    /// <summary>Loads the saved notes (most recently updated first) into cards.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var notes = await _repo.GetAllAsync();
            Cards.Clear();
            foreach (var note in notes)
                Cards.Add(new NoteSlideItem(note));
            this.RaisePropertyChanged(nameof(HasNotes));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load notes");
        }
    }

    /// <summary>Creates a new note and refreshes the cards.</summary>
    public async Task AddNoteAsync(string title, string body)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) return;
        try
        {
            await _repo.InsertAsync(new Note { Title = title.Trim(), Body = body });
            await LoadAsync();
            StatusText = "Note added.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to add note");
            StatusText = "Couldn't add the note.";
        }
    }

    /// <summary>Updates an existing note's title/body and refreshes the cards.</summary>
    public async Task UpdateNoteAsync(NoteSlideItem card, string title, string body)
    {
        try
        {
            card.Note.Title = title.Trim();
            card.Note.Body = body;
            await _repo.UpdateAsync(card.Note);
            await LoadAsync();
            StatusText = "Note saved.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update note");
            StatusText = "Couldn't save the note.";
        }
    }

    /// <summary>Deletes a note and refreshes the cards.</summary>
    public async Task DeleteNoteAsync(NoteSlideItem card)
    {
        try
        {
            await _repo.DeleteAsync(card.Note.Id);
            await LoadAsync();
            StatusText = "Note deleted.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete note");
            StatusText = "Couldn't delete the note.";
        }
    }
}

/// <summary>One saved note rendered as a projectable slide card in the Notes tab.</summary>
public class NoteSlideItem : ReactiveObject
{
    private bool _isLive;

    public NoteSlideItem(Note note)
    {
        Note = note;
        Preview = (note.Body ?? string.Empty).Replace("\r", "").Replace('\n', ' ');
    }

    public Note Note { get; }
    public string Title => string.IsNullOrWhiteSpace(Note.Title) ? "Untitled note" : Note.Title;
    public string Body => Note.Body;

    /// <summary>Single-line body preview for the card face.</summary>
    public string Preview { get; }

    /// <summary>True when this note is the one currently on the live output.</summary>
    public bool IsLive
    {
        get => _isLive;
        set => this.RaiseAndSetIfChanged(ref _isLive, value);
    }
}
