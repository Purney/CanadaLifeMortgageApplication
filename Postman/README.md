# Postman demonstration

Import [the collection](MortgageApplication.postman_collection.json) into Postman. It demonstrates all five API endpoints using 32 actual HTTP requests and 111 response assertions. There are no mock responses or frontend validators. Expected 400 and 404 responses are passing demonstrations when the API returns the documented status and error body.

## Start and import

From the solution directory:

```powershell
dotnet run --project src/MortgageApplication.Api
```

The default Development profile listens at `http://localhost:5080` and applies the committed SQLite migrations automatically. The Blazor frontend is not required for this demonstration.

1. In Postman, choose **Import** and select `Postman/MortgageApplication.postman_collection.json`.
2. Optionally import `Postman/Localhost.postman_environment.json` and select **Mortgage API — localhost** as the environment. The collection also works with no environment selected.
3. Keep `baseUrl` as `http://localhost:5080`, without a trailing slash. Change that variable if your API uses another address.
4. Use the default configured policy: 6% interest, 90% LTV, 45% projected DTI and 4.5 times income. Worked numerical results and decision-filter assertions assume those values. If you change the policy, adapt the assertions or restore defaults and restart.
5. Run the collection in order, with one iteration and all requests selected. Inspect **Test Results** for individual responses or the collection runner's results.

No authentication, API keys or Postman API credentials are needed by the collection. For localhost requests, use the Postman desktop application, or the web application with its desktop agent.

## What the folders show

| Folder | Demonstration |
| --- | --- |
| 01 Happy path | Create an application, retrieve it, create an approved assessment, retrieve its unchanged snapshot, create a distinct second assessment, and list newest first with default query values. |
| 02 Assessment history | First/next page, ascending/descending order, approved/declined filters, full snapshots, total count before paging and empty pages. |
| 03 Declined outcome | Valid zero-deposit application and a saved declined assessment. A business decline returns **201 Created**, not a validation or server error. |
| 04 API errors | Required fields, invalid ranges, cross-field deposit rule, monetary precision, term limit, JSON binding/parsing errors, query validation, missing records and mismatched parent/assessment references. |

Collection scripts capture `applicationId`, `assessmentId`, `secondAssessmentId`, `declinedApplicationId` and `declinedAssessmentId` from successful API responses. The first request also generates unsaved GUIDs for 404 demonstrations. There is no need to copy/paste IDs.

Run folder 01 before folder 02. Run folders 01 and 03 before all of folder 04. Captured references live in collection variables; avoid same-named environment variables, which would override them. If starting over, run the first request again and continue in order. It clears old references and creates a fresh application. It does not delete earlier records.

The full run creates **two applications and three assessments**. Every additional assessment POST creates another record. Use a local demonstration database and avoid resending POST requests just to reread a response. History assertions expect exactly the two assessments created for the current approved application; extra manual assessments change those counts. There is no cleanup endpoint, automatic delete, retry, or destructive request.

## Essential workflow

Send these requests individually and inspect their response bodies and Test Results:

1. **01 Happy path → 01 Create a valid application**: HTTP 201, saved inputs, ID and relative Location header.
2. **01 Happy path → 02 Retrieve the saved application**: HTTP 200 using the captured ID.
3. **01 Happy path → 03 Create an approved assessment**: no body; HTTP 201, repayment 1,159.74, LTV 90%, DTI 33.19%, both borrowing estimates, input/rule snapshots.
4. **01 Happy path → 04 Retrieve the saved assessment**: HTTP 200; the entire saved snapshot matches the creation response.
5. **04 API errors → 03 Deposit equals property value**: HTTP 400 with `errors.depositAmount` from the API.
6. **03 Declined outcome → 01 Create valid inputs with zero deposit**, then **02 Create a declined assessment**: still HTTP 201; show the saved `declined` decision and LTV explanation.

The malformed JSON and fractional term examples cover binding failures; the history folder covers paging and filtering. Request the newest saved result using `limit=1&sort=desc`.

## Optional command-line runner

If Newman is already installed:

```powershell
newman run Postman/MortgageApplication.postman_collection.json
```

To use the supplied environment:

```powershell
newman run Postman/MortgageApplication.postman_collection.json -e Postman/Localhost.postman_environment.json
```

For an API on another port, pass `--env-var baseUrl=http://localhost:YOUR_PORT`. Newman is optional tooling, not a project runtime dependency. The collection uses the Postman Collection v2.1 format.

## Verification

A full collection run checks all 32 requests and 111 assertions. Expected database writes are two applications and three assessments; invalid requests must create no extra records. Use an isolated local database when checking these counts.

Postman's documentation explains [post-response scripts and Test Results](https://learning.postman.com/docs/tests-and-scripts/write-scripts/test-scripts/). Application workflows, calculation rules and configuration are in the [application guide](../DEMONSTRATION.md).
