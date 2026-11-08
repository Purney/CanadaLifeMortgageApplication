using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MortgageApplication.Api.Controllers;
using MortgageApplication.Api.Services;

namespace MortgageApplication.Tests;

public sealed class ControllerCompatibilityTests : IDisposable
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        $"mortgage-controller-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public void AllFiveOperationsAreControllerActionsUsingTheApplicationServiceBoundary()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var actions = factory.Services
            .GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToArray();

        Assert.Equal(5, actions.Length);
        Assert.All(actions, action => Assert.Equal(
            typeof(MortgageApplicationsController),
            action.ControllerTypeInfo.AsType()));

        using var scope = factory.Services.CreateScope();

        Assert.IsType<MortgageApplicationService>(
            scope.ServiceProvider.GetRequiredService<IMortgageApplicationService>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"annualIncome\":\"private-binding-value\"}")]
    [InlineData("{\"termYears\":1.5}")]
    [InlineData("{")]
    public async Task BodyBindingErrorsUseTheGenericProblemContractWithoutInternalDetails(string body)
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/applications", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Invalid request.", problem.GetProperty("title").GetString());
        Assert.Contains("query parameters", problem.GetProperty("detail").GetString());
        Assert.False(problem.TryGetProperty("errors", out _));
        Assert.DoesNotContain("private-binding-value", problem.ToString());
        Assert.DoesNotContain("JsonException", problem.ToString());
        Assert.DoesNotContain("$.", problem.ToString());
    }

    [Fact]
    public async Task BusinessValidationKeepsTheExistingCamelCaseKeysAndMessages()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/applications", new
        {
            annualIncome = 0,
            monthlyExpenses = -1,
            depositAmount = 100000,
            propertyValue = 100000,
            termYears = 41
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");

        Assert.Equal(4, errors.EnumerateObject().Count());
        Assert.Equal("Must be positive and at most 1,000,000,000.", errors.GetProperty("annualIncome")[0].GetString());
        Assert.Equal("Must be nonnegative and at most 1,000,000,000.", errors.GetProperty("monthlyExpenses")[0].GetString());
        Assert.Equal("Deposit must be less than property value.", errors.GetProperty("depositAmount")[0].GetString());
        Assert.Equal("Term is required and must be a whole number from 1 to 40.", errors.GetProperty("termYears")[0].GetString());
        Assert.False(errors.TryGetProperty("AnnualIncome", out _));
    }

    [Theory]
    [InlineData("offset=")]
    [InlineData("limit=")]
    [InlineData("offset=invalid")]
    [InlineData("limit=1.5")]
    public async Task QueryBindingErrorsUseTheSameGenericProblemContract(string query)
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/applications/{Guid.NewGuid()}/assessments?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Invalid request.", problem.GetProperty("title").GetString());
        Assert.Contains("query parameters", problem.GetProperty("detail").GetString());
        Assert.False(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task SwaggerPreservesTypedQueryDefaultsAndBodylessAssessmentPost()
    {
        using var factory = new ApiFactory(databasePath);
        using var client = factory.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var path = document.GetProperty("paths").GetProperty("/applications/{applicationId}/assessments");
        var list = path.GetProperty("get");
        var parameters = list.GetProperty("parameters").EnumerateArray()
            .ToDictionary(parameter => parameter.GetProperty("name").GetString()!);

        AssertQuerySchema(parameters["offset"], "integer", "0");
        AssertQuerySchema(parameters["limit"], "integer", "10");
        AssertQuerySchema(parameters["status"], "string", "\"all\"");
        AssertQuerySchema(parameters["sort"], "string", "\"desc\"");

        Assert.False(path.GetProperty("post").TryGetProperty("requestBody", out _));
        Assert.True(path.GetProperty("post").GetProperty("responses").TryGetProperty("201", out _));
        Assert.True(list.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(list.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(list.GetProperty("responses").TryGetProperty("404", out _));
    }

    private static void AssertQuerySchema(JsonElement parameter, string type, string defaultValue)
    {
        var schema = parameter.GetProperty("schema");

        Assert.Equal("query", parameter.GetProperty("in").GetString());
        Assert.Equal(type, schema.GetProperty("type").GetString());
        Assert.Equal(defaultValue, schema.GetProperty("default").GetRawText());
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(databasePath + suffix);
        }
    }
}
