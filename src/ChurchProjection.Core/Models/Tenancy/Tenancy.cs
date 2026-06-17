namespace ChurchProjection.Core.Models.Tenancy;

/// <summary>A church. Owns the shared song library and a fixed number of seats.</summary>
public sealed class Organization
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int SeatCount { get; set; }
}

/// <summary>A campus/location of an organization. Each branch has its own login.</summary>
public sealed class Branch
{
    public string Id { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>A claimed seat: one install/device that has signed in for a branch.</summary>
public sealed class Seat
{
    public string DeviceId { get; set; } = "";
    public string BranchId { get; set; } = "";
    public DateTime ClaimedAtUtc { get; set; }
}

/// <summary>
/// The persisted result of a successful sign-in. Stored locally so the app can start offline within
/// a grace window and reattach the tenant without contacting the cloud every launch.
/// </summary>
public sealed class AuthSession
{
    public string Token { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string BranchId { get; set; } = "";
    public string BranchName { get; set; } = "";
    public string DeviceId { get; set; } = "";

    /// <summary>Seats the branch is entitled to (from its plan/subscription).</summary>
    public int SeatCount { get; set; }

    /// <summary>Active seats currently claimed in the branch.</summary>
    public int SeatsUsed { get; set; }

    /// <summary>The branch's plan code (e.g. "free", "standard").</summary>
    public string PlanCode { get; set; } = "";

    /// <summary>Subscription status: trial | active | past_due | suspended | canceled.</summary>
    public string SubscriptionStatus { get; set; } = "";

    /// <summary>Daily AI-listening (speech-to-text) allowance in minutes. 0 = offline STT only.</summary>
    public int SttMinutesPerDay { get; set; }

    public DateTime LastValidatedUtc { get; set; }
}
