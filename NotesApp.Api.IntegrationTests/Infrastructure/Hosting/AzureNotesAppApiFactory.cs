namespace NotesApp.Api.IntegrationTests.Infrastructure.Hosting
{
    /// <summary>
    /// WebApplicationFactory variant for tests that exercise the real Azure Blob Storage.
    ///
    /// Inherits test authentication from NotesAppApiFactory but skips the fake blob
    /// substitution, allowing the real AzureBlobStorageService (registered via
    /// DefaultAzureCredential in Infrastructure.DependencyInjection) to handle blobs.
    ///
    /// Prerequisites before running tests that use this factory:
    ///   1. appsettings.json must have Azure:Storage:Blob:ServiceUri set (already done).
    ///   2. ConnectionStrings:AzureBlobStorage must NOT exist in secrets.json — that path
    ///      bypasses DefaultAzureCredential and breaks GetUserDelegationKeyAsync.
    ///   3. The runner identity (az login locally, Managed Identity in CI) must have:
    ///        - Storage Blob Data Contributor  (upload/download/delete)
    ///        - Storage Blob Delegator         (GetUserDelegationKeyAsync for SAS URLs)
    ///      assigned in Azure Portal → Storage Account → Access Control (IAM).
    /// </summary>
    public sealed class AzureNotesAppApiFactory : NotesAppApiFactory
    {
        // Returning false tells the base ConfigureWebHost to skip the fake substitution,
        // leaving the real AzureBlobStorageService registered by DependencyInjection intact.
        protected override bool UseFakeBlobStorage => false;
    }
}