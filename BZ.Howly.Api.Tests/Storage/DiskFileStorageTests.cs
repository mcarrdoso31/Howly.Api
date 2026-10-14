using BZ.Howly.Api.Utils.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace BZ.Howly.Api.Tests.Storage;

public sealed class DiskFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "howly-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly DiskFileStorage _storage;

    public DiskFileStorageTests()
    {
        _storage = new DiskFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Saves_and_reads_back_the_same_bytes()
    {
        var key = await _storage.SaveAsync("avatars", ".png", new MemoryStream([1, 2, 3]));

        await using var stream = await _storage.OpenReadAsync(key);
        using var copy = new MemoryStream();
        await stream!.CopyToAsync(copy);
        copy.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Generates_a_new_name_inside_the_folder()
    {
        var key = await _storage.SaveAsync("avatars", ".png", new MemoryStream([1]));

        key.Should().MatchRegex("^avatars/[0-9a-f]{32}\\.png$");
        File.Exists(Path.Combine(_root, "avatars", Path.GetFileName(key))).Should().BeTrue();
    }

    [Fact]
    public async Task Deletes_a_file()
    {
        var key = await _storage.SaveAsync("avatars", ".png", new MemoryStream([1]));

        await _storage.DeleteAsync(key);

        (await _storage.OpenReadAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_for_a_missing_file()
    {
        (await _storage.OpenReadAsync("avatars/0123456789abcdef0123456789abcdef.png")).Should().BeNull();
    }

    [Theory]
    [InlineData("../secrets.txt")]
    [InlineData("avatars/../../appsettings.json")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("avatars/not-a-generated-name.png")]
    public async Task Refuses_keys_it_did_not_generate(string key)
    {
        var read = () => _storage.OpenReadAsync(key);
        var delete = () => _storage.DeleteAsync(key);

        await read.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("../avatars", ".png")]
    [InlineData("avatars", ".exe")]
    [InlineData("avatars", "png/../x")]
    public async Task Refuses_unsafe_folders_and_extensions(string folder, string extension)
    {
        var save = () => _storage.SaveAsync(folder, extension, new MemoryStream([1]));

        await save.Should().ThrowAsync<ArgumentException>();
    }
}
