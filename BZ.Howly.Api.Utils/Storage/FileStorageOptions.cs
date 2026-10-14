namespace BZ.Howly.Api.Utils.Storage;

public enum FileStorageProvider
{
    /// <summary>A local folder: development and tests.</summary>
    Disk,

    /// <summary>Azure Blob Storage, shared by every instance: production.</summary>
    AzureBlob,
}

public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public FileStorageProvider Provider { get; set; } = FileStorageProvider.Disk;

    /// <summary>Disk: folder that holds the uploaded files. Relative paths start at the API content root.</summary>
    public string RootPath { get; set; } = "App_Data/files";

    /// <summary>AzureBlob: the storage account connection string (Azurite's in development: "UseDevelopmentStorage=true").</summary>
    public string? ConnectionString { get; set; }

    /// <summary>AzureBlob: the container that holds the files (created when missing).</summary>
    public string Container { get; set; } = "howly-files";
}
