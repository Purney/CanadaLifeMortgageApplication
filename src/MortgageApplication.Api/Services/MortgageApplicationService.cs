using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MortgageApplication.Api.Contracts;
using MortgageApplication.Api.Data;
using MortgageApplication.Api.Models;
using ApplicationEntity = MortgageApplication.Api.Models.MortgageApplication;

namespace MortgageApplication.Api.Services;

public sealed class MortgageApplicationService(
    MortgageDbContext database,
    AffordabilityCalculator calculator,
    AssessmentPolicy policy,
    TimeProvider clock) : IMortgageApplicationService
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApplicationResponse> CreateAsync(ApplicationInputs inputs, CancellationToken cancellationToken)
    {
        var application = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            AnnualIncome = inputs.AnnualIncome,
            MonthlyExpenses = inputs.MonthlyExpenses,
            DepositAmount = inputs.DepositAmount,
            PropertyValue = inputs.PropertyValue,
            TermYears = inputs.TermYears
        };
        database.Applications.Add(application);
        await database.SaveChangesAsync(cancellationToken);

        return application.ToResponse();
    }

    public async Task<ApplicationResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await database.Applications
            .AsNoTracking()
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);

        return application?.ToResponse();
    }

    public async Task<AssessmentResponse?> AssessAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await database.Applications
            .AsNoTracking()
            .SingleOrDefaultAsync(application => application.Id == applicationId, cancellationToken);
        if (application is null)
        {
            return null;
        }

        var inputs = application.ToInputs();
        var assessedAt = clock.GetUtcNow();
        var snapshot = new AssessmentResponse(
            Guid.NewGuid(),
            applicationId,
            assessedAt,
            AffordabilityCalculator.CalculationVersion,
            inputs,
            policy,
            calculator.Calculate(inputs, policy));

        database.Assessments.Add(new AffordabilityAssessment
        {
            Id = snapshot.Id,
            ApplicationId = applicationId,
            CreatedAtUtcTicks = assessedAt.UtcTicks,
            SnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotOptions)
        });
        await database.SaveChangesAsync(cancellationToken);

        return snapshot.Rounded();
    }

    public async Task<AssessmentResponse?> GetAssessmentAsync(
        Guid applicationId,
        Guid assessmentId,
        CancellationToken cancellationToken)
    {
        var assessment = await database.Assessments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                assessment => assessment.ApplicationId == applicationId && assessment.Id == assessmentId,
                cancellationToken);

        return ReadSnapshot(assessment);
    }

    public async Task<AssessmentPage?> ListAssessmentsAsync(
        Guid applicationId,
        int offset,
        int limit,
        string status,
        string sort,
        CancellationToken cancellationToken)
    {
        var applicationExists = await database.Applications.AnyAsync(application => application.Id == applicationId, cancellationToken);
        if (!applicationExists)
        {
            return null;
        }

        // The JSON path and SQL structure are fixed; the decision is a parameter.
        // Filter, count, order and page in SQLite before deserializing snapshots.
        IQueryable<AffordabilityAssessment> source = status == "all"
            ? database.Assessments
            : database.Assessments.FromSqlInterpolated($"SELECT * FROM \"Assessments\" WHERE json_extract(\"SnapshotJson\", '$.result.decision') = {status}");
        var query = source.AsNoTracking().Where(assessment => assessment.ApplicationId == applicationId);
        var totalCount = await query.CountAsync(cancellationToken);

        var ordered = sort == "asc"
            ? query.OrderBy(assessment => assessment.CreatedAtUtcTicks).ThenBy(assessment => assessment.Sequence)
            : query.OrderByDescending(assessment => assessment.CreatedAtUtcTicks).ThenByDescending(assessment => assessment.Sequence);
        var assessments = await ordered.Skip(offset).Take(limit).ToListAsync(cancellationToken);
        var snapshots = assessments.Select(assessment => ReadSnapshot(assessment)!).ToArray();

        return new AssessmentPage(snapshots, offset, limit, totalCount);
    }

    private static AssessmentResponse? ReadSnapshot(AffordabilityAssessment? assessment)
    {
        if (assessment is null)
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<AssessmentResponse>(assessment.SnapshotJson, SnapshotOptions)
            ?? throw new InvalidOperationException("Assessment snapshot is invalid.");

        return snapshot.Rounded();
    }
}
