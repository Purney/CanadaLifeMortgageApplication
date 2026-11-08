using MortgageApplication.Api.Contracts;

namespace MortgageApplication.Api.Models;

public sealed class MortgageApplication
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public decimal AnnualIncome { get; set; }
    public decimal MonthlyExpenses { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal PropertyValue { get; set; }
    public int TermYears { get; set; }

    public ApplicationInputs ToInputs() => new(AnnualIncome, MonthlyExpenses, DepositAmount, PropertyValue, TermYears);

    public ApplicationResponse ToResponse() => new(Id, new DateTimeOffset(DateTime.SpecifyKind(CreatedAtUtc, DateTimeKind.Utc)), ToInputs());
}
