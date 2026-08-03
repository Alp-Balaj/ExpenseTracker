using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Stream R. The five sentences the feature is, pinned one at a time:
//
//   1. Gross is account truth      -> a 60/20 split still debits 60.
//   3. A settlement is not income  -> settling credits an account and closes
//                                     part of the receivable; the income list
//                                     never moves.
//   4. The receivable is forward-looking -> open = expected - settled, and it
//                                     leaves the default list when it reaches
//                                     zero.
//
// Plus the bounds, the four business-rule 400s, the Fix-8 ownership rule, and a
// regression pin that an expense with no split behaves exactly as it did before
// any of this existed.
[Collection(IntegrationTestCollection.Name)]
public sealed class ReimbursementTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ReimbursementTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Sentence 1: the balance mirroring is untouched by the split. The account
    // paid the whole bill, so the account is down the whole bill.
    [Fact]
    public async Task A_split_expense_debits_the_gross_amount_not_the_users_share()
    {
        var world = await SeedAsync();

        await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 20m);

        Assert.Equal(940m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
    }

    [Fact]
    public async Task A_split_expense_reports_its_three_reimbursement_figures()
    {
        var world = await SeedAsync();

        var expenseId = await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 20m);

        var detail = await world.Client.GetFromJsonAsync<JsonElement>($"/api/Expense/{expenseId}");
        Assert.Equal(60m, detail.GetProperty("amount").GetDecimal());
        Assert.Equal(20m, detail.GetProperty("expectedReimbursement").GetDecimal());
        Assert.Equal(0m, detail.GetProperty("settledReimbursement").GetDecimal());
        Assert.Equal(20m, detail.GetProperty("openReimbursement").GetDecimal());

        var list = await world.Client.GetFromJsonAsync<JsonElement>("/api/Expense");
        var listed = list.EnumerateArray().Single();
        Assert.Equal(20m, listed.GetProperty("expectedReimbursement").GetDecimal());
        Assert.Equal(20m, listed.GetProperty("openReimbursement").GetDecimal());
    }

    // Sentences 3 and 4 together: two instalments land on accounts the user
    // picks, the receivable closes, and nothing anywhere claims income happened.
    [Fact]
    public async Task Two_partial_settlements_close_the_receivable_without_creating_income()
    {
        var world = await SeedAsync();

        // One real income, so "unchanged" is a claim about a non-empty number.
        (await world.Client.PostAsJsonAsync("/api/Income", new
        {
            amount = 200m,
            date = "2026-07-01",
            accountId = world.Account,
            categoryId = world.IncomeCategory
        })).EnsureSuccessStatusCode();

        Assert.Equal(200m, await IncomeTotalAsync(world.Client));
        Assert.Equal(1200m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));

        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        Assert.Equal(1100m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));

        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);

        // First instalment onto the second account: a settlement may land
        // anywhere the user owns, not just where the bill was paid from.
        var first = await SettleAsync(world.Client, reimbursementId, world.OtherAccount, 25m);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var afterFirst = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(25m, afterFirst.GetProperty("settledAmount").GetDecimal());
        Assert.Equal(15m, afterFirst.GetProperty("openAmount").GetDecimal());

        Assert.Equal(525m, await ApiClient.GetAccountBalanceAsync(world.Client, world.OtherAccount));
        Assert.Equal(200m, await IncomeTotalAsync(world.Client));

        // Second instalment back onto the paying account, closing it out.
        var second = await SettleAsync(world.Client, reimbursementId, world.Account, 15m);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var afterSecond = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40m, afterSecond.GetProperty("settledAmount").GetDecimal());
        Assert.Equal(0m, afterSecond.GetProperty("openAmount").GetDecimal());
        Assert.Equal(2, afterSecond.GetProperty("settlements").GetArrayLength());

        Assert.Equal(1115m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
        Assert.Equal(525m, await ApiClient.GetAccountBalanceAsync(world.Client, world.OtherAccount));

        // Income never saw any of it.
        Assert.Equal(200m, await IncomeTotalAsync(world.Client));

        // A closed receivable leaves the default view but is still on record.
        var open = await world.Client.GetFromJsonAsync<JsonElement>("/api/Reimbursement");
        Assert.Empty(open.EnumerateArray());

        var all = await world.Client.GetFromJsonAsync<JsonElement>("/api/Reimbursement?includeSettled=true");
        Assert.Single(all.EnumerateArray());

        var expense = await world.Client.GetFromJsonAsync<JsonElement>($"/api/Expense/{expenseId}");
        Assert.Equal(40m, expense.GetProperty("settledReimbursement").GetDecimal());
        Assert.Equal(0m, expense.GetProperty("openReimbursement").GetDecimal());
    }

    // The one that would leave money behind if the delete path only knew about
    // the expense's own debit.
    [Fact]
    public async Task Deleting_a_settled_split_expense_restores_every_balance()
    {
        var world = await SeedAsync();

        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);

        (await SettleAsync(world.Client, reimbursementId, world.OtherAccount, 25m)).EnsureSuccessStatusCode();
        (await SettleAsync(world.Client, reimbursementId, world.Account, 15m)).EnsureSuccessStatusCode();

        var delete = await world.Client.DeleteAsync($"/api/Expense/{expenseId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
        Assert.Equal(500m, await ApiClient.GetAccountBalanceAsync(world.Client, world.OtherAccount));

        var all = await world.Client.GetFromJsonAsync<JsonElement>("/api/Reimbursement?includeSettled=true");
        Assert.Empty(all.EnumerateArray());
    }

    [Fact]
    public async Task Removing_an_unsettled_reimbursement_leaves_the_expense_alone()
    {
        var world = await SeedAsync();

        var expenseId = await CreateExpenseAsync(world, amount: 60m, expectedReimbursement: 20m);

        var put = await world.Client.PutAsJsonAsync($"/api/Expense/{expenseId}",
            ExpenseBody(world, amount: 60m, expectedReimbursement: null));
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var expense = await world.Client.GetFromJsonAsync<JsonElement>($"/api/Expense/{expenseId}");
        Assert.Equal(JsonValueKind.Null, expense.GetProperty("expectedReimbursement").ValueKind);
        Assert.Equal(0m, expense.GetProperty("openReimbursement").GetDecimal());

        // The bill itself never moved.
        Assert.Equal(940m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));

        var open = await world.Client.GetFromJsonAsync<JsonElement>("/api/Reimbursement?includeSettled=true");
        Assert.Empty(open.EnumerateArray());
    }

    #region The four documented business-rule 400s

    [Fact]
    public async Task Lowering_the_amount_below_the_expected_reimbursement_is_rejected()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);

        var put = await world.Client.PutAsJsonAsync($"/api/Expense/{expenseId}",
            ExpenseBody(world, amount: 30m, expectedReimbursement: 40m));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("Amount cannot be less than the expected reimbursement.",
            await put.Content.ReadAsStringAsync());

        // Rolled back whole: neither the amount nor the balance moved.
        Assert.Equal(900m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
    }

    [Fact]
    public async Task Raising_the_expected_reimbursement_above_the_amount_is_rejected()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);

        var put = await world.Client.PutAsJsonAsync($"/api/Expense/{expenseId}",
            ExpenseBody(world, amount: 100m, expectedReimbursement: 150m));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("Expected reimbursement cannot exceed the expense amount.",
            await put.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Creating_an_expense_expecting_back_more_than_it_cost_is_rejected()
    {
        var world = await SeedAsync();

        var post = await world.Client.PostAsJsonAsync("/api/Expense",
            ExpenseBody(world, amount: 40m, expectedReimbursement: 60m));

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Contains("Expected reimbursement cannot exceed the expense amount.",
            await post.Content.ReadAsStringAsync());

        // The transaction rolled back, so the debit never happened either.
        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
    }

    [Fact]
    public async Task Lowering_the_expected_reimbursement_below_what_is_settled_is_rejected()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);

        (await SettleAsync(world.Client, reimbursementId, world.Account, 25m)).EnsureSuccessStatusCode();

        var put = await world.Client.PutAsJsonAsync($"/api/Expense/{expenseId}",
            ExpenseBody(world, amount: 100m, expectedReimbursement: 10m));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("Expected reimbursement cannot be less than the amount already settled.",
            await put.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Removing_a_reimbursement_that_has_settlements_is_rejected()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);

        (await SettleAsync(world.Client, reimbursementId, world.Account, 25m)).EnsureSuccessStatusCode();

        var put = await world.Client.PutAsJsonAsync($"/api/Expense/{expenseId}",
            ExpenseBody(world, amount: 100m, expectedReimbursement: null));

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("Cannot remove a reimbursement that has settlements.",
            await put.Content.ReadAsStringAsync());

        // Still there, still settled.
        var expense = await world.Client.GetFromJsonAsync<JsonElement>($"/api/Expense/{expenseId}");
        Assert.Equal(40m, expense.GetProperty("expectedReimbursement").GetDecimal());
        Assert.Equal(25m, expense.GetProperty("settledReimbursement").GetDecimal());
    }

    #endregion

    [Fact]
    public async Task Settling_more_than_is_open_is_rejected()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);

        var response = await SettleAsync(world.Client, reimbursementId, world.Account, 41m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(900m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
    }

    // Fix-8: a foreign account has to be indistinguishable from one that was
    // never there. Same status, same sentence, and the owner's balance untouched.
    [Fact]
    public async Task Settling_onto_another_users_account_looks_exactly_like_a_missing_one()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);

        var stranger = await SeedAsync();

        var foreign = await SettleAsync(world.Client, reimbursementId, stranger.Account, 10m);
        var missing = await SettleAsync(world.Client, reimbursementId, Guid.NewGuid(), 10m);

        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(missing.StatusCode, foreign.StatusCode);
        Assert.Equal(
            await missing.Content.ReadAsStringAsync(),
            await foreign.Content.ReadAsStringAsync());

        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(stranger.Client, stranger.Account));
    }

    [Fact]
    public async Task Settling_another_users_reimbursement_is_not_found()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);

        var intruder = await SeedAsync();

        var response = await SettleAsync(intruder.Client, reimbursementId, intruder.Account, 10m);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(intruder.Client, intruder.Account));
    }

    [Fact]
    public async Task A_reimbursement_of_another_user_is_not_listed()
    {
        var world = await SeedAsync();
        await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);

        var stranger = await SeedAsync();

        var listed = await stranger.Client.GetFromJsonAsync<JsonElement>("/api/Reimbursement");
        Assert.Empty(listed.EnumerateArray());
    }

    // The regression pin: nothing about an ordinary expense changed.
    [Fact]
    public async Task An_unsplit_expense_behaves_exactly_as_before()
    {
        var world = await SeedAsync();

        var create = await world.Client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Groceries",
            amount = 30m,
            date = "2026-07-10",
            accountId = world.Account,
            categoryId = world.Category
        });
        create.EnsureSuccessStatusCode();
        var expenseId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(970m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));

        var expense = await world.Client.GetFromJsonAsync<JsonElement>($"/api/Expense/{expenseId}");
        Assert.Equal(30m, expense.GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, expense.GetProperty("expectedReimbursement").ValueKind);
        Assert.Equal(0m, expense.GetProperty("settledReimbursement").GetDecimal());
        Assert.Equal(0m, expense.GetProperty("openReimbursement").GetDecimal());

        // No receivable was invented for it.
        var listed = await world.Client.GetFromJsonAsync<JsonElement>("/api/Reimbursement?includeSettled=true");
        Assert.Empty(listed.EnumerateArray());

        // Edit and delete still move the balance by the amount and nothing else.
        (await world.Client.PutAsJsonAsync($"/api/Expense/{expenseId}", new
        {
            title = "Groceries",
            amount = 50m,
            date = "2026-07-10",
            accountId = world.Account,
            categoryId = world.Category
        })).EnsureSuccessStatusCode();
        Assert.Equal(950m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));

        (await world.Client.DeleteAsync($"/api/Expense/{expenseId}")).EnsureSuccessStatusCode();
        Assert.Equal(1000m, await ApiClient.GetAccountBalanceAsync(world.Client, world.Account));
    }

    // The settlement's account foreign key is RESTRICT, exactly like an expense's.
    // Deleting the user has to resolve both new tables before the accounts they
    // point at, or delete-my-data would fail at the database.
    [Fact]
    public async Task Deleting_the_user_takes_the_reimbursement_and_its_settlements()
    {
        var world = await SeedAsync();
        var expenseId = await CreateExpenseAsync(world, amount: 100m, expectedReimbursement: 40m);
        var reimbursementId = await FindReimbursementIdAsync(world.Client, expenseId);
        (await SettleAsync(world.Client, reimbursementId, world.OtherAccount, 25m)).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var userId = await db.Reimbursements
            .Where(r => r.Id == reimbursementId)
            .Select(r => r.UserId)
            .SingleAsync();

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.False(await db.Reimbursements.AnyAsync(r => r.UserId == userId));
        Assert.False(await db.ReimbursementSettlements.AnyAsync(s => s.UserId == userId));
    }

    #region Helpers

    private sealed record World(
        HttpClient Client, Guid Currency, Guid Account, Guid OtherAccount, Guid Category, Guid IncomeCategory);

    private async Task<World> SeedAsync()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currency = await ApiClient.CreateCurrencyAsync(client);
        var account = await ApiClient.CreateAccountAsync(client, currency, balance: 1000m);
        var other = await ApiClient.CreateAccountAsync(client, currency, balance: 500m);
        var category = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
        var incomeCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 1);

        return new World(client, currency, account, other, category, incomeCategory);
    }

    private static object ExpenseBody(World world, decimal amount, decimal? expectedReimbursement) => new
    {
        title = "Dinner",
        amount,
        date = "2026-07-15",
        accountId = world.Account,
        categoryId = world.Category,
        expectedReimbursement
    };

    private static async Task<Guid> CreateExpenseAsync(World world, decimal amount, decimal? expectedReimbursement)
    {
        var response = await world.Client.PostAsJsonAsync("/api/Expense",
            ExpenseBody(world, amount, expectedReimbursement));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> FindReimbursementIdAsync(HttpClient client, Guid expenseId)
    {
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/Reimbursement");
        return listed.EnumerateArray()
            .Single(r => r.GetProperty("expenseId").GetGuid() == expenseId)
            .GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SettleAsync(
        HttpClient client, Guid reimbursementId, Guid accountId, decimal amount) =>
        client.PostAsJsonAsync($"/api/Reimbursement/{reimbursementId}/settlements", new
        {
            accountId,
            amount,
            date = "2026-07-20"
        });

    private static async Task<decimal> IncomeTotalAsync(HttpClient client)
    {
        var incomes = await client.GetFromJsonAsync<JsonElement>("/api/Income");
        return incomes.EnumerateArray().Sum(i => i.GetProperty("amount").GetDecimal());
    }

    #endregion
}
