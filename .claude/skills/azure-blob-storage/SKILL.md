---
name: azure-blob-storage
description: Azure Blob Storage SDK v12 patterns for .NET. Covers IBlobStorageService abstraction, AddAzureClients DI registration with dual auth mode (connection string dev / DefaultAzureCredential prod), upload with BlobUploadOptions, download, delete, exists check, user delegation SAS URL generation, RequestFailedException handling, retry policy, and integration test patterns (FakeBlobStorageService + WebApplicationFactory substitution). Grounded in the actual patterns used in this codebase.
invocable: false
---

# Azure Blob Storage — SDK v12

## When to Use This Skill

Use this skill when:
- Adding new upload, download, delete, or URL-generation operations to `IBlobStorageService` / `AzureBlobStorageService`
- Changing DI registration (`AddAzureClients`, auth mode, retry policy)
- Reviewing `RequestFailedException` error-handling patterns
- Writing integration tests that touch blob storage (fake vs. real Azure)
- Evaluating whether to use connection-string auth vs. `DefaultAzureCredential`

See [advanced-patterns.md](advanced-patterns.md) for retry policy tuning, user delegation SAS deep dive, and the `AzureNotesAppApiFactory` real-Azure integration test pattern.

---

## Core Principles

1. **`IBlobStorageService` abstraction in Application** — the Application layer never references `Azure.Storage.Blobs` directly. The concrete `AzureBlobStorageService` lives in Infrastructure. This allows swapping in a `FakeBlobStorageService` in tests.
2. **`BlobServiceClient` is thread-safe and reusable** — register it as a singleton via `AddAzureClients`. Never `new` it in a handler or service.
3. **Always set `ContentType`** in `BlobHttpHeaders` when uploading. Omitting it causes browsers and CDNs to treat files as `application/octet-stream`.
4. **Use `DeleteIfExistsAsync`**, not `DeleteAsync` — idempotent by design; delete operations should succeed even if the blob never existed.
5. **Wrap all SDK calls in `try/catch (RequestFailedException)`** and return `Result.Fail(...)` — never let SDK exceptions propagate to controllers.
6. **User delegation SAS over account-key SAS** in production — user delegation SAS is backed by Entra identity and can be revoked; account-key SAS cannot.
7. **Prefer `DefaultAzureCredential` in production** — connection strings embed account keys. Use them only for local dev.

---

## IBlobStorageService Abstraction

The Application layer depends only on this interface (in `Application.Abstractions.Storage`):

```csharp
public interface IBlobStorageService
{
    Task<Result<StorageUploadResult>> UploadAsync(
        string containerName, string blobPath,
        Stream content, string contentType,
        CancellationToken cancellationToken = default);

    Task<Result<StorageDownloadResult>> DownloadAsync(
        string containerName, string blobPath,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(
        string containerName, string blobPath,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ExistsAsync(
        string containerName, string blobPath,
        CancellationToken cancellationToken = default);

    Task<Result<string>> GenerateDownloadUrlAsync(
        string containerName, string blobPath,
        TimeSpan validity,
        CancellationToken cancellationToken = default);
}
```

---

## DI Registration: Dual Auth Mode

The codebase supports two auth modes. Connection string takes priority (simpler for local dev); URI + `DefaultAzureCredential` is used for Azure deployment.

```csharp
var blobConnectionString = configuration.GetConnectionString("AzureBlobStorage");
var blobServiceUri       = configuration["Azure:Storage:Blob:ServiceUri"];

if (!string.IsNullOrEmpty(blobConnectionString))
{
    // Local dev: connection string with account key
    services.AddAzureClients(azure =>
    {
        azure.AddBlobServiceClient(blobConnectionString);
        azure.ConfigureDefaults(options =>
        {
            options.Retry.MaxRetries          = 3;
            options.Retry.Mode                = RetryMode.Exponential;
            options.Retry.Delay               = TimeSpan.FromSeconds(1);
            options.Retry.MaxDelay            = TimeSpan.FromSeconds(30);
            options.Retry.NetworkTimeout      = TimeSpan.FromSeconds(100);
        });
    });
    services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
}
else if (!string.IsNullOrEmpty(blobServiceUri))
{
    // Azure deployment: Managed Identity via DefaultAzureCredential
    // Requires roles: "Storage Blob Data Contributor" + "Storage Blob Delegator"
    services.AddAzureClients(azure =>
    {
        azure.AddBlobServiceClient(new Uri(blobServiceUri));
        azure.ConfigureDefaults(options =>
        {
            options.Retry.MaxRetries     = 3;
            options.Retry.Mode           = RetryMode.Exponential;
            options.Retry.Delay          = TimeSpan.FromSeconds(1);
            options.Retry.MaxDelay       = TimeSpan.FromSeconds(30);
            options.Retry.NetworkTimeout = TimeSpan.FromSeconds(100);
        });
        // DefaultAzureCredential chain (in order):
        // 1. Environment variables  2. Managed Identity  3. Visual Studio
        // 4. VS Code (Azure ext)    5. Azure CLI          6. Azure PowerShell
        azure.UseCredential(new DefaultAzureCredential());
    });
    services.AddScoped<IBlobStorageService, AzureBlobStorageService>();
}
// If neither is configured, IBlobStorageService is not registered.
// Handlers that need it will throw at runtime — this is intentional.
```

**appsettings.json (dev)**
```json
{
  "ConnectionStrings": {
    "AzureBlobStorage": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

**appsettings.json (prod / Azure)**
```json
{
  "Azure": {
    "Storage": {
      "Blob": {
        "ServiceUri": "https://yourstorageaccount.blob.core.windows.net"
      }
    }
  }
}
```

---

## Upload

```csharp
public async Task<Result<StorageUploadResult>> UploadAsync(
    string containerName, string blobPath,
    Stream content, string contentType,
    CancellationToken cancellationToken = default)
{
    try
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobPath);

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType ?? "application/octet-stream"
            }
        };

        var response = await blobClient.UploadAsync(content, uploadOptions, cancellationToken);

        var props = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

        return Result.Ok(new StorageUploadResult(
            BlobPath:    blobPath,
            ContentType: contentType,
            SizeBytes:   props.Value.ContentLength,
            ETag:        response.Value.ETag.ToString()));
    }
    catch (RequestFailedException ex)
    {
        _logger.LogError(ex, "Upload failed. Container: {C}, Path: {P}, Status: {S}",
            containerName, blobPath, ex.Status);
        return Result.Fail(new Error("Blob.Upload.Failed")
            .WithMetadata("Status",    ex.Status)
            .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
    }
}
```

Key points:
- `CreateIfNotExistsAsync` is idempotent — safe to call every time (adds a round-trip; cache `containerClient` if perf matters)
- `BlobUploadOptions` is required to set `ContentType`; passing just the stream without options loses the content type
- `UploadAsync` overwrites by default; if you want to prevent overwrite, set `uploadOptions.Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }`

---

## Download

```csharp
public async Task<Result<StorageDownloadResult>> DownloadAsync(
    string containerName, string blobPath,
    CancellationToken cancellationToken = default)
{
    try
    {
        var blobClient = _blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobPath);

        var response = await blobClient.DownloadAsync(cancellationToken);

        return Result.Ok(new StorageDownloadResult(
            content:     response.Value.Content,
            contentType: response.Value.ContentType,
            sizeBytes:   response.Value.ContentLength));
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        return Result.Fail(new Error("Blob.NotFound")
            .WithMetadata("Container", containerName)
            .WithMetadata("Path",      blobPath));
    }
    catch (RequestFailedException ex)
    {
        _logger.LogError(ex, "Download failed. Container: {C}, Path: {P}", containerName, blobPath);
        return Result.Fail(new Error("Blob.Download.Failed")
            .WithMetadata("Status",    ex.Status)
            .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
    }
}
```

`DownloadAsync` buffers the entire blob into memory. For large files (> a few MB), prefer `DownloadStreamingAsync` which returns a network stream — see [advanced-patterns.md](advanced-patterns.md).

---

## Delete

```csharp
public async Task<Result> DeleteAsync(
    string containerName, string blobPath,
    CancellationToken cancellationToken = default)
{
    try
    {
        var blobClient = _blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobPath);

        // DeleteIfExistsAsync is idempotent — returns false if blob didn't exist
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

        return Result.Ok();
    }
    catch (RequestFailedException ex)
    {
        _logger.LogError(ex, "Delete failed. Container: {C}, Path: {P}", containerName, blobPath);
        return Result.Fail(new Error("Blob.Delete.Failed")
            .WithMetadata("Status",    ex.Status)
            .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
    }
}
```

---

## Generate Download URL (User Delegation SAS)

Requires `DefaultAzureCredential` auth (Managed Identity or Azure CLI). Does **not** work with connection-string auth because `GetUserDelegationKeyAsync` requires Entra identity.

Required Azure RBAC role: **Storage Blob Delegator** (on the storage account) in addition to **Storage Blob Data Contributor** (on the container).

```csharp
public async Task<Result<string>> GenerateDownloadUrlAsync(
    string containerName, string blobPath,
    TimeSpan validity,
    CancellationToken cancellationToken = default)
{
    if (validity <= TimeSpan.Zero)
        return Result.Fail(new Error("Blob.Url.InvalidValidity"));

    try
    {
        var startsOn  = DateTimeOffset.UtcNow;
        var expiresOn = startsOn.Add(validity);

        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient      = containerClient.GetBlobClient(blobPath);

        // This call requires Entra identity — fails with connection-string auth
        var delegationKey = await _blobServiceClient
            .GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerClient.Name,
            BlobName          = blobClient.Name,
            Resource          = "b",   // "b" = blob (not container)
            StartsOn          = startsOn,
            ExpiresOn         = expiresOn
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasParams = sasBuilder.ToSasQueryParameters(
            delegationKey.Value,
            _blobServiceClient.AccountName);

        var uri = new BlobUriBuilder(blobClient.Uri) { Sas = sasParams }.ToUri();

        return Result.Ok(uri.ToString());
    }
    catch (RequestFailedException ex)
    {
        _logger.LogError(ex, "SAS generation failed. Container: {C}, Path: {P}", containerName, blobPath);
        return Result.Fail(new Error("Blob.Url.GenerationFailed")
            .WithMetadata("Status",    ex.Status)
            .WithMetadata("ErrorCode", ex.ErrorCode ?? "Unknown"));
    }
}
```

---

## RequestFailedException Error Codes

Always catch `RequestFailedException`. Common `ex.ErrorCode` values:

| ErrorCode | Meaning | HTTP Status |
|---|---|---|
| `BlobNotFound` | Blob does not exist | 404 |
| `ContainerNotFound` | Container does not exist | 404 |
| `AuthorizationPermissionMismatch` | Missing RBAC role | 403 |
| `BlobAlreadyExists` | Upload with `IfNoneMatch` condition failed | 409 |
| `LeaseIdMissing` | Blob is leased and no lease ID provided | 412 |
| `ServerBusy` | Throttled — retry | 503 |

```csharp
catch (RequestFailedException ex) when (ex.Status == 404)
{
    // Specific 404 handling
}
catch (RequestFailedException ex)
{
    // General SDK failure — log ex.Status and ex.ErrorCode
}
```

---

## Testing: FakeBlobStorageService

For most integration tests, replace `IBlobStorageService` with an in-memory fake. This keeps tests fast and avoids real Azure dependencies:

```csharp
// In WebApplicationFactory:
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureTestServices(services =>
    {
        var descriptor = services.SingleOrDefault(
            d => d.ServiceType == typeof(IBlobStorageService));
        if (descriptor != null)
            services.Remove(descriptor);

        services.AddSingleton<IBlobStorageService, FakeBlobStorageService>();
    });
}
```

`FakeBlobStorageService` stores uploads in a `ConcurrentDictionary<string, (byte[], string)>` and returns `Result.Ok(...)` for all operations. It lets upload tests verify that `UploadAsync` was called with the correct container/path without hitting Azure.

For tests that need real Azure storage (SAS URL tests, content verification), see [advanced-patterns.md](advanced-patterns.md) for the `AzureNotesAppApiFactory` pattern.

---

## Anti-Patterns

```csharp
// DON'T: Store connection strings with account keys in production config
// "AzureBlobStorage": "AccountKey=abc123..." in appsettings.Production.json
// Use DefaultAzureCredential + Managed Identity instead

// DON'T: Register BlobServiceClient manually
services.AddSingleton(new BlobServiceClient(connectionString)); // misses retry, telemetry
// Use AddAzureClients instead

// DON'T: Skip ContentType in upload options
await blobClient.UploadAsync(content, overwrite: true); // loses ContentType
// Always use BlobUploadOptions with BlobHttpHeaders.ContentType

// DON'T: Use DeleteAsync when DeleteIfExistsAsync is appropriate
await blobClient.DeleteAsync(); // throws 404 if blob doesn't exist
// Use DeleteIfExistsAsync for idempotent operations

// DON'T: Let RequestFailedException propagate to controllers
var response = await blobClient.DownloadAsync(ct); // if blob missing, throws → 500
// Catch RequestFailedException and return Result.Fail(...)

// DON'T: Call GetUserDelegationKeyAsync with connection-string auth
// It only works with Entra-based credentials (Managed Identity, Azure CLI, etc.)

// DON'T: Use account-key SAS in production
// Account-key SAS cannot be revoked; use user delegation SAS instead

// DON'T: Inject BlobServiceClient as scoped
services.AddScoped<BlobServiceClient>(...); // BlobServiceClient is thread-safe; singleton only
```

---

## Resources

- **Azure.Storage.Blobs NuGet**: https://www.nuget.org/packages/Azure.Storage.Blobs
- **BlobServiceClient docs**: https://learn.microsoft.com/en-us/dotnet/api/azure.storage.blobs.blobserviceclient
- **DefaultAzureCredential**: https://learn.microsoft.com/en-us/dotnet/api/azure.identity.defaultazurecredential
- **User delegation SAS**: https://learn.microsoft.com/en-us/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet
- **Microsoft.Extensions.Azure**: https://learn.microsoft.com/en-us/dotnet/azure/sdk/dependency-injection
- **RequestFailedException**: https://learn.microsoft.com/en-us/dotnet/api/azure.requestfailedexception
