using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MortgageApplication.Tests;

internal sealed class ApiFactory(string databasePath, string environment = "Development",
    Dictionary<string, string?>? overrides = null, TimeProvider? clock = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:MortgageDatabase"] = $"Data Source={databasePath};Pooling=False",
                ["Logging:LogLevel:Default"] = "Error"
            };
            if (overrides is not null)
            {
                foreach (var item in overrides)
                {
                    settings[item.Key] = item.Value;
                }
            }

            config.AddInMemoryCollection(settings);
        });

        if (clock is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            });
        }
    }
}

internal sealed class FrozenClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
}
