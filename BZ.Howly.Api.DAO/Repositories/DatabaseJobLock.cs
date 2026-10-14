using System.Collections.Concurrent;
using System.Data;
using BZ.Howly.Api.Core.Interfaces.DAO;
using BZ.Howly.Api.DAO.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BZ.Howly.Api.DAO.Repositories;

/// <summary>
/// On SQL Server, an application lock (<c>sp_getapplock</c>) held by a connection of its own: every instance shares the
/// database, so it is shared by all of them, and it is released if the instance dies. Other providers (SQLite in the
/// tests) run in a single process, where a local semaphore is enough. Under the lock, the <see cref="JobRun"/> row
/// tells when the last round started, so N instances still make one round per interval.
/// </summary>
public sealed class DatabaseJobLock(HowlyDbContext context) : IJobLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new();

    public async Task<IAsyncDisposable?> TryStartRoundAsync(
        string name, DateTimeOffset now, TimeSpan? minimumGap = null, CancellationToken cancellationToken = default)
    {
        var held = context.Database.IsSqlServer()
            ? await TryAcquireSqlServerAsync(name, cancellationToken)
            : TryAcquireLocal(name);
        if (held is null)
            return null;

        try
        {
            var run = await context.JobRuns.SingleOrDefaultAsync(r => r.Name == name, cancellationToken);
            if (minimumGap is { } gap && run is not null && now - run.LastRunAt < gap)
            {
                await held.DisposeAsync(); // this interval's round already happened on some instance
                return null;
            }

            if (run is null)
                context.JobRuns.Add(new JobRun { Name = name, LastRunAt = now });
            else
                run.LastRunAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return held;
        }
        catch
        {
            await held.DisposeAsync();
            throw;
        }
    }

    private static IAsyncDisposable? TryAcquireLocal(string name)
    {
        var local = LocalLocks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        return local.Wait(0) ? new Release(() => { local.Release(); return Task.CompletedTask; }) : null;
    }

    private async Task<IAsyncDisposable?> TryAcquireSqlServerAsync(string name, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(context.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var result = await ExecuteAsync(connection, "sp_getapplock", name, cancellationToken,
                new SqlParameter("@LockMode", "Exclusive"), new SqlParameter("@LockOwner", "Session"), new SqlParameter("@LockTimeout", 0));
            if (result < 0) // -1 timeout: another instance holds it
            {
                await connection.DisposeAsync();
                return null;
            }

            return new Release(async () =>
            {
                try
                {
                    await ExecuteAsync(connection, "sp_releaseapplock", name, CancellationToken.None, new SqlParameter("@LockOwner", "Session"));
                }
                finally
                {
                    // Closing the session releases a session lock anyway: never leave it held by a forgotten connection.
                    await connection.DisposeAsync();
                }
            });
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, string procedure, string resource, CancellationToken cancellationToken, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(procedure, connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@Resource", resource));
        command.Parameters.AddRange(parameters);
        var result = new SqlParameter("@Result", SqlDbType.Int) { Direction = ParameterDirection.ReturnValue };
        command.Parameters.Add(result);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (int)result.Value;
    }

    private sealed class Release(Func<Task> release) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await release();
    }
}
