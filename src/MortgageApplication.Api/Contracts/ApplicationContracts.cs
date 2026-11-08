using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MortgageApplication.Api.Contracts;

public sealed record CreateApplicationRequest
{
    [Description("Gross annual income. Positive, at most 1 billion; up to two decimal places.")]
    [Required, Range(typeof(decimal), "0.01", "1000000000")]
    public decimal? AnnualIncome { get; init; }

    [Description("Continuing monthly debt payments, excluding living costs and housing payments replaced by this mortgage. Nonnegative; at most 1 billion, up to two decimals.")]
    [Required, Range(typeof(decimal), "0", "1000000000")]
    public decimal? MonthlyExpenses { get; init; }

    [Description("Deposit: nonnegative and below property value; at most 1 billion, up to two decimals.")]
    [Required, Range(typeof(decimal), "0", "1000000000")]
    public decimal? DepositAmount { get; init; }

    [Description("Positive property value, at most 1 billion; up to two decimal places.")]
    [Required, Range(typeof(decimal), "0.01", "1000000000")]
    public decimal? PropertyValue { get; init; }

    [Description("Whole years from 1 to 40 inclusive.")]
    [Required, Range(1, 40)]
    public int? TermYears { get; init; }

    public ApplicationInputs ToInputs() => new(
        AnnualIncome!.Value,
        MonthlyExpenses!.Value,
        DepositAmount!.Value,
        PropertyValue!.Value,
        TermYears!.Value);
}

public sealed record ApplicationInputs(
    decimal AnnualIncome,
    decimal MonthlyExpenses,
    decimal DepositAmount,
    decimal PropertyValue,
    int TermYears);

public sealed record ApplicationResponse(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    ApplicationInputs Inputs);
