using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Driven through the SDK's real client over the app's real pipeline, because what
// can go wrong here lives at the seams: which credential opens which door, whose
// rows a tool sees, and whether a tool call that moves money keeps the balance
// honest. Like IdempotentWriteTests, every write assertion checks the balance too.
[Collection(IntegrationTestCollection.Name)]
public sealed class McpServerTests
{
    private const string Endpoint = "/api/mcp";

    private readonly CustomWebApplicationFactory _factory;

    public McpServerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Fixture(ApiClient.AuthenticatedUser User, Guid AccountId, Guid ExpenseCategoryId, Guid IncomeCategoryId)
    {
        public HttpClient Client => User.Client;
    }

    private async Task<Fixture> ArrangeAsync(decimal balance = 1_000m)
    {
        var user = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(user.Client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(user.Client, currencyId, balance);
        var expenseCategory = await ApiClient.CreateCategoryAsync(user.Client, categoryType: 0);
        var incomeCategory = await ApiClient.CreateCategoryAsync(user.Client, categoryType: 1);
        return new Fixture(user, accountId, expenseCategory, incomeCategory);
    }

    private static async Task<(Guid Id, string Token)> CreateTokenAsync(HttpClient client, bool canWrite = false)
    {
        var response = await client.PostAsJsonAsync("/api/McpToken", new { name = "Test client", canWrite });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    private async Task<McpClient> ConnectAsync(string token)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, Endpoint),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            http,
            ownsHttpClient: true);

        return await McpClient.CreateAsync(transport);
    }

    private static Task<CallToolResult> CallAsync(McpClient client, string tool, object? args = null)
    {
        var arguments = args is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.SerializeToElement(args).EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value);

        return client.CallToolAsync(tool, arguments).AsTask();
    }

    private static string Text(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static JsonElement Json(CallToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return JsonDocument.Parse(Text(result)).RootElement;
    }

    private static string Error(CallToolResult result)
    {
        Assert.True(result.IsError);
        return Text(result);
    }

    private static object Expense(Fixture fx, decimal amount = 25m, string? key = null) => new
    {
        title = "Coffee",
        amount,
        date = "2026-11-01",
        accountId = fx.AccountId,
        categoryId = fx.ExpenseCategoryId,
        idempotencyKey = key
    };

    private HttpRequestMessage RawPost(string? bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" })
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    #region Which credential opens which door
    [Fact]
    public async Task Endpoint_refuses_an_anonymous_caller()
    {
        var response = await _factory.CreateClient().SendAsync(RawPost(null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Endpoint_refuses_an_unknown_token()
    {
        var response = await _factory.CreateClient().SendAsync(RawPost("et_mcp_" + new string('0', 64)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Endpoint_refuses_a_browser_session_jwt()
    {
        var fx = await ArrangeAsync();
        var jwt = fx.Client.DefaultRequestHeaders.Authorization!.Parameter;

        var response = await _factory.CreateClient().SendAsync(RawPost(jwt));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // The boundary that matters most: a token given to an AI client must not reach
    // the REST API, where it could change a password or delete the account.
    [Theory]
    [InlineData("GET", "/api/Account")]
    [InlineData("GET", "/api/McpToken")]
    [InlineData("POST", "/api/McpToken")]
    [InlineData("POST", "/api/User/change-password")]
    [InlineData("POST", "/api/User/delete-account")]
    [InlineData("GET", "/api/Export/json")]
    public async Task Token_does_not_open_the_rest_api(string method, string path)
    {
        var fx = await ArrangeAsync();
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);

        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = JsonContent.Create(new { name = "x", password = "x" });

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    #endregion

    #region Token lifecycle
    [Fact]
    public async Task Token_is_stored_hashed_and_only_ever_shown_once()
    {
        var fx = await ArrangeAsync();
        var (id, token) = await CreateTokenAsync(fx.Client);

        var list = await fx.Client.GetStringAsync("/api/McpToken");
        Assert.DoesNotContain(token, list);
        Assert.Contains(token[^4..], list);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.McpTokens.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.NotEqual(token, row.TokenHash);
        Assert.DoesNotContain(token, row.TokenHash);
    }

    [Fact]
    public async Task A_revoked_token_stops_working()
    {
        var fx = await ArrangeAsync();
        var (id, token) = await CreateTokenAsync(fx.Client);

        (await _factory.CreateClient().SendAsync(RawPost(token))).EnsureSuccessStatusCode();

        var revoke = await fx.Client.DeleteAsync($"/api/McpToken/{id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        var response = await _factory.CreateClient().SendAsync(RawPost(token));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_user_cannot_revoke_someone_elses_token()
    {
        var victim = await ArrangeAsync();
        var attacker = await ArrangeAsync();
        var (id, token) = await CreateTokenAsync(victim.Client);

        var revoke = await attacker.Client.DeleteAsync($"/api/McpToken/{id}");

        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        (await _factory.CreateClient().SendAsync(RawPost(token))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Changing_the_password_kills_every_token()
    {
        var fx = await ArrangeAsync();
        var (_, token) = await CreateTokenAsync(fx.Client);

        var change = await fx.Client.PostAsJsonAsync("/api/User/change-password", new
        {
            currentPassword = "P@ssw0rd-123!",
            newPassword = "N3w-P@ssw0rd-456!"
        });
        change.EnsureSuccessStatusCode();

        var response = await _factory.CreateClient().SendAsync(RawPost(token));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_the_user_takes_their_tokens()
    {
        var fx = await ArrangeAsync();
        await CreateTokenAsync(fx.Client);

        var delete = await fx.Client.PostAsJsonAsync("/api/User/delete-account", new { password = "P@ssw0rd-123!" });
        delete.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.McpTokens.AnyAsync(t => t.UserId == fx.User.UserId));
    }
    #endregion

    #region Reads
    [Fact]
    public async Task Client_sees_the_tools_with_honest_annotations()
    {
        var fx = await ArrangeAsync();
        var (_, token) = await CreateTokenAsync(fx.Client);
        await using var mcp = await ConnectAsync(token);

        var tools = (await mcp.ListToolsAsync()).ToDictionary(t => t.Name);

        Assert.Equal(
            new[]
            {
                "get_budget_status", "get_cashflow_projection", "get_spending_insights", "list_accounts",
                "list_categories", "list_expenses", "list_incomes", "log_expense", "log_income"
            },
            tools.Keys.OrderBy(k => k, StringComparer.Ordinal));

        Assert.All(
            tools.Values.Where(t => !t.Name.StartsWith("log_")),
            t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.All(
            tools.Values.Where(t => t.Name.StartsWith("log_")),
            t => Assert.False(t.ProtocolTool.Annotations?.ReadOnlyHint));
    }

    [Fact]
    public async Task Every_read_tool_runs_for_a_real_user()
    {
        var fx = await ArrangeAsync();
        var (_, token) = await CreateTokenAsync(fx.Client);
        await using var mcp = await ConnectAsync(token);

        foreach (var tool in new[]
                 {
                     "list_accounts", "list_categories", "list_expenses", "list_incomes",
                     "get_budget_status", "get_spending_insights", "get_cashflow_projection"
                 })
        {
            Json(await CallAsync(mcp, tool));
        }
    }

    [Fact]
    public async Task Reads_show_only_the_tokens_owner()
    {
        var mine = await ArrangeAsync(balance: 111m);
        var theirs = await ArrangeAsync(balance: 999m);
        var (_, writeToken) = await CreateTokenAsync(theirs.Client, canWrite: true);
        await using (var other = await ConnectAsync(writeToken))
            Json(await CallAsync(other, "log_expense", Expense(theirs)));

        var (_, token) = await CreateTokenAsync(mine.Client);
        await using var mcp = await ConnectAsync(token);

        // Signup seeds a starter account, so the owner has more than the one made here.
        var accounts = Json(await CallAsync(mcp, "list_accounts")).EnumerateArray().ToList();
        Assert.DoesNotContain(accounts, a => a.GetProperty("id").GetGuid() == theirs.AccountId);

        var account = Assert.Single(accounts, a => a.GetProperty("id").GetGuid() == mine.AccountId);
        Assert.Equal(111m, account.GetProperty("balance").GetDecimal());
        Assert.Equal("EUR", account.GetProperty("currency").GetString());

        Assert.Equal(0, Json(await CallAsync(mcp, "list_expenses")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_expenses_filters_and_pages()
    {
        var fx = await ArrangeAsync();
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        foreach (var (title, date) in new[] { ("Rent", "2026-09-01"), ("100% juice", "2026-10-01"), ("Lunch", "2026-10-15") })
        {
            Json(await CallAsync(mcp, "log_expense", new
            {
                title, amount = 10m, date, accountId = fx.AccountId, categoryId = fx.ExpenseCategoryId
            }));
        }

        var october = Json(await CallAsync(mcp, "list_expenses", new { from = "2026-10-01", to = "2026-10-31" }));
        Assert.Equal(2, october.GetProperty("totalCount").GetInt32());
        Assert.Equal("Lunch", october.GetProperty("items")[0].GetProperty("title").GetString());

        var page = Json(await CallAsync(mcp, "list_expenses", new { limit = 1, offset = 1 }));
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal("100% juice", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());

        // A wildcard in the search text is text, not a pattern.
        var percent = Json(await CallAsync(mcp, "list_expenses", new { search = "0%" }));
        Assert.Equal(1, percent.GetProperty("totalCount").GetInt32());

        var caseless = Json(await CallAsync(mcp, "list_expenses", new { search = "LUNCH" }));
        Assert.Equal(1, caseless.GetProperty("totalCount").GetInt32());

        Assert.Contains("limit", Error(await CallAsync(mcp, "list_expenses", new { limit = 100_000 })));
    }

    [Fact]
    public async Task Budget_status_rejects_a_malformed_month()
    {
        var fx = await ArrangeAsync();
        var (_, token) = await CreateTokenAsync(fx.Client);
        await using var mcp = await ConnectAsync(token);

        Assert.Contains("YYYY-MM", Error(await CallAsync(mcp, "get_budget_status", new { month = "November" })));
    }
    #endregion

    #region Writes
    [Fact]
    public async Task Log_expense_debits_the_account_and_shows_up_in_the_app()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        var logged = Json(await CallAsync(mcp, "log_expense", Expense(fx, amount: 25m)));

        Assert.Equal(975m, logged.GetProperty("newAccountBalance").GetDecimal());
        Assert.Equal(975m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));

        var row = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/Expense/{logged.GetProperty("id").GetGuid()}");
        Assert.Equal("Coffee", row.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Log_income_credits_the_account()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        Json(await CallAsync(mcp, "log_income", new
        {
            amount = 200m,
            date = "2026-11-01",
            accountId = fx.AccountId,
            categoryId = fx.IncomeCategoryId
        }));

        Assert.Equal(1_200m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task A_read_only_token_cannot_write()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: false);
        await using var mcp = await ConnectAsync(token);

        Assert.Contains("read-only", Error(await CallAsync(mcp, "log_expense", Expense(fx))));

        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
        Assert.Equal(0, Json(await CallAsync(mcp, "list_expenses")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Cannot_write_to_someone_elses_account_or_category()
    {
        var attacker = await ArrangeAsync();
        var victim = await ArrangeAsync(balance: 500m);
        var (_, token) = await CreateTokenAsync(attacker.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        var foreignAccount = attacker with { AccountId = victim.AccountId };
        Assert.Contains("Account not found", Error(await CallAsync(mcp, "log_expense", Expense(foreignAccount))));

        var foreignCategory = attacker with { ExpenseCategoryId = victim.ExpenseCategoryId };
        Assert.Contains("category", Error(await CallAsync(mcp, "log_expense", Expense(foreignCategory))));

        Assert.Equal(500m, await ApiClient.GetAccountBalanceAsync(victim.Client, victim.AccountId));
        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(attacker.Client, attacker.AccountId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-25)]
    public async Task A_non_positive_amount_is_refused(int amount)
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        Assert.Contains("greater than zero", Error(await CallAsync(mcp, "log_expense", Expense(fx, amount: amount))));

        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task An_income_category_is_refused_for_an_expense()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        var wrong = fx with { ExpenseCategoryId = fx.IncomeCategoryId };
        Assert.Contains("Expense category", Error(await CallAsync(mcp, "log_expense", Expense(wrong))));

        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task A_retried_write_with_the_same_key_moves_money_once()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        var first = Json(await CallAsync(mcp, "log_expense", Expense(fx, key: "retry-1")));
        var second = Json(await CallAsync(mcp, "log_expense", Expense(fx, key: "retry-1")));

        Assert.False(first.GetProperty("alreadyLogged").GetBoolean());
        Assert.True(second.GetProperty("alreadyLogged").GetBoolean());
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());

        Assert.Equal(975m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
        Assert.Equal(1, Json(await CallAsync(mcp, "list_expenses")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_failed_write_releases_its_key_for_the_retry()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var (_, token) = await CreateTokenAsync(fx.Client, canWrite: true);
        await using var mcp = await ConnectAsync(token);

        var wrong = fx with { ExpenseCategoryId = Guid.NewGuid() };
        Error(await CallAsync(mcp, "log_expense", Expense(wrong, key: "retry-2")));

        var retried = Json(await CallAsync(mcp, "log_expense", Expense(fx, key: "retry-2")));

        Assert.False(retried.GetProperty("alreadyLogged").GetBoolean());
        Assert.Equal(975m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }
    #endregion
}
