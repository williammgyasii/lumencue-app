namespace ChurchProjection.Core.Models.Tenancy;

/// <summary>Canonical premium feature keys (must match the server's plan <c>features</c> map).</summary>
public static class FeatureKeys
{
    public const string VideoBackgrounds = "video_backgrounds";
    public const string SharedLibrary = "shared_library";
    public const string MultiCampus = "multi_campus";
}

/// <summary>
/// An immutable, UI-friendly snapshot of the signed-in branch's entitlements, derived from the
/// <see cref="AuthSession"/>. The whole in-app paywall binds to this: subscription/trial state,
/// premium feature flags, and the monthly AI allowance.
/// </summary>
public sealed class EntitlementState
{
    /// <summary>Empty/locked state used before sign-in or after sign-out.</summary>
    public static readonly EntitlementState Empty = new();

    public string PlanCode { get; init; } = "";
    public string SubscriptionStatus { get; init; } = "";
    public DateTime? CurrentPeriodEndUtc { get; init; }

    /// <summary>Monthly AI-listening allowance in minutes (0 = AI not included on this plan).</summary>
    public int AiMinutesAllowance { get; init; }

    /// <summary>AI-listening minutes already used this calendar month.</summary>
    public int AiMinutesUsed { get; init; }

    public IReadOnlyList<string> Features { get; init; } = [];

    public static EntitlementState From(AuthSession? session)
    {
        if (session is null) return Empty;
        return new EntitlementState
        {
            PlanCode = session.PlanCode,
            SubscriptionStatus = session.SubscriptionStatus,
            CurrentPeriodEndUtc = session.CurrentPeriodEndUtc,
            AiMinutesAllowance = session.SttMinutesPerMonth,
            AiMinutesUsed = session.SttMinutesUsed,
            Features = session.Features.ToArray(),
        };
    }

    private bool Is(string status) => string.Equals(SubscriptionStatus, status, StringComparison.OrdinalIgnoreCase);
    public bool HasFeature(string key) => Features.Contains(key);

    public bool IsMaster => string.Equals(PlanCode, "master", StringComparison.OrdinalIgnoreCase);
    public bool IsTrial => Is("trial");

    /// <summary>past_due / suspended: premium pauses but local projection keeps working.</summary>
    public bool IsInGracePeriod => Is("past_due") || Is("suspended");

    /// <summary>Whether the subscription currently grants premium/cloud access.</summary>
    public bool IsActive =>
        IsMaster || ((Is("trial") || Is("active"))
                     && (CurrentPeriodEndUtc is null || CurrentPeriodEndUtc > DateTime.UtcNow));

    // --- Premium feature gates (server-authoritative for AI/Bible/shared-library; UI mirror here). ---
    public bool CanUseVideoBackgrounds => IsActive && HasFeature(FeatureKeys.VideoBackgrounds);
    public bool CanUseSharedLibrary => IsActive && HasFeature(FeatureKeys.SharedLibrary);
    public bool CanUseMultiCampus => IsActive && HasFeature(FeatureKeys.MultiCampus);

    // --- AI allowance ---
    public bool IsUnlimitedAi => IsMaster;
    public bool AiIncluded => IsUnlimitedAi || AiMinutesAllowance > 0;
    public int AiMinutesRemaining => IsUnlimitedAi ? int.MaxValue : Math.Max(0, AiMinutesAllowance - AiMinutesUsed);
    public bool AiExhausted => AiIncluded && !IsUnlimitedAi && AiMinutesRemaining <= 0;

    /// <summary>True once usage is within 10% of the allowance (soft warning), but not yet exhausted.</summary>
    public bool AiNearLimit =>
        AiIncluded && !IsUnlimitedAi && !AiExhausted && AiMinutesRemaining <= Math.Max(1, AiMinutesAllowance / 10);

    /// <summary>Can the user currently start AI listening? (Included, active, and not exhausted.)</summary>
    public bool CanUseAi => IsActive && AiIncluded && !AiExhausted;

    public int? TrialDaysLeft =>
        IsTrial && CurrentPeriodEndUtc is { } end
            ? Math.Max(0, (int)Math.Ceiling((end - DateTime.UtcNow).TotalDays))
            : null;

    /// <summary>Show an Upgrade affordance for anything below the top paid tier or any inactive plan.</summary>
    public bool ShowUpgrade =>
        !IsMaster && (IsTrial || IsInGracePeriod || !IsActive
                      || string.Equals(PlanCode, "standard", StringComparison.OrdinalIgnoreCase));

    /// <summary>Status strip text for the operator top bar (empty = no banner needed).</summary>
    public string BannerText
    {
        get
        {
            if (IsMaster) return "";
            if (IsTrial)
            {
                var days = TrialDaysLeft ?? 0;
                return days <= 0
                    ? "Your trial has ended — upgrade to keep AI, sync and premium features."
                    : $"Trial — {days} day{(days == 1 ? "" : "s")} left. Upgrade anytime.";
            }
            if (Is("past_due")) return "Payment past due — premium features pause soon. Renew to keep AI and sync.";
            if (Is("suspended")) return "Subscription suspended — premium paused. Local projection still works. Renew to restore.";
            if (Is("canceled") || !IsActive) return "Subscription inactive — upgrade to continue using premium features.";
            return "";
        }
    }

    public bool HasBanner => !string.IsNullOrEmpty(BannerText);
}
