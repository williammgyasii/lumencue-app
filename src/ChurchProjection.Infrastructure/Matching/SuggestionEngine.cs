using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Channels;
using ChurchProjection.Core.Parsing;
using ChurchProjection.Core.Services;
using Serilog;

namespace ChurchProjection.Infrastructure.Matching;

/// <summary>
/// Background, latest-wins matching pipeline. Transcript windows are pushed into a
/// single-slot channel; a dedicated consumer matches the newest window and cancels any
/// in-flight match as soon as a fresher window arrives. Results are surfaced via an
/// observable and never touch the UI thread until a subscriber marshals them.
/// </summary>
public sealed class SuggestionEngine : ISuggestionEngine
{
    private readonly IAiMatcherService _matcher;
    private readonly Channel<string> _channel;
    // Separate slot for the high-frequency interim stream so a flood of live partials never drops the
    // finalised-utterance window (which carries the full fuzzy + semantic match), and vice versa.
    private readonly Channel<string> _interimChannel;
    private readonly Subject<IReadOnlyList<AiSuggestion>> _suggestions = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private readonly Task _interimWorker;

    public SuggestionEngine(IAiMatcherService matcher)
    {
        _matcher = matcher;

        _channel = CreateLatestWinsChannel();
        _interimChannel = CreateLatestWinsChannel();

        // Full match (scripture + content) on finalised windows.
        _worker = Task.Run(() => RunAsync(_channel, scriptureOnly: false));
        // Fast scripture-only match on live partials, so verses surface as they're spoken.
        _interimWorker = Task.Run(() => RunAsync(_interimChannel, scriptureOnly: true));
    }

    // Capacity 1 + DropOldest: only the freshest pending window survives, so a slow match can never
    // cause a backlog of stale transcript windows.
    private static Channel<string> CreateLatestWinsChannel() =>
        Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public IObservable<IReadOnlyList<AiSuggestion>> Suggestions => _suggestions.AsObservable();

    public void Push(string transcriptWindow)
    {
        if (string.IsNullOrWhiteSpace(transcriptWindow)) return;
        _channel.Writer.TryWrite(transcriptWindow);
    }

    public void PushInterim(string transcriptWindow)
    {
        if (string.IsNullOrWhiteSpace(transcriptWindow)) return;
        _interimChannel.Writer.TryWrite(transcriptWindow);
    }

    public void HandleSegment(string finalSegmentText)
    {
        if (string.IsNullOrWhiteSpace(finalSegmentText)) return;

        var command = VoiceCommandParser.Detect(finalSegmentText);
        if (command == NavCommand.None)
        {
            // No command spoken: feed the progressive reference builder, which stitches a reference
            // uttered in fragments across pauses and surfaces it once it is showable.
            _ = HandleSpokenAsync(finalSegmentText);
            return;
        }

        _ = HandleNavigationAsync(command);
    }

    private async Task HandleSpokenAsync(string finalSegmentText)
    {
        try
        {
            var results = await _matcher.AccumulateSpokenAsync(finalSegmentText, _shutdown.Token).ConfigureAwait(false);
            if (results.Count > 0)
                _suggestions.OnNext(results);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Spoken reference accumulation failed");
        }
    }

    private async Task HandleNavigationAsync(NavCommand command)
    {
        try
        {
            var results = await _matcher.NavigateAsync(command, _shutdown.Token).ConfigureAwait(false);
            if (results.Count > 0)
                _suggestions.OnNext(results);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Voice navigation command failed");
        }
    }

    private async Task RunAsync(Channel<string> channel, bool scriptureOnly)
    {
        var reader = channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
            {
                if (!reader.TryRead(out var text)) continue;

                // Collapse any windows that queued up while we were busy down to the latest.
                while (reader.TryRead(out var newer)) text = newer;

                await MatchLatestAsync(reader, text, scriptureOnly).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Suggestion engine worker terminated unexpectedly");
        }
    }

    private async Task MatchLatestAsync(ChannelReader<string> reader, string text, bool scriptureOnly)
    {
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);

        var matchTask = _matcher.MatchAsync(text, scriptureOnly, runCts.Token);
        // Race the match against the arrival of a newer window; whichever wins, the loop
        // picks up the latest input next iteration.
        var newerInput = reader.WaitToReadAsync(runCts.Token).AsTask();

        var winner = await Task.WhenAny(matchTask, newerInput).ConfigureAwait(false);

        if (winner == matchTask)
        {
            try
            {
                var results = await matchTask.ConfigureAwait(false);
                _suggestions.OnNext(results);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "AI matching failed for transcript window");
            }
            return;
        }

        // A newer window arrived first: abandon this match and let the loop process the latest.
        runCts.Cancel();
        try { await matchTask.ConfigureAwait(false); }
        catch { /* drained */ }
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _interimChannel.Writer.TryComplete();
        _shutdown.Cancel();
        try { Task.WaitAll([_worker, _interimWorker], TimeSpan.FromSeconds(2)); }
        catch { /* ignore shutdown races */ }
        _shutdown.Dispose();
        _suggestions.Dispose();
    }
}
