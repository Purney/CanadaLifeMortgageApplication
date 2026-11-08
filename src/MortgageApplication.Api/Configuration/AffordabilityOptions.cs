using Microsoft.Extensions.Options;
using MortgageApplication.Api.Contracts;

namespace MortgageApplication.Api.Configuration;

public sealed class AffordabilityOptions
{
    public decimal? AnnualInterestRatePercentage { get; set; }
    public decimal? MaxLtvPercentage { get; set; }
    public decimal? MaxDtiPercentage { get; set; }
    public decimal? MaxIncomeMultiple { get; set; }

    public AssessmentPolicy ToPolicy() => new(AnnualInterestRatePercentage!.Value,
        MaxLtvPercentage!.Value, MaxDtiPercentage!.Value, MaxIncomeMultiple!.Value);
}

public sealed class AffordabilityOptionsValidator : IValidateOptions<AffordabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, AffordabilityOptions value)
    {
        var errors = new List<string>();
        Check(value.AnnualInterestRatePercentage, 0, 100, true, nameof(value.AnnualInterestRatePercentage));
        Check(value.MaxLtvPercentage, 0, 100, false, nameof(value.MaxLtvPercentage));
        Check(value.MaxDtiPercentage, 0, 100, false, nameof(value.MaxDtiPercentage));
        Check(value.MaxIncomeMultiple, 0, 100, false, nameof(value.MaxIncomeMultiple));

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);

        void Check(decimal? number, decimal min, decimal max, bool inclusiveMin, string key)
        {
            if (number is null || number > max || (inclusiveMin ? number < min : number <= min))
            {
                errors.Add($"Affordability:{key} is required and must be {(inclusiveMin ? "at least" : "greater than")} {min} and at most {max}.");
            }
        }
    }
}
