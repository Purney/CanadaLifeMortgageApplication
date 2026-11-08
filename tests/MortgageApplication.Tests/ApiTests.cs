using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MortgageApplication.Api.Contracts;
using MortgageApplication.Api.Data;

namespace MortgageApplication.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"mortgage-tests-{Guid.NewGuid():N}.db");

    private static readonly CreateApplicationRequest Sample = new()
    {
        AnnualIncome = 60000,
        MonthlyExpenses = 500,
        DepositAmount = 20000,
        PropertyValue = 200000,
        TermYears = 25
    };

    [Fact]
    public async Task CreateRetrieveAssessAndListPreserveSnapshotsAndUseInsertionOrderForTimestampTies()
    {
        using var factory = new ApiFactory(databasePath, clock: new FrozenClock());
        using var client = factory.CreateClient();
        var created = await client.PostAsJsonAsync("/applications", Sample);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var application = (await created.Content.ReadFromJsonAsync<ApplicationResponse>())!;

        Assert.Equal($"/applications/{application.Id}", created.Headers.Location!.OriginalString);
        Assert.Equal(application, await client.GetFromJsonAsync<ApplicationResponse>(created.Headers.Location));
        Assert.Empty((await client.GetFromJsonAsync<AssessmentPage>($"/applications/{application.Id}/assessments"))!.Items);

        var first = await Assess(client, application.Id);
        var second = await Assess(client, application.Id);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.CreatedAtUtc, second.CreatedAtUtc);
        Assert.Equal(second, (await client.GetFromJsonAsync<AssessmentPage>($"/applications/{application.Id}/assessments"))!.Items[0]);
        Assert.Equal(first, await client.GetFromJsonAsync<AssessmentResponse>($"/applications/{application.Id}/assessments/{first.Id}"));
        Assert.Equal("approved", first.Result.Decision);
        Assert.Equal(1159.74m, first.Result.MonthlyRepayment);
        Assert.Equal(application.Inputs, first.Inputs);
        Assert.Equal(6m, first.AppliedRules.AnnualInterestRatePercentage);

        await using var scope = factory.Services.CreateAsyncScope();

        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<MortgageDbContext>().Assessments.CountAsync());
    }

    [Fact]
    public async Task FilePersistenceAndHistoricalRulesSurviveRestartWhileNewAssessmentUsesNewRules()
    {
        ApplicationResponse application;
        AssessmentResponse original;
        using (var factory = new ApiFactory(databasePath))
        {
            using (var client = factory.CreateClient())
            {
                application = await Create(client);
                original = await Assess(client, application.Id);
            }
        }
        using var restarted = new ApiFactory(databasePath, overrides: new()
        {
            ["Affordability:MaxIncomeMultiple"] = "2"
        });
        using var newClient = restarted.CreateClient();

        Assert.Equal(application, await newClient.GetFromJsonAsync<ApplicationResponse>($"/applications/{application.Id}"));
        Assert.Equal(original, await newClient.GetFromJsonAsync<AssessmentResponse>($"/applications/{application.Id}/assessments/{original.Id}"));

        var changed = await Assess(newClient, application.Id);

        Assert.Equal("declined", changed.Result.Decision);
        Assert.Equal(2m, changed.AppliedRules.MaxIncomeMultiple);
        Assert.Equal(120000m, changed.Result.MaximumBorrowingForProperty);
        Assert.Equal(original, await newClient.GetFromJsonAsync<AssessmentResponse>($"/applications/{application.Id}/assessments/{original.Id}"));
    }

    [Fact]
    public async Task ConfigurationChangesDoNotChangePolicyUntilRestart()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var application = await Create(client);

        Assert.True(File.Exists(databasePath));
        factory.Services.GetRequiredService<IConfiguration>()["Affordability:MaxIncomeMultiple"] = "2";
        var assessment = await Assess(client, application.Id);

        Assert.Equal(4.5m, assessment.AppliedRules.MaxIncomeMultiple);
        Assert.Equal("approved", assessment.Result.Decision);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("{\"annualIncome\":\"abc\"}")]
    [InlineData("{\"annualIncome\":60000,\"monthlyExpenses\":0,\"depositAmount\":0,\"propertyValue\":100000,\"termYears\":1.5}")]
    public async Task InvalidBodiesReturnJsonProblemDetails(string json)
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var response = await client.PostAsync("/applications", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(400, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task MissingAndNullFieldsProduceFieldErrorsWhileZerosAreAccepted()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var invalid = await client.PostAsJsonAsync("/applications", new
        {
            annualIncome = (decimal?)null
        });
        var errors = (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        foreach (var field in new[] { "annualIncome", "monthlyExpenses", "depositAmount", "propertyValue", "termYears" })
        {
            Assert.True(errors.TryGetProperty(field, out _));
        }
        var valid = await client.PostAsJsonAsync("/applications", Sample with
        {
            MonthlyExpenses = 0,
            DepositAmount = 0
        });

        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        var application = (await valid.Content.ReadFromJsonAsync<ApplicationResponse>())!;

        Assert.Equal("declined", (await Assess(client, application.Id)).Result.Decision);
    }

    [Fact]
    public async Task UnknownAndMismatchedIdsReturnNotFoundProblems()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var application = await Create(client);
        var assessment = await Assess(client, application.Id);
        var other = await Create(client);
        var paths = new[] { $"/applications/{Guid.NewGuid()}", $"/applications/{Guid.NewGuid()}/assessments",
            $"/applications/{application.Id}/assessments/{Guid.NewGuid()}", $"/applications/{other.Id}/assessments/{assessment.Id}" };
        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/applications/{Guid.NewGuid()}/assessments", null)).StatusCode);
    }

    [Fact]
    public async Task FailedAssessmentInsertReturns500AndLeavesNoPartialRecord()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var application = await Create(client);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MortgageDbContext>().Database.ExecuteSqlRawAsync(
                        "CREATE TRIGGER reject_assessment BEFORE INSERT ON Assessments BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        }
        var response = await client.PostAsync($"/applications/{application.Id}/assessments", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        await using var check = factory.Services.CreateAsyncScope();

        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<MortgageDbContext>().Assessments.CountAsync());
    }

    [Fact]
    public async Task SwaggerUiAndSpecificationExposeFiveOperations()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/swagger/index.html");

        Assert.Contains("Swagger UI", html);

        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        Assert.Equal(5, document.GetProperty("paths").EnumerateObject().Sum(path => path.Value.EnumerateObject().Count()));
        Assert.Contains("monthlyExpenses", document.ToString());
        Assert.DoesNotContain("/assessments/latest", document.ToString());

        var list = document.GetProperty("paths").GetProperty("/applications/{applicationId}/assessments").GetProperty("get");
        var parameters = list.GetProperty("parameters").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToArray();

        Assert.Contains("offset", parameters);
        Assert.Contains("limit", parameters);
        Assert.Contains("status", parameters);
        Assert.Contains("sort", parameters);
        Assert.True(list.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(list.GetProperty("responses").TryGetProperty("404", out _));

        var offsetSchema = list.GetProperty("parameters").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "offset").GetProperty("schema");

        Assert.Equal("integer", offsetSchema.GetProperty("type").GetString());
        Assert.Equal(0, offsetSchema.GetProperty("default").GetInt32());
    }

    [Fact]
    public async Task ProductionStartupDoesNotCreateDatabaseOrExposeSwagger()
    {
        using var factory = new ApiFactory(databasePath, "Production");
        using var client = factory.CreateClient();

        Assert.False(File.Exists(databasePath));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }

    [Theory]
    [InlineData("Affordability:MaxDtiPercentage", null)]
    [InlineData("Affordability:MaxDtiPercentage", "0")]
    [InlineData("Affordability:MaxLtvPercentage", "101")]
    [InlineData("Affordability:AnnualInterestRatePercentage", "-1")]
    [InlineData("Affordability:MaxIncomeMultiple", "0")]
    [InlineData("Affordability:AnnualInterestRatePercentage", "not-a-number")]
    public void InvalidPolicyFailsStartup(string key, string? value)
    {
        using var factory = new ApiFactory(databasePath, overrides: new()
        {
            [key] = value
        });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.False(File.Exists(databasePath));
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

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var assessment = (await response.Content.ReadFromJsonAsync<AssessmentResponse>())!;

        Assert.Equal($"/applications/{applicationId}/assessments/{assessment.Id}", response.Headers.Location!.OriginalString);

        return assessment;
    }

    public void Dispose()
    {
        // Only this test's explicitly allocated temp database is removed.
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(databasePath + suffix);
        }
    }
}
