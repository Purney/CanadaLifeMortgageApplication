using System.ComponentModel;

namespace MortgageApplication.Api.Contracts;

public sealed record AssessmentPolicy(
    decimal AnnualInterestRatePercentage,
    decimal MaxLtvPercentage,
    decimal MaxDtiPercentage,
    decimal MaxIncomeMultiple);

public sealed record AssessmentResult(
    decimal LoanAmount,
    decimal MonthlyRepayment,
    [property: Description("Loan-to-value as a percentage, e.g. 90 means 90%.")] decimal LtvPercentage,
    [property: Description("Existing debt plus proposed repayment divided by gross monthly income, as a percentage.")] decimal DtiPercentage,
    [property: Description("approved or declined; all configured limits must pass.")] string Decision,
    [property: Description("Total principal supported by income, debt, term and rate, before a property LTV cap.")] decimal AffordabilityBasedBorrowingCapacity,
    [property: Description("Total principal capacity further capped by this property's configured maximum LTV.")] decimal MaximumBorrowingForProperty,
    string Explanation)
{
    public AssessmentResult Rounded() => this with
    {
        LoanAmount = Round(LoanAmount),
        MonthlyRepayment = Round(MonthlyRepayment),
        LtvPercentage = Round(LtvPercentage),
        DtiPercentage = Round(DtiPercentage),
        AffordabilityBasedBorrowingCapacity = Round(AffordabilityBasedBorrowingCapacity),
        MaximumBorrowingForProperty = Round(MaximumBorrowingForProperty)
    };

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.ToEven);
}

public sealed record AssessmentResponse(
    Guid Id,
    Guid ApplicationId,
    DateTimeOffset CreatedAtUtc,
    string CalculationVersion,
    ApplicationInputs Inputs,
    AssessmentPolicy AppliedRules,
    AssessmentResult Result)
{
    public AssessmentResponse Rounded() => this with
    {
        Result = Result.Rounded()
    };
}

public sealed record AssessmentPage(
    IReadOnlyList<AssessmentResponse> Items,
    int Offset,
    int Limit,
    int TotalCount);
