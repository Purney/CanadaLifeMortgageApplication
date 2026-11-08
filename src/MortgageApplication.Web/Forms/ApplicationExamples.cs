namespace MortgageApplication.Web.Forms;

public sealed record ApplicationExample(string Key, string Title, string Description);

public static class ApplicationExamples
{
    public static IReadOnlyList<ApplicationExample> All { get; } = Array.AsReadOnly<ApplicationExample>([
        new("standard", "Standard figures", "A valid sample. Save the application, then run an assessment to see the default policy approve it."),
        new("missing", "Required fields", "Leave all five fields blank. Choose Save application to see the API return the required-field messages."),
        new("income", "Income outside the range", "Income is zero. Choose Save application to see the API return the positive-income rule."),
        new("deposit", "Deposit matches the property", "Deposit equals the property value. Choose Save application to see the API return the relationship rule."),
        new("precision", "Too many decimal places", "Income has three decimal places. Choose Save application to see the API return the money precision rule."),
        new("term", "Term outside the range", "The term is 41 years. Choose Save application to see the API return the 1–40 year limit."),
        new("declined", "A declined assessment", "These figures are valid, with a zero deposit. Save the application, then run an assessment: the default 90% LTV policy declines it. This is a business outcome, not a service error.")
    ]);

    public static ApplicationExample? Find(string? key) => All.FirstOrDefault(example => example.Key == key);

    public static ApplicationForm Create(string? key)
    {
        var model = new ApplicationForm();
        if (Find(key) is null || key == "missing")
        {
            return model;
        }

        model.FillExample();

        switch (key)
        {
            case "income":
                model.AnnualIncome = "0";
                break;

            case "deposit":
                model.DepositAmount = model.PropertyValue;
                break;

            case "precision":
                model.AnnualIncome = "60000.001";
                break;

            case "term":
                model.TermYears = "41";
                break;

            case "declined":
                model.DepositAmount = "0";
                break;
        }

        return model;
    }
}
