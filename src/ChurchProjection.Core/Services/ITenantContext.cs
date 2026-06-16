namespace ChurchProjection.Core.Services;

/// <summary>
/// Holds the active organization/branch for the current session. Repositories scope tenant data
/// (songs) by <see cref="OrganizationId"/>. Until a real cloud sign-in sets it, a single local
/// "default" organization is used so the app remains fully usable offline.
/// </summary>
public interface ITenantContext
{
    /// <summary>Placeholder org id used before any cloud sign-in (one local library).</summary>
    const string DefaultOrganizationId = "local-default";

    /// <summary>The organization that owns tenant-scoped content right now.</summary>
    string OrganizationId { get; }

    /// <summary>Human-readable organization name for display (empty for the local default).</summary>
    string OrganizationName { get; }

    /// <summary>The signed-in branch, if any (used for seat attribution, not content scoping).</summary>
    string? BranchId { get; }

    /// <summary>True once a real cloud organization (not the local default) is active.</summary>
    bool HasRemoteOrganization { get; }

    /// <summary>Sets the active organization/branch (called on successful sign-in).</summary>
    void Set(string organizationId, string organizationName, string? branchId);

    /// <summary>Reverts to the local default organization (called on sign-out).</summary>
    void Reset();
}
