# Testing notes (.NET 10 port)

This document covers the local testing workflow for the .NET 10 port of the
Authorize.Net SDK and what to expect when running the suite.

## Setup

1. Copy [.env.example](.env.example) to `.env.local` and fill in your sandbox
   credentials (the file is gitignored). `MD5_HASH_KEY` is optional — the
   integration tests load it but never assert on it.
2. From a bash shell at the repo root, run:

   ```
   ./scripts/run-tests.sh             # integration tests against the sandbox
   ./scripts/run-tests.sh --mock      # NMock3 unit tests (broken on .NET 10 — see below)
   ./scripts/run-tests.sh --all       # everything
   ```

The script loads `.env.local` into the process environment and invokes
`dotnet test` with a filter that selects the integration suites
(`Api/Controllers/Test/` and `Api/Controllers/SampleTest/`).

Why environment variables instead of `App.config`? On modern .NET,
`ConfigurationManager.AppSettings` reads the *entry assembly's* `.config`
file. Under `dotnet test` the entry assembly is `testhost.dll`, so the
project's `App.config` is ignored. The test bootstrap in
[UnitTestData.cs](AuthorizeNETtest/UnitTestData.cs) falls back to env vars,
which is the path the script uses.

## Expected results

Against a default Authorize.Net sandbox account:

```
Failed:     1
Passed:    24
Skipped:   10
Total:     35
```

### Skipped (10) — by design

These have `[Ignore(...)]` attributes for things requiring special account
configuration or pre-existing transaction state:

- eCheck variants (5) — require eCheck merchant configuration
- ApplePay, VisaCheckout — require encrypted payment-token data
- Settled transaction credit — requires a known settled transaction ID
- DecryptPaymentDataRequestTest — requires a real session encryption key
- IllFormedCredentialsTest — disabled because of a stale access token

### Failed (1) — known, not a port regression

**`SampleCodeCreateTransactionWithPayPal`** at
[CreateTransactionSampleTest.cs:347](AuthorizeNETtest/Api/Controllers/SampleTest/CreateTransactionSampleTest.cs#L347)
fails with `NullReferenceException` when PayPal is not enabled on the
sandbox merchant account. The test author explicitly documented the
prerequisite at line 315:

```csharp
/*
 * Please enable the PayPal feature of your ANet merchant account.
 */
```

When PayPal is disabled, the API rejects the transaction at the processor
layer and returns no parseable `transactionResponse`, which the test reads
without a null guard. This passes against the shared CI sandbox (which has
PayPal enabled) but fails against a default sandbox.

We are **not using PayPal**, so this is being left as-is and treated as
expected. No action needed unless we later enable PayPal in production.

### Mock tests — broken on .NET 10

`Api/Controllers/MockTest/` (45 tests) uses **NMock3**, a legacy
.NET Framework mocking library that can't load on .NET 10 — its Castle
DynamicProxy dependency requires `System.Security.Permissions`, which only
exists in .NET Framework. The test project's `.csproj` already excludes
five of the most affected controllers; the remainder fail at runtime.

These tests are not worth porting — they mock out the controller itself, so
the code under test never actually runs. They effectively assert
`Assert.IsNotNull(theObjectIJustReturned)` and add no verification value
that the integration tests don't already provide. See discussion in
[the validation branch context](#).

## What is and isn't covered

The integration suite exercises the full request/response stack against the
real sandbox: XML serialization → HTTPS → API → XML deserialization → typed
response objects. If the .NET 10 port had broken any layer of that pipeline,
these tests would catch it.

**Covered failure paths:**

- Invalid credentials → `E00007` error code
  ([APIInvalidCredentials.cs](AuthorizeNETtest/Api/Controllers/Test/APIInvalidCredentials.cs))
- Expired credit card on ARB subscription → error message walk
  ([ErrorMessagesSampleTest.cs](AuthorizeNETtest/Api/Controllers/SampleTest/ErrorMessagesSampleTest.cs))

**Not covered (left to application-level tests):**

- Declined transactions (sandbox supports magic-amount triggers per the
  [Authorize.Net testing guide](https://developer.authorize.net/hello_world/testing_guide.html))
- CVV / AVS mismatches
- Duplicate-transaction detection
- Field-validation errors
- Network / timeout / TLS errors

Coverage of declines and validation errors should live in the consuming
application's integration tests, not in this SDK's suite.
