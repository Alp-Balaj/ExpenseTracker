using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Sugg-4: planned transactions and the due queue they feed.
//
// Driven entirely through the HTTP API, like the other invariant suites — which
// matters more here than usual, because generating the queue is a side effect of
// reading it. A test that poked the DbContext directly would never exercise the
// path the app actually takes.
//
// Dates are relative to UTC today (what the service measures "due" against)
// rather than hardcoded, so the suite does not start failing in February.
[Collection(IntegrationTestCollection.Name)]
public sealed class PlannedTransactionTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PlannedTransactionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd");

    private sealed record Fixture(
        HttpClient Client, Guid CurrencyId, Guid AccountId, Guid ExpenseCategoryId);

    private async Task<Fixture> ArrangeAsync(decimal balance = 10_000m)
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "EUR");
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance);
        var categoryId = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
        return new Fixture(client, currencyId, accountId, categoryId);
    }

    private static async Task<Guid> CreateTemplateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/PlannedTransaction", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> QueueAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/PlannedTransaction/occurrences");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
    }

    private static async Task<HttpResponseMessage> CommitAsync(
        HttpClient client, Guid occurrenceId, object? body = null) =>
        await client.PostAsJsonAsync(
            $"/api/PlannedTransaction/occurrences/{occurrenceId}/commit", body ?? new { });

    // ── Generation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Reading_the_queue_twice_does_not_queue_the_same_period_twice()
    {
        var fx = await ArrangeAsync();
        await CreateTemplateAsync(fx.Client, new
        {
            title = "Rent",
            amount = 800m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var first = await QueueAsync(fx.Client);
        var second = await QueueAsync(fx.Client);

        // Generation runs on every read, so idempotency is not a nicety here —
        // it is the only thing between the user and a duplicate rent every time
        // the page refreshes.
        Assert.Single(first);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(
            first[0].GetProperty("id").GetGuid(),
            second[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A_backdated_monthly_template_catches_up_one_occurrence_per_missed_month()
    {
        var fx = await ArrangeAsync();
        var firstOfThisMonth = new DateOnly(Today.Year, Today.Month, 1);

        await CreateTemplateAsync(fx.Client, new
        {
            title = "Catch-up rent",
            amount = 100m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(firstOfThisMonth.AddMonths(-2)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var queue = await QueueAsync(fx.Client);

        // Three months back means three separate things to deal with — not one
        // vague "overdue". This is the whole reason occurrences are rows.
        Assert.Equal(3, queue.Count);
        Assert.Equal(
            [Iso(firstOfThisMonth.AddMonths(-2)), Iso(firstOfThisMonth.AddMonths(-1)), Iso(firstOfThisMonth)],
            queue.Select(o => o.GetProperty("dueDate").GetString()).ToList());
    }

    [Fact]
    public async Task A_paused_template_queues_nothing_new()
    {
        var fx = await ArrangeAsync();
        await CreateTemplateAsync(fx.Client, new
        {
            title = "Paused",
            amount = 50m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId,
            isActive = false
        });

        Assert.Empty(await QueueAsync(fx.Client));
    }

    [Fact]
    public async Task Occurrences_stop_at_the_end_date()
    {
        var fx = await ArrangeAsync();
        var firstOfThisMonth = new DateOnly(Today.Year, Today.Month, 1);

        await CreateTemplateAsync(fx.Client, new
        {
            title = "Finished",
            amount = 60m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(firstOfThisMonth.AddMonths(-2)),
            endDate = Iso(firstOfThisMonth.AddMonths(-2)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        Assert.Single(await QueueAsync(fx.Client));
    }

    [Fact]
    public async Task Nothing_is_queued_before_it_is_due()
    {
        var fx = await ArrangeAsync();
        await CreateTemplateAsync(fx.Client, new
        {
            title = "Next week",
            amount = 70m,
            kind = 0,
            recurrence = 0,
            startDate = Iso(Today.AddDays(7)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        Assert.Empty(await QueueAsync(fx.Client));
    }

    [Fact]
    public async Task The_queue_never_reaches_across_users()
    {
        var owner = await ArrangeAsync();
        await CreateTemplateAsync(owner.Client, new
        {
            title = "Private rent",
            amount = 900m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = owner.ExpenseCategoryId,
            fromAccountId = owner.AccountId
        });
        var ownersQueue = await QueueAsync(owner.Client);
        Assert.Single(ownersQueue);

        var intruder = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        Assert.Empty(await QueueAsync(intruder));

        // Someone else's occurrence is a 404, never a 403 — the response must not
        // confirm that the id exists at all.
        var stolen = await CommitAsync(intruder, ownersQueue[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
    }

    // ── Committing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Committing_posts_an_expense_and_drops_the_balance()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        await CreateTemplateAsync(fx.Client, new
        {
            title = "Netflix",
            amount = 9.99m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var occurrence = (await QueueAsync(fx.Client))[0];
        (await CommitAsync(fx.Client, occurrence.GetProperty("id").GetGuid())).EnsureSuccessStatusCode();

        Assert.Equal(990.01m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));

        var expenses = await fx.Client.GetFromJsonAsync<JsonElement>("/api/Expense");
        var posted = expenses.EnumerateArray().Single(e => e.GetProperty("title").GetString() == "Netflix");
        Assert.Equal(9.99m, posted.GetProperty("amount").GetDecimal());
        // Dated to the due date by default — the prediction stands unless the
        // user says otherwise.
        Assert.Equal(Iso(Today.AddDays(-1)), posted.GetProperty("date").GetString());

        // And it leaves the queue.
        Assert.Empty(await QueueAsync(fx.Client));
    }

    [Fact]
    public async Task Committing_uses_the_amount_account_and_date_the_user_confirms()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        // The phone bill sometimes comes out of cash instead.
        var cashAccountId = await ApiClient.CreateAccountAsync(fx.Client, fx.CurrencyId, balance: 500m);

        await CreateTemplateAsync(fx.Client, new
        {
            title = "Phone",
            amount = 30m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-3)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var occurrence = (await QueueAsync(fx.Client))[0];
        (await CommitAsync(fx.Client, occurrence.GetProperty("id").GetGuid(), new
        {
            amount = 41.50m,
            date = Iso(Today),
            fromAccountId = cashAccountId
        })).EnsureSuccessStatusCode();

        // The default account is untouched and the one actually used is debited.
        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
        Assert.Equal(458.50m, await ApiClient.GetAccountBalanceAsync(fx.Client, cashAccountId));

        var expenses = await fx.Client.GetFromJsonAsync<JsonElement>("/api/Expense");
        var posted = expenses.EnumerateArray().Single(e => e.GetProperty("title").GetString() == "Phone");
        Assert.Equal(41.50m, posted.GetProperty("amount").GetDecimal());
        Assert.Equal(Iso(Today), posted.GetProperty("date").GetString());
    }

    [Fact]
    public async Task Committing_carries_the_account_over_but_not_the_amount()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var cashAccountId = await ApiClient.CreateAccountAsync(fx.Client, fx.CurrencyId, balance: 500m);
        var firstOfThisMonth = new DateOnly(Today.Year, Today.Month, 1);

        var templateId = await CreateTemplateAsync(fx.Client, new
        {
            title = "Electricity",
            amount = 60m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(firstOfThisMonth.AddMonths(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var queue = await QueueAsync(fx.Client);
        (await CommitAsync(fx.Client, queue[0].GetProperty("id").GetGuid(), new
        {
            amount = 95m,
            fromAccountId = cashAccountId
        })).EnsureSuccessStatusCode();

        var template = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/PlannedTransaction/{templateId}");

        // The account carries over, so next month pre-fills with cash rather than
        // offering the bank account again...
        Assert.Equal(cashAccountId, template.GetProperty("fromAccountId").GetGuid());
        // ...but one cold month must not redefine what electricity costs.
        Assert.Equal(60m, template.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Committing_can_be_told_to_update_the_template_amount()
    {
        var fx = await ArrangeAsync();
        var templateId = await CreateTemplateAsync(fx.Client, new
        {
            title = "Gym",
            amount = 25m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var occurrence = (await QueueAsync(fx.Client))[0];
        (await CommitAsync(fx.Client, occurrence.GetProperty("id").GetGuid(), new
        {
            amount = 29m,
            updateTemplateAmount = true
        })).EnsureSuccessStatusCode();

        var template = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/PlannedTransaction/{templateId}");
        Assert.Equal(29m, template.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Committing_twice_is_refused_and_posts_nothing_the_second_time()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        await CreateTemplateAsync(fx.Client, new
        {
            title = "Rent",
            amount = 800m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var occurrenceId = (await QueueAsync(fx.Client))[0].GetProperty("id").GetGuid();
        (await CommitAsync(fx.Client, occurrenceId)).EnsureSuccessStatusCode();

        var second = await CommitAsync(fx.Client, occurrenceId);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        // The balance moved exactly once. Double-posting rent is the failure this
        // whole design exists to prevent.
        Assert.Equal(200m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
    }

    [Fact]
    public async Task Skipping_posts_nothing_and_leaves_the_balance_alone()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        await CreateTemplateAsync(fx.Client, new
        {
            title = "Did not happen",
            amount = 120m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        var occurrenceId = (await QueueAsync(fx.Client))[0].GetProperty("id").GetGuid();
        (await fx.Client.PostAsJsonAsync(
            $"/api/PlannedTransaction/occurrences/{occurrenceId}/skip", new { })).EnsureSuccessStatusCode();

        Assert.Equal(1_000m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
        Assert.Empty(await QueueAsync(fx.Client));

        // ...and a skipped occurrence cannot then be committed.
        Assert.Equal(HttpStatusCode.BadRequest, (await CommitAsync(fx.Client, occurrenceId)).StatusCode);
    }

    [Fact]
    public async Task Committing_a_planned_income_raises_the_balance()
    {
        var fx = await ArrangeAsync(balance: 100m);
        var incomeCategoryId = await ApiClient.CreateCategoryAsync(fx.Client, categoryType: 1);

        await CreateTemplateAsync(fx.Client, new
        {
            title = "Salary",
            amount = 2_500m,
            kind = 1,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = incomeCategoryId,
            toAccountId = fx.AccountId
        });

        var occurrenceId = (await QueueAsync(fx.Client))[0].GetProperty("id").GetGuid();
        (await CommitAsync(fx.Client, occurrenceId)).EnsureSuccessStatusCode();

        Assert.Equal(2_600m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));

        var incomes = await fx.Client.GetFromJsonAsync<JsonElement>("/api/Income");
        Assert.Single(incomes.EnumerateArray());
    }

    [Fact]
    public async Task Committing_a_planned_transfer_moves_both_balances_and_books_the_contribution()
    {
        var fx = await ArrangeAsync(balance: 1_000m);
        var savingsAccountId = await ApiClient.CreateAccountAsync(fx.Client, fx.CurrencyId, balance: 0m);
        var goalCategoryId = await ApiClient.CreateCategoryAsync(fx.Client, categoryType: 2);

        var goalResponse = await fx.Client.PostAsJsonAsync("/api/SavingGoal", new
        {
            title = "Rainy day",
            targetAmount = 5_000m,
            savedAmount = 0m,
            deadline = Iso(Today.AddYears(1)),
            accountId = savingsAccountId,
            categoryId = goalCategoryId
        });
        goalResponse.EnsureSuccessStatusCode();
        var goalId = (await goalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await CreateTemplateAsync(fx.Client, new
        {
            title = "Monthly savings",
            amount = 200m,
            kind = 2,
            recurrence = 2,
            startDate = Iso(Today.AddDays(-1)),
            fromAccountId = fx.AccountId,
            toAccountId = savingsAccountId,
            savingGoalId = goalId
        });

        var occurrenceId = (await QueueAsync(fx.Client))[0].GetProperty("id").GetGuid();
        (await CommitAsync(fx.Client, occurrenceId)).EnsureSuccessStatusCode();

        Assert.Equal(800m, await ApiClient.GetAccountBalanceAsync(fx.Client, fx.AccountId));
        Assert.Equal(200m, await ApiClient.GetAccountBalanceAsync(fx.Client, savingsAccountId));
        // Goes through the same posting service TransferController uses, so the
        // savings goal is credited exactly as a hand-entered transfer would.
        Assert.Equal(200m, await ApiClient.GetSavingGoalSavedAmountAsync(fx.Client, goalId));

        var transfers = await fx.Client.GetFromJsonAsync<JsonElement>("/api/Transfer");
        Assert.Single(transfers.EnumerateArray());
    }

    [Fact]
    public async Task Committing_without_an_account_is_refused_rather_than_guessed()
    {
        var fx = await ArrangeAsync();
        // No default account: a pure reminder until the user says where it came from.
        await CreateTemplateAsync(fx.Client, new
        {
            title = "Dentist",
            amount = 150m,
            kind = 0,
            recurrence = 0,
            startDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId
        });

        var occurrenceId = (await QueueAsync(fx.Client))[0].GetProperty("id").GetGuid();

        var refused = await CommitAsync(fx.Client, occurrenceId);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // Still pending, so naming the account puts it right.
        Assert.Single(await QueueAsync(fx.Client));
        (await CommitAsync(fx.Client, occurrenceId, new { fromAccountId = fx.AccountId }))
            .EnsureSuccessStatusCode();
    }

    // ── Validation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_planned_expense_must_use_an_expense_category()
    {
        var fx = await ArrangeAsync();
        var incomeCategoryId = await ApiClient.CreateCategoryAsync(fx.Client, categoryType: 1);

        // The point of sharing the Expense category axis is that a committed
        // occurrence lands in the budget the user already has. A planned expense
        // filed under an income category could never do that.
        var response = await fx.Client.PostAsJsonAsync("/api/PlannedTransaction", new
        {
            title = "Wrong axis",
            amount = 10m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today),
            categoryId = incomeCategoryId,
            fromAccountId = fx.AccountId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_planned_transfer_needs_two_different_accounts_and_no_category()
    {
        var fx = await ArrangeAsync();

        var withCategory = await fx.Client.PostAsJsonAsync("/api/PlannedTransaction", new
        {
            title = "Categorised transfer",
            amount = 10m,
            kind = 2,
            recurrence = 2,
            startDate = Iso(Today),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId,
            toAccountId = fx.AccountId
        });
        Assert.Equal(HttpStatusCode.BadRequest, withCategory.StatusCode);

        var sameAccount = await fx.Client.PostAsJsonAsync("/api/PlannedTransaction", new
        {
            title = "Circular transfer",
            amount = 10m,
            kind = 2,
            recurrence = 2,
            startDate = Iso(Today),
            fromAccountId = fx.AccountId,
            toAccountId = fx.AccountId
        });
        Assert.Equal(HttpStatusCode.BadRequest, sameAccount.StatusCode);
    }

    [Fact]
    public async Task An_end_date_before_the_start_date_is_refused()
    {
        var fx = await ArrangeAsync();

        var response = await fx.Client.PostAsJsonAsync("/api/PlannedTransaction", new
        {
            title = "Backwards",
            amount = 10m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(Today),
            endDate = Iso(Today.AddDays(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_template_reports_what_is_pending_and_when_it_next_comes_round()
    {
        var fx = await ArrangeAsync();
        var firstOfThisMonth = new DateOnly(Today.Year, Today.Month, 1);

        var templateId = await CreateTemplateAsync(fx.Client, new
        {
            title = "Rent",
            amount = 800m,
            kind = 0,
            recurrence = 2,
            startDate = Iso(firstOfThisMonth.AddMonths(-1)),
            categoryId = fx.ExpenseCategoryId,
            fromAccountId = fx.AccountId
        });

        // Reading the list is what generates; the counts come back with it.
        var list = await fx.Client.GetFromJsonAsync<JsonElement>("/api/PlannedTransaction");
        var template = list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == templateId);

        Assert.Equal(2, template.GetProperty("pendingCount").GetInt32());
        Assert.Equal(
            Iso(firstOfThisMonth.AddMonths(1)),
            template.GetProperty("nextDueDate").GetString());
    }
}
