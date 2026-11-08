# Mortgage Application API

An ASP.NET Core 10 JSON API for submitting and retrieving mortgage applications and performing affordability assessments. The solution includes an optional Blazor frontend and xUnit tests.

## Setup

Install Git and the **.NET 10 SDK**. No database server or credentials are required.

```powershell
git clone https://github.com/Purney/CanadaLifeMortgageApplication.git
cd CanadaLifeMortgageApplication
dotnet restore
dotnet build
```

## Run the application

From the solution directory:

```powershell
dotnet run --project src/MortgageApplication.Api
```

Open [Swagger UI](http://localhost:5080/swagger/index.html) to use the JSON API. The default launch profile uses Development and applies the committed EF Core migrations automatically. SQLite creates `mortgages.db` in the API's working directory; keep this file to preserve data across restarts.

To run the optional frontend, open a second terminal in the solution directory:

```powershell
dotnet run --project src/MortgageApplication.Web
```

Open [the frontend](http://localhost:5082/). Keep the API running; the frontend connects to `http://localhost:5080/` by default.

## Run tests

From the solution directory:

```powershell
dotnet test
```

The tests cover affordability calculations, critical validation rules, API responses, persistence across restarts, saved assessments and frontend integration. Database tests use isolated temporary SQLite files.

## Design and reflection

### Key design decisions

1. **Separate HTTP handling from business logic.** Controllers delegate persistence and assessment orchestration to an application service. Separate validators own input rules, and a pure calculator makes affordability logic testable without HTTP or a database. The optional frontend consumes the API and displays its validation results.
2. **Use SQLite with EF Core migrations.** Applications persist across restarts, while local setup needs no database server. Committed migrations provide a repeatable schema.
3. **Save assessment snapshots.** Each assessment records its inputs, applied rules, result, timestamp and calculation version. Retrieving an earlier assessment returns its saved result, even after policy changes.

### Assessment assumptions

Income is gross annual income. Monthly expenses mean continuing debt payments, excluding living costs and housing payments replaced by the mortgage. All amounts use the same currency.

The requested loan is property value minus deposit. Monthly repayment uses a repayment-mortgage calculation over the chosen term. LTV is loan divided by property value; DTI is existing monthly debt plus the proposed repayment, divided by gross monthly income.

Default approval limits are **90% LTV**, **45% DTI** and **4.5 times annual income**, using **6% annual interest**. All limits must pass, with equality allowed. Maximum borrowing is limited by the DTI payment budget and income multiple; a second estimate also applies the property's LTV cap. Decisions use unrounded values, and the response explains failed limits. These illustrative rules are configured in the API's `appsettings.json` and take effect after restart.

### Scaling consideration

The first infrastructure change would be moving from local SQLite to a shared server database if higher load causes write contention or requires multiple API instances. I would measure those bottlenecks first. The calculation is small and bounded, so background processing is not the first change.

### Trade-offs

Assessments run synchronously, and history uses simple offset paging with complete snapshots stored as JSON. I left out authentication, Docker, background jobs, update/delete operations and idempotency keys to keep the initial scope focused. The affordability model excludes living-cost checks and comprehensive underwriting.

### Next steps

Add authorization and operational security before handling real applicant data. Then use database and load testing to guide improvements, including idempotent creation, managed policy versions and more efficient history queries where needed.
