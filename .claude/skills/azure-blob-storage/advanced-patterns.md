# Azure Blob Storage — Advanced Patterns

## Contents

- [Retry Policy Tuning](#retry-policy-tuning)
- [DownloadStreamingAsync for Large Files](#downloadstreamingasync-for-large-files)
- [User Delegation SAS: Auth Requirements and Expiry](#user-delegation-sas-auth-requirements-and-expiry)
- [Connection String vs. URI + DefaultAzureCredential Decision Guide](#connection-string-vs-uri--defaultazurecredential-decision-guide)
- [Real Azure Integration Tests (AzureNotesAppApiFactory)](#real-azure-integration-tests-azurenotesappapifactory)

---

## Retry Policy Tuning

Retry policy is configured at the `AddAzureClients` level and applies to all clients registered in that block. The codebase uses exponential backoff:

```csharp
azure.ConfigureDefaults(options =>
{
    options.Retry.MaxRetries     = 3;
    options.Retry.Mode           = RetryMode.Exponential;
    options.Retry.Delay          = TimeSpan.FromSeconds(1);   // initial delay
    options.Retry.MaxDelay       = TimeSpan.FromSeconds(30);  // cap
    options.Retry.NetworkTimeout = TimeSpan.FromSeconds(100); // per-attempt timeout
});
```

**When to adjust:**
- `MaxRetries = 3` is conservative. For batch background jobs (Worker) that can tolerate longer waits, raise to 5.
- `NetworkTimeout` of 100 s is per-attempt. For large file uploads, this may be too short — increase proportionally to expected file size and bandwidth.
- For user-facing requests (API endpoints), keep `MaxRetries` low (2–3) to avoid stacking latency on the user.

**Fixed delay vs. Exponential:**
```csharp
options.Retry.Mode  = RetryMode.Fixed;  // retries after Delay every time (for testing/diagnostics)
options.Retry.Mode  = RetryMode.Exponential; // recommended for production
```

**Per-client override:** If you need different retry behavior for a specific client (e.g., a background service vs. a user-facing API), register them in separate `AddAzureClients` calls.

---

## DownloadStreamingAsync for Large Files

`DownloadAsync` buffers the entire blob into memory before returning. For files larger than a few MB, use `DownloadStreamingAsync` instead — it returns a network stream that you pipe directly to the response:

```csharp
public async Task<Result<StorageDownloadResult>> DownloadStreamingAsync(
    string containerName, string blobPath,
    CancellationToken cancellationToken = default)
{
    try
    {
        var blobClient = _blobServiceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobPath);

        // Returns a network stream — does NOT buffer the whole file
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);

        return Result.Ok(new StorageDownloadResult(
            content:     response.Value.Content,    // Stream — must be consumed before disposal
            contentType: response.Value.Details.ContentType,
            sizeBytes:   response.Value.Details.ContentLength));
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        return Result.Fail(new Error("Blob.NotFound"));
    }
    catch (RequestFailedException ex)
    {
        return Result.Fail(new Error("Blob.Download.Failed")
            .WithMetadata("Status", ex.Status));
    }
}
```

**Piping to HTTP response (controller):**
```csharp
var result = await _blobStorage.DownloadStreamingAsync(container, path, ct);
if (result.IsFailed) return result.ToActionResult();

var download = result.Value;
return File(download.Content, download.ContentType);
// ASP.NET Core streams this directly — never buffers in memory
```

**Decision table:**

| Method | Use when |
|---|---|
| `DownloadAsync` | Small files (< 4 MB), simple in-memory processing |
| `DownloadStreamingAsync` | Large files, piping to HTTP response, memory-sensitive |
| `DownloadToAsync(Stream)` | Writing directly to a local file or pre-allocated stream |

---

## User Delegation SAS: Auth Requirements and Expiry

User delegation SAS is the recommended SAS type for production because it is backed by an Entra identity and can be revoked. Account-key SAS cannot be revoked without rotating the account key.

**Required Azure RBAC roles (on the storage account):**
- `Storage Blob Data Contributor` — read/write/delete blob data
- `Storage Blob Delegator` — call `GetUserDelegationKeyAsync` (on storage account scope, not container)

**How it works:**
1. `GetUserDelegationKeyAsync(startsOn, expiresOn)` requests a delegation key from Entra, valid for the specified window (max 7 days per Azure limit).
2. `BlobSasBuilder.ToSasQueryParameters(delegationKey, accountName)` signs the SAS with that key.
3. The resulting URL is valid until `ExpiresOn` and grants only the permissions set via `SetPermissions`.

**Expiry guidance:**
- Short-lived URLs (1–4 hours) for direct file downloads returned to API clients — reduces exposure window.
- The delegation key itself can be up to 7 days. Generating a new key per request is fine; it's cheap.
- Clock skew: set `StartsOn` slightly in the past (e.g., `UtcNow.AddMinutes(-2)`) to tolerate clock drift between your server and Azure.

```csharp
var startsOn  = DateTimeOffset.UtcNow.AddMinutes(-2);  // tolerate clock drift
var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
```

**What fails with connection-string auth:**
`GetUserDelegationKeyAsync` returns `403 AuthorizationPermissionMismatch` when called with account-key credentials. The method requires an Entra OAuth token — only `DefaultAzureCredential` (Managed Identity, Azure CLI, etc.) provides one. This means SAS URL generation only works in Azure or when `az login` is active locally.

---

## Connection String vs. URI + DefaultAzureCredential Decision Guide

| Criterion | Connection String | URI + DefaultAzureCredential |
|---|---|---|
| Local dev simplicity | Easy — share the string | Requires `az login` or VS login |
| Production security | Account key in config (avoid) | Managed Identity — no secrets |
| User delegation SAS | Not supported | Supported |
| Key rotation | Manual and risky | N/A — no keys |
| Azure deployment | Works but not recommended | Recommended |
| CI/CD pipelines | Works with secrets | Use workload identity federation |

**Recommended setup:**
- Development: connection string in user secrets (`dotnet user-secrets set ...`)
- Production: `Azure:Storage:Blob:ServiceUri` in app config, Managed Identity assigned in Azure Portal
- The codebase's dual-mode registration (`if connectionString ... else if serviceUri`) handles this automatically

---

## Real Azure Integration Tests (AzureNotesAppApiFactory)

The default `NotesAppApiFactory` replaces `IBlobStorageService` with a `FakeBlobStorageService`. For tests that need to verify real Azure interactions (SAS URL format, actual blob content), use `AzureNotesAppApiFactory`:

```csharp
/// <summary>
/// Factory that opts out of the fake blob storage and uses the real
/// AzureBlobStorageService backed by a real Azure Storage account.
///
/// IMPORTANT: Requires Azure credentials in the test environment.
/// Cannot use connection-string auth because GetUserDelegationKeyAsync
/// needs an Entra token — use DefaultAzureCredential (az login, Managed Identity).
/// </summary>
public class AzureNotesAppApiFactory : NotesAppApiFactory
{
    protected override bool UseFakeBlobStorage => false;
}
```

Usage:
```csharp
[Collection("Azure")]
public class AttachmentDownloadAzureTests : IClassFixture<AzureNotesAppApiFactory>
{
    private readonly AzureNotesAppApiFactory _factory;

    public AttachmentDownloadAzureTests(AzureNotesAppApiFactory factory)
        => _factory = factory;

    [Fact]
    public async Task DownloadUrl_ReturnsValidSasUri()
    {
        var client = _factory.CreateClientAsUser(Guid.NewGuid());
        var response = await client.GetAsync("/api/attachments/{id}/download-url");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<DownloadUrlDto>();
        body!.Url.Should().Contain("?sv=");  // SAS query param present
    }
}
```

**Pitfalls:**
- Do not use connection-string auth with `AzureNotesAppApiFactory` — `GetUserDelegationKeyAsync` will 403. The factory depends on `DefaultAzureCredential` (CI: Managed Identity; local: `az login`).
- Azure tests are slow and incur real storage costs. Keep them in a separate test collection and run them explicitly in CI rather than on every PR.
- Use a dedicated test storage account (not the production account) — prefix containers with `test-` and clean up after each run.
