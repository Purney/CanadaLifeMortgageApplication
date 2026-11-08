using System.Net;

namespace MortgageApplication.Web.Services;

public sealed class MortgageApiException(
    string message,
    HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        errors ?? new Dictionary<string, string[]>();
}
