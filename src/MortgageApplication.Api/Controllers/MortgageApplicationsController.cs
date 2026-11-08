using Microsoft.AspNetCore.Mvc;
using MortgageApplication.Api.Contracts;
using MortgageApplication.Api.Services;

namespace MortgageApplication.Api.Controllers;

[ApiController]
[Route("applications")]
[Tags("Mortgage applications")]
public sealed class MortgageApplicationsController(IMortgageApplicationService service) : ControllerBase
{
    [HttpPost]
    [EndpointSummary("Submit a mortgage application")]
    [EndpointDescription("Valid inputs are persisted even if affordability later declines them. Use one currency. Monthly expenses means continuing debt payments, excluding living costs and housing replaced by this mortgage.")]
    [ProducesResponseType<ApplicationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var errors = ApplicationValidator.Validate(request);
        if (errors.Count != 0)
        {
            return ValidationError(errors);
        }

        var response = await service.CreateAsync(request.ToInputs(), cancellationToken);

        return Created($"/applications/{response.Id}", response);
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Retrieve a saved application")]
    [ProducesResponseType<ApplicationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var application = await service.GetAsync(id, cancellationToken);
        if (application is null)
        {
            return MissingApplication();
        }

        return Ok(application);
    }

    [HttpPost("{applicationId:guid}/assessments")]
    [EndpointSummary("Create and save a new affordability assessment")]
    [EndpointDescription("No request body. Every request creates a new immutable assessment, including declines. Returns input/rule snapshots. Retrying can create another record.")]
    [ProducesResponseType<AssessmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Assess(Guid applicationId, CancellationToken cancellationToken)
    {
        var assessment = await service.AssessAsync(applicationId, cancellationToken);
        if (assessment is null)
        {
            return MissingApplication();
        }

        return Created($"/applications/{applicationId}/assessments/{assessment.Id}", assessment);
    }

    [HttpGet("{applicationId:guid}/assessments/{assessmentId:guid}")]
    [EndpointSummary("Retrieve a saved assessment without recalculating")]
    [ProducesResponseType<AssessmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssessment(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken)
    {
        var assessment = await service.GetAssessmentAsync(applicationId, assessmentId, cancellationToken);
        if (assessment is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "Assessment not found for this application.");
        }

        return Ok(assessment);
    }

    [HttpGet("{applicationId:guid}/assessments")]
    [EndpointSummary("List saved assessments with paging, decision filtering and creation-time sorting")]
    [EndpointDescription("Returns full immutable snapshots. offset defaults to 0 (nonnegative); limit to 10 (1–100); status to all (all, approved, declined); sort to desc (asc or desc). totalCount counts the filtered matches before paging. Timestamp ties use insertion sequence in the same direction. Existing applications with no matches return an empty page; unknown applications return 404.")]
    [ProducesResponseType<AssessmentPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAssessments(Guid applicationId, [FromQuery] AssessmentQuery query, CancellationToken cancellationToken)
    {
        var errors = AssessmentQueryValidator.Validate(query);
        if (errors.Count != 0)
        {
            return ValidationError(errors);
        }

        var page = await service.ListAssessmentsAsync(applicationId, query.Offset, query.Limit, query.Status, query.Sort, cancellationToken);
        if (page is null)
        {
            return MissingApplication();
        }

        return Ok(page);
    }

    private ObjectResult MissingApplication() => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "Application not found.");

    private static ObjectResult ValidationError(Dictionary<string, string[]> errors)
    {
        // Match the established camel-case field keys and HTTP validation-problem shape.
        var result = new ObjectResult(new HttpValidationProblemDetails(errors))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
        result.ContentTypes.Add("application/problem+json");

        return result;
    }
}
