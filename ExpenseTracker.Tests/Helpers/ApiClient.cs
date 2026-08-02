using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Infrastructure;

namespace ExpenseTracker.Tests.Helpers;

// Thin helpers over the real HTTP API so invariant tests drive the app the
// same way the SPA does — no direct DbContext/repository access.
public static class ApiClient
{
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(CustomWebApplicationFactory factory) =>
        (await CreateAuthenticatedUserAsync(factory)).Client;

    /// <summary>
    /// A signed-in client plus the identity behind it.
    /// <para>
    /// The user id matters to Stream P's tests specifically: the scheduler's jobs
    /// run with no HTTP context and address users by id, so a test that drives a
    /// job has to know which id the client it just created belongs to.
    /// </para>
    /// </summary>
    public sealed record AuthenticatedUser(HttpClient Client, string UserId, string Email);

    public static async Task<AuthenticatedUser> CreateAuthenticatedUserAsync(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@tests.local";
        const string password = "P@ssw0rd-123!";

        var signup = await client.PostAsJsonAsync("/api/User/signup", new
        {
            firstName = "Test",
            lastName = "User",
            email,
            password
        });
        signup.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/User/login", new { email, password });
        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new AuthenticatedUser(client, ReadSubjectClaim(token), email);
    }

    /// <summary>
    /// Pulls <c>sub</c> out of the JWT payload — the claim the bearer middleware
    /// maps to NameIdentifier, and therefore the id every user-owned row carries.
    /// Read rather than looked up in the database so the helper needs no scope.
    /// </summary>
    private static string ReadSubjectClaim(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = (payload.Length % 4) switch
        {
            2 => payload + "==",
            3 => payload + "=",
            _ => payload
        };

        using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
        return document.RootElement.GetProperty("sub").GetString()!;
    }

    public static async Task<Guid> CreateCurrencyAsync(HttpClient client, string code = "USD", decimal rate = 1m)
    {
        var response = await client.PostAsJsonAsync("/api/Currency", new
        {
            name = code,
            code,
            symbol = "$",
            exchangeRateToBase = rate
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateAccountAsync(HttpClient client, Guid currencyId, decimal balance = 0m)
    {
        var response = await client.PostAsJsonAsync("/api/Account", new
        {
            name = "Test Account",
            amountType = 0,
            balance,
            currencyId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<Guid> CreateCategoryAsync(HttpClient client, int categoryType = 0, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/Category", new
        {
            name = name ?? $"Test Category {Guid.NewGuid():N}",
            categoryType
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<decimal> GetAccountBalanceAsync(HttpClient client, Guid accountId)
    {
        var response = await client.GetAsync($"/api/Account/{accountId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
    }

    public static async Task<decimal> GetSavingGoalSavedAmountAsync(HttpClient client, Guid goalId)
    {
        var response = await client.GetAsync($"/api/SavingGoal/{goalId}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("savedAmount").GetDecimal();
    }
}
