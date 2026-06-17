using ChurchProjection.Core.Models.Tenancy;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Holds the current branch's <see cref="EntitlementState"/> and notifies the UI when it changes
/// (after sign-in, background re-validation, or sign-out). Single source of truth for the paywall.
/// </summary>
public interface IEntitlementService
{
    EntitlementState Current { get; }

    /// <summary>Raised on the thread that called <see cref="Update"/>; UI subscribers should marshal.</summary>
    event Action<EntitlementState>? Changed;

    /// <summary>Recompute from a session (null clears to <see cref="EntitlementState.Empty"/>).</summary>
    void Update(AuthSession? session);

    void Clear();
}

public sealed class EntitlementService : IEntitlementService
{
    private EntitlementState _current = EntitlementState.Empty;

    public EntitlementState Current => _current;

    public event Action<EntitlementState>? Changed;

    public void Update(AuthSession? session)
    {
        _current = EntitlementState.From(session);
        Changed?.Invoke(_current);
    }

    public void Clear() => Update(null);
}
