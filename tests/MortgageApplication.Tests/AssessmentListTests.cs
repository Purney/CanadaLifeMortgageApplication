using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MortgageApplication.Api.Contracts;
using MortgageApplication.Api.Data;

namespace MortgageApplication.Tests;

public sealed class AssessmentListTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"mortgage-list-tests-{Guid.NewGuid():N}.db");

    private static readonly CreateApplicationRequest Sample = new()
    {
        AnnualIncome = 60000,
        MonthlyExpenses = 500,
        DepositAmount = 20000,
        PropertyValue = 200000,
        TermYears = 25
    };

    [Fact]
    public async Task DefaultsReturnTenFullSnapshotsNewestFirstWithFilteredTotalAndNoWrites()
    {
        using var factory = new ApiFactory(databasePath, clock: new FrozenClock());
        using var client = factory.CreateClient();
        var application = await Create(client);
        var saved = new List<AssessmentResponse>();
        for (var i = 0; i < 12; i++)
        {
            saved.Add(await Assess(client, application.Id));
        }
        var page = await List(client, application.Id);

        Assert.Equal(0, page.Offset);
        Assert.Equal(10, page.Limit);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(saved.AsEnumerable().Reverse().Take(10), page.Items);
        Assert.Equal(application.Inputs, page.Items[0].Inputs);
        Assert.Equal(1159.74m, page.Items[0].Result.MonthlyRepayment);
        Assert.Equal(6m, page.Items[0].AppliedRules.AnnualInterestRatePercentage);
        Assert.Equal("1", page.Items[0].CalculationVersion);

        var ascending = await List(client, application.Id, "?offset=2&limit=3&sort=asc");

        Assert.Equal(saved.Skip(2).Take(3), ascending.Items);
        Assert.Equal(12, ascending.TotalCount);
        Assert.Equal(2, ascending.Offset);
        Assert.Equal(3, ascending.Limit);

        var descending = await List(client, application.Id, "?offset=2&limit=3&sort=desc");

        Assert.Equal(saved.AsEnumerable().Reverse().Skip(2).Take(3), descending.Items);

        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Equal(12, await scope.ServiceProvider.GetRequiredService<MortgageDbContext>().Assessments.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/applications/{application.Id}/assessments/latest")).StatusCode);
    }

    [Fact]
    public async Task BothDirectionsOrderByTimestampBeforeInsertionSequence()
    {
        var clock = new AdjustableClock();
        using var factory = new ApiFactory(databasePath, clock: clock);
        using var client = factory.CreateClient();
        var application = await Create(client);
        var first = await Assess(client, application.Id);
        clock.Current = clock.Current.AddHours(-1);
        var second = await Assess(client, application.Id);
        clock.Current = clock.Current.AddHours(1);
        var third = await Assess(client, application.Id);

        Assert.Equal(new[] { second.Id, first.Id, third.Id }, (await List(client, application.Id, "?sort=asc")).Items.Select(x => x.Id));
        Assert.Equal(new[] { third.Id, first.Id, second.Id }, (await List(client, application.Id, "?sort=desc")).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task SavedDecisionFilteringHappensBeforePagingAndPreservesOldPolicyAfterRestart()
    {
        ApplicationResponse application;
        var approved = new List<AssessmentResponse>();
        using (var factory = new ApiFactory(databasePath, clock: new FrozenClock()))
        {
            using (var client = factory.CreateClient())
            {
                application = await Create(client);
                for (var i = 0; i < 3; i++)
                {
                    approved.Add(await Assess(client, application.Id));
                }
            }
        }
        using var restarted = new ApiFactory(databasePath,
                    overrides: new()
                    {
                        ["Affordability:MaxIncomeMultiple"] = "2"
                    }, clock: new FrozenClock());
        using var newClient = restarted.CreateClient();
        var declined = new List<AssessmentResponse>();
        for (var i = 0; i < 12; i++)
        {
            declined.Add(await Assess(newClient, application.Id));
        }
        var approvedPage = await List(newClient, application.Id, "?status=approved&sort=desc&offset=1&limit=1");

        Assert.Equal(3, approvedPage.TotalCount);
        Assert.Equal(approved[1], Assert.Single(approvedPage.Items));
        Assert.Equal(4.5m, approvedPage.Items[0].AppliedRules.MaxIncomeMultiple);

        var declinedPage = await List(newClient, application.Id, "?status=declined&sort=asc&offset=10&limit=2");

        Assert.Equal(12, declinedPage.TotalCount);
        Assert.Equal(declined.Skip(10), declinedPage.Items);
        Assert.All(declinedPage.Items, x => Assert.Equal("declined", x.Result.Decision));
        Assert.Equal(15, (await List(newClient, application.Id, "?status=all")).TotalCount);
    }

    [Fact]
    public async Task EmptyFilteredAndBeyondEndPagesStayIsolatedToExistingApplication()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var first = await Create(client);
        var second = await Create(client);
        var empty = await List(client, first.Id);

        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalCount);
        await Assess(client, second.Id);

        Assert.Empty((await List(client, first.Id)).Items);
        await Assess(client, first.Id);
        var filtered = await List(client, first.Id, "?status=declined");

        Assert.Empty(filtered.Items);
        Assert.Equal(0, filtered.TotalCount);

        var beyondEnd = await List(client, first.Id, "?offset=2147483647&limit=100");

        Assert.Empty(beyondEnd.Items);
        Assert.Equal(1, beyondEnd.TotalCount);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/applications/{Guid.NewGuid()}/assessments")).StatusCode);
    }

    [Theory]
    [InlineData("offset=-1", "offset")]
    [InlineData("offset=abc", "offset")]
    [InlineData("offset=1.5", "offset")]
    [InlineData("offset=2147483648", "offset")]
    [InlineData("offset=", "offset")]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=101", "limit")]
    [InlineData("limit=-1", "limit")]
    [InlineData("limit=abc", "limit")]
    [InlineData("limit=1.5", "limit")]
    [InlineData("limit=", "limit")]
    [InlineData("status=pending", "status")]
    [InlineData("status=Approved", "status")]
    [InlineData("status=", "status")]
    [InlineData("status=approved%27%20OR%201%3D1", "status")]
    [InlineData("sort=newest", "sort")]
    [InlineData("sort=DESC", "sort")]
    [InlineData("sort=", "sort")]
    public async Task InvalidQueryValuesProduceFieldLevelProblemDetails(string query, string field)
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var application = await Create(client);
        var response = await client.GetAsync($"/applications/{application.Id}/assessments?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (query is "offset=abc" or "offset=1.5" or "offset=2147483648" or "offset=" or "limit=abc" or "limit=1.5" or "limit=")
        {
            Assert.Equal(400, problem.GetProperty("status").GetInt32());
            Assert.Contains("query parameters", problem.GetProperty("detail").GetString());
        }
        else
        {
            Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
        }
    }

    private static async Task<ApplicationResponse> Create(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/applications", Sample);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApplicationResponse>())!;
    }

    private static async Task<AssessmentResponse> Assess(HttpClient client, Guid applicationId)
    {
        var response = await client.PostAsync($"/applications/{applicationId}/assessments", null);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<AssessmentResponse>())!;
    }

    private static async Task<AssessmentPage> List(HttpClient client, Guid applicationId, string query = "") =>
        (await client.GetFromJsonAsync<AssessmentPage>($"/applications/{applicationId}/assessments{query}"))!;

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(databasePath + suffix);
        }
    }

    private sealed class AdjustableClock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Current;
    }
}
