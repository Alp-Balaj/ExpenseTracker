using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Shared.Enums;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Suggestion-9 item 2: until the UserIndexesAndForeignKeys migration, UserId was
// a bare string with no foreign key behind it, so deleting a user left every row
// they ever created in place — invisible to the app and counted by nothing.
//
// There is no delete-user endpoint, so this drives the DbContext directly: the
// invariant being pinned is a property of the schema, not of a controller. It is
// also the test that would catch the cascade failing on ordering — the
// table-to-table foreign keys are RESTRICT, so the cascade from AspNetUsers has
// to resolve every child before the row it points at.
[Collection(IntegrationTestCollection.Name)]
public sealed class UserDeletionCascadeTests
{
    private readonly CustomWebApplicationFactory _factory;

    public UserDeletionCascadeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Deleting_a_user_leaves_no_orphaned_rows()
    {
        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);

        // One row in every user-owned table.
        var currencyId = await ApiClient.CreateCurrencyAsync(client, "USD");
        var fromAccount = await ApiClient.CreateAccountAsync(client, currencyId, balance: 10_000m);
        var toAccount = await ApiClient.CreateAccountAsync(client, currencyId, balance: 10_000m);
        var expenseCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 0);
        var goalCategory = await ApiClient.CreateCategoryAsync(client, categoryType: 2);

        (await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Expense",
            amount = 10m,
            date = "2026-01-01",
            accountId = fromAccount,
            categoryId = expenseCategory
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/Income", new
        {
            amount = 20m,
            date = "2026-01-02",
            accountId = fromAccount,
            categoryId = expenseCategory
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/FutureExpense", new
        {
            title = "Bill",
            amount = 30m,
            predictedDate = "2026-02-01",
            categoryId = expenseCategory
        })).EnsureSuccessStatusCode();

        (await client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 500m,
            period = 0,
            categoryId = expenseCategory
        })).EnsureSuccessStatusCode();

        var goalResponse = await client.PostAsJsonAsync("/api/SavingGoal", new
        {
            title = "Goal",
            targetAmount = 1000m,
            savedAmount = 0m,
            deadline = "2026-12-31",
            accountId = toAccount,
            categoryId = goalCategory
        });
        goalResponse.EnsureSuccessStatusCode();
        var goalId = (await goalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // A transfer linked to the goal, which also books a SavingContribution.
        (await client.PostAsJsonAsync("/api/Transfer", new
        {
            amount = 40m,
            date = "2026-01-03",
            fromAccountId = fromAccount,
            toAccountId = toAccount,
            savingGoalId = goalId
        })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The suite shares one database across users, so the user under test is
        // identified through a row it definitely owns rather than by picking
        // one out of AspNetUsers.
        var userId = await db.Currencies
            .Where(c => c.Id == currencyId)
            .Select(c => c.UserId)
            .SingleAsync();

        Assert.True(await db.Expenses.AnyAsync(x => x.UserId == userId));

        var user = await db.Users.SingleAsync(u => u.Id == userId);
        db.Users.Remove(user);
        await db.SaveChangesAsync();

        Assert.False(await db.Accounts.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Budgets.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Categories.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Currencies.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Expenses.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.FutureExpenses.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Incomes.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.SavingContributions.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.SavingGoals.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.Transfers.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.RefreshTokens.AnyAsync(x => x.UserId == userId));
        Assert.False(await db.UserPreferences.AnyAsync(x => x.UserId == userId));
    }

    [Fact]
    public async Task A_row_cannot_be_written_for_a_user_that_does_not_exist()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Categories.Add(new Category
        {
            Name = "Orphan",
            CategoryType = CategoryType.Expense,
            UserId = "no-such-user"
        });

        // Before the foreign key existed this simply succeeded.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
