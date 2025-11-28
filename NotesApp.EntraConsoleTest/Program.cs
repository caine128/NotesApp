using System.Net.Http.Headers;
using Microsoft.Identity.Client;

const string tenantId = "193d982e-0c16-4579-8efa-a931f731df7c";                   // Directory (tenant) ID
const string mobileClientId = "8104b02d-db95-455a-881c-647500eb7245";
const string apiClientId = "d1047ffd-a054-4a9f-aeb0-198996f0c0c6";            // Application (client) ID of NotesApp-Api
const string apiScopeName = "notes.readwrite";       // The scope name you created
const string apiBaseUrl = "https://localhost:7011";      // Change to your actual API URL/port 

// Full scope string: api://<API_CLIENT_ID>/<scopeName>
var scopes = new[] { $"api://{apiClientId}/{apiScopeName}" };

// Build the public client app using your tenant authority
var pca = PublicClientApplicationBuilder
    .Create(mobileClientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .Build();

Console.WriteLine("Acquiring token via Device Code flow...");

// Device code flow (perfect for console / CLI) – uses https://microsoft.com/devicelogin
var result = await pca.AcquireTokenWithDeviceCode(
        scopes,
        deviceCodeResult =>
        {
            Console.WriteLine(deviceCodeResult.Message);
            return Task.CompletedTask;
        })
    .ExecuteAsync();

Console.WriteLine("Access token acquired.");
Console.WriteLine();

Console.WriteLine("Access token (first 200 chars):");
Console.WriteLine(result.AccessToken[..200]);
Console.WriteLine();

// Now call your API
using var httpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
httpClient.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", result.AccessToken);

// Example: GET /api/tasks/day?date=2025-11-10
var date = new DateOnly(2025, 11, 10);
var response = await httpClient.GetAsync($"/api/tasks/day?date={date:yyyy-MM-dd}");
Console.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");

var content = await response.Content.ReadAsStringAsync();
Console.WriteLine("Response JSON:");
Console.WriteLine(content);

Console.WriteLine();
Console.WriteLine("Done. Press Enter to exit...");
Console.ReadLine();