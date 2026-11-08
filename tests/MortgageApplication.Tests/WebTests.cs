using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MortgageApplication.Api.Data;
using MortgageApplication.Web.Forms;
using MortgageApplication.Web.Services;

namespace MortgageApplication.Tests;

public sealed class WebTests
{
    [Theory]
    [InlineData("missing", 5, "annualIncome")]
    [InlineData("income", 1, "annualIncome")]
    [InlineData("deposit", 1, "depositAmount")]
    [InlineData("precision", 1, "annualIncome")]
    [InlineData("term", 1, "termYears")]
    public async Task ExampleErrorsComeFromTheApiAndDoNotCreateRecords(string key, int count, string field)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mortgage-example-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new ApiFactory(path);
            using var http = factory.CreateClient();
            var error = await Assert.ThrowsAsync<MortgageApiException>(() => new MortgageApiClient(http).CreateApplicationAsync(ApplicationExamples.Create(key)));

            Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
            Assert.Equal(count, error.Errors.Count);
            Assert.Contains(field, error.Errors.Keys);

            using var scope = factory.Services.CreateScope();

            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<MortgageDbContext>().Applications.CountAsync());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("1e1000")]
    public async Task InvalidTermTextReachesApiBindingAndCreatesNoRecord(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mortgage-binding-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new ApiFactory(path);
            using var http = factory.CreateClient();
            var form = ApplicationExamples.Create("standard");
            form.TermYears = text;
            var error = await Assert.ThrowsAsync<MortgageApiException>(() => new MortgageApiClient(http).CreateApplicationAsync(form));

            Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);

            using var scope = factory.Services.CreateScope();

            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<MortgageDbContext>().Applications.CountAsync());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Theory]
    [InlineData("not-a-reference")]
    [InlineData("12345678-1234-1234-1234-123456789abc")]
    public async Task ReferenceTextIsSentToTheRealApi(string reference)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mortgage-reference-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new ApiFactory(path);
            using var http = factory.CreateClient();
            var error = await Assert.ThrowsAsync<MortgageApiException>(() => new MortgageApiClient(http).FindApplicationAsync(reference));

            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public void PresetsHaveIndependentRawTextModels()
    {
        Assert.Null(ApplicationExamples.Find("unknown"));
        Assert.Null(ApplicationExamples.Create("unknown").AnnualIncome);

        var first = ApplicationExamples.Create("income");
        first.AnnualIncome = "123";

        Assert.Equal("0", ApplicationExamples.Create("income").AnnualIncome);
        Assert.Equal(7, ApplicationExamples.All.Select(example => example.Key).Distinct().Count());
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData(" ", "null")]
    [InlineData("60000.00000000000000000000000000001", "60000.00000000000000000000000000001")]
    [InlineData("6e+4", "6e+4")]
    [InlineData("1e1000", "1e1000")]
    [InlineData("1.5", "1.5")]
    [InlineData("abc", "\"abc\"")]
    [InlineData("01", "\"01\"")]
    [InlineData("true", "\"true\"")]
    [InlineData("{}", "\"{}\"")]
    public void JsonTransportPreservesNumberTokensAndLeavesInvalidTextForApiBinding(string? input, string expected) =>
        Assert.Equal(expected, JsonInput.FromText(input).GetRawText());

    [Theory]
    [InlineData("", false)]
    [InlineData("/api", false)]
    [InlineData("ftp://localhost/", false)]
    [InlineData("https://user:secret@example.test/", false)]
    [InlineData("https://example.test/?key=value", false)]
    [InlineData("https://example.test/#fragment", false)]
    [InlineData("http://localhost:5080/", true)]
    [InlineData("https://example.test/api", true)]
    public void ApiAddressValidationRequiresAnAbsoluteHttpAddress(string address, bool valid) =>
        Assert.Equal(valid, MortgageApiOptions.IsValid(new()
        {
            ApiBaseUrl = address
        }));

    [Fact]
    public async Task WebClientMatchesRealApiContractsAcrossTheCompleteWorkflow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mortgage-web-contract-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new ApiFactory(path);
            using var http = factory.CreateClient();
            var client = new MortgageApiClient(http);
            var invalid = await Assert.ThrowsAsync<MortgageApiException>(() => client.CreateApplicationAsync(new ApplicationForm()));

            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(5, invalid.Errors.Count);
            Assert.Contains("annualIncome", invalid.Errors.Keys);

            var form = new ApplicationForm();
            form.FillExample();
            var application = await client.CreateApplicationAsync(form);

            Assert.Equal(60000m, application.Inputs.AnnualIncome);
            Assert.Equal(application, await client.GetApplicationAsync(application.Id));
            Assert.Equal(application, await client.FindApplicationAsync(application.Id.ToString()));
            Assert.Empty((await client.GetHistoryAsync(application.Id)).Items);

            var saved = await client.CreateAssessmentAsync(application.Id);

            Assert.Equal("approved", saved.Result.Decision);
            Assert.Equal(1159.74m, saved.Result.MonthlyRepayment);
            Assert.Equal(saved, await client.GetAssessmentAsync(application.Id, saved.Id));

            var page = await client.GetHistoryAsync(application.Id, 0, 10, "approved", "asc");

            Assert.Equal(saved, Assert.Single(page.Items));
            Assert.Empty((await client.GetHistoryAsync(application.Id, status: "declined")).Items);

            var error = await Assert.ThrowsAsync<MortgageApiException>(() => client.GetApplicationAsync(Guid.NewGuid()));

            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);

            var declinedForm = ApplicationExamples.Create("declined");
            var declinedApplication = await client.CreateApplicationAsync(declinedForm);
            var declined = await client.CreateAssessmentAsync(declinedApplication.Id);

            Assert.Equal("declined", declined.Result.Decision);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                File.Delete(path + suffix);
            }
        }
    }

    [Fact]
    public async Task CreationSendsOnlyTheFiveExpectedFields()
    {
        string? body = null;
        using var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/applications", request.RequestUri!.AbsolutePath);
            body = await request.Content!.ReadAsStringAsync(token);

            return new(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new ApplicationRecord(Guid.NewGuid(),
                DateTimeOffset.UtcNow, new(60000, 500, 20000, 200000, 25)))
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        var form = new ApplicationForm();
        form.FillExample();
        form.AnnualIncome = "60000.00000000000000000000000000001";
        form.MonthlyExpenses = "5e2";
        await new MortgageApiClient(http).CreateApplicationAsync(form);
        using var json = JsonDocument.Parse(body!);

        Assert.Equal(5, json.RootElement.EnumerateObject().Count());
        Assert.Equal("60000.00000000000000000000000000001", json.RootElement.GetProperty("annualIncome").GetRawText());
        Assert.Equal("5e2", json.RootElement.GetProperty("monthlyExpenses").GetRawText());
        Assert.Equal(25, json.RootElement.GetProperty("termYears").GetInt32());
    }

    [Fact]
    public async Task ReferenceIsEscapedAsOnePathSegment()
    {
        using var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/applications/a%2Fb%3Fc%23d", request.RequestUri!.AbsolutePath);
            Assert.Empty(request.RequestUri.Query);
            Assert.Empty(request.RequestUri.Fragment);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { }) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        var error = await Assert.ThrowsAsync<MortgageApiException>(() => new MortgageApiClient(http).FindApplicationAsync("a/b?c#d"));

        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task ClientPreservesApiFieldErrorsWithoutRetryingPost()
    {
        var calls = 0;
        using var handler = new StubHandler((_, _) =>
        {
            calls++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { errors = new { depositAmount = new[] { "Deposit must be less than property value." } } })
            });
        });
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        var error = await Assert.ThrowsAsync<MortgageApiException>(() => new MortgageApiClient(http).CreateApplicationAsync(new()));

        Assert.Contains("Deposit", Assert.Single(error.Errors["depositAmount"]));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ClientMakesFailuresUsefulAndPreservesCallerCancellation()
    {
        using var handler = new StubHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            throw new HttpRequestException("Internal transport details");
        });
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        var client = new MortgageApiClient(http);
        var error = await Assert.ThrowsAsync<MortgageApiException>(() => client.GetApplicationAsync(Guid.NewGuid()));

        Assert.Contains("unavailable", error.Message);
        Assert.DoesNotContain("Internal", error.Message);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetApplicationAsync(Guid.NewGuid(), cancelled.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "upstream secret", "couldn't complete")]
    [InlineData(HttpStatusCode.NotFound, "{}", "couldn't find")]
    [InlineData(HttpStatusCode.OK, "invalid JSON", "unexpected response")]
    public async Task ClientHandlesNonJsonAndErrorResponses(HttpStatusCode status, string body, string expected)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new("http://localhost/") };
        var error = await Assert.ThrowsAsync<MortgageApiException>(() => new MortgageApiClient(http).GetApplicationAsync(Guid.NewGuid()));

        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData("/", "Your mortgage.")]
    [InlineData("/applications/new", "Use example figures")]
    [InlineData("/applications/find", "Use example reference")]
    public async Task WebRoutesRenderPickersWithoutCallingTheApi(string route, string expected)
    {
        using var factory = new NoApiCallsWebFactory();
        using var http = factory.CreateClient();
        var response = await http.GetAsync(route);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(expected, html);
        Assert.Contains("blazor.web.js", html);
        Assert.Matches("class=\"brand-wordmark\"[^>]*>Canada Life", html);
        Assert.Matches("class=\"brand-product\"[^>]*>Mortgages", html);
        Assert.DoesNotContain("href=\"/examples\"", html);
        Assert.DoesNotContain("Check inputs", html);
        if (route == "/applications/new")
        {
            foreach (var example in ApplicationExamples.All)
            {
                Assert.Contains(example.Title, html);
            }
            Assert.Contains("inputmode=\"decimal\"", html);
            Assert.Contains("inputmode=\"numeric\"", html);
            Assert.DoesNotContain("type=\"number\"", html);
            Assert.DoesNotContain(" min=", html);
            Assert.DoesNotContain(" max=", html);
            Assert.DoesNotContain(" step=", html);
            Assert.DoesNotMatch(@"<input[^>]*\srequired[\s=>]", html);
        }
        if (route == "/applications/find")
        {
            Assert.Contains("Malformed reference", html);
            Assert.Contains("Unsaved reference", html);
        }
    }

    [Fact]
    public async Task SeparateExamplesPageHasBeenRemoved()
    {
        using var factory = new NoApiCallsWebFactory();
        using var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/examples")).StatusCode);
    }

    [Fact]
    public void WebRejectsInvalidApiAddressBeforeServingRequests()
    {
        using var factory = new InvalidWebFactory();

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    private static void DeleteDatabase(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(path + suffix);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    }

    private sealed class NoApiCallsWebFactory : WebApplicationFactory<MortgageApplication.Web.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureServices(services =>
            services.AddHttpClient<MortgageApiClient>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler((_, _) =>
                throw new InvalidOperationException("Rendering an example must not call the API."))));
    }

    private sealed class InvalidWebFactory : WebApplicationFactory<MortgageApplication.Web.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["MortgageApi:ApiBaseUrl"] = "relative/path" }));
    }
}
