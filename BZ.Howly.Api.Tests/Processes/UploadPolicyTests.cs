using System.Text;
using BZ.Howly.Api.Core.Shared;
using FluentAssertions;

namespace BZ.Howly.Api.Tests.Processes;

public class UploadPolicyTests
{
    private static readonly byte[] PdfHeader = Encoding.ASCII.GetBytes("%PDF-1.7\n%âãÏÓ");
    private static readonly byte[] ZipHeader = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00]; // docx, xlsx…

    [Theory]
    [InlineData("procuracao.pdf")]
    [InlineData("planilha de resíduos.xlsx")]
    [InlineData("contrato.docx")]
    [InlineData("leiaute.xsd")]
    [InlineData("nota.xml")]
    [InlineData("observações.txt")]
    [InlineData("foto.JPEG")]
    [InlineData("macro.xlsm")]
    public void Accepts_documents(string fileName)
    {
        UploadPolicy.Check(fileName, ZipHeader).Should().Be(UploadCheck.Accepted);
    }

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("script.PS1")]
    [InlineData("run.bat")]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("installer.msi")]
    [InlineData("tool.jar")]
    [InlineData("atalho.lnk")]
    [InlineData("doc.pdf.exe")]
    public void Blocks_executables_scripts_and_web_pages_by_extension(string fileName)
    {
        UploadPolicy.Check(fileName, PdfHeader).Should().Be(UploadCheck.BlockedType);
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 })]        // Windows executable (MZ)
    [InlineData(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 })]  // Linux executable (ELF)
    [InlineData(new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69 })]  // script with shebang (#!/bi…)
    public void Blocks_executables_renamed_as_documents(byte[] header)
    {
        UploadPolicy.Check("relatorio.pdf", header).Should().Be(UploadCheck.BlockedContent);
    }

    [Theory]
    [InlineData("sem-extensao")]
    [InlineData("")]
    [InlineData("   ")]
    public void Needs_a_file_name_with_extension(string fileName)
    {
        UploadPolicy.Check(fileName, PdfHeader).Should().Be(UploadCheck.MissingName);
    }

    [Fact]
    public void Rejects_empty_files() => UploadPolicy.Check("vazio.pdf", []).Should().Be(UploadCheck.Empty);

    [Theory]
    [InlineData(@"C:\Users\bia\Desktop\procuração.pdf", "procuração.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("  nome  com  espaço .pdf ", "nome  com  espaço .pdf")]
    public void Cleans_the_file_name_kept_for_downloads(string fileName, string expected)
    {
        UploadPolicy.CleanFileName(fileName).Should().Be(expected);
    }

    [Fact]
    public void Keeps_long_names_within_the_limit()
    {
        var name = UploadPolicy.CleanFileName(new string('a', 300) + ".pdf");

        name.Should().HaveLength(UploadPolicy.MaxFileNameLength).And.EndWith(".pdf");
    }

    [Theory]
    [InlineData("macro.xlsm", true)]
    [InlineData("contrato.docm", true)]
    [InlineData("contrato.docx", false)]
    public void Knows_which_office_files_can_carry_macros(string fileName, bool expected) =>
        UploadPolicy.MayContainMacros(fileName).Should().Be(expected);
}
