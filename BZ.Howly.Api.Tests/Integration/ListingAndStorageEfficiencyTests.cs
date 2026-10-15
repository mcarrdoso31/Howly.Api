using BZ.Howly.Api.Core.Identity;
using BZ.Howly.Api.Core.Interfaces.Service;
using BZ.Howly.Api.Core.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static BZ.Howly.Api.Tests.Integration.ApiTestClient;

namespace BZ.Howly.Api.Tests.Integration;

/// <summary>The API on a stopped clock: everything changed in a test happens at the very same instant.</summary>
public sealed class FrozenClockApiFactory : HowlyApiFactory
{
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>
/// Lists page the same way every time (even when everything changed at the same instant), the list filters keep their
/// results, uploads are taken as a stream and "mark all read" touches only the reader's notifications.
/// </summary>
public class ListingAndStorageEfficiencyTests(FrozenClockApiFactory factory) : IClassFixture<FrozenClockApiFactory>
{
    private static int _sequence;
    private static string NewCnpj()
    {
        var body = $"8{Interlocked.Increment(ref _sequence):D7}0001";
        int Digit(string s, int[] w) { var r = s.Select((c, i) => (c - '0') * w[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        var first = Digit(body, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        return $"{body}{first}{Digit(body + first, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])}";
    }

    private static readonly object[] Contacts = [new { name = "Bia", email = "bia@beagle.example", phone = "(11) 91234-5678", receivesNotices = true }];

    internal record Scenario(string Slug, HttpClient Client, string Staff, Guid StaffId, Guid CustomerId, Guid TypeId);

    internal async Task<Scenario> ScenarioAsync()
    {
        var slug = $"le-{Guid.NewGuid():N}"[..20];
        await factory.CreateTenantAsync(slug, "Listas");
        var client = factory.CreatePortalClient(slug);
        var analyst = NewUsername("analyst");
        var staffId = await factory.CreateUserInAsync(slug, analyst, Password, "Jane Analyst", Roles.Admin);
        var staff = await client.LoginInternalAsync(analyst);
        var customer = await (await client.SendAsync(HttpMethod.Post, "/api/customers", staff, new { document = NewCnpj(), companyName = "Beagle", contacts = Contacts })).JsonAsync();
        var type = await (await client.SendAsync(HttpMethod.Post, "/api/process-types", staff,
            new { name = "LO", steps = new object[] { new { name = "Análise", owner = "Staff", durationDays = 1 }, new { name = "Protocolo", owner = "Agency" } } })).JsonAsync();
        return new Scenario(slug, client, staff, staffId, customer.GetProperty("id").GetGuid(), type.GetProperty("id").GetGuid());
    }

    internal static async Task<Guid> OpenAsync(Scenario s, string title, Guid? assigneeId = null) =>
        (await (await s.Client.SendAsync(HttpMethod.Post, "/api/processes", s.Staff,
            new { customerId = s.CustomerId, processTypeId = s.TypeId, title, assigneeId })).JsonAsync()).GetProperty("id").GetGuid();

    internal static async Task<List<Guid>> AllPagesAsync(Scenario s, string url, int pageSize, int pages)
    {
        var ids = new List<Guid>();
        for (var page = 1; page <= pages; page++)
        {
            var body = await (await s.Client.SendAsync(HttpMethod.Get, $"{url}{(url.Contains('?') ? '&' : '?')}page={page}&pageSize={pageSize}", s.Staff)).JsonAsync();
            ids.AddRange(body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
        }

        return ids;
    }

    [Fact]
    public async Task Processes_changed_at_the_same_instant_page_without_repeating_or_skipping()
    {
        var s = await ScenarioAsync();
        var opened = new List<Guid>();
        for (var i = 0; i < 5; i++)
            opened.Add(await OpenAsync(s, $"LO {i}"));

        var paged = await AllPagesAsync(s, "/api/processes", pageSize: 2, pages: 3);

        paged.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(opened);
    }

    [Fact]
    public async Task Customers_with_the_same_name_page_without_repeating_or_skipping()
    {
        var s = await ScenarioAsync();
        var created = new List<Guid> { s.CustomerId };
        for (var i = 0; i < 4; i++)
            created.Add((await (await s.Client.SendAsync(HttpMethod.Post, "/api/customers", s.Staff, new { document = NewCnpj(), companyName = "Beagle", contacts = Contacts })).JsonAsync())
                .GetProperty("id").GetGuid());

        var paged = await AllPagesAsync(s, "/api/customers", pageSize: 2, pages: 3);

        paged.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(created);
    }

    [Fact]
    public async Task Marking_all_read_touches_only_the_readers_notifications()
    {
        var s = await ScenarioAsync();
        var other = NewUsername("bruno");
        var otherId = await factory.CreateUserInAsync(s.Slug, other, Password, "Bruno Analyst", Roles.Analyst);
        var otherToken = await s.Client.LoginInternalAsync(other);
        await OpenAsync(s, "Para o Bruno", assigneeId: otherId);
        await OpenAsync(s, "Outro para o Bruno", assigneeId: otherId);
        await (await s.Client.SendAsync(HttpMethod.Post, "/api/processes", otherToken,
            new { customerId = s.CustomerId, processTypeId = s.TypeId, title = "Para a Jane", assigneeId = s.StaffId })).JsonAsync();

        await s.Client.SendAsync(HttpMethod.Post, "/api/me/notifications/read-all", otherToken);

        static async Task<int> UnreadAsync(HttpClient client, string token) =>
            (await (await client.SendAsync(HttpMethod.Get, "/api/me/notifications", token)).JsonAsync()).GetProperty("unreadCount").GetInt32();
        (await UnreadAsync(s.Client, otherToken)).Should().Be(0);
        (await UnreadAsync(s.Client, s.Staff)).Should().Be(1);
    }

    [Fact]
    public async Task The_team_screen_counts_only_open_processes()
    {
        var s = await ScenarioAsync();
        var done = await OpenAsync(s, "Concluído");
        await OpenAsync(s, "Aberto");
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{done}/advance", s.Staff, new { });
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{done}/advance", s.Staff, new { });

        var me = await (await s.Client.SendAsync(HttpMethod.Get, $"/api/internal-users/{s.StaffId}", s.Staff)).JsonAsync();

        me.GetProperty("openProcessCount").GetInt32().Should().Be(1);
    }

    /// <summary>A file read once, front to back, that cannot be measured nor rewound (like a request body).</summary>
    private sealed class ForwardOnlyFile(byte[] header, long length) : Stream
    {
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);
            for (var i = 0; i < n; i++)
                buffer[offset + i] = _position + i < header.Length ? header[_position + i] : (byte)'x';
            _position += n;
            return n;
        }
    }

    private static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray();

    private int StoredFiles(string slug) =>
        Directory.Exists(factory.FilesRoot) ? Directory.GetFiles(factory.FilesRoot, "*", SearchOption.AllDirectories).Length : 0;

    [Fact]
    public async Task An_upload_is_taken_as_a_stream_and_keeps_its_size()
    {
        var s = await ScenarioAsync();
        using var scope = factory.CreateTenantScope(s.Slug);
        const long size = 3 * 1024 * 1024 + 17;

        var result = await scope.ServiceProvider.GetRequiredService<IDocumentExampleService>()
            .UploadAsync(new IncomingFile("modelo.pdf", new ForwardOnlyFile(Pdf, size)), s.StaffId);

        result.Status.Should().Be(OperationStatus.Success);
        result.Value!.Size.Should().Be(size);
    }

    [Fact]
    public async Task An_upload_over_the_limit_is_refused_and_leaves_no_file()
    {
        var s = await ScenarioAsync();
        using var scope = factory.CreateTenantScope(s.Slug);
        var before = StoredFiles(s.Slug);

        var result = await scope.ServiceProvider.GetRequiredService<IDocumentExampleService>()
            .UploadAsync(new IncomingFile("grande.pdf", new ForwardOnlyFile(Pdf, UploadPolicy.MaxBytes + 1)), s.StaffId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["file"].Should().ContainSingle(m => m.Contains("25 MB"));
        StoredFiles(s.Slug).Should().Be(before);
    }

    [Fact]
    public async Task A_refused_type_is_still_refused_before_anything_is_stored()
    {
        var s = await ScenarioAsync();
        using var scope = factory.CreateTenantScope(s.Slug);
        var before = StoredFiles(s.Slug);

        var result = await scope.ServiceProvider.GetRequiredService<IDocumentExampleService>()
            .UploadAsync(new IncomingFile("planilha.pdf", new ForwardOnlyFile("MZ\x90\0"u8.ToArray(), 1024)), s.StaffId);

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        StoredFiles(s.Slug).Should().Be(before);
    }
}

/// <summary>The overdue and waiting filters, on a clock of their own (moving it forward would age the other tests' sign-ins).</summary>
public class ListFilterResultsTests(FrozenClockApiFactory factory) : IClassFixture<FrozenClockApiFactory>
{
    private readonly ListingAndStorageEfficiencyTests _helpers = new(factory);

    [Fact]
    public async Task The_overdue_and_waiting_filters_keep_their_results()
    {
        var s = await _helpers.ScenarioAsync();
        var late = await ListingAndStorageEfficiencyTests.OpenAsync(s, "Atrasado");
        var paused = await ListingAndStorageEfficiencyTests.OpenAsync(s, "Pausado");
        var done = await ListingAndStorageEfficiencyTests.OpenAsync(s, "Concluído");
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{done}/advance", s.Staff, new { });
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{done}/advance", s.Staff, new { });
        factory.Clock.Advance(TimeSpan.FromDays(3));
        await s.Client.SendAsync(HttpMethod.Post, $"/api/processes/{paused}/pause", s.Staff, new { reason = "Aguardando o cliente." });

        var overdue = await ListingAndStorageEfficiencyTests.AllPagesAsync(s, "/api/processes?overdue=true", pageSize: 20, pages: 1);
        var waitingOnStaff = await ListingAndStorageEfficiencyTests.AllPagesAsync(s, "/api/processes?waitingOn=Staff", pageSize: 20, pages: 1);

        overdue.Should().BeEquivalentTo([late]);
        waitingOnStaff.Should().BeEquivalentTo([late]);
    }

}
