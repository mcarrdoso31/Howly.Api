using System.Text;
using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

/// <summary>
/// Documents are PDF, DOCX, XLSX, JPG/JPEG, PNG or HEIC/HEIF, nothing else: the extension says which, and the first bytes
/// must be of that format (a renamed file is refused).
/// </summary>
public class AllowedUploadFormatsTests
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ");
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00, 0x08, 0x00, 0x00, 0x00]; // docx, xlsx
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x18, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00]; // with an Exif segment
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] OldOffice = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00, 0x00, 0x00]; // doc, xls

    private static byte[] Heif(string brand) => [0x00, 0x00, 0x00, 0x18, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(brand)];

    public static TheoryData<string, byte[]> Accepted => new()
    {
        { "procuracao.pdf", Pdf },
        { "RELATORIO.PDF", Pdf },
        { "contrato.docx", Zip },
        { "planilha de resíduos.xlsx", Zip },
        { "foto.jpg", Jpeg },
        { "foto.JPEG", Jpeg },
        { "print.png", Png },
        { "IMG_0001.HEIC", Heif("heic") },
        { "visita.heic", Heif("heix") },
        { "visita.heic", Heif("hevc") },
        { "visita.heic", Heif("hevx") },
        { "visita.heif", Heif("mif1") },
        { "visita.heif", Heif("msf1") },
        { "visita.heif", Heif("heif") },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Accepts_the_allowed_formats_with_their_own_content(string fileName, byte[] header) =>
        UploadPolicy.Check(fileName, header).Should().Be(UploadCheck.Accepted);

    public static TheoryData<string, byte[]> OtherFormats => new()
    {
        { "contrato.doc", OldOffice },
        { "planilha.xls", OldOffice },
        { "arquivos.zip", Zip },
        { "macro.xlsm", Zip },
        { "apresentacao.pptx", Zip },
        { "observacoes.txt", Encoding.ASCII.GetBytes("texto simples") },
        { "nota.xml", Encoding.ASCII.GetBytes("<?xml version") },
        { "dados.csv", Encoding.ASCII.GetBytes("a;b;c") },
        { "imagem.gif", Encoding.ASCII.GetBytes("GIF89a") },
    };

    [Theory]
    [MemberData(nameof(OtherFormats))]
    public void Refuses_every_other_format(string fileName, byte[] header) =>
        UploadPolicy.Check(fileName, header).Should().Be(UploadCheck.NotAllowedType);

    public static TheoryData<string, byte[]> Renamed => new()
    {
        { "relatorio.pdf", Zip },
        { "contrato.docx", Pdf },
        { "planilha.xlsx", OldOffice },
        { "foto.jpg", Png },
        { "print.png", Jpeg },
        { "video.heic", Heif("mp42") },
        { "texto.pdf", Encoding.ASCII.GetBytes("só um texto") },
        { "curto.pdf", Encoding.ASCII.GetBytes("%P") },
    };

    [Theory]
    [MemberData(nameof(Renamed))]
    public void Refuses_files_whose_content_is_not_of_their_extension(string fileName, byte[] header) =>
        UploadPolicy.Check(fileName, header).Should().Be(UploadCheck.ContentMismatch);

    [Fact]
    public void Reads_enough_bytes_to_tell_a_heic_photo() =>
        UploadPolicy.HeaderLength.Should().BeGreaterThanOrEqualTo(12);

    [Fact]
    public void Lists_the_allowed_extensions_in_one_place() =>
        UploadPolicy.AllowedExtensions.Should().BeEquivalentTo(".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png", ".heic", ".heif");
}
