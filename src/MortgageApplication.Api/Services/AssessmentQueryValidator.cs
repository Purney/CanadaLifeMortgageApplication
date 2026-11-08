using MortgageApplication.Api.Contracts;

namespace MortgageApplication.Api.Services;

public static class AssessmentQueryValidator
{
    public static Dictionary<string, string[]> Validate(AssessmentQuery query)
    {
        var errors = new Dictionary<string, string[]>();
        if (query.Offset < 0)
        {
            errors["offset"] = ["Offset must be a whole number greater than or equal to zero."];
        }

        if (query.Limit is < 1 or > 100)
        {
            errors["limit"] = ["Limit must be a whole number from 1 to 100."];
        }

        if (query.Status is not ("all" or "approved" or "declined"))
        {
            errors["status"] = ["Status must be all, approved, or declined."];
        }

        if (query.Sort is not ("asc" or "desc"))
        {
            errors["sort"] = ["Sort must be asc or desc."];
        }

        return errors;
    }
}
