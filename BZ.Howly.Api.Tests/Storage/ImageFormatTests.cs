using System.Text;
using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Storage;

public class ImageFormatTests
{
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    public static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    public static readonly byte[] WebP = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8];

    [Fact]
    public void Detects_png() => ImageFormat.Detect(Png)!.ContentType.Should().Be("image/png");

    [Fact]
    public void Detects_jpeg() => ImageFormat.Detect(Jpeg)!.ContentType.Should().Be("image/jpeg");

    [Fact]
    public void Detects_webp() => ImageFormat.Detect(WebP)!.ContentType.Should().Be("image/webp");

    [Fact]
    public void Knows_the_file_extension() => ImageFormat.Detect(Png)!.Extension.Should().Be(".png");

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("GIF89a......")]
    [InlineData("just some text")]
    [InlineData("RIFF....WAVE")]
    public void Rejects_anything_else(string content)
    {
        ImageFormat.Detect(Encoding.UTF8.GetBytes(content)).Should().BeNull();
    }

    [Fact]
    public void Rejects_a_header_too_short() => ImageFormat.Detect([0x89, 0x50]).Should().BeNull();

    [Fact]
    public void Finds_the_format_by_extension() => ImageFormat.FromExtension(".webp")!.ContentType.Should().Be("image/webp");
}
