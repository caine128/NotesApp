using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using Microsoft.Identity.Client;


const string tenantId = "193d982e-0c16-4579-8efa-a931f731df7c";               // Directory (tenant) ID
const string mobileClientId = "8104b02d-db95-455a-881c-647500eb7245";
const string apiClientId = "d1047ffd-a054-4a9f-aeb0-198996f0c0c6";            // Application (client) ID of NotesApp-Api
const string apiScopeName = "notes.readwrite";                                // The scope name you created
const string apiBaseUrl = "https://localhost:7011";                           // TODO : Change to your actual API URL/port 
var policy = "NotesApp_SignUpSignIn";

// Full scope string: api://<API_CLIENT_ID>/<scopeName>
var scopes = new[] { $"api://{apiClientId}/{apiScopeName}" };

var authority = $"https://notesappciam.ciamlogin.com/{tenantId}/{policy}";

// Build the public client app using your tenant authority
var pca = PublicClientApplicationBuilder
    .Create(mobileClientId)
    .WithAuthority(authority)
    .WithRedirectUri("http://localhost")
    .Build();

Console.WriteLine("==============================================================");
Console.WriteLine("ENTRA EXTERNAL ID TOKEN CLAIMS INSPECTOR");
Console.WriteLine("==============================================================");
Console.WriteLine();
Console.WriteLine("Acquiring token via Interactive flow...");
Console.WriteLine();

var result = await pca
    .AcquireTokenInteractive(scopes)
    .WithPrompt(Prompt.SelectAccount)
    .ExecuteAsync();

Console.WriteLine("✅ Authentication successful!");
Console.WriteLine();

// Decode and display ID Token claims
Console.WriteLine("==============================================================");
Console.WriteLine("ID TOKEN CLAIMS (User Identity)");
Console.WriteLine("==============================================================");
if (!string.IsNullOrEmpty(result.IdToken))
{
    var idTokenHandler = new JwtSecurityTokenHandler();
    var idToken = idTokenHandler.ReadJwtToken(result.IdToken);

    Console.WriteLine($"Total Claims: {idToken.Claims.Count()}");
    Console.WriteLine();

    foreach (var claim in idToken.Claims.OrderBy(c => c.Type))
    {
        Console.WriteLine($"  {claim.Type,-35} = {claim.Value}");
    }
}
else
{
    Console.WriteLine("  ⚠️ No ID Token received");
}

Console.WriteLine();
Console.WriteLine("==============================================================");
Console.WriteLine("ACCESS TOKEN CLAIMS (API Authorization)");
Console.WriteLine("==============================================================");

var accessTokenHandler = new JwtSecurityTokenHandler();
var accessToken = accessTokenHandler.ReadJwtToken(result.AccessToken);

Console.WriteLine($"Total Claims: {accessToken.Claims.Count()}");
Console.WriteLine();

foreach (var claim in accessToken.Claims.OrderBy(c => c.Type))
{
    Console.WriteLine($"  {claim.Type,-35} = {claim.Value}");
}

Console.WriteLine();
Console.WriteLine("==============================================================");
Console.WriteLine("CRITICAL CLAIMS FOR ACCOUNT LINKING");
Console.WriteLine("==============================================================");

// Extract critical claims from ID token
var criticalClaims = new Dictionary<string, string>();

if (!string.IsNullOrEmpty(result.IdToken))
{
    var idToken = accessTokenHandler.ReadJwtToken(result.IdToken);

    criticalClaims["oid"] = idToken.Claims.FirstOrDefault(c => c.Type == "oid")?.Value ?? "NOT PRESENT";
    criticalClaims["sub"] = idToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value ?? "NOT PRESENT";
    criticalClaims["email"] = idToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? "NOT PRESENT";
    criticalClaims["email_verified"] = idToken.Claims.FirstOrDefault(c => c.Type == "email_verified")?.Value ?? "NOT PRESENT";
    criticalClaims["preferred_username"] = idToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value ?? "NOT PRESENT";
    criticalClaims["name"] = idToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? "NOT PRESENT";
    criticalClaims["given_name"] = idToken.Claims.FirstOrDefault(c => c.Type == "given_name")?.Value ?? "NOT PRESENT";
    criticalClaims["family_name"] = idToken.Claims.FirstOrDefault(c => c.Type == "family_name")?.Value ?? "NOT PRESENT";
    criticalClaims["iss"] = idToken.Claims.FirstOrDefault(c => c.Type == "iss")?.Value ?? "NOT PRESENT";
    criticalClaims["idp"] = idToken.Claims.FirstOrDefault(c => c.Type == "idp")?.Value ?? "NOT PRESENT";
}

foreach (var claim in criticalClaims)
{
    var indicator = claim.Value == "NOT PRESENT" ? "❌" : "✅";
    Console.WriteLine($"  {indicator} {claim.Key,-25} = {claim.Value}");
}

Console.WriteLine();
Console.WriteLine("==============================================================");
Console.WriteLine("ANALYSIS FOR ACCOUNT LINKING");
Console.WriteLine("==============================================================");

var explicitEmail = criticalClaims.GetValueOrDefault("email", "NOT PRESENT");
var preferredUsername = criticalClaims.GetValueOrDefault("preferred_username", "NOT PRESENT");
var emailVerified = criticalClaims.GetValueOrDefault("email_verified", "NOT PRESENT");
var iss = criticalClaims.GetValueOrDefault("iss", "NOT PRESENT");

// Determine which claim has the email
string emailSource;
string emailValue;

if (explicitEmail != "NOT PRESENT")
{
    emailSource = "email claim";
    emailValue = explicitEmail;
}
else if (preferredUsername != "NOT PRESENT")
{
    emailSource = "preferred_username claim (FALLBACK)";
    emailValue = preferredUsername;
}
else
{
    emailSource = "NONE";
    emailValue = "NOT PRESENT";
}

Console.WriteLine($"Provider (iss): {iss}");
Console.WriteLine($"Email Source: {emailSource}");
Console.WriteLine($"Email Value: {emailValue}");
Console.WriteLine($"Email Verified Claim: {emailVerified}");
Console.WriteLine();

// Determine if provider is trusted
bool isOAuthProvider = iss.Contains("google", StringComparison.OrdinalIgnoreCase) ||
                       iss.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                       iss.Contains("apple", StringComparison.OrdinalIgnoreCase) ||
                       iss.Contains("facebook", StringComparison.OrdinalIgnoreCase);

bool isEntraExternalId = iss.Contains("ciamlogin", StringComparison.OrdinalIgnoreCase);

Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine("ACCOUNT LINKING VERDICT");
Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
Console.WriteLine();

if (emailValue == "NOT PRESENT")
{
    Console.WriteLine("❌ CANNOT LINK: No email found in any claim");
    Console.WriteLine();
    Console.WriteLine("   Resolution: Configure token to include email claim");
}
else if (isOAuthProvider)
{
    Console.WriteLine("✅ SAFE TO AUTO-LINK");
    Console.WriteLine();
    Console.WriteLine("   Reason: OAuth provider (Google/Microsoft/Apple/Facebook)");
    Console.WriteLine("   These providers verify emails before issuing tokens");
    Console.WriteLine($"   Email: {emailValue}");
    Console.WriteLine();
    Console.WriteLine("   Action: Link new UserLogin to existing User by email");
}
else if (isEntraExternalId)
{
    Console.WriteLine("✅ SAFE TO AUTO-LINK");
    Console.WriteLine();
    Console.WriteLine("   Reason: Entra External ID with Email OTP enabled");
    Console.WriteLine("   Email verified during sign-up via OTP flow");
    Console.WriteLine($"   Email: {emailValue}");
    Console.WriteLine();
    if (emailVerified == "true")
    {
        Console.WriteLine("   ✅ Bonus: email_verified=true explicitly confirms it");
    }
    else if (emailVerified == "NOT PRESENT")
    {
        Console.WriteLine("   ℹ️  Note: email_verified claim not present");
        Console.WriteLine("      Email is implicitly verified via OTP flow");
    }
    else
    {
        Console.WriteLine($"   ⚠️  Warning: email_verified={emailVerified}");
        Console.WriteLine("      This is unexpected for OTP-enabled flow");
    }
    Console.WriteLine();
    Console.WriteLine("   Action: Link new UserLogin to existing User by email");
}
else
{
    Console.WriteLine("⚠️  UNKNOWN PROVIDER");
    Console.WriteLine();
    Console.WriteLine($"   Provider: {iss}");
    Console.WriteLine($"   Email: {emailValue}");
    Console.WriteLine($"   Email Verified: {emailVerified}");
    Console.WriteLine();
    Console.WriteLine("   Recommendation: Review provider verification policies");
    Console.WriteLine("   Default: Create separate user (safer)");
}

Console.WriteLine();
Console.WriteLine("==============================================================");
Console.WriteLine("CODE IMPLEMENTATION GUIDANCE");
Console.WriteLine("==============================================================");
Console.WriteLine();

if (emailValue != "NOT PRESENT" && (isOAuthProvider || isEntraExternalId))
{
    Console.WriteLine("Your CurrentUserService.cs already extracts email correctly:");
    Console.WriteLine();
    Console.WriteLine("  var email = principal.FindFirst(\"email\")?.Value ??");
    Console.WriteLine("              principal.FindFirst(\"preferred_username\")?.Value;");
    Console.WriteLine();
    Console.WriteLine("This will get: " + emailValue);
    Console.WriteLine();
    Console.WriteLine("To enable account linking, add this in GetOrCreateUserAsync():");
    Console.WriteLine();
    Console.WriteLine("  // Look for existing User by email (BEFORE creating new User)");
    Console.WriteLine("  User? user = null;");
    Console.WriteLine("  if (!string.IsNullOrWhiteSpace(email))");
    Console.WriteLine("  {");
    Console.WriteLine("      var normalized = email.Trim().ToLowerInvariant();");
    Console.WriteLine("      user = await _dbContext.Users");
    Console.WriteLine("          .Where(u => u.Email == normalized)");
    Console.WriteLine("          .FirstOrDefaultAsync();");
    Console.WriteLine("  }");
    Console.WriteLine();
    Console.WriteLine("Then: if (user is null) { create new user as before }");
}

Console.WriteLine();
Console.WriteLine("==============================================================");

Console.WriteLine("Access token (first 200 chars):");
Console.WriteLine(result.AccessToken);
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