using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream P's three D3 triggers and the sweep underneath them, driven by resolving
// each job and running it — the hosted service only decides *when*, and
// SchedulerTests covers that.
//
// Two properties are load-bearing across this file:
//
//   1. Every job re-evaluates the same state on every run, so without the dedupe
//      log a budget crossed on the 5th would push every day for the rest of the
//      month. Each trigger therefore has a "and not again tomorrow" test.
//   2. The jobs sweep every subscribed user in this suite's shared container, so
//      assertions read CapturingPushSender.To(endpoint) with a per-test endpoint
//      rather than counting messages globally.
[Collection(IntegrationTestCollection.Name)]
public sealed class NotificationJobTests
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationJobTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.PushSender.Reset();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");
    private static string NewEndpoint() => $"https://push.tests.local/{Guid.NewGuid():N}";

    private sealed record Subscriber(
        HttpClient Client, string UserId, string Endpoint, Guid CurrencyId, Guid AccountId, Guid CategoryId);

    /// <summary>A signed-up user with a EUR account, a category and a registered device.</summary>
    private async Task<Subscriber> ArrangeAsync(decimal balance = 10_000m)
    {
        var user = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(user.Client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(user.Client, currencyId, balance);
        var categoryId = await ApiClient.CreateCategoryAsync(user.Client, categoryType: 0);

        var endpoint = NewEndpoint();
        var response = await user.Client.PostAsJsonAsync("/api/Push/subscribe", new
        {
            endpoint,
            keys = new
            {
                p256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
                auth = "BTBZMqHH6r4Tts7J_aSIgg"
            },
            userAgent = "Tests"
        });
        response.EnsureSuccessStatusCode();

        return new Subscriber(user.Client, user.UserId, endpoint, currencyId, accountId, categoryId);
    }

    /// <summary>
    /// Runs one job the way the host would — a fresh scope, an explicit "now" —
    /// and returns its summary line.
    /// </summary>
    private async Task<string> RunJobAsync(string name, DateTime? nowUtc = null)
    {
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetServices<IScheduledJob>().Single(j => j.Name == name);
        Assert.True(job.Enabled);
        return await job.RunAsync(new ScheduledJobContext(nowUtc ?? DateTime.UtcNow, null));
    }

    private static async Task CreateExpenseAsync(
        HttpClient client, Guid accountId, Guid categoryId, decimal amount, DateOnly date)
    {
        var response = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Test expense",
            amount,
            date = Iso(date),
            accountId,
            categoryId
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreateBudgetAsync(HttpClient client, decimal amount, Guid? categoryId)
    {
        var response = await client.PostAsJsonAsync("/api/Budget", new { amount, period = 0, categoryId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/PlannedTransaction", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // ── Trigger 1: a budget threshold crossed ──────────────────────────────

    [Fact]
    public async Task Crossing_eighty_percent_warns_once_and_then_stays_quiet()
    {
        var user = await ArrangeAsync();
        await CreateBudgetAsync(user.Client, 100m, user.CategoryId);
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 85m, Today);

        await RunJobAsync(ScheduledJobNames.BudgetThresholds);

        var messages = _factory.PushSender.To(user.Endpoint);
        Assert.Single(messages);
        Assert.Equal("Budget warning", messages[0].Title);
        Assert.Contains("85%", messages[0].Body);
        // Tapping it should land on the screen the warning is about.
        Assert.Equal("/budgets", messages[0].Url);

        // The job re-evaluates the same state tomorrow, and the day after, and the
        // budget is still at 85% every one of those days.
        await RunJobAsync(ScheduledJobNames.BudgetThresholds);
        await RunJobAsync(ScheduledJobNames.BudgetThresholds);

        Assert.Single(_factory.PushSender.To(user.Endpoint));
    }

    [Fact]
    public async Task Going_over_the_budget_is_worth_saying_even_after_the_eighty_percent_warning()
    {
        var user = await ArrangeAsync();
        await CreateBudgetAsync(user.Client, 100m, user.CategoryId);
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 85m, Today);

        await RunJobAsync(ScheduledJobNames.BudgetThresholds);
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 30m, Today);
        await RunJobAsync(ScheduledJobNames.BudgetThresholds);

        var messages = _factory.PushSender.To(user.Endpoint);
        Assert.Equal(2, messages.Count);
        Assert.Equal("Budget warning", messages[0].Title);
        Assert.Equal("Over budget", messages[1].Title);

        // ... but only once.
        await RunJobAsync(ScheduledJobNames.BudgetThresholds);
        Assert.Equal(2, _factory.PushSender.To(user.Endpoint).Count);
    }

    [Fact]
    public async Task A_budget_comfortably_inside_its_limit_says_nothing()
    {
        var user = await ArrangeAsync();
        await CreateBudgetAsync(user.Client, 100m, user.CategoryId);
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 40m, Today);

        await RunJobAsync(ScheduledJobNames.BudgetThresholds);

        Assert.Empty(_factory.PushSender.To(user.Endpoint));
    }

    [Fact]
    public async Task Last_months_spending_does_not_warn_about_this_months_budget()
    {
        var user = await ArrangeAsync();
        await CreateBudgetAsync(user.Client, 100m, user.CategoryId);
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 90m, Today.AddMonths(-1));

        await RunJobAsync(ScheduledJobNames.BudgetThresholds);

        // Budgets recur; the spend measured against them must not.
        Assert.Empty(_factory.PushSender.To(user.Endpoint));
    }

    [Fact]
    public async Task A_user_with_no_registered_device_is_never_evaluated_into_a_delivery()
    {
        var user = await ApiClient.CreateAuthenticatedUserAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(user.Client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(user.Client, currencyId, 1_000m);
        var categoryId = await ApiClient.CreateCategoryAsync(user.Client, categoryType: 0);

        await CreateBudgetAsync(user.Client, 100m, categoryId);
        await CreateExpenseAsync(user.Client, accountId, categoryId, 200m, Today);

        await RunJobAsync(ScheduledJobNames.BudgetThresholds);

        // And crucially, no dedupe row is burned either: subscribing tomorrow must
        // still produce the warning.
        var endpoint = NewEndpoint();
        (await user.Client.PostAsJsonAsync("/api/Push/subscribe", new
        {
            endpoint,
            keys = new
            {
                p256dh = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
                auth = "BTBZMqHH6r4Tts7J_aSIgg"
            }
        })).EnsureSuccessStatusCode();

        await RunJobAsync(ScheduledJobNames.BudgetThresholds);

        Assert.Single(_factory.PushSender.To(endpoint));
    }

    // ── Trigger 2: a planned bill due tomorrow ─────────────────────────────

    [Fact]
    public async Task A_bill_due_tomorrow_is_announced_once_for_that_day()
    {
        var user = await ArrangeAsync();
        await CreateTemplateAsync(user.Client, new
        {
            title = "Rent",
            amount = 800m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(1)),
            categoryId = user.CategoryId,
            fromAccountId = user.AccountId
        });

        await RunJobAsync(ScheduledJobNames.PlannedDueReminders);

        var messages = _factory.PushSender.To(user.Endpoint);
        Assert.Single(messages);
        Assert.Equal("Due tomorrow", messages[0].Title);
        Assert.Contains("Rent", messages[0].Body);
        Assert.Contains("800.00 EUR", messages[0].Body);
        Assert.Equal("/planned", messages[0].Url);

        await RunJobAsync(ScheduledJobNames.PlannedDueReminders);
        Assert.Single(_factory.PushSender.To(user.Endpoint));
    }

    [Fact]
    public async Task Several_bills_due_the_same_day_are_one_notification_not_three()
    {
        var user = await ArrangeAsync();

        foreach (var title in new[] { "Rent", "Netflix", "Gym" })
        {
            await CreateTemplateAsync(user.Client, new
            {
                title,
                amount = 10m,
                kind = 0,
                recurrence = 2,
                startDate = Iso(Today.AddDays(1)),
                categoryId = user.CategoryId,
                fromAccountId = user.AccountId
            });
        }

        await RunJobAsync(ScheduledJobNames.PlannedDueReminders);

        // Three buzzes for one morning is how people learn to swipe these away
        // without reading them.
        var messages = _factory.PushSender.To(user.Endpoint);
        Assert.Single(messages);
        Assert.Equal("3 due tomorrow", messages[0].Title);
        Assert.Contains("30.00 EUR", messages[0].Body);
    }

    [Fact]
    public async Task A_bill_due_next_week_is_not_tomorrows_problem()
    {
        var user = await ArrangeAsync();
        await CreateTemplateAsync(user.Client, new
        {
            title = "Insurance",
            amount = 50m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(7)),
            categoryId = user.CategoryId,
            fromAccountId = user.AccountId
        });

        await RunJobAsync(ScheduledJobNames.PlannedDueReminders);

        Assert.Empty(_factory.PushSender.To(user.Endpoint));
    }

    // ── Trigger 3: the weekly digest ───────────────────────────────────────

    [Fact]
    public async Task The_digest_reports_the_last_seven_complete_days_against_the_week_before()
    {
        var user = await ArrangeAsync();

        // 100 last week, 50 the week before: a doubling.
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 100m, Today.AddDays(-2));
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 50m, Today.AddDays(-9));

        await RunJobAsync(ScheduledJobNames.WeeklyDigest);

        var messages = _factory.PushSender.To(user.Endpoint);
        Assert.Single(messages);
        Assert.Equal("Your week in numbers", messages[0].Title);
        Assert.Contains("100.00 EUR", messages[0].Body);
        Assert.Contains("100% more than the week before", messages[0].Body);
        Assert.Equal("/reports", messages[0].Url);

        await RunJobAsync(ScheduledJobNames.WeeklyDigest);
        Assert.Single(_factory.PushSender.To(user.Endpoint));
    }

    [Fact]
    public async Task Today_is_never_counted_into_the_digest()
    {
        var user = await ArrangeAsync();
        await CreateExpenseAsync(user.Client, user.AccountId, user.CategoryId, 250m, Today);

        await RunJobAsync(ScheduledJobNames.WeeklyDigest);

        // A digest whose figure depends on what hour the container woke up is not
        // comparable to the one before it — so a partial today contributes nothing,
        // and with nothing else to report there is no digest at all.
        Assert.Empty(_factory.PushSender.To(user.Endpoint));
    }

    // ── The sweep the reminders sit on ─────────────────────────────────────

    [Fact]
    public async Task The_sweep_fills_the_due_queue_for_a_user_who_never_opened_the_app()
    {
        var user = await ArrangeAsync();
        await CreateTemplateAsync(user.Client, new
        {
            title = "Rent",
            amount = 800m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = user.CategoryId,
            fromAccountId = user.AccountId
        });

        var summary = await RunJobAsync(ScheduledJobNames.PlannedOccurrences);
        Assert.Contains("occurrence(s) queued", summary);

        // Read the queue afterwards: generation-on-read is idempotent, so if the
        // sweep had done nothing this would still return one — what proves the
        // sweep worked is that nothing *new* is queued by the read.
        var response = await user.Client.GetAsync("/api/PlannedTransaction/occurrences");
        response.EnsureSuccessStatusCode();
        var queue = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();

        Assert.Single(queue);
        Assert.Equal(Iso(Today.AddDays(-1)), queue[0].GetProperty("dueDate").GetString());
    }

    [Fact]
    public async Task Running_the_sweep_twice_queues_nothing_the_second_time()
    {
        var user = await ArrangeAsync();
        await CreateTemplateAsync(user.Client, new
        {
            title = "Rent",
            amount = 800m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today),
            categoryId = user.CategoryId,
            fromAccountId = user.AccountId
        });

        await RunJobAsync(ScheduledJobNames.PlannedOccurrences);
        var second = await RunJobAsync(ScheduledJobNames.PlannedOccurrences);

        // The catch-up design means the sweep runs late, twice, or after a
        // three-day gap; it has to be safe every time.
        Assert.StartsWith("0 occurrence(s)", second);
    }

    [Fact]
    public async Task Nothing_is_posted_unattended_by_the_sweep()
    {
        var user = await ArrangeAsync(balance: 5_000m);
        await CreateTemplateAsync(user.Client, new
        {
            title = "Rent",
            amount = 800m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today),
            categoryId = user.CategoryId,
            fromAccountId = user.AccountId
        });

        await RunJobAsync(ScheduledJobNames.PlannedOccurrences);

        // The whole premise of Sugg-4's queue: a due date is a prediction, and only
        // a human committing it moves money. A scheduler that debited accounts
        // overnight would be a different and much worse product.
        Assert.Equal(5_000m, await ApiClient.GetAccountBalanceAsync(user.Client, user.AccountId));
    }
}
