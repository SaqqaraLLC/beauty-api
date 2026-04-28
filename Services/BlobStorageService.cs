using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Beauty.Api.Services;

public class BlobStorageService
{
    private readonly BlobServiceClient? _client;
    private readonly string _container;
    private readonly string _mediaContainer;
    private readonly bool _configured;

    public BlobStorageService(IConfiguration config)
    {
        var connStr = config["Storage:ConnectionString"];
        _container      = config["Storage:Container"]      ?? "documents";
        _mediaContainer = config["Storage:MediaContainer"] ?? "media";
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            _client = new BlobServiceClient(connStr);
            _configured = true;
        }
    }

    private BlobServiceClient Client =>
        _client ?? throw new InvalidOperationException("Azure Blob Storage is not configured (Storage:ConnectionString missing).");

    /// <summary>Upload a file and return the blob name (not a public URL).</summary>
    public async Task<string> UploadAsync(Stream data, string fileName, string contentType)
    {
        // Use a UUID-based name so original filenames are never exposed
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var blobName = $"{Guid.NewGuid()}{ext}";

        var container = Client.GetBlobContainerClient(_container);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);

        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(data, new BlobHttpHeaders { ContentType = contentType });

        return blobName;
    }

    /// <summary>Generate a short-lived SAS URL (1 hour) so admins can view a document.</summary>
    public string GenerateSasUrl(string blobName, int expiryMinutes = 60)
    {
        var container = Client.GetBlobContainerClient(_container);
        var blob = container.GetBlobClient(blobName);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _container,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blob.GenerateSasUri(sasBuilder).ToString();
    }

    public async Task DeleteAsync(string blobName)
    {
        var container = Client.GetBlobContainerClient(_container);
        var blob = container.GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync();
    }

    /// <summary>Upload a media file (video/image) to the public media container and return its public URL.</summary>
    public async Task<string> UploadMediaAsync(Stream data, string blobName, string contentType)
    {
        var container = Client.GetBlobContainerClient(_mediaContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(data, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        });

        return blob.Uri.ToString();
    }

    /// <summary>Generate a write-only SAS URL so the frontend can upload directly to blob storage.</summary>
    public async Task<string> GenerateUploadSasUrlAsync(string blobName, string contentType)
    {
        var container = Client.GetBlobContainerClient(_mediaContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blob = container.GetBlobClient(blobName);
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = _mediaContainer,
            BlobName          = blobName,
            Resource          = "b",
            ExpiresOn         = DateTimeOffset.UtcNow.AddMinutes(30),
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);
        return blob.GenerateSasUri(sasBuilder).ToString();
    }

    /// <summary>Get the public URL for a blob in the media container.</summary>
    public string GetPublicUrl(string blobName)
    {
        var container = Client.GetBlobContainerClient(_mediaContainer);
        return container.GetBlobClient(blobName).Uri.ToString();
    }
}
