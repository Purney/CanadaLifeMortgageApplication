using Microsoft.AspNetCore.Diagnostics;

namespace MortgageApplication.Api.Infrastructure;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var badRequest = exception as BadHttpRequestException;
        context.Response.StatusCode = badRequest?.StatusCode ?? StatusCodes.Status500InternalServerError;

        var problem = badRequest is not null
            ? ApiProblems.InvalidRequest(context.Response.StatusCode)
            : new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = context.Response.StatusCode,
                Title = "An unexpected error occurred.",
                Detail = "The request could not be completed."
            };

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        });
    }
}
