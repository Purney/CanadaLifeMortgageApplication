# Mortgage Application Demo

A .NET 10 JSON API and Blazor frontend for submitting mortgage applications and saving simple affordability assessments. The solution contains `MortgageApplication.Api`, `MortgageApplication.Web`, and an xUnit test project.

## Run locally

For the updated interview format, see [the demonstration script](DEMONSTRATION.md): fresh-clone commands, a five-minute demo, code walkthrough and discussion prompts. Commit and push the intended submission before rehearsing a fresh clone; local uncommitted changes and the ignored `Planning/` folder will not be present in it.

Install the **.NET 10 SDK**. From the solution directory:

```powershell
dotnet restore
dotnet run --project src/MortgageApplication.Api
```

Open [Swagger UI](http://localhost:5080/swagger/index.html). The launch profile selects Development, where committed migrations are applied automatically before requests are served. SQLite creates `mortgages.db` in the API's working directory; retain that file across restarts to preserve data. No database server or credentials are needed.

In a **second terminal**, also from the solution directory, start the frontend:

```powershell
dotnet run --project src/MortgageApplication.Web
```

Open the [Blazor demo](http://localhost:5082/). Keep both processes running. There is no login or authentication.

### Demonstrate the three tasks

1. Choose **Start an application**. Enter the five values or choose a preset from **Use example figures**, then save. The API response explains missing fields, supported limits, monetary precision, and the deposit/property relationship.
2. Keep the saved application reference. **Find application** retrieves it by that reference, including after an API restart with the same database.
3. Choose **Run an assessment** from the application overview. The result shows the decision/explanation, repayment, LTV, DTI, both borrowing estimates, and the saved inputs and rules. Return to the application to filter, sort, refresh, or page its assessment history and open any saved result.

The UI uses Canada Life-inspired red/light styling based on [the supplied reference](https://www.canadalife.co.uk/). It is clearly labeled as a technical exercise. Amounts are displayed as GBP for the UK demo; the API remains currency-neutral and performs no currency conversion. The example button fills fields without submitting or creating data. All records and decisions come from the API.

### Demonstrate API validation

On **New application**, hover over or focus **Use example figures** to open the choices. Click or tap also opens the picker; Tab reaches each option and Escape closes it. Choose standard figures or one of the former error examples. Selecting only fills the inputs and clears the previous API response.

- Choose **Required fields**, **Income outside the range**, **Deposit matches the property**, **Too many decimal places**, or **Term outside the range**, then explicitly choose **Save application**. The request reaches the API; its actual HTTP 400 response supplies the summary and field messages. Invalid requests create no records.
- Enter `1.5` for the term or nonnumeric text in a money field and save to demonstrate API JSON binding errors. The Web form retains raw text, with no browser numeric constraints or frontend business validator. Blank fields become JSON null, valid JSON number tokens retain their original precision, and other text is sent as a JSON string for API binding to accept or reject.
- Choose **Standard figures** to clear the previous response, then save for a successful request. Run an assessment from the saved application.
- Choose **A declined assessment**, save, then run an assessment. Zero deposit is valid input, but the default 90% LTV rule declines it. This saves an application and assessment; it demonstrates a business outcome. Custom policy can change the decision.
- On **Find application**, open **Use example reference**, choose **Malformed reference** or **Unsaved reference**, then explicitly choose **Find application**. Both requests reach the API and display its actual missing-record response. A real saved reference navigates to the returned application.

The pickers make no requests until Save or Find is chosen. The separate Error examples page has been removed. The API remains the sole source of input validation; POST requests are never retried automatically.

### Frontend API connection

The Web server calls the API through a typed HTTP client; browser CORS configuration is unnecessary. The default API address is `http://localhost:5080/`. To change it, edit `MortgageApi:ApiBaseUrl` in the Web project's settings or set:

```powershell
$env:MortgageApi__ApiBaseUrl = 'http://localhost:5080/'
dotnet run --project src/MortgageApplication.Web
```

The address is validated at startup as an absolute HTTP/HTTPS URL without credentials, query, or fragment. Requests time out after 20 seconds. POST requests are not automatically retried: if a timeout occurs, an operation may already have been saved. The UI provides errors/retry controls without inventing results when the API is unavailable. Blazor Interactive Server requires a live connection to the Web process.

Build and run the automated checks:

```powershell
dotnet build
dotnet test
```

Tests use isolated temporary SQLite files and exercise actual HTTP routing through the ASP.NET Core test host. They cover calculations and boundaries, validation, startup policy, migrations, snapshot history across restart, ordering, failures, and Swagger. Frontend tests cover raw JSON transport, preset requests against the actual API, invalid-record rejection, configuration, HTTP/error handling, rendered pickers, and Web-client compatibility with the API contracts.

## Try the API

For a ready-made interview demonstration, import the [Postman collection](Postman/MortgageApplication.postman_collection.json). It captures IDs automatically and includes approved/declined assessments, history paging/filtering/sorting, and actual API 400/404 examples. See [Postman setup and the short demo order](Postman/README.md); an optional [localhost environment](Postman/Localhost.postman_environment.json) is included.

In Swagger, use **Try it out** on `POST /applications`:

```json
{
  "annualIncome": 60000,
  "monthlyExpenses": 500,
  "depositAmount": 20000,
  "propertyValue": 200000,
  "termYears": 25
}
```

Copy the returned `id`, then call `POST /applications/{applicationId}/assessments` with **no request body**. The returned assessment has its own ID and Location header. Retrieve it by that ID or list the application's saved assessments. Each assessment POST creates another record; retrying a POST may create duplicates.

Equivalent PowerShell:

```powershell
$baseUrl = 'http://localhost:5080'
$body = @{ annualIncome=60000; monthlyExpenses=500; depositAmount=20000; propertyValue=200000; termYears=25 } | ConvertTo-Json
$application = Invoke-RestMethod "$baseUrl/applications" -Method Post -ContentType 'application/json' -Body $body
$assessment = Invoke-RestMethod "$baseUrl/applications/$($application.id)/assessments" -Method Post
Invoke-RestMethod "$baseUrl/applications/$($application.id)/assessments/$($assessment.id)"
Invoke-RestMethod "$baseUrl/applications/$($application.id)/assessments?offset=0&limit=10&status=all&sort=desc"
```

| Operation | Success | Expected client errors |
| --- | --- | --- |
| `POST /applications` | 201 + Location | 400 validation/JSON errors |
| `GET /applications/{id}` | 200 | 404 unknown ID |
| `POST /applications/{id}/assessments` | 201 + Location, including declines | 404 unknown application |
| `GET /applications/{id}/assessments/{assessmentId}` | 200 saved snapshot | 404 unknown/mismatched pair |
| `GET /applications/{id}/assessments` | 200 filtered, sorted page of full saved snapshots | 400 invalid query; 404 unknown application |

Errors use JSON Problem Details. Validation errors contain field names in `errors`. Invalid GUID routes return 404. Save failures return 500 without exposing database details or claiming creation succeeded.

## List saved assessments

`GET /applications/{id}/assessments` accepts:

| Query parameter | Default | Allowed values |
| --- | --- | --- |
| `offset` | 0 | Whole number from 0 to 2,147,483,647 |
| `limit` | 10 | Whole number from 1 to 100 |
| `status` | `all` | `all`, `approved`, `declined` (lowercase) |
| `sort` | `desc` | `asc`, `desc` by creation timestamp, then insertion sequence in the same direction |

The response contains `items` (full assessment snapshots), `offset`, `limit`, and `totalCount`. The count includes all matching assessments for this application **before paging**. Filtering uses the saved decision, so a historical approval stays approved even after policy changes. Only the selected page is loaded and deserialized. Unknown applications return 404; an existing application with no matches, or an offset beyond the last result, returns 200 with empty `items` and the appropriate count. Invalid ranges/filter/sort return field-keyed 400 validation errors; malformed numeric query binding returns 400 Problem Details.

For example, `?status=approved&sort=asc&offset=10&limit=5` skips the first ten saved approvals and returns up to five in oldest-first order. To get the newest record, request `?limit=1&sort=desc`. The former `/assessments/latest` endpoint has been removed. The by-ID endpoint remains useful for the assessment creation Location header and direct references.

I considered returning fewer fields in list results and requiring a detail lookup, but the field count and small application size make complete records practical. Offset paging is intentionally simple: concurrent inserts can move page boundaries, and the count/page queries are separate, so they may observe different committed writes. A cursor or transactionally consistent view would be a future improvement if consumers require it.

## Inputs and assessment rules

All monetary values use one consistent, unspecified currency. Income is **gross annual income**. For this exercise, `monthlyExpenses` means **existing monthly debt payments that continue alongside the proposed mortgage**, excluding ordinary living costs and housing payments replaced by this mortgage. This is deliberately narrower than the ordinary meaning of expenses.

Amounts must have at most two decimal places and be at most 1,000,000,000. Income and property value must be positive; expenses and deposit may be zero. Deposit must be below property value. Term is 1–40 whole years. Missing/null fields are invalid. Excessive expenses and zero deposits are valid submissions that may produce a declined assessment.

Default rules, all configurable:

| Configuration key under `Affordability` | Default |
| --- | --- |
| `AnnualInterestRatePercentage` | 6 |
| `MaxLtvPercentage` | 90 |
| `MaxDtiPercentage` | 45 |
| `MaxIncomeMultiple` | 4.5 |

For loan `L = property value − deposit`, gross monthly income `I = annual income / 12`, monthly rate `r = annual rate percentage / 1200`, and `n = term years × 12`:

```text
annuityFactor = sum((1 + r)^(-k), k = 1..n)
monthlyRepayment = L / annuityFactor
LTV% = 100 × L / propertyValue
DTI% = 100 × (monthlyExpenses + monthlyRepayment) / I
allowedPayment = max(0, maxDtiPercentage / 100 × I − monthlyExpenses)
affordabilityBasedBorrowingCapacity = min(allowedPayment × annuityFactor, maxIncomeMultiple × annualIncome)
maximumBorrowingForProperty = min(affordabilityBasedBorrowingCapacity, maxLtvPercentage / 100 × propertyValue)
```

Approve when LTV, DTI, and requested principal all meet their respective limits, with equality allowed. Explain every failed limit. The decimal discount recurrence avoids subtracting nearly equal numbers at tiny interest rates and works at zero interest, when the annuity factor is simply `n`. Ratio limits are compared by cross-multiplying before division, avoiding false declines from repeating monthly-income decimals at exact equality. Decisions use unrounded values; displayed money/percentages use two decimal places and midpoint-to-even rounding. A displayed ratio can therefore round to its limit while the unrounded value correctly produces a decline.

The example above produces a 180,000 loan, approximately 1,159.74 monthly repayment, 90% LTV, 33.19% DTI, and approval. Affordability-based capacity is 270,000; the property-specific limit is 180,000. Both estimates mean **total principal**, not additional borrowing or a guarantee of funding another purchase. These are illustrative exercise rules, not lender policy or comprehensive underwriting.

## Configuration and database setup

Edit `src/MortgageApplication.Api/appsettings.json` or use environment overrides. Rules are validated and frozen at startup; changes take effect after **restart**. Existing assessment snapshots remain unchanged. Interest accepts 0–100%; LTV/DTI must be greater than 0 and at most 100%; the income multiple must be greater than 0 and at most 100. All four keys are required. These ranges are defensive exercise bounds.

```powershell
$env:Affordability__MaxLtvPercentage = '85'
$env:ConnectionStrings__MortgageDatabase = 'Data Source=C:/data/mortgages.db'
dotnet run --project src/MortgageApplication.Api
```

The configured database's parent directory must already exist. Without an absolute path, SQLite resolves the database relative to the process working directory.

For environments other than Development, apply migrations explicitly before starting. From the solution directory, use the same connection string as the application:

```powershell
dotnet tool restore
$env:ConnectionStrings__MortgageDatabase = 'Data Source=C:/data/mortgages.db'
dotnet ef database update --project src/MortgageApplication.Api
dotnet run --project src/MortgageApplication.Api --no-launch-profile -- --environment Production --urls http://localhost:5080
```

Swagger UI and automatic migrations are enabled only in Development. Migration failure prevents Development startup. The initial migration introduces new tables; no legacy database conversion is needed. Do not commit runtime database files. Keep committed migrations and the model snapshot together.

## Design and reflection

**Key decisions.** (1) Separate API and Blazor Web projects plus tests: thin UI pages consume the JSON API through a typed client, while validation and affordability decisions remain authoritative in the API. Web-local wire models avoid a dependency on API internals; contract tests guard that small duplication. (2) SQLite with EF Core and migrations: durable local storage with a repeatable setup and no external server. (3) Immutable assessment snapshots: each record captures inputs, applied settings, unrounded result, timestamp, and calculation version so configuration changes cannot rewrite history. Metadata columns support lookups; the full snapshot is JSON stored atomically in one row. Applications have a one-to-many relationship to assessments.

**Controller structure and readability.** `MortgageApplicationsController` exposes the five HTTP operations and delegates persistence and assessment orchestration through `IMortgageApplicationService`. `ApplicationValidator` and `AssessmentQueryValidator` each own their rules and messages once. MVC model binding failures use a shared generic Problem Details response; schema annotations do not replace the custom business validation. The exception handler lives outside startup. The calculator keeps decimal arithmetic, comparison order, unrounded decisions, and snapshot version unchanged.

Handwritten C# uses braces, one statement per line, and logical spacing; `.editorconfig` records the shared conventions. The Web client extracts API error reading into a helper and keeps `MortgageApiException` in its own file. These changes preserve the existing routes, JSON contracts, relative Location headers, SQLite data, and migrations. No database migration is required.

**Scaling.** Measure first, then replace local SQLite with a shared server database if concurrent assessment writes cause contention or multiple API instances are needed. Review migrations, numeric types, snapshot storage, and provider behavior during that move. The bounded calculator is inexpensive; queues and caching are not the first changes.

**Trade-offs.** No authentication, update/delete endpoints, background jobs, Docker, policy administration, idempotency keys, living-cost affordability checks, or tamper-proof auditing. Assessments are created synchronously. Offset-paged history returns full snapshots. SQLite filters saved decisions using a fixed parameterized JSON extraction query before counting/paging; JSON storage is less convenient for indexed reporting across individual calculation fields. This API is intended for local assessment and requires security work before handling real applicant data.

**Next steps.** Add authorization and operational security, retention/audit controls, idempotent creation if required, managed policy versions, and database/load testing driven by actual deployment needs. Consider cursor paging and an indexed decision column if history volume or concurrent reads/writes justify them.

The detailed local decision record is [reasonings.md](Planning/reasonings.md); the completed local implementation checklist is [implementation-plan.md](Planning/implementation-plan.md). The original brief is [task.md](Planning/task.md). `Planning/` is intentionally ignored by Git; the submission-facing setup and design explanation are in this README, with the live demonstration and discussion prompts in [DEMONSTRATION.md](DEMONSTRATION.md).
