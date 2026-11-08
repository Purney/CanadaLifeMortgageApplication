using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MortgageApplication.Api.Configuration;
using MortgageApplication.Api.Contracts;
using MortgageApplication.Api.Data;
using MortgageApplication.Api.Infrastructure;
using MortgageApplication.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers(options =>
{
    // Attributes describe Swagger schemas; our validators own business rules and messages.
    options.ModelValidatorProviders.Clear();
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
}).ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = ApiProblems.InvalidModelState;
});
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IValidateOptions<AffordabilityOptions>, AffordabilityOptionsValidator>();
builder.Services.AddOptions<AffordabilityOptions>()
    .BindConfiguration("Affordability")
    .ValidateOnStart();
builder.Services.AddSingleton<AssessmentPolicy>(services =>
    services.GetRequiredService<IOptions<AffordabilityOptions>>().Value.ToPolicy());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AffordabilityCalculator>();
builder.Services.AddScoped<IMortgageApplicationService, MortgageApplicationService>();
builder.Services.AddDbContext<MortgageDbContext>((services, options) =>
{
    var connectionString = services.GetRequiredService<IConfiguration>()
        .GetConnectionString("MortgageDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:MortgageDatabase is required.");

    options.UseSqlite(connectionString);
});

var app = builder.Build();

// Freeze the effective policy before serving requests or migrating.
_ = app.Services.GetRequiredService<AssessmentPolicy>();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var database = scope.ServiceProvider.GetRequiredService<MortgageDbContext>();

    await database.Database.MigrateAsync();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

await app.RunAsync();

public partial class Program;
