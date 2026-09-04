using ChurchProjection.Core.Models.Content;
using ChurchProjection.Core.Services;
using ChurchProjection.UI.ViewModels.Operator;
using ReactiveUI;
using System.Reactive.Concurrency;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class TopicalSearchCardWidthTests
{
    public TopicalSearchCardWidthTests()
    {
        RxApp.MainThreadScheduler = ImmediateScheduler.Instance;
        RxApp.TaskpoolScheduler = ImmediateScheduler.Instance;
    }

    [Fact]
    public async Task Search_stamps_one_third_card_width()
    {
        var fake = new FakeSearch { Hits = [Hit()] };
        var vm = new TopicalSearchViewModel(fake);
        vm.SetCardPaneWidth(616);

        await vm.RunAutoSearchAsync("god so loved the world");

        var card = Assert.Single(vm.Results);
        Assert.Equal(OperatorWorkspaceChrome.ScriptureCardWidth(616), card.CardWidth);
    }

    [Fact]
    public async Task Wider_pane_grows_existing_cards()
    {
        var fake = new FakeSearch { Hits = [Hit()] };
        var vm = new TopicalSearchViewModel(fake);
        vm.SetCardPaneWidth(500);
        await vm.RunAutoSearchAsync("love");
        var narrow = vm.Results[0].CardWidth;

        vm.SetCardPaneWidth(900);

        Assert.True(vm.Results[0].CardWidth > narrow);
        Assert.Equal(OperatorWorkspaceChrome.ScriptureCardWidth(900), vm.Results[0].CardWidth);
        var used = vm.Results[0].CardWidth * 3
                   + OperatorWorkspaceChrome.ScriptureCardMarginX * 3
                   + OperatorWorkspaceChrome.ScriptureListPaddingX;
        Assert.True(used <= 900 - 1);
    }

    private static ScriptureSearchHit Hit() => new(
        new ScripturePassage
        {
            Book = "John",
            Chapter = 3,
            VerseStart = 16,
            Text = "For God so loved the world.",
            Translation = "BSB",
        },
        1.0,
        ScriptureSearchHit.KindKeyword);

    private sealed class FakeSearch : IScriptureSearchService
    {
        public List<ScriptureSearchHit> Hits { get; set; } = [];

        public bool IsIndexReady(string translation) => true;
        public Task EnsureIndexedAsync(string translation, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<List<ScriptureSearchHit>> SearchAsync(string query, string translation, int maxResults = 12, CancellationToken cancellationToken = default)
            => Task.FromResult(Hits);
    }
}
