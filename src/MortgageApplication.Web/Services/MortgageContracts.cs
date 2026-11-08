namespace MortgageApplication.Web.Services;

public sealed record MortgageInputs(
    decimal AnnualIncome,
    decimal MonthlyExpenses,
    decimal DepositAmount,
    decimal PropertyValue,
    int TermYears);

public sealed record ApplicationRecord(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    MortgageInputs Inputs);

public sealed record AppliedPolicy(
    decimal AnnualInterestRatePercentage,
    decimal MaxLtvPercentage,
    decimal MaxDtiPercentage,
    decimal MaxIncomeMultiple);

public sealed record AssessmentFigures(
    decimal LoanAmount,
    decimal MonthlyRepayment,
    decimal LtvPercentage,
    decimal DtiPercentage,
    string Decision,
    decimal AffordabilityBasedBorrowingCapacity,
    decimal MaximumBorrowingForProperty,
    string Explanation);

public sealed record AssessmentRecord(
    Guid Id,
    Guid ApplicationId,
    DateTimeOffset CreatedAtUtc,
    string CalculationVersion,
    MortgageInputs Inputs,
    AppliedPolicy AppliedRules,
    AssessmentFigures Result);

public sealed record AssessmentHistory(
    IReadOnlyList<AssessmentRecord> Items,
    int Offset,
    int Limit,
    int TotalCount);
