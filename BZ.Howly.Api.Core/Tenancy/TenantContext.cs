namespace BZ.Howly.Api.Core.Tenancy;

/// <summary>
/// The consultancy the current work belongs to: set once per request from the subdomain (or by a background job for
/// each tenant it goes through). Every query and insert of tenant data is scoped by it.
/// </summary>
public interface ITenantContext
{
    /// <summary>Null until a tenant is set: no tenant data can be read nor written then.</summary>
    Tenant? Tenant { get; }

    Guid? TenantId => Tenant?.Id;

    /// <summary>Sets the tenant; it cannot change afterwards within the same scope.</summary>
    void Set(Tenant tenant);
}

public sealed class TenantContext : ITenantContext
{
    public Tenant? Tenant { get; private set; }

    public void Set(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (Tenant is not null && Tenant.Id != tenant.Id)
            throw new InvalidOperationException("The tenant of a scope cannot change.");
        Tenant = tenant;
    }
}
