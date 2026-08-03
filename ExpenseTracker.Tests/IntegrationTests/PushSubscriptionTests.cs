using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream P, the device-registration half of Web Push: /api/Push.
//
// The push service itself is CapturingPushSender (registered by the factory), so
// nothing here touches the network; the encryption it stands in for is pinned
// separately by WebPushEncryptorTests.
[Collection(IntegrationTestCollection.Name)]
public sealed class PushSubscriptionTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PushSubscriptionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.PushSender.Reset();
    }

    // Unique per test: the notification jobs sweep every subscribed user in the
    // shared container, so assertions are always scoped to this test's endpoint.
    private static string NewEndpoint() => $"https://push.tests.local/{Guid.NewGuid():N}";

    private static async Task<HttpResponseMessage> SubscribeAsync(
        HttpClient client, string endpoint, string? userAgent = null) =>
        await client.PostAsJsonAsync("/api/Push/subscribe", new
        {
            endpoint,
            keys = new
            {
                p256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
                auth = "BTBZMqHH6r4Tts7J_aSIgg"
            },
            userAgent
        });

    private static async Task<JsonElement> DevicesAsync(HttpClient client, string? currentEndpoint = null)
    {
        var url = currentEndpoint is null ? "/api/Push/devices" : $"/api/Push/devices?endpoint={Uri.EscapeDataString(currentEndpoint)}";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Configuration_hands_the_browser_the_application_server_key()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        var response = await client.GetAsync("/api/Push/configuration");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("publicKey").GetString()));
    }

    [Fact]
    public async Task An_unconfigured_deployment_says_so_instead_of_offering_a_prompt_that_leads_nowhere()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        _factory.PushSender.IsConfigured = false;

        try
        {
            var configuration = await client.GetAsync("/api/Push/configuration");
            configuration.EnsureSuccessStatusCode();
            var body = await configuration.Content.ReadFromJsonAsync<JsonElement>();

            Assert.False(body.GetProperty("available").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("publicKey").ValueKind);

            // And subscribing is refused as a server problem, not a bad request:
            // a browser permission is granted once, so it must not be spent on a
            // deployment that cannot send.
            var subscribe = await SubscribeAsync(client, NewEndpoint());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, subscribe.StatusCode);
        }
        finally
        {
            _factory.PushSender.IsConfigured = true;
        }
    }

    [Fact]
    public async Task Push_endpoints_require_authentication()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/Push/configuration")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/Push/devices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SubscribeAsync(anonymous, NewEndpoint())).StatusCode);
    }

    [Fact]
    public async Task Subscribing_registers_the_device_and_lists_it_back()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var endpoint = NewEndpoint();

        var response = await SubscribeAsync(client, endpoint, "Firefox on Linux");
        response.EnsureSuccessStatusCode();

        var device = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Firefox on Linux", device.GetProperty("userAgent").GetString());
        Assert.True(device.GetProperty("isCurrentDevice").GetBoolean());

        var devices = await DevicesAsync(client, endpoint);
        Assert.Equal(1, devices.GetArrayLength());
        Assert.True(devices[0].GetProperty("isCurrentDevice").GetBoolean());

        // The endpoint is a capability to push to that device; it is never handed
        // back out, and the client already has its own.
        Assert.False(devices[0].TryGetProperty("endpoint", out _));
    }

    [Fact]
    public async Task Re_subscribing_the_same_browser_updates_the_row_instead_of_duplicating_it()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var endpoint = NewEndpoint();

        (await SubscribeAsync(client, endpoint, "Chrome")).EnsureSuccessStatusCode();
        (await SubscribeAsync(client, endpoint, "Chrome, updated")).EnsureSuccessStatusCode();

        var devices = await DevicesAsync(client);
        Assert.Equal(1, devices.GetArrayLength());
        Assert.Equal("Chrome, updated", devices[0].GetProperty("userAgent").GetString());
    }

    [Fact]
    public async Task A_device_handed_to_another_account_stops_receiving_the_first_accounts_notifications()
    {
        var first = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var second = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var endpoint = NewEndpoint();

        (await SubscribeAsync(first.Client, endpoint)).EnsureSuccessStatusCode();
        (await SubscribeAsync(second.Client, endpoint)).EnsureSuccessStatusCode();

        // One endpoint is one browser installation. Signing in as someone else on
        // a shared phone must move the row, not leave the previous account quietly
        // receiving budget warnings on it.
        Assert.Equal(0, (await DevicesAsync(first.Client)).GetArrayLength());
        Assert.Equal(1, (await DevicesAsync(second.Client)).GetArrayLength());
    }

    [Fact]
    public async Task Unsubscribing_removes_only_the_callers_own_device()
    {
        var owner = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var stranger = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var endpoint = NewEndpoint();

        (await SubscribeAsync(owner.Client, endpoint)).EnsureSuccessStatusCode();

        var byStranger = await stranger.Client.PostAsJsonAsync("/api/Push/unsubscribe", new { endpoint });
        byStranger.EnsureSuccessStatusCode();
        Assert.False((await byStranger.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removed").GetBoolean());
        Assert.Equal(1, (await DevicesAsync(owner.Client)).GetArrayLength());

        var byOwner = await owner.Client.PostAsJsonAsync("/api/Push/unsubscribe", new { endpoint });
        byOwner.EnsureSuccessStatusCode();
        Assert.True((await byOwner.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removed").GetBoolean());
        Assert.Equal(0, (await DevicesAsync(owner.Client)).GetArrayLength());

        // Idempotent: the browser may have dropped its side already.
        var again = await owner.Client.PostAsJsonAsync("/api/Push/unsubscribe", new { endpoint });
        again.EnsureSuccessStatusCode();
        Assert.False((await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task The_test_button_reaches_the_device_and_still_works_the_second_time()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var endpoint = NewEndpoint();
        (await SubscribeAsync(client, endpoint)).EnsureSuccessStatusCode();

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await client.PostAsync("/api/Push/test", null);
            response.EnsureSuccessStatusCode();
            Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("delivered").GetInt32());
        }

        // Exempt from the dedupe log on purpose — a test that only works once is
        // not a test.
        Assert.Equal(2, _factory.PushSender.To(endpoint).Count);
    }

    [Fact]
    public async Task A_dead_endpoint_is_dropped_rather_than_retried_for_ever()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var endpoint = NewEndpoint();
        (await SubscribeAsync(client, endpoint)).EnsureSuccessStatusCode();

        _factory.PushSender.NextOutcome = PushSendOutcome.Gone;
        try
        {
            (await client.PostAsync("/api/Push/test", null)).EnsureSuccessStatusCode();
        }
        finally
        {
            _factory.PushSender.NextOutcome = PushSendOutcome.Delivered;
        }

        // 410 from a push service is definitive: the browser is gone.
        Assert.Equal(0, (await DevicesAsync(client)).GetArrayLength());
    }
}
