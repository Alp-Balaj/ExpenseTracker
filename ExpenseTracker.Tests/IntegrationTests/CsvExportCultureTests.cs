using System.Globalization;
using System.Net.Http.Json;
using ExpenseTracker.Tests.Helpers;
using ExpenseTracker.Tests.Infrastructure;
using Xunit;

namespace ExpenseTracker.Tests.IntegrationTests;

// Locks in Fix-10's CSV half: amounts/dates must render with InvariantCulture
// regardless of the server process's default culture, and fields containing
// commas/quotes must be quoted/escaped so the sheet isn't corrupted.
[Collection(IntegrationTestCollection.Name)]
public sealed class CsvExportCultureTests : IDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly CultureInfo? _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;

    public CsvExportCultureTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public void Dispose()
    {
        CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
    }

    [Fact]
    public async Task Export_uses_invariant_formatting_and_escapes_commas_under_de_DE_culture()
    {
        // A comma-decimal culture as the server process default -- if any format
        // call ever drops its explicit InvariantCulture, this flips 1234.5 to 1234,5
        // and silently corrupts the CSV's column count.
        // Override-free, so the hostile culture this test installs is the same
        // comma-decimal one everywhere and not the host user's personalised
        // variant of it.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");

        var client = await ApiClient.CreateAuthenticatedClientAsync(_factory);
        var currencyId = await ApiClient.CreateCurrencyAsync(client);
        var accountId = await ApiClient.CreateAccountAsync(client, currencyId, balance: 0m);
        var categoryId = await ApiClient.CreateCategoryAsync(client, name: "Food, \"Drink\"");

        var create = await client.PostAsJsonAsync("/api/Expense", new
        {
            title = "Dinner",
            amount = 1234.5m,
            date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            accountId,
            categoryId
        });
        create.EnsureSuccessStatusCode();

        var export = await client.PostAsJsonAsync("/api/Reports/transactions/export", new
        {
            from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd"),
            to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)).ToString("yyyy-MM-dd"),
            includeExpenses = true,
            includeIncomes = false
        });
        export.EnsureSuccessStatusCode();

        var csv = await export.Content.ReadAsStringAsync();

        Assert.Contains("1234.5", csv);
        Assert.DoesNotContain("1234,5", csv);
        Assert.Contains("\"Food, \"\"Drink\"\"\"", csv);
    }
}
