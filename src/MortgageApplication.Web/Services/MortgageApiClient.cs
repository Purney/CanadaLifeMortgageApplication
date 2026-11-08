using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MortgageApplication.Web.Forms;

namespace MortgageApplication.Web.Services;

public sealed class MortgageApiClient(HttpClient httpClient)
{
    public Task<ApplicationRecord> CreateApplicationAsync(ApplicationForm form, CancellationToken cancellationToken = default) =>
        SendAsync<ApplicationRecord>(HttpMethod.Post, "applications", new
        {
            AnnualIncome = JsonInput.FromText(form.AnnualIncome),
            MonthlyExpenses = JsonInput.FromText(form.MonthlyExpenses),
            DepositAmount = JsonInput.FromText(form.DepositAmount),
            PropertyValue = JsonInput.FromText(form.PropertyValue),
            TermYears = JsonInput.FromText(form.TermYears)
        }, cancellationToken);

    public Task<ApplicationRecord> GetApplicationAsync(Guid id, CancellationToken cancellationToken = default) =>
        SendAsync<ApplicationRecord>(HttpMethod.Get, $"applications/{id}", null, cancellationToken);

    public Task<ApplicationRecord> FindApplicationAsync(string reference, CancellationToken cancellationToken = default) =>
        SendAsync<ApplicationRecord>(HttpMethod.Get, $"applications/{Uri.EscapeDataString(reference.Trim())}", null, cancellationToken);

    public Task<AssessmentRecord> CreateAssessmentAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
        SendAsync<AssessmentRecord>(HttpMethod.Post, $"applications/{applicationId}/assessments", null, cancellationToken);

    public Task<AssessmentRecord> GetAssessmentAsync(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken = default) =>
        SendAsync<AssessmentRecord>(HttpMethod.Get, $"applications/{applicationId}/assessments/{assessmentId}", null, cancellationToken);

    public Task<AssessmentHistory> GetHistoryAsync(Guid applicationId, int offset = 0, int limit = 10,
        string status = "all", string sort = "desc", CancellationToken cancellationToken = default) =>
        SendAsync<AssessmentHistory>(HttpMethod.Get,
            $"applications/{applicationId}/assessments?offset={offset}&limit={limit}&status={Uri.EscapeDataString(status)}&sort={Uri.EscapeDataString(sort)}",
            null, cancellationToken);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string route,
        object? body,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, route);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await ReadApiErrorAsync(response, cancellationToken);
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new MortgageApiException("The mortgage service returned an empty response. Please try again shortly.");
        }
        catch (HttpRequestException)
        {
            throw new MortgageApiException("The mortgage service is unavailable. Please try again shortly.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MortgageApiException(method == HttpMethod.Post
                ? "The request timed out. It may have been saved; check your records before submitting again."
                : "The mortgage service took too long to respond. Please try again shortly.");
        }
        catch (JsonException)
        {
            throw new MortgageApiException("The mortgage service returned an unexpected response. Please try again shortly.");
        }
    }

    private static async Task<MortgageApiException> ReadApiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ApiProblem? problem = null;

        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblem>(cancellationToken);
        }
        catch (JsonException)
        {
            // An upstream failure might not include a JSON Problem Details body.
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.NotFound => "We couldn't find that saved record. Check the reference and try again.",
            HttpStatusCode.BadRequest => problem?.Detail ?? "Check the highlighted fields and try again.",
            _ => "The mortgage service couldn't complete the request. Please try again shortly."
        };

        return new MortgageApiException(message, response.StatusCode, problem?.Errors);
    }

    private sealed record ApiProblem(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}
