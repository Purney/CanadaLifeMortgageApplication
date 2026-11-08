using MortgageApplication.Api.Contracts;
using MortgageApplication.Api.Services;

namespace MortgageApplication.Tests;

public sealed class ValidationTests
{
    private static readonly CreateApplicationRequest Valid = new()
    {
        AnnualIncome = 60000,
        MonthlyExpenses = 500,
        DepositAmount = 20000,
        PropertyValue = 200000,
        TermYears = 25
    };

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [Valid with { AnnualIncome = null }, "annualIncome"];
        yield return [Valid with { AnnualIncome = 0 }, "annualIncome"];
        yield return [Valid with { AnnualIncome = -1 }, "annualIncome"];
        yield return [Valid with { AnnualIncome = 1_000_000_000.01m }, "annualIncome"];
        yield return [Valid with { AnnualIncome = 100.001m }, "annualIncome"];
        yield return [Valid with { MonthlyExpenses = null }, "monthlyExpenses"];
        yield return [Valid with { MonthlyExpenses = -1 }, "monthlyExpenses"];
        yield return [Valid with { MonthlyExpenses = 1_000_000_000.01m }, "monthlyExpenses"];
        yield return [Valid with { MonthlyExpenses = .001m }, "monthlyExpenses"];
        yield return [Valid with { DepositAmount = null }, "depositAmount"];
        yield return [Valid with { DepositAmount = -1 }, "depositAmount"];
        yield return [Valid with { DepositAmount = 200000 }, "depositAmount"];
        yield return [Valid with { DepositAmount = 200001 }, "depositAmount"];
        yield return [Valid with { DepositAmount = .001m }, "depositAmount"];
        yield return [Valid with { PropertyValue = null }, "propertyValue"];
        yield return [Valid with { PropertyValue = 0 }, "propertyValue"];
        yield return [Valid with { PropertyValue = -1 }, "propertyValue"];
        yield return [Valid with { PropertyValue = 1_000_000_000.01m }, "propertyValue"];
        yield return [Valid with { PropertyValue = 100000.001m }, "propertyValue"];
        yield return [Valid with { TermYears = null }, "termYears"];
        yield return [Valid with { TermYears = 0 }, "termYears"];
        yield return [Valid with { TermYears = 41 }, "termYears"];
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void CriticalRulesReturnFieldErrors(CreateApplicationRequest request, string field) =>
        Assert.Contains(field, ApplicationValidator.Validate(request));

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void ValidZeroValuesTrailingZerosAndTermBoundariesAreAccepted(int term) =>
        Assert.Empty(ApplicationValidator.Validate(Valid with
        {
            MonthlyExpenses = 0.000m,
            DepositAmount = 0,
            TermYears = term
        }));

    [Fact]
    public void InclusiveMaximumsAndDebtAboveIncomeAreValidInput() =>
        Assert.Empty(ApplicationValidator.Validate(Valid with
        {
            AnnualIncome = 1_000_000_000m,
            PropertyValue = 1_000_000_000m,
            MonthlyExpenses = 1_000_000_000m
        }));
}
