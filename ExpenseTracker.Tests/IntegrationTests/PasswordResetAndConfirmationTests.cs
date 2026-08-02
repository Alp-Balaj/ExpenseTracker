using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream C: forgot/reset password and email confirmation, driven end to end over
// HTTP. Tokens travel through CapturingEmailSender (the factory's stand-in for the
// logging email sender) exactly the way a user would read them out of an inbox.
// Email confirmation is recorded and surfaced, never enforced: login with an
// unconfirmed address must keep working forever — production users predate the flow.
[Collection(IntegrationTestCollection.Name)]
public sealed class PasswordResetAndConfirmationTests
{
    private const string InitialPassword = "P@ssw0rd-123!";

    private readonly CustomWebApplicationFactory _factory;

    public PasswordResetAndConfirmationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<string> SignUpAsync(HttpClient client)
    {
        var email = $"{Guid.NewGuid():N}@tests.local";
        var signup = await client.PostAsJsonAsync("/api/User/signup", new
        {
            firstName = "Reset",
            lastName = "Tester",
            email,
            password = InitialPassword
        });
        signup.EnsureSuccessStatusCode();
        return email;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/User/login", new { email, password });

    private static async Task<string> BearerTokenAsync(HttpClient client, string email, string password)
    {
        var login = await LoginAsync(client, email, password);
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Password_reset_round_trip_via_the_emailed_token()
    {
        var client = _factory.CreateClient();
        var email = await SignUpAsync(client);
        const string newPassword = "N3w-P@ssw0rd-456!";

        var forgot = await client.PostAsJsonAsync("/api/User/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgot.StatusCode);

        var message = _factory.EmailSender.LastTo(email);
        Assert.NotNull(message);
        var token = CapturingEmailSender.ExtractToken(message!);

        var reset = await client.PostAsJsonAsync("/api/User/reset-password", new
        {
            email,
            token,
            newPassword
        });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // Old password is dead, new one works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, email, InitialPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email, newPassword)).StatusCode);
    }

    [Fact]
    public async Task A_wrong_or_stale_token_is_rejected_and_changes_nothing()
    {
        var client = _factory.CreateClient();
        var email = await SignUpAsync(client);

        // Outright garbage.
        var garbage = await client.PostAsJsonAsync("/api/User/reset-password", new
        {
            email,
            token = "not-a-real-token",
            newPassword = "N3w-P@ssw0rd-456!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);

        // A token that was real once: consumed by a successful reset, then replayed.
        (await client.PostAsJsonAsync("/api/User/forgot-password", new { email })).EnsureSuccessStatusCode();
        var token = CapturingEmailSender.ExtractToken(_factory.EmailSender.LastTo(email)!);

        (await client.PostAsJsonAsync("/api/User/reset-password", new { email, token, newPassword = "N3w-P@ssw0rd-456!" }))
            .EnsureSuccessStatusCode();

        var replay = await client.PostAsJsonAsync("/api/User/reset-password", new
        {
            email,
            token,
            newPassword = "An0ther-P@ss-789!"
        });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        // The replay changed nothing: the first reset's password still logs in.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email, "N3w-P@ssw0rd-456!")).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_answers_200_for_an_unknown_email_and_sends_nothing()
    {
        var client = _factory.CreateClient();
        var unknown = $"{Guid.NewGuid():N}@tests.local";

        var response = await client.PostAsJsonAsync("/api/User/forgot-password", new { email = unknown });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(_factory.EmailSender.LastTo(unknown));
    }

    [Fact]
    public async Task Email_confirmation_round_trip_and_login_never_requires_it()
    {
        var client = _factory.CreateClient();
        var email = await SignUpAsync(client);

        // Unconfirmed email, working login — the invariant production depends on.
        var token = await BearerTokenAsync(client, email, InitialPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var me = await client.GetFromJsonAsync<JsonElement>("/api/User/me");
        Assert.False(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(email, me.GetProperty("email").GetString());

        // Resend → confirm → surfaced as confirmed.
        var send = await client.PostAsync("/api/User/send-confirmation-email", null);
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var confirmToken = CapturingEmailSender.ExtractToken(_factory.EmailSender.LastTo(email)!);
        var confirm = await client.PostAsJsonAsync("/api/User/confirm-email", new { email, token = confirmToken });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        me = await client.GetFromJsonAsync<JsonElement>("/api/User/me");
        Assert.True(me.GetProperty("emailConfirmed").GetBoolean());

        // Still logs in fine after confirmation too, and a bad confirm token 400s.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, email, InitialPassword)).StatusCode);

        var other = await SignUpAsync(client);
        var bad = await client.PostAsJsonAsync("/api/User/confirm-email", new { email = other, token = "garbage" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }
}
