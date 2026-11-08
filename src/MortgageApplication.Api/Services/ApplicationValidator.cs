using MortgageApplication.Api.Contracts;

namespace MortgageApplication.Api.Services;

public static class ApplicationValidator
{
    public static Dictionary<string, string[]> Validate(CreateApplicationRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        ValidateMoney(request.AnnualIncome, "annualIncome", positive: true);
        ValidateMoney(request.MonthlyExpenses, "monthlyExpenses");
        ValidateMoney(request.DepositAmount, "depositAmount");
        ValidateMoney(request.PropertyValue, "propertyValue", positive: true);

        if (request.DepositAmount is not null && request.PropertyValue is not null && request.DepositAmount >= request.PropertyValue)
        {
            errors["depositAmount"] = ["Deposit must be less than property value."];
        }

        if (request.TermYears is null or < 1 or > 40)
        {
            errors["termYears"] = ["Term is required and must be a whole number from 1 to 40."];
        }

        return errors;

        void ValidateMoney(decimal? value, string key, bool positive = false)
        {
            if (value is null)
            {
                errors[key] = ["This field is required."];
            }
            else if (value < 0 || (positive && value == 0) || value > 1_000_000_000m)
            {
                errors[key] = [$"Must be {(positive ? "positive" : "nonnegative")} and at most 1,000,000,000."];
            }
            else if (decimal.Round(value.Value, 2) != value.Value)
            {
                errors[key] = ["Must have at most two decimal places."];
            }
        }
    }
}
