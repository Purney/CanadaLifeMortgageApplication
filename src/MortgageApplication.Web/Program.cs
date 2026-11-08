using Microsoft.Extensions.Options;
using MortgageApplication.Web.Components;
using MortgageApplication.Web.Services;

namespace MortgageApplication.Web;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        builder.Services.AddOptions<MortgageApiOptions>().Bind(builder.Configuration.GetSection("MortgageApi"))
            .Validate(MortgageApiOptions.IsValid, "MortgageApi:ApiBaseUrl must be an absolute HTTP or HTTPS URL without credentials, query or fragment.")
            .ValidateOnStart();

        builder.Services.AddHttpClient<MortgageApiClient>((services, http) =>
        {
            var options = services.GetRequiredService<IOptions<MortgageApiOptions>>().Value;

            http.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(20);
        });

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/error", createScopeForErrors: true);
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        app.Run();
    }
}
