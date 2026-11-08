using System.Globalization;

namespace MortgageApplication.Web.Services;

public static class DisplayFormat
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    public static string Money(decimal value) => value.ToString("C2", British);

    public static string Percentage(decimal value) => value.ToString("0.##", British) + "%";

    public static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("dd MMM yyyy 'at' HH:mm:ss 'UTC'", British);
}
