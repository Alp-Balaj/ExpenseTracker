using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

/// <summary>
/// Where a pot expense lands in the member's own life: the reserved "Shared"
/// category, their budget, and their category totals.
///
/// <para>
/// This is the wave's most delicate seam, because it is the one place where the
/// group's money reaches into a personal number that R and V rewrote a day
/// earlier. The rule it has to keep is the charter's, and it is a rule about
/// <em>timing</em> as much as about amounts: <b>a top-up is a transfer and moves
/// no budget; a share is an expense and moves the budget on the day the expense
/// happened.</b> Money sitting in a pot has not been consumed by anybody yet.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public sealed class PotSharedCategoryTests
{
    private readonly CustomWebApplicationFactory _factory;

    public PotSharedCategoryTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static async Task<IReadOnlyList<JsonElement>> CategoriesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/Category")).EnumerateArray().ToList();

    private static async Task<JsonElement> SharedCategoryAsync(HttpClient client) =>
        Assert.Single(await CategoriesAsync(client), c => c.TryGetProperty("systemKind", out _));

    private static async Task<JsonElement> BudgetStatusAsync(HttpClient client, string month, Guid categoryId)
    {
        var response = await client.GetAsync($"/api/Budget/status?month={month}");
        response.EnsureSuccessStatusCode();

        return Assert.Single(
            (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(),
            b => b.TryGetProperty("categoryId", out var id)
                 && id.ValueKind != JsonValueKind.Null
                 && id.GetGuid() == categoryId);
    }

    // ── The category itself ────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_a_pot_is_joining_one_so_the_category_appears_then_too()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);

        Assert.DoesNotContain(await CategoriesAsync(alp.Client), c => c.TryGetProperty("systemKind", out _));

        await PotFixture.CreatePotIdAsync(alp.Client, "Flat");

        var shared = await SharedCategoryAsync(alp.Client);
        Assert.Equal("Shared", shared.GetProperty("name").GetString());
        Assert.Equal(0, shared.GetProperty("categoryType").GetInt32());
    }

    [Fact]
    public async Task One_shared_category_covers_every_pot_and_a_rejoin_does_not_add_a_second()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var household = await PotFixture.CreatePotIdAsync(alp.Client, "Household");
        var trip = await PotFixture.CreatePotIdAsync(alp.Client, "Trip");

        var code = await PotFixture.InviteCodeAsync(alp.Client, household);
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);   // idempotent
        (await PotFixture.LeaveAsync(mara.Client, household)).EnsureSuccessStatusCode();
        await PotFixture.JoinExpectingSuccessAsync(mara.Client, code);   // rejoin
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, trip));

        // "One 'Shared' covers all of a user's pots, not one per pot." Asserted
        // through the path most likely to produce a second: joining, leaving and
        // rejoining, twice over, across two groups.
        await SharedCategoryAsync(mara.Client);
        await SharedCategoryAsync(alp.Client);
    }

    [Fact]
    public async Task The_shared_category_can_be_renamed_but_not_deleted_once_a_share_points_at_it()
    {
        var (alp, mara) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var sharedId = (await SharedCategoryAsync(mara.Client)).GetProperty("id").GetGuid();

        // Nothing points at it yet, and the charter's rule is "undeletable while
        // shares reference it" — not "undeletable". A member who has never spent
        // from a pot may still remove it.
        var alpsShared = (await SharedCategoryAsync(alp.Client)).GetProperty("id").GetGuid();
        (await alp.Client.DeleteAsync($"/api/Category/{alpsShared}")).EnsureSuccessStatusCode();

        // Now give Mara a share.
        var alpMembership = await PotFixture.MyMembershipIdAsync(alp.Client, potId);
        var maraMembership = await PotFixture.MyMembershipIdAsync(mara.Client, potId);
        var alpAccount = await PotFixture.FundedAccountAsync(alp.Client);

        await PotFixture.TopUpExpectingSuccessAsync(alp.Client, potId, 100m, alpAccount);
        await PotFixture.LogExpenseExpectingSuccessAsync(
            alp.Client, potId, 20m, PotFixture.Evenly(alpMembership, maraMembership));

        var refused = await mara.Client.DeleteAsync($"/api/Category/{sharedId}");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(
            "Your pots put your share of every shared expense in this category, "
            + "so it cannot be deleted while any of them is still there.",
            await PotFixture.MessageAsync(refused));

        // It is still hers to rename, though. The only thing reserved about it
        // is that it cannot be removed out from under her pot spending.
        var renamed = await mara.Client.PutAsJsonAsync($"/api/Category/{sharedId}", new
        {
            id = sharedId,
            name = "The flat",
            categoryType = 0,
            color = "#123456"
        });
        renamed.EnsureSuccessStatusCode();

        var after = await SharedCategoryAsync(mara.Client);
        Assert.Equal("The flat", after.GetProperty("name").GetString());
        // And renaming did not clear the mark, which would have walked straight
        // past the guard above.
        Assert.Equal(0, after.GetProperty("systemKind").GetInt32());
    }

    [Fact]
    public async Task A_client_cannot_mint_a_system_category_of_its_own()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);

        var response = await alp.Client.PostAsJsonAsync("/api/Category", new
        {
            name = "Undeletable",
            categoryType = 0,
            systemKind = 0
        });
        response.EnsureSuccessStatusCode();

        // The field is read-only: it goes out so the UI can badge the row, and
        // never comes back in. Otherwise anybody could create a category they
        // then could not remove.
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(created.TryGetProperty("systemKind", out _));

        var id = created.GetProperty("id").GetGuid();
        (await alp.Client.DeleteAsync($"/api/Category/{id}")).EnsureSuccessStatusCode();
    }

    // ── Acceptance 3 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_top_up_moves_the_account_and_neither_the_budget_nor_the_category_total()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var accountId = await PotFixture.FundedAccountAsync(alp.Client, balance: 1_000m);
        var sharedId = (await SharedCategoryAsync(alp.Client)).GetProperty("id").GetGuid();

        (await alp.Client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 500m,
            period = 0,
            categoryId = sharedId
        })).EnsureSuccessStatusCode();

        await PotFixture.TopUpExpectingSuccessAsync(alp.Client, potId, 200m, accountId, date: "2026-08-05");

        // The money left the account, because it did.
        Assert.Equal(800m, await ApiClient.GetAccountBalanceAsync(alp.Client, accountId));

        // And nothing was consumed, so no budget moved and no category counted
        // it. A top-up is a transfer: the pot is holding it, with his name still
        // on it. Charging it here would then charge his share a second time when
        // the group actually spends it.
        var budget = await BudgetStatusAsync(alp.Client, "2026-08", sharedId);
        Assert.Equal(0m, budget.GetProperty("spent").GetDecimal());

        var shared = await SharedCategoryAsync(alp.Client);
        Assert.Equal(0m, shared.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task A_share_moves_the_shared_budget_and_category_total_on_the_expenses_own_date()
    {
        var users = await PotFixture.UsersAsync(_factory, 2);
        var (alp, mara) = (users[0], users[1]);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var alpMembership = await PotFixture.MyMembershipIdAsync(alp.Client, potId);
        var maraMembership = await PotFixture.MyMembershipIdAsync(mara.Client, potId);

        var alpAccount = await PotFixture.FundedAccountAsync(alp.Client);
        await PotFixture.TopUpExpectingSuccessAsync(alp.Client, potId, 300m, alpAccount);

        var maraShared = (await SharedCategoryAsync(mara.Client)).GetProperty("id").GetGuid();
        (await mara.Client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 100m,
            period = 0,
            categoryId = maraShared
        })).EnsureSuccessStatusCode();

        // Logged in September, dated August. The share lands in August, exactly
        // as an ordinary expense logged late does — the date is when the money
        // was spent, not when somebody got round to typing it.
        await PotFixture.LogExpenseExpectingSuccessAsync(
            alp.Client, potId, 60m,
            PotFixture.Evenly(alpMembership, maraMembership),
            title: "The big shop", date: "2026-08-14");

        var august = await BudgetStatusAsync(mara.Client, "2026-08", maraShared);
        Assert.Equal(30m, august.GetProperty("spent").GetDecimal());
        Assert.Equal(70m, august.GetProperty("remaining").GetDecimal());

        var september = await BudgetStatusAsync(mara.Client, "2026-09", maraShared);
        Assert.Equal(0m, september.GetProperty("spent").GetDecimal());

        // Her category total says the same thing.
        var shared = await SharedCategoryAsync(mara.Client);
        Assert.Equal(30m, shared.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(30m, shared.GetProperty("convertedTotal").GetDecimal());

        // Her half only. The other 30 is Alp's consumption and belongs in his
        // budget, not in hers — this is the whole point of attribution.
        Assert.Equal(30m, (await SharedCategoryAsync(alp.Client)).GetProperty("totalAmount").GetDecimal());

        // And her own account never moved: the pot paid, not her.
        var accounts = await mara.Client.GetFromJsonAsync<JsonElement>("/api/Account");
        Assert.All(accounts.EnumerateArray(), a => Assert.Equal(0m, a.GetProperty("balance").GetDecimal()));
    }

    [Fact]
    public async Task Deleting_the_pot_expense_takes_the_share_back_out_of_the_budget()
    {
        var users = await PotFixture.UsersAsync(_factory, 2);
        var (alp, mara) = (users[0], users[1]);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var alpMembership = await PotFixture.MyMembershipIdAsync(alp.Client, potId);
        var maraMembership = await PotFixture.MyMembershipIdAsync(mara.Client, potId);
        var alpAccount = await PotFixture.FundedAccountAsync(alp.Client);
        await PotFixture.TopUpExpectingSuccessAsync(alp.Client, potId, 300m, alpAccount);

        var maraShared = (await SharedCategoryAsync(mara.Client)).GetProperty("id").GetGuid();
        (await mara.Client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 100m,
            period = 0,
            categoryId = maraShared
        })).EnsureSuccessStatusCode();

        var logged = await PotFixture.LogExpenseExpectingSuccessAsync(
            alp.Client, potId, 50m,
            PotFixture.Evenly(alpMembership, maraMembership),
            date: "2026-08-14");

        Assert.Equal(25m, (await BudgetStatusAsync(mara.Client, "2026-08", maraShared))
            .GetProperty("spent").GetDecimal());

        var expenseId = Assert.Single(PotFixture.EntriesOf(logged), e => e.GetProperty("kind").GetInt32() == 1)
                              .GetProperty("id").GetGuid();

        (await PotFixture.DeleteExpenseAsync(alp.Client, potId, expenseId)).EnsureSuccessStatusCode();

        // "Deleting reverses everything it caused" reaches all the way into
        // somebody else's budget, because that is one of the things it caused.
        Assert.Equal(0m, (await BudgetStatusAsync(mara.Client, "2026-08", maraShared))
            .GetProperty("spent").GetDecimal());
        Assert.Equal(0m, (await SharedCategoryAsync(mara.Client)).GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public async Task A_fronted_expense_debits_the_payer_gross_and_budgets_them_their_share()
    {
        var users = await PotFixture.UsersAsync(_factory, 2);
        var (alp, mara) = (users[0], users[1]);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var alpMembership = await PotFixture.MyMembershipIdAsync(alp.Client, potId);
        var maraMembership = await PotFixture.MyMembershipIdAsync(mara.Client, potId);
        var alpAccount = await PotFixture.FundedAccountAsync(alp.Client, balance: 500m);

        var alpShared = (await SharedCategoryAsync(alp.Client)).GetProperty("id").GetGuid();
        (await alp.Client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 200m,
            period = 0,
            categoryId = alpShared
        })).EnsureSuccessStatusCode();

        await PotFixture.LogExpenseExpectingSuccessAsync(
            alp.Client, potId, 80m,
            PotFixture.Evenly(alpMembership, maraMembership),
            title: "Alp paid at the till", date: "2026-08-14",
            paidFromAccountId: alpAccount);

        // Gross out of the account — 80 really left it. Net into the budget —
        // 40 was his. Exactly Stream R's discipline, one scale out: the account
        // balance is cash truth and the budget is analysis truth.
        Assert.Equal(420m, await ApiClient.GetAccountBalanceAsync(alp.Client, alpAccount));
        Assert.Equal(40m, (await BudgetStatusAsync(alp.Client, "2026-08", alpShared))
            .GetProperty("spent").GetDecimal());
    }

    [Fact]
    public async Task An_overall_budget_counts_pot_shares_too()
    {
        var (alp, _) = await PotFixture.TwoUsersAsync(_factory);

        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Flat");
        var membership = await PotFixture.MyMembershipIdAsync(alp.Client, potId);
        var accountId = await PotFixture.FundedAccountAsync(alp.Client);
        await PotFixture.TopUpExpectingSuccessAsync(alp.Client, potId, 200m, accountId);

        var categoryId = await ApiClient.CreateCategoryAsync(alp.Client, categoryType: 0);
        (await alp.Client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Coffee",
            amount = 15m,
            date = "2026-08-02",
            accountId,
            categoryId
        })).EnsureSuccessStatusCode();

        (await alp.Client.PostAsJsonAsync("/api/Budget", new
        {
            amount = 300m,
            period = 0,
            categoryId = (Guid?)null
        })).EnsureSuccessStatusCode();

        await PotFixture.LogExpenseExpectingSuccessAsync(
            alp.Client, potId, 45m, PotFixture.Evenly(membership), date: "2026-08-14");

        // The "Overall" budget spans every expense category, and a pot share is
        // spending in one of them. 15 personal + 45 shared.
        var response = await alp.Client.GetAsync("/api/Budget/status?month=2026-08");
        response.EnsureSuccessStatusCode();

        var overall = Assert.Single(
            (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray(),
            b => b.GetProperty("categoryId").ValueKind == JsonValueKind.Null);

        Assert.Equal(60m, overall.GetProperty("spent").GetDecimal());
    }

    [Fact]
    public async Task A_share_in_a_currency_the_member_has_no_rate_for_is_flagged_not_dropped()
    {
        var users = await PotFixture.UsersAsync(_factory, 2);
        var (alp, mara) = (users[0], users[1]);

        // A pot in a currency neither member's seeded catalogue contains, so
        // Mara has no rate to convert the share with.
        var potId = await PotFixture.CreatePotIdAsync(alp.Client, "Tokyo", currencyCode: "JPY");
        await PotFixture.JoinExpectingSuccessAsync(
            mara.Client, await PotFixture.InviteCodeAsync(alp.Client, potId));

        var alpMembership = await PotFixture.MyMembershipIdAsync(alp.Client, potId);
        var maraMembership = await PotFixture.MyMembershipIdAsync(mara.Client, potId);
        var alpAccount = await PotFixture.FundedAccountAsync(alp.Client);

        await PotFixture.TopUpExpectingSuccessAsync(alp.Client, potId, 10_000m, alpAccount);
        await PotFixture.LogExpenseExpectingSuccessAsync(
            alp.Client, potId, 5_000m, PotFixture.Evenly(alpMembership, maraMembership));

        var shared = await SharedCategoryAsync(mara.Client);

        // The subtotal is there in the pot's own currency, and the base-currency
        // figure is honestly absent rather than silently zero. "A member with no
        // rate for the pot's code sees the position in the pot's currency and
        // HasMissingRates set, never a silently dropped figure."
        Assert.True(shared.GetProperty("hasMissingRates").GetBoolean());
        Assert.Equal(JsonValueKind.Null, shared.GetProperty("convertedTotal").ValueKind);

        var subtotal = Assert.Single(shared.GetProperty("subtotals").EnumerateArray());
        Assert.Equal("JPY", subtotal.GetProperty("currency").GetString());
        Assert.Equal(2_500m, subtotal.GetProperty("total").GetDecimal());

        // The group's own screen is unaffected: it was never converting.
        var ledger = await PotFixture.LedgerAsync(mara.Client, potId);
        Assert.Equal("JPY", ledger.GetProperty("currencyCode").GetString());
        Assert.Equal(5_000m, PotFixture.BalanceOf(ledger));
    }
}
