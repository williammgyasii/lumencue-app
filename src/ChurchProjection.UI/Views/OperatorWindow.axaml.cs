using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.ReactiveUI;
using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Models.Slides;
using ChurchProjection.Core.Services;
using ChurchProjection.UI.ViewModels;
using ChurchProjection.UI.ViewModels.Operator;
using Serilog;

namespace ChurchProjection.UI.Views;

public partial class OperatorWindow : ReactiveWindow<OperatorViewModel>
{
    public OperatorWindow()
    {
        InitializeComponent();

        // Handle paging arrows in the TUNNEL phase so they fire before any focused ListBox swallows
        // them for its own selection movement. This is why arrows previously did nothing once a
        // Now Singing card or scripture verse was clicked (the list had focus and ate the key).
        AddHandler(KeyDownEvent, OnNavPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    // Centralised live-paging arrows, run before list controls see the key.
    // Now Singing: step the live marker through the song's slides. Everywhere else: page the live
    // deck (and roll over to the next/prev queue item at the ends).
    private void OnNavPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;
        if (e.Source is TextBox) return; // never hijack typing / caret movement

        var inNowSinging = vm.ShowNowSinging && vm.HasNowSinging;
        var inOpenNote = vm.IsNotesTab && vm.HasOpenNote;

        switch (e.Key)
        {
            case Key.Right:
                if (inNowSinging) vm.StepLive(+1);
                else if (inOpenNote) vm.StepNoteLive(+1);
                else vm.AdvanceForward();
                e.Handled = true;
                break;
            case Key.Left:
                if (inNowSinging) vm.StepLive(-1);
                else if (inOpenNote) vm.StepNoteLive(-1);
                else vm.AdvanceBackward();
                e.Handled = true;
                break;
            // Up/Down only drive live stepping inside Now Singing; elsewhere they stay free for
            // normal list navigation (search results, queue, etc.).
            case Key.Down:
                if (inNowSinging) { vm.StepLive(+1); e.Handled = true; }
                else if (inOpenNote) { vm.StepNoteLive(+1); e.Handled = true; }
                break;
            case Key.Up:
                if (inNowSinging) { vm.StepLive(-1); e.Handled = true; }
                else if (inOpenNote) { vm.StepNoteLive(-1); e.Handled = true; }
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.Source is TextBox)
        {
            if (e.Key == Key.Escape)
            {
                (e.Source as TextBox)?.ClearValue(TextBox.TextProperty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
            return;
        }

        switch (e.Key)
        {
            // Left/Right (and Up/Down in Now Singing) are handled in OnNavPreviewKeyDown (tunnel).

            case Key.Space:
            case Key.Enter:
                vm.TransitionCommand.Execute().Subscribe();
                e.Handled = true;
                break;

            case Key.Escape:
                vm.BlankCommand.Execute().Subscribe();
                e.Handled = true;
                break;

            case Key.Q:
                if (vm.ContentSearch.SelectedItem is not null)
                {
                    vm.ServiceQueue.AddItem(vm.ContentSearch.SelectedItem);
                    vm.StatusText = $"Queued: {vm.ContentSearch.SelectedItem.Title}";
                }
                e.Handled = true;
                break;

            case Key.A:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && vm.ContentSearch.Results.Count > 0)
                {
                    vm.ServiceQueue.AddAllItems(vm.ContentSearch.Results);
                    vm.StatusText = $"Queued {vm.ContentSearch.Results.Count} items";
                    e.Handled = true;
                }
                break;
        }

        if (!e.Handled)
            base.OnKeyDown(e);
    }

    // Single click sends straight to the Program (live) output, ProPresenter-style.
    public void OnContentListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: ContentItem item })
            vm.SendItemToLive(item);
    }

    public void OnSuggestionsTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: SuggestionItem item })
            vm.SendSuggestionToLive(item);
    }

    public void OnTopicalListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: ContentItem item })
            vm.SendItemToLive(item);
    }

    public void OnPageBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm) vm.AdvanceBackward();
    }

    public void OnPageForward(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm) vm.AdvanceForward();
    }

    public void OnContentListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;

        if (sender is ListBox listBox && listBox.SelectedItem is ContentItem item)
        {
            vm.SendItemToLive(item);
        }
    }

    public void OnTopicalListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: ContentItem item })
            vm.SendItemToLive(item);
    }

    // Song search: tap selects (binding); double-tap opens the full song in the Now Singing tab.
    public void OnSongSearchTapped(object? sender, TappedEventArgs e) { }

    public void OnSongSearchDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: SongHitViewModel hit })
            vm.OpenSong(hit.Song);
    }

    // "Show full song" button on a result: open it in the Now Singing tab (the slide view).
    public void OnShowFullSong(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Control { DataContext: SongHitViewModel hit })
            vm.OpenSong(hit.Song);
    }

    // Double-clicking a slide in the Now Singing tab projects just that section and marks it live.
    public void OnNowSingingSlideDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: SongSlideItem slide })
            vm.SendSlideLive(slide);
    }

    // ----- Notes tab -----

    // Clicking a note card opens its slide breakdown.
    public void OnNotesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: NoteSlideItem card })
            vm.OpenNote(card);
    }

    // Double-clicking a slide sends it live.
    public void OnNotePageDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: NotePageSlideItem page })
            vm.SendNotePageLive(page);
    }

    public void OnNotePageKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;
        if (e.Key is Key.Enter or Key.Space)
        {
            if (sender is ListBox { SelectedItem: NotePageSlideItem page })
                vm.SendNotePageLive(page);
            e.Handled = true;
        }
    }

    public void OnCloseOpenNote(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm)
            vm.CloseOpenNote();
    }

    public void OnStepNoteLivePrev(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm) vm.StepNoteLive(-1);
    }

    public void OnStepNoteLiveNext(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm) vm.StepNoteLive(+1);
    }

    public async void OnEditOpenNote(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;
        if (vm.OpenNoteCard is not { } card) return;
        await OpenNoteEditorAsync(vm, card);
    }

    // Right-click note menu: send the note live (first slide).
    public void OnNoteSendLiveMenu(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: NoteSlideItem card })
            vm.SendNoteLive(card);
    }

    // "+ Add note" button: open the note dialog and persist a new note on confirm.
    public async void OnAddNote(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OperatorViewModel vm) return;
            var editor = vm.CreateNoteEditor("New note", "", "", NoteSplitMode.OneParagraphPerSlide);
            var dialog = new NoteEditorWindow { DataContext = editor };
            await dialog.ShowDialog(this);
            if (!editor.Confirmed) return;
            await vm.Notes.AddNoteAsync(editor.NoteTitle, editor.Body, editor.SplitMode);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add note");
        }
    }

    // Right-click note menu: edit the note's title/body in the dialog.
    public async void OnEditNoteMenu(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OperatorViewModel vm) return;
            if (sender is not MenuItem { DataContext: NoteSlideItem card }) return;
            await OpenNoteEditorAsync(vm, card);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to edit note");
        }
    }

    private async Task OpenNoteEditorAsync(OperatorViewModel vm, NoteSlideItem card)
    {
        var editor = vm.CreateNoteEditor("Edit note", card.Note.Title, card.Note.Body, card.Note.SplitMode);
        var dialog = new NoteEditorWindow { DataContext = editor };
        await dialog.ShowDialog(this);
        if (!editor.Confirmed) return;
        await vm.Notes.UpdateNoteAsync(card, editor.NoteTitle, editor.Body, editor.SplitMode);
        if (vm.OpenNoteCard == card)
            vm.OpenNote(card);
    }

    // Right-click note menu: delete the note.
    public async void OnDeleteNoteMenu(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: NoteSlideItem card })
                await vm.Notes.DeleteNoteAsync(card);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete note");
        }
    }

    public void OnStepLivePrev(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm) vm.StepLive(-1);
    }

    public void OnStepLiveNext(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm) vm.StepLive(+1);
    }

    // Arrow keys step the live output through the slides; Enter/Space sends the focused slide.
    public void OnNowSingingKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;
        switch (e.Key)
        {
            case Key.Right:
            case Key.Down:
                vm.StepLive(+1);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Up:
                vm.StepLive(-1);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                if (sender is ListBox { SelectedItem: SongSlideItem slide })
                {
                    vm.SendSlideLive(slide);
                    e.Handled = true;
                }
                break;
        }
    }

    // Right-click slide menu: send the section live.
    public void OnSlideSendLiveMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SongSlideItem slide })
            vm.SendSlideLive(slide);
    }

    // Right-click slide menu: quick-edit just this slide's section in a small dialog.
    public async void OnQuickEditSlideMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OperatorViewModel vm) return;
            if (sender is not MenuItem { DataContext: SongSlideItem slide }) return;

            var heading = string.IsNullOrWhiteSpace(slide.Song.Artist)
                ? $"{slide.Song.Title}  ·  {slide.Section.Label}"
                : $"{slide.Song.Title} — {slide.Song.Artist}  ·  {slide.Section.Label}";
            var editor = new QuickSlideEditViewModel(heading, slide.Section.SectionType, slide.Section.Text);
            var dialog = new QuickSlideEditWindow { DataContext = editor };
            await dialog.ShowDialog(this);

            if (!editor.Confirmed) return;
            slide.Section.SectionType = string.IsNullOrWhiteSpace(editor.SectionType) ? "verse" : editor.SectionType;
            slide.Section.Text = editor.Text.Trim();
            await vm.SaveSongEditAsync(slide.Song);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to quick-edit slide");
        }
    }

    // Right-click slide menu: open the full song editor.
    public async void OnFullEditSongMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: SongSlideItem slide })
            await OpenSongEditorAsync(slide.Song);
    }

    public void OnSongAddToQueueMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SongHitViewModel hit })
            vm.LoadSongToQueue(hit.Song);
    }

    // ----- Backgrounds palette -----

    // Clicking a background tile swaps the live media layer (the theme/text is untouched).
    public void OnBackgroundTileTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Control { DataContext: BackgroundTileViewModel tile })
        {
            // Feature gate: motion/video backgrounds are a Pro feature. Still images stay free.
            if (tile.IsVideo && vm.VideoBackgroundsLocked)
            {
                vm.RequestUpgradeCommand.Execute(
                    ChurchProjection.Core.Models.Tenancy.FeatureKeys.VideoBackgrounds).Subscribe();
                return;
            }
            vm.Backgrounds.SelectCommand.Execute(tile).Subscribe();
        }
    }

    public void OnRemoveBackgroundClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Control { DataContext: BackgroundTileViewModel tile })
            vm.Backgrounds.RemoveCommand.Execute(tile).Subscribe();
    }

    public async void OnAddBackgroundClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add backgrounds (images or motion clips)",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Backgrounds")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.mp4", "*.mov", "*.m4v", "*.webm", "*.mkv", "*.avi"],
                },
            ],
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                await vm.Backgrounds.AddAsync(path);
        }
    }

    public void OnSongShowLyricsMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SongHitViewModel hit })
            vm.OpenSong(hit.Song);
    }

    public void OnSuggestionsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;

        if (sender is ListBox listBox && listBox.SelectedItem is SuggestionItem item)
        {
            vm.SendSuggestionToLive(item);
        }
    }

    public void OnSendSuggestionToLiveMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SuggestionItem item })
            vm.SendSuggestionToLive(item);
    }

    public void OnShowFullChapterFromSuggestion(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SuggestionItem item })
            vm.ShowFullChapter(item);
    }

    public void OnShowFullBookFromBookmark(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SuggestionItem item })
            vm.ShowFullBook(item);
    }

    public void OnToggleBookmarkMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SuggestionItem item })
            vm.Transcription.ToggleBookmark(item);
    }

    public void OnToggleBookmarkButton(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Button { DataContext: SuggestionItem item })
            vm.Transcription.ToggleBookmark(item);
    }

    public void OnBookmarksDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: SuggestionItem item })
            vm.SendSuggestionToLive(item);
    }

    public void OnSendBookmarkToLiveMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SuggestionItem item })
            vm.SendSuggestionToLive(item);
    }

    public void OnRemoveBookmarkMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SuggestionItem item })
            vm.Transcription.RemoveBookmark(item);
    }

    public void OnRemoveBookmarkButton(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Button { DataContext: SuggestionItem item })
            vm.Transcription.RemoveBookmark(item);
    }

    public void OnSendContentToLiveMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: ContentItem item })
            vm.SendItemToLive(item);
    }

    public void OnShowFullChapterFromContent(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: ContentItem item })
            vm.ShowFullChapter(item);
    }

    public void OnBookmarkScriptureMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: ContentItem item })
            vm.BookmarkScripture(item);
    }

    // Double-click a library song = open it in the "Now Singing" tab (load its slides).
    // Sending live / editing are on the right-click menu.
    public void OnLibrarySongDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: Song song })
            vm.OpenSong(song);
    }

    public void OnSongSendLiveMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: Song song })
            vm.StartSongLive(song);
    }

    public void OnSongAddToQueueLibraryMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: Song song })
            vm.LoadSongToQueue(song);
    }

    public async void OnEditSongMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Song song })
            await OpenSongEditorAsync(song);
    }

    // Top-bar "Songs" button: open a blank editor to paste lyrics and build a new song.
    public async void OnNewSongClicked(object? sender, TappedEventArgs e) => await OpenSongEditorAsync(null);

    private async Task OpenSongEditorAsync(Song? song)
    {
        try
        {
            if (DataContext is not OperatorViewModel vm) return;
            var editor = vm.CreateSongEditor();
            if (song is not null) editor.SetSong(song);
            editor.Saved += () => _ = vm.RefreshLibraryAsync();
            var dialog = new SongEditorWindow { DataContext = editor };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open song editor");
        }
    }

    // Single click on a saved playlist loads it straight into the service queue for fast recall.
    public void OnPlaylistTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is ListBox { SelectedItem: SavedPlaylist playlist })
            vm.LoadPlaylist(playlist);
    }

    public void OnLoadPlaylistMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SavedPlaylist playlist })
            vm.LoadPlaylist(playlist);
    }

    public void OnDeletePlaylistMenu(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is MenuItem { DataContext: SavedPlaylist playlist })
            vm.DeletePlaylist(playlist);
    }

    public void OnDeletePlaylistButton(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Button { DataContext: SavedPlaylist playlist })
            vm.DeletePlaylist(playlist);
    }

    public async void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OperatorViewModel vm) return;
            var dialog = new SettingsWindow { DataContext = vm };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open settings dialog");
        }
    }

    public async void OnPreServiceClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OperatorViewModel vm) return;
            var dialog = new PreServiceWindow { DataContext = new PreServiceViewModel(vm) };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open pre-service dialog");
        }
    }

    public async void OnThemeStudioClicked(object? sender, TappedEventArgs e)
    {
        try
        {
            if (DataContext is not OperatorViewModel vm) return;
            var dialog = new ThemeStudioWindow { DataContext = vm.CreateThemeStudio() };
            await dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open theme studio dialog");
        }
    }

    // ----- Media Playback tab -----

    // Tapping a media tile sends it live on the selected target (videos start playing with sound).
    public void OnMediaTileTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Control { DataContext: MediaTileViewModel tile })
            vm.MediaPlayback.SelectCommand.Execute(tile).Subscribe();
    }

    // Right-clicking a tile offers a per-screen "Send to <screen> only / All screens" menu.
    public void OnMediaTileContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm || sender is not Control { DataContext: MediaTileViewModel tile } control)
            return;

        var media = vm.MediaPlayback;
        var menu = new ContextMenu();
        foreach (var target in media.Targets)
        {
            var key = target.Key;
            var header = target.Key == ChurchProjection.UI.Services.MediaTarget.AllScreens
                ? "Send to all screens"
                : $"Send to {target.Name} only";
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => media.SendTileTo(tile, key);
            menu.Items.Add(item);
        }

        // "Move to folder ▸" submenu: All media (no folder) + each user collection.
        var moveItem = new MenuItem { Header = "Move to folder" };
        foreach (var folder in media.Folders)
        {
            // Skip the synthetic "Uncategorized" view — "All media (no folder)" already unfiles.
            if (!folder.IsAll && folder.Id is null) continue;

            var label = folder.IsAll ? "All media (no folder)" : folder.Name;
            var collectionId = folder.IsAll ? null : folder.Id;
            var sub = new MenuItem
            {
                Header = label,
                // Tick the folder this file currently lives in.
                Icon = (tile.Model.CollectionId == collectionId) ? new TextBlock { Text = "\u2713" } : null,
            };
            sub.Click += async (_, _) => await media.MoveTileToFolder(tile, collectionId);
            moveItem.Items.Add(sub);
        }

        if (moveItem.Items.Count > 0)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(moveItem);
        }

        if (menu.Items.Count == 0) return;
        menu.Open(control);
        e.Handled = true;
    }

    public void OnRemoveMediaTile(object? sender, TappedEventArgs e)
    {
        if (DataContext is OperatorViewModel vm && sender is Control { DataContext: MediaTileViewModel tile })
            vm.MediaPlayback.RemoveCommand.Execute(tile).Subscribe();
        e.Handled = true;
    }

    public async void OnAddMediaClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not OperatorViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add media (graphics or videos)",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Media")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif", "*.mp4", "*.mov", "*.m4v", "*.webm", "*.mkv", "*.avi"],
                },
            ],
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
                await vm.MediaPlayback.AddAsync(path);
        }
    }

}
