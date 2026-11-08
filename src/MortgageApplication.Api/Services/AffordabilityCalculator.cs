using System.Globalization;
using MortgageApplication.Api.Contracts;

namespace MortgageApplication.Api.Services;

public sealed class AffordabilityCalculator
{
    public const string CalculationVersion = "1";

    private const int MonthsPerYear = 12;
    private const decimal PercentageScale = 100m;
    private const decimal AnnualPercentageToMonthlyRate = 1200m;

    public AssessmentResult Calculate(ApplicationInputs inputs, AssessmentPolicy policy)
    {
        var annuityFactor = CalculateAnnuityFactor(inputs.TermYears, policy.AnnualInterestRatePercentage);
        var loanAmount = inputs.PropertyValue - inputs.DepositAmount;
        var monthlyRepayment = loanAmount / annuityFactor;
        var ltvPercentage = loanAmount * PercentageScale / inputs.PropertyValue;

        // Cross-multiply policy comparisons before dividing. AnnualIncome / 12 can
        // repeat in decimal and incorrectly reject exact DTI equality on small inputs.
        var discountedDebt = inputs.MonthlyExpenses * annuityFactor;
        var dtiNumerator = (discountedDebt + loanAmount) * AnnualPercentageToMonthlyRate;
        var dtiDenominator = inputs.AnnualIncome * annuityFactor;
        var dtiLimit = policy.MaxDtiPercentage * dtiDenominator;
        var dtiPercentage = dtiNumerator / dtiDenominator;

        var paymentLimitedLoan = Math.Max(
            0m,
            (dtiLimit - discountedDebt * AnnualPercentageToMonthlyRate) / AnnualPercentageToMonthlyRate);
        var incomeCap = policy.MaxIncomeMultiple * inputs.AnnualIncome;
        var propertyCap = policy.MaxLtvPercentage * inputs.PropertyValue / PercentageScale;
        var borrowingCapacity = Math.Min(paymentLimitedLoan, incomeCap);

        var declineReasons = new List<string>();

        if (loanAmount * PercentageScale > policy.MaxLtvPercentage * inputs.PropertyValue)
        {
            declineReasons.Add($"LTV exceeds {Format(policy.MaxLtvPercentage)}%.");
        }

        if (dtiNumerator > dtiLimit)
        {
            declineReasons.Add($"DTI exceeds {Format(policy.MaxDtiPercentage)}%.");
        }

        if (loanAmount > incomeCap)
        {
            declineReasons.Add($"Requested loan exceeds {Format(policy.MaxIncomeMultiple)} times annual income.");
        }

        var approved = declineReasons.Count == 0;
        var explanation = approved
            ? "All configured affordability limits are met."
            : string.Join(" ", declineReasons);

        return new AssessmentResult(
            loanAmount,
            monthlyRepayment,
            ltvPercentage,
            dtiPercentage,
            approved ? "approved" : "declined",
            borrowingCapacity,
            Math.Min(borrowingCapacity, propertyCap),
            explanation);
    }

    private static decimal CalculateAnnuityFactor(int termYears, decimal annualInterestRatePercentage)
    {
        // Discounted monthly payments sum to the loan's present value. The recurrence
        // avoids subtracting nearly equal values at small interest rates and works at zero.
        var discount = 1m / (1m + annualInterestRatePercentage / AnnualPercentageToMonthlyRate);
        var annuityFactor = 0m;
        var discountedPayment = 1m;

        for (var month = 0; month < termYears * MonthsPerYear; month++)
        {
            discountedPayment *= discount;
            annuityFactor += discountedPayment;
        }

        return annuityFactor;
    }

    private static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
