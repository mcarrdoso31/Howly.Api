namespace BZ.Howly.Api.Utils.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Folder that holds the uploaded files. Relative paths start at the API content root.</summary>
    public string RootPath { get; set; } = "App_Data/files";
}
