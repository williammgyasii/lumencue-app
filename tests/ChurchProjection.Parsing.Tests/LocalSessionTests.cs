using ChurchProjection.Core.Models.Tenancy;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

// While cloud sign-in is bypassed, the app boots into the operator on a locally-synthesized session.
// These tests pin the behaviour we depend on: that session must resolve to an active, unlimited
// "master" entitlement so nothing in the app is paywalled, and it must carry no cloud token (it is
// offline by construction — there is no seat to authenticate).
public class LocalSessionTests
{
    [Fact]
    public void Master_resolves_to_active_unlimited_master_entitlement()
    {
        var state = EntitlementState.From(LocalSession.Master());

        Assert.True(state.IsMaster);
        Assert.True(state.IsActive);
        Assert.True(state.IsUnlimitedAi);
        Assert.True(state.CanUseAi);
        Assert.True(state.CanUseVideoBackgrounds);
        Assert.True(state.CanUseSharedLibrary);
        Assert.True(state.CanUseMultiCampus);
    }

    [Fact]
    public void Master_shows_no_upsell_or_banner()
    {
        var state = EntitlementState.From(LocalSession.Master());

        Assert.False(state.ShowUpgrade);
        Assert.False(state.HasBanner);
    }

    [Fact]
    public void Master_is_offline_and_names_the_account_for_the_top_bar()
    {
        var session = LocalSession.Master();

        Assert.Equal("", session.Token);                       // no cloud seat token
        Assert.False(string.IsNullOrWhiteSpace(session.OrganizationName));
        Assert.False(string.IsNullOrWhiteSpace(session.BranchName));
        Assert.True(session.SeatCount > 0);
    }
}
