using ChurchProjection.Core.Services;

namespace ChurchProjection.Infrastructure.Services;

/// <inheritdoc />
public sealed class TenantContext : ITenantContext
{
    private string _organizationId = ITenantContext.DefaultOrganizationId;
    private string _organizationName = "";
    private string? _branchId;

    public string OrganizationId => _organizationId;
    public string OrganizationName => _organizationName;
    public string? BranchId => _branchId;
    public bool HasRemoteOrganization => _organizationId != ITenantContext.DefaultOrganizationId;

    public void Set(string organizationId, string organizationName, string? branchId)
    {
        if (string.IsNullOrWhiteSpace(organizationId)) return;
        _organizationId = organizationId;
        _organizationName = organizationName ?? "";
        _branchId = branchId;
    }

    public void Reset()
    {
        _organizationId = ITenantContext.DefaultOrganizationId;
        _organizationName = "";
        _branchId = null;
    }
}
