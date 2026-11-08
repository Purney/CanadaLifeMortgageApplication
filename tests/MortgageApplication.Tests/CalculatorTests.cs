using MortgageApplication.Api.Contracts;
using MortgageApplication.Api.Services;

namespace MortgageApplication.Tests;

public sealed class CalculatorTests
{
    private readonly AffordabilityCalculator calculator = new();

    private static readonly AssessmentPolicy Policy = new(6, 90, 45, 4.5m);

    private static readonly ApplicationInputs Sample = new(60000, 500, 20000, 200000, 25);

    [Fact]
    public void KnownExampleMatchesIndependentAnnuityCalculation()
    {
        var result = calculator.Calculate(Sample, Policy);
        // Independent closed-form double calculation, not the implementation's recurrence.
        var expectedPayment = 180000 * .005 / (1 - Math.Pow(1.005, -300));

        Assert.InRange((double)result.MonthlyRepayment, expectedPayment - .000001, expectedPayment + .000001);
        Assert.Equal(1159.74m, result.Rounded().MonthlyRepayment);
        Assert.Equal(90m, result.LtvPercentage);
        Assert.Equal(33.19m, result.Rounded().DtiPercentage);
        Assert.Equal(270000m, result.AffordabilityBasedBorrowingCapacity);
        Assert.Equal(180000m, result.MaximumBorrowingForProperty);
        Assert.Equal("approved", result.Decision);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00000000000000000001")]
    public void ZeroAndNearZeroInterestRemainStable(string rate)
    {
        var result = calculator.Calculate(Sample, Policy with
        {
            AnnualInterestRatePercentage = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)
        });

        Assert.InRange(result.MonthlyRepayment, 600m, 600.000000000001m);
        Assert.Equal("approved", result.Decision);
    }

    [Fact]
    public void ZeroRateBorrowingUsesPaymentBudgetTimesNumberOfPayments()
    {
        var result = calculator.Calculate(new(12000, 400, 10000, 100000, 1), new(0, 90, 45, 100));

        Assert.Equal(600m, result.AffordabilityBasedBorrowingCapacity);
        Assert.Equal(7500m, result.MonthlyRepayment);
    }

    [Fact]
    public void EachFailedRuleIsExplained()
    {
        var result = calculator.Calculate(new(10000, 1000, 0, 200000, 25), Policy);

        Assert.Equal("declined", result.Decision);
        Assert.Contains("LTV", result.Explanation);
        Assert.Contains("DTI", result.Explanation);
        Assert.Contains("annual income", result.Explanation);
        Assert.Equal(0m, result.MaximumBorrowingForProperty);
    }

    [Fact]
    public void LtvEqualityPassesAndJustAboveFailsWithoutResponseRoundingAffectingDecision()
    {
        Assert.Equal("approved", calculator.Calculate(Sample, Policy).Decision);

        var result = calculator.Calculate(Sample with
        {
            DepositAmount = 19999.99m
        }, Policy);

        Assert.Equal("declined", result.Decision);
        Assert.Equal(90m, result.Rounded().LtvPercentage);
        Assert.Contains("LTV", result.Explanation);
        Assert.DoesNotContain("DTI", result.Explanation);
    }

    [Fact]
    public void DtiEqualityPassesAndOnePennyMoreDebtFails()
    {
        var inputs = new ApplicationInputs(12000, 400, 9400, 10000, 1);
        var policy = new AssessmentPolicy(0, 90, 45, 100);

        Assert.Equal(45m, calculator.Calculate(inputs, policy).DtiPercentage);
        Assert.Equal("approved", calculator.Calculate(inputs, policy).Decision);

        var result = calculator.Calculate(inputs with
        {
            MonthlyExpenses = 400.01m
        }, policy);

        Assert.Equal("declined", result.Decision);
        Assert.Contains("DTI", result.Explanation);
        Assert.DoesNotContain("LTV", result.Explanation);
    }

    [Fact]
    public void DtiEqualityWithRepeatingMonthlyIncomeApprovesAndCapacityMatchesLoan()
    {
        var inputs = new ApplicationInputs(1m, 0m, .55m, 1m, 1);
        var policy = Policy with
        {
            AnnualInterestRatePercentage = 0
        };
        var result = calculator.Calculate(inputs, policy);

        Assert.Equal(45m, result.DtiPercentage);
        Assert.Equal(.0375m, result.MonthlyRepayment);
        Assert.Equal(.45m, result.AffordabilityBasedBorrowingCapacity);
        Assert.Equal(result.LoanAmount, result.MaximumBorrowingForProperty);
        Assert.Equal("approved", result.Decision);

        var moreDebt = calculator.Calculate(inputs with
        {
            MonthlyExpenses = .01m
        }, policy);

        Assert.Equal("declined", moreDebt.Decision);
        Assert.Contains("DTI", moreDebt.Explanation);
        Assert.Equal(.33m, moreDebt.MaximumBorrowingForProperty);
        Assert.True(moreDebt.MaximumBorrowingForProperty < moreDebt.LoanAmount);

        var moreLoan = calculator.Calculate(inputs with
        {
            DepositAmount = .54m
        }, policy);

        Assert.Equal("declined", moreLoan.Decision);
        Assert.Contains("DTI", moreLoan.Explanation);
        Assert.Equal(.45m, moreLoan.MaximumBorrowingForProperty);
    }

    [Fact]
    public void SmallIncomeDtiEqualityAlsoWorksWithExistingDebt()
    {
        var result = calculator.Calculate(new(1m, .01m, .67m, 1m, 1), Policy with
        {
            AnnualInterestRatePercentage = 0
        });

        Assert.Equal(45m, result.DtiPercentage);
        Assert.Equal(.33m, result.MaximumBorrowingForProperty);
        Assert.Equal("approved", result.Decision);
    }

    [Fact]
    public void CrossMultipliedComparisonsSupportAcceptedExtremeInputsWithoutOverflow()
    {
        var policy = new AssessmentPolicy(0, 100, 100, 100);
        var highCapacity = calculator.Calculate(new(1_000_000_000m, 0, 0, 1_000_000_000m, 40), policy);

        Assert.Equal("approved", highCapacity.Decision);
        Assert.Equal(40_000_000_000m, highCapacity.AffordabilityBasedBorrowingCapacity);

        var highDebt = calculator.Calculate(new(.01m, 1_000_000_000m, 0, 1_000_000_000m, 40), policy);

        Assert.Equal("declined", highDebt.Decision);
        Assert.Equal(0m, highDebt.MaximumBorrowingForProperty);
        Assert.True(highDebt.DtiPercentage > 100m);
    }

    [Fact]
    public void IncomeMultipleEqualityPassesAndOnePennyMoreLoanFails()
    {
        var inputs = new ApplicationInputs(60000, 0, 30000, 300000, 40);

        Assert.Equal("approved", calculator.Calculate(inputs, Policy).Decision);

        var result = calculator.Calculate(inputs with
        {
            DepositAmount = 29999.99m
        }, Policy with
        {
            MaxLtvPercentage = 100
        });

        Assert.Equal("declined", result.Decision);
        Assert.Contains("annual income", result.Explanation);
        Assert.DoesNotContain("DTI", result.Explanation);
    }

    [Fact]
    public void GreaterTermReducesPaymentAndHigherDebtReducesCapacity()
    {
        var shortTerm = calculator.Calculate(Sample with
        {
            TermYears = 10
        }, Policy);
        var longTerm = calculator.Calculate(Sample, Policy);

        Assert.True(longTerm.MonthlyRepayment < shortTerm.MonthlyRepayment);

        var highDebt = calculator.Calculate(Sample with
        {
            MonthlyExpenses = 2000
        }, Policy);

        Assert.True(highDebt.AffordabilityBasedBorrowingCapacity < longTerm.AffordabilityBasedBorrowingCapacity);
        Assert.True(calculator.Calculate(Sample with
        {
            DepositAmount = 40000
        }, Policy).LtvPercentage < longTerm.LtvPercentage);

        Assert.True(calculator.Calculate(Sample with
        {
            AnnualIncome = 120000
        }, Policy).DtiPercentage < longTerm.DtiPercentage);
    }

    [Fact]
    public void BorrowingEstimatesAgreeWhenPropertyDoesNotLimitCapacity()
    {
        var result = calculator.Calculate(Sample with
        {
            PropertyValue = 1000000
        }, Policy);

        Assert.Equal(result.AffordabilityBasedBorrowingCapacity, result.MaximumBorrowingForProperty);
    }

    [Theory]
    [InlineData(2250)]
    [InlineData(5000)]
    public void ExhaustedOrNegativeBudgetHasZeroCapacity(int expenses)
    {
        var result = calculator.Calculate(Sample with
        {
            MonthlyExpenses = expenses
        }, Policy);

        Assert.Equal(0, result.AffordabilityBasedBorrowingCapacity);
        Assert.Equal("declined", result.Decision);
    }

    [Fact]
    public void PolicyOverridesChangeDecisionAndBothCapacities()
    {
        var baseline = calculator.Calculate(Sample, Policy);
        var changed = calculator.Calculate(Sample, Policy with
        {
            MaxIncomeMultiple = 2
        });

        Assert.Equal("declined", changed.Decision);
        Assert.Equal(120000m, changed.AffordabilityBasedBorrowingCapacity);
        Assert.Equal(120000m, changed.MaximumBorrowingForProperty);
        Assert.True(changed.MaximumBorrowingForProperty < baseline.MaximumBorrowingForProperty);
        Assert.True(calculator.Calculate(Sample, Policy with
        {
            AnnualInterestRatePercentage = 10
        }).MonthlyRepayment > baseline.MonthlyRepayment);
    }
}
