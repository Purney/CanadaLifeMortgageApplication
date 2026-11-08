using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace MortgageApplication.Api.Contracts;

public sealed class AssessmentQuery
{
    [FromQuery(Name = "offset")]
    [DefaultValue(0)]
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [FromQuery(Name = "limit")]
    [DefaultValue(10)]
    [Range(1, 100)]
    public int Limit { get; set; } = 10;

    [FromQuery(Name = "status")]
    [DefaultValue("all")]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Status { get; set; } = "all";

    [FromQuery(Name = "sort")]
    [DefaultValue("desc")]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string Sort { get; set; } = "desc";
}
