using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Infrastructure.OAuth;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// OAuth in front of /api/mcp, for clients like claude.ai that cannot take a pasted
// token. The flow is driven by hand (register → consent → token → refresh) so each
// rule can be broken on purpose, and once end to end through the MCP SDK's own OAuth
// client, which is the closest thing in-process to what claude.ai does.
[Collection(IntegrationTestCollection.Name)]
public sealed class McpOAuthTests
{
    private const string RedirectUri = "https://client.example/callback";
    private const string Resource = "http://localhost/api/mcp";

    private readonly CustomWebApplicationFactory _factory;

    public McpOAuthTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Helpers
    private HttpClient Anonymous() => _factory.CreateClient(new() { AllowAutoRedirect = false });

    private sealed record Pkce(string Verifier, string Challenge);

    private static Pkce NewPkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new Pkce(verifier, challenge);
    }

    private async Task<JsonElement> RegisterAsync(string authMethod = "none", params string[] redirectUris)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/oauth/register", new
        {
            client_name = "Test connector",
            redirect_uris = redirectUris.Length > 0 ? redirectUris : new[] { RedirectUri },
            token_endpoint_auth_method = authMethod,
            grant_types = new[] { "authorization_code", "refresh_token" }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Dictionary<string, string?> AuthorizeQuery(string clientId, Pkce pkce, string scope = "mcp:read mcp:write",
        string redirectUri = RedirectUri, string? resource = Resource) => new()
    {
        ["response_type"] = "code",
        ["client_id"] = clientId,
        ["redirect_uri"] = redirectUri,
        ["state"] = "xyz",
        ["scope"] = scope,
        ["code_challenge"] = pkce.Challenge,
        ["code_challenge_method"] = "S256",
        ["resource"] = resource
    };

    /// <summary>What the consent page does when the user clicks Allow or Deny.</summary>
    private static async Task<Uri> DecideAsync(HttpClient user, Dictionary<string, string?> query, bool approve = true, bool canWrite = false)
    {
        var body = query.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        body["approve"] = approve;
        body["can_write"] = canWrite;

        var response = await user.PostAsJsonAsync("/api/oauth/authorize", body);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Uri(json.GetProperty("redirect_url").GetString()!);
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        QueryHelpers.ParseQuery(uri.Query).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    private Task<HttpResponseMessage> TokenAsync(Dictionary<string, string> form, AuthenticationHeaderValue? auth = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/oauth/token") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Authorization = auth;
        return Anonymous().SendAsync(request);
    }

    private Task<HttpResponseMessage> ExchangeAsync(string clientId, string code, Pkce pkce, string redirectUri = RedirectUri) =>
        TokenAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = pkce.Verifier,
            ["resource"] = Resource
        });

    private Task<HttpResponseMessage> RefreshAsync(string clientId, string refreshToken) =>
        TokenAsync(new() { ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["refresh_token"] = refreshToken });

    private sealed record Connection(ApiClient.AuthenticatedUser User, string ClientId, string AccessToken, string RefreshToken, string Scope);

    private async Task<Connection> ConnectAsync(bool canWrite = false)
    {
        var user = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var pkce = NewPkce();

        var redirect = await DecideAsync(user.Client, AuthorizeQuery(clientId, pkce), canWrite: canWrite);
        var response = await ExchangeAsync(clientId, Query(redirect)["code"], pkce);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Connection(user, clientId,
            tokens.GetProperty("access_token").GetString()!,
            tokens.GetProperty("refresh_token").GetString()!,
            tokens.GetProperty("scope").GetString()!);
    }

    private async Task<HttpStatusCode> CallMcpAsync(string bearer)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" })
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return (await Anonymous().SendAsync(request)).StatusCode;
    }
    #endregion

    #region Discovery
    [Fact]
    public async Task Protected_resource_metadata_names_this_server_as_its_own_authorization_server()
    {
        foreach (var path in new[] { "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/api/mcp" })
        {
            var json = await Anonymous().GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(Resource, json.GetProperty("resource").GetString());
            Assert.Equal("http://localhost", Assert.Single(json.GetProperty("authorization_servers").EnumerateArray()).GetString());
        }
    }

    [Fact]
    public async Task Authorization_server_metadata_advertises_pkce_and_registration()
    {
        var json = await Anonymous().GetFromJsonAsync<JsonElement>("/.well-known/oauth-authorization-server");

        Assert.Equal("http://localhost", json.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost/oauth/authorize", json.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("http://localhost/api/oauth/token", json.GetProperty("token_endpoint").GetString());
        Assert.Equal("http://localhost/api/oauth/register", json.GetProperty("registration_endpoint").GetString());
        Assert.Equal("S256", Assert.Single(json.GetProperty("code_challenge_methods_supported").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task Metadata_uses_the_scheme_the_proxy_reports()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/oauth-authorization-server");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var json = await (await Anonymous().SendAsync(request)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("https://localhost", json.GetProperty("issuer").GetString());
    }
    #endregion

    #region Registration
    [Theory]
    [InlineData("http://evil.example/callback")]   // plain http off loopback
    [InlineData("javascript:alert(1)")]
    [InlineData("https://client.example/cb#frag")]
    [InlineData("not a uri")]
    public async Task Registration_refuses_unsafe_redirect_uris(string uri)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/oauth/register", new { redirect_uris = new[] { uri } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_redirect_uri", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Registration_issues_a_secret_only_to_confidential_clients()
    {
        var pub = await RegisterAsync("none");
        var conf = await RegisterAsync("client_secret_post");

        Assert.False(pub.TryGetProperty("client_secret", out _));
        Assert.StartsWith("et_mcs_", conf.GetProperty("client_secret").GetString());
    }
    #endregion

    #region Consent
    [Fact]
    public async Task Consent_screen_describes_the_client_and_where_it_will_redirect()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;

        var url = QueryHelpers.AddQueryString("/api/oauth/authorize", AuthorizeQuery(clientId, NewPkce()));
        var json = await user.GetFromJsonAsync<JsonElement>(url);

        Assert.Equal("Test connector", json.GetProperty("client_name").GetString());
        Assert.Equal("client.example", json.GetProperty("redirect_host").GetString());
        Assert.True(json.GetProperty("requested_write").GetBoolean());
    }

    [Fact]
    public async Task Consent_needs_a_signed_in_user()
    {
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var url = QueryHelpers.AddQueryString("/api/oauth/authorize", AuthorizeQuery(clientId, NewPkce()));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Anonymous().GetAsync(url)).StatusCode);
    }

    // RFC 6749 §4.1.2.1: never redirect to a URI the client did not register, or the
    // consent page becomes an open redirect that hands out codes.
    [Fact]
    public async Task An_unregistered_redirect_uri_is_an_error_on_the_page_not_a_redirect()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var query = AuthorizeQuery(clientId, NewPkce(), redirectUri: "https://attacker.example/callback");

        var describe = await user.GetAsync(QueryHelpers.AddQueryString("/api/oauth/authorize", query));
        var body = query.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        body["approve"] = true;
        var decide = await user.PostAsJsonAsync("/api/oauth/authorize", body);

        Assert.Equal(HttpStatusCode.BadRequest, describe.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, decide.StatusCode);
    }

    [Fact]
    public async Task A_request_without_pkce_is_bounced_back_to_the_client()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var query = AuthorizeQuery(clientId, NewPkce());
        query.Remove("code_challenge");

        var json = await user.GetFromJsonAsync<JsonElement>(QueryHelpers.AddQueryString("/api/oauth/authorize", query));
        var redirect = new Uri(json.GetProperty("error_redirect_url").GetString()!);

        Assert.StartsWith(RedirectUri, redirect.GetLeftPart(UriPartial.Path));
        Assert.Equal("invalid_request", Query(redirect)["error"]);
        Assert.Equal("xyz", Query(redirect)["state"]);
    }

    [Fact]
    public async Task Denying_returns_access_denied_and_no_code()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;

        var redirect = Query(await DecideAsync(user, AuthorizeQuery(clientId, NewPkce()), approve: false));

        Assert.Equal("access_denied", redirect["error"]);
        Assert.False(redirect.ContainsKey("code"));
        Assert.Equal("xyz", redirect["state"]);
        Assert.Equal("http://localhost", redirect["iss"]);
    }
    #endregion

    #region Tokens
    [Fact]
    public async Task The_full_flow_yields_a_token_that_opens_the_mcp_endpoint()
    {
        var connection = await ConnectAsync();

        Assert.StartsWith("et_mcp_", connection.AccessToken);
        Assert.Equal("mcp:read", connection.Scope); // write was requested but not granted
        Assert.Equal(HttpStatusCode.OK, await CallMcpAsync(connection.AccessToken));
    }

    [Fact]
    public async Task The_oauth_token_still_does_not_open_the_rest_api()
    {
        var connection = await ConnectAsync(canWrite: true);
        var http = Anonymous();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/Account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/McpToken")).StatusCode);
    }

    [Fact]
    public async Task A_code_works_once()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var pkce = NewPkce();
        var code = Query(await DecideAsync(user, AuthorizeQuery(clientId, pkce)))["code"];

        Assert.Equal(HttpStatusCode.OK, (await ExchangeAsync(clientId, code, pkce)).StatusCode);
        var replay = await ExchangeAsync(clientId, code, pkce);

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal("invalid_grant", (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_code_needs_the_matching_pkce_verifier()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var code = Query(await DecideAsync(user, AuthorizeQuery(clientId, NewPkce())))["code"];

        var response = await ExchangeAsync(clientId, code, NewPkce());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_code_cannot_be_redeemed_by_another_client()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var clientId = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var otherClient = (await RegisterAsync()).GetProperty("client_id").GetString()!;
        var pkce = NewPkce();
        var code = Query(await DecideAsync(user, AuthorizeQuery(clientId, pkce)))["code"];

        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(otherClient, code, pkce)).StatusCode);
    }

    [Fact]
    public async Task A_confidential_client_must_present_its_secret()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var registration = await RegisterAsync("client_secret_basic");
        var clientId = registration.GetProperty("client_id").GetString()!;
        var secret = registration.GetProperty("client_secret").GetString()!;
        var pkce = NewPkce();
        var code = Query(await DecideAsync(user, AuthorizeQuery(clientId, pkce)))["code"];
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = RedirectUri, ["code_verifier"] = pkce.Verifier
        };

        var withoutSecret = await TokenAsync(new(form) { ["client_id"] = clientId });
        Assert.Equal(HttpStatusCode.Unauthorized, withoutSecret.StatusCode);

        // The failed attempt did not spend the code: client authentication runs first.
        var basic = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}")));
        Assert.Equal(HttpStatusCode.OK, (await TokenAsync(form, basic)).StatusCode);
    }

    [Fact]
    public async Task Write_access_is_whatever_the_user_ticked()
    {
        var readOnly = await ConnectAsync(canWrite: false);
        var readWrite = await ConnectAsync(canWrite: true);

        Assert.Equal("mcp:read", readOnly.Scope);
        Assert.Equal("mcp:read mcp:write", readWrite.Scope);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.McpTokens.Where(t => t.UserId == readOnly.User.UserId).Select(t => t.CanWrite).SingleAsync());
        Assert.True(await db.McpTokens.Where(t => t.UserId == readWrite.User.UserId).Select(t => t.CanWrite).SingleAsync());
    }

    [Fact]
    public async Task Refreshing_rotates_both_tokens_and_retires_the_old_ones()
    {
        var connection = await ConnectAsync();

        var response = await RefreshAsync(connection.ClientId, connection.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fresh = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newAccess = fresh.GetProperty("access_token").GetString()!;
        var newRefresh = fresh.GetProperty("refresh_token").GetString()!;

        Assert.NotEqual(connection.AccessToken, newAccess);
        Assert.NotEqual(connection.RefreshToken, newRefresh);
        Assert.Equal(HttpStatusCode.OK, await CallMcpAsync(newAccess));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMcpAsync(connection.AccessToken));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(connection.ClientId, connection.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(connection.ClientId, newRefresh)).StatusCode);
    }

    [Fact]
    public async Task An_expired_access_token_is_refused_with_invalid_token()
    {
        var connection = await ConnectAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.McpTokens.Where(t => t.UserId == connection.User.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mcp") { Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" }) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
        var response = await Anonymous().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("error=\"invalid_token\"", Assert.Single(response.Headers.WwwAuthenticate).Parameter);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(connection.ClientId, connection.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task The_connection_is_listed_in_settings_and_revoking_it_ends_it()
    {
        var connection = await ConnectAsync();

        var list = await connection.User.Client.GetFromJsonAsync<JsonElement>("/api/McpToken");
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal("Test connector", row.GetProperty("name").GetString());
        Assert.True(row.GetProperty("viaOAuth").GetBoolean());

        await connection.User.Client.DeleteAsync($"/api/McpToken/{row.GetProperty("id").GetGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMcpAsync(connection.AccessToken));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(connection.ClientId, connection.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_ends_every_oauth_connection()
    {
        var connection = await ConnectAsync();

        var change = await connection.User.Client.PostAsJsonAsync("/api/User/change-password",
            new { currentPassword = "P@ssw0rd-123!", newPassword = "N3w-P@ssw0rd-456!" });
        change.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, await CallMcpAsync(connection.AccessToken));
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(connection.ClientId, connection.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Reconnecting_the_same_client_replaces_the_old_connection()
    {
        var connection = await ConnectAsync();
        var pkce = NewPkce();
        var redirect = await DecideAsync(connection.User.Client, AuthorizeQuery(connection.ClientId, pkce));
        Assert.Equal(HttpStatusCode.OK, (await ExchangeAsync(connection.ClientId, Query(redirect)["code"], pkce)).StatusCode);

        var list = await connection.User.Client.GetFromJsonAsync<JsonElement>("/api/McpToken");
        Assert.Single(list.EnumerateArray());
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMcpAsync(connection.AccessToken));
    }

    [Fact]
    public async Task The_revocation_endpoint_ends_the_connection()
    {
        var connection = await ConnectAsync();

        var response = await Anonymous().PostAsync("/api/oauth/revoke", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = connection.ClientId,
            ["token"] = connection.RefreshToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await CallMcpAsync(connection.AccessToken));
    }
    #endregion

    #region End to end
    // The SDK's client discovers everything from the 401, registers itself, sends the
    // "browser" to the authorization URL and redeems the code — the same sequence
    // claude.ai runs. The delegate plays the user: it approves on the consent API with
    // the user's JWT and hands back the code from the redirect.
    [Fact]
    public async Task The_sdk_oauth_client_connects_and_calls_a_tool()
    {
        var user = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var http = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/api/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            OAuth = new ClientOAuthOptions
            {
                RedirectUri = new Uri("http://127.0.0.1:43123/callback"),
                DynamicClientRegistration = new DynamicClientRegistrationOptions { ClientName = "SDK test client" },
                AuthorizationRedirectDelegate = async (authorizationUrl, redirectUri, cancellationToken) =>
                {
                    Assert.Equal("/oauth/authorize", authorizationUrl.AbsolutePath);
                    var query = QueryHelpers.ParseQuery(authorizationUrl.Query).ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString());
                    var redirect = await DecideAsync(user, query, canWrite: true);
                    return Query(redirect)["code"];
                }
            }
        }, http, ownsHttpClient: true);

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, t => t.Name == "list_accounts");
        var result = await client.CallToolAsync("list_accounts", new Dictionary<string, object?>());
        Assert.NotEqual(true, result.IsError);
    }
    #endregion

    #region Units
    [Theory]
    [InlineData("10.0.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.5.4", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.100.1.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700::1111", true)]
    public void Metadata_document_fetches_never_reach_internal_addresses(string address, bool allowed)
    {
        Assert.Equal(allowed, ClientMetadataDocumentFetcher.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000/cb", "http://127.0.0.1:61234/cb", true)]   // loopback: any port
    [InlineData("http://127.0.0.1:5000/cb", "http://127.0.0.1:5000/other", false)]
    [InlineData("https://a.example/cb", "https://a.example/cb", true)]
    [InlineData("https://a.example/cb", "https://a.example/cb?x=1", false)]
    [InlineData("https://a.example/cb", "https://a.example:444/cb", false)]
    public void Redirect_uris_match_exactly_except_for_loopback_ports(string registered, string requested, bool matches)
    {
        Assert.Equal(matches, RedirectUriRules.Matches(new[] { registered }, requested));
    }
    #endregion
}
