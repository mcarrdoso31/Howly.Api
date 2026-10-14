namespace BZ.Howly.Api.DAO.Context;

/// <summary>When a periodic job last started a round, shared by every instance (see <see cref="Repositories.DatabaseJobLock"/>).</summary>
public sealed class JobRun
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastRunAt { get; set; }
}
