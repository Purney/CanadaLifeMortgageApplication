using MortgageApplication.Api.Contracts;

namespace MortgageApplication.Api.Services;

public interface IMortgageApplicationService
{
    Task<ApplicationResponse> CreateAsync(ApplicationInputs inputs, CancellationToken cancellationToken);
    Task<ApplicationResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<AssessmentResponse?> AssessAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<AssessmentResponse?> GetAssessmentAsync(Guid applicationId, Guid assessmentId, CancellationToken cancellationToken);
    Task<AssessmentPage?> ListAssessmentsAsync(Guid applicationId, int offset, int limit, string status, string sort, CancellationToken cancellationToken);
}
