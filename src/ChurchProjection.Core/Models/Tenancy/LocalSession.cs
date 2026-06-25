using ChurchProjection.Core.Services;

namespace ChurchProjection.Core.Models.Tenancy;

/// <summary>
/// Builds a locally-synthesized session used while cloud sign-in is bypassed. It grants the
/// <c>master</c> plan so the operator runs with full, unlimited entitlements and nothing is
/// paywalled, without contacting the cloud.
///
/// It deliberately carries <b>no seat token</b>: cloud-authenticated features (live STT, premium
/// Bible translations, song sync) cannot run until real sign-in is restored. The org defaults to
/// <see cref="ITenantContext.DefaultOrganizationId"/> so a fresh install uses the existing local
/// library; an already-signed-in church keeps its own org (set by the caller).
/// </summary>
public static class LocalSession
{
    public static AuthSession Master() => new()
    {
        Token = "",
        OrganizationId = ITenantContext.DefaultOrganizationId,
        OrganizationName = "LumenCue",
        BranchId = "local",
        BranchName = "Local",
        DeviceId = "",
        SeatCount = 999,
        SeatsUsed = 1,
        PlanCode = "master",
        SubscriptionStatus = "active",
        CurrentPeriodEndUtc = null,
        SttMinutesPerMonth = 100_000,
        SttMinutesUsed = 0,
        Features = [FeatureKeys.VideoBackgrounds, FeatureKeys.SharedLibrary, FeatureKeys.MultiCampus],
        LastValidatedUtc = DateTime.UtcNow,
    };
}
