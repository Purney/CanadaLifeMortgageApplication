using Microsoft.AspNetCore.Mvc;

namespace MortgageApplication.Api.Infrastructure;

internal static class ApiProblems
{
    public const string BindingDetail = "Check the request body, route values, and query parameters for the documented format.";

    public static ProblemDetails InvalidRequest(int statusCode = StatusCodes.Status400BadRequest) => new()
    {
        Status = statusCode,
        Title = "Invalid request.",
        Detail = BindingDetail
    };

    public static ObjectResult InvalidModelState(ActionContext context)
    {
        // ModelState contains binding errors only. Do not expose serializer/internal details.
        var result = new ObjectResult(InvalidRequest())
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
        result.ContentTypes.Add("application/problem+json");

        return result;
    }
}
