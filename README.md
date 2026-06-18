# FormulaWeb → QuickBooks Integration Agent

A small **Windows service** that is the *only* thing allowed to touch the QuickBooks Desktop SDK.
It is a **thin translator**: the FormulaWeb ERP calls a clean REST/JSON API; the agent converts
to/from **qbXML**, talks to QuickBooks locally over the SDK, and returns JSON.

> **The one rule:** no business logic lives here. No accounting rules, no decisions about *what* or
> *when* to sync — all of that stays in the ERP (NestJS). qbXML and COM never leak past this agent;
> the ERP only ever sees JSON. If you're tempted to "also compute X while we're here" — that belongs
> in the ERP.

---

## What it does (the pipe)

```
ERP (NestJS)  --HTTPS+JSON-->  [ Minimal API ]
                                     |
                               API-key auth
                                     |
                          single-consumer queue  ──►  STA worker thread (one at a time)
                                                            |
                                                   IQuickBooksGateway
                                                   ├─ FixtureQuickBooksGateway  (simulated, no SDK)
                                                   └─ LiveQuickBooksGateway      (QBXMLRP2 + qbXML)
                                                            |
                                                      QuickBooks Desktop
```

Every QuickBooks operation is **serialized** through one STA (single-threaded apartment) thread,
because the QuickBooks SDK is apartment-threaded COM and does not tolerate concurrent sessions.

## Endpoints

| Method | Route                              | Purpose                                              |
|-------:|------------------------------------|------------------------------------------------------|
| GET    | `/health`                          | `{ qbReachable, companyFileOpen, sdkVersion, agentVersion, mode, ... }` |
| GET    | `/customers?updatedSince={iso}`    | List customers (optional modified-date filter)       |
| GET    | `/customers/{listId}`              | One customer, or `404`                               |
| POST   | `/customers`                       | Create a customer; **requires `Idempotency-Key` header**; returns `201` with `listId` + `editSequence` |
| GET    | `/openapi.json`                    | The OpenAPI contract (source of truth for the ERP client) |
| —      | `/swagger`                         | Swagger UI for browsing endpoints/params             |

`/health`, `/openapi.json`, and `/swagger` are exempt from auth; everything else requires the API key.

## Versions / platform (verified June 2026)

- **.NET 10 (LTS)** — `net10.0-windows`, built **x64** (the SDK COM is 64-bit since QB 2022).
- **QuickBooks Desktop SDK 16.0 (QBFC16)** — matches **QuickBooks Enterprise 24.0**.
- Windows Server 2022.

## Project layout

```
src/
  Fw3.QbAgent.Core/        Contracts (DTOs), structured error model, the QB queue + STA worker,
                           idempotency store, configuration. No ASP.NET, no COM.
  Fw3.QbAgent.QuickBooks/  qbXML mapping (CustomerMapper), the gateway implementations
                           (Fixture + Live), the QBXMLRP2 connection.
  Fw3.QbAgent.Host/        Minimal API, OpenAPI/Swagger, API-key auth, Windows Service hosting,
                           Serilog logging (app log + qbXML audit log).
tests/
  Fw3.QbAgent.Tests/       Mapping, idempotency, queue serialization, full HTTP round-trip (fixtures).
fixtures/                  qbXML response fixtures used by the Fixture gateway / tests.
openapi.json               Committed copy of the contract (also served live at /openapi.json).
```

---

## Configuration (`appsettings.json`)

```jsonc
"QbAgent": {
  "Mode": "Fixture",                 // "Fixture" (simulated) or "Live" (real QuickBooks)
  "CompanyFilePath": "C:\\QBDATA\\afi master.qbw",
  "ConnectToOpenFile": true,         // true => use the file currently open in QB (BeginSession "")
  "AppName": "FormulaWeb QuickBooks Agent",
  "QbXmlVersion": "16.0",
  "FixturesPath": "fixtures",        // relative => resolved next to the binaries
  "IdempotencyPath": "data/idempotency", // relative => under C:\ProgramData\Fw3QbAgent
  "QbXmlAuditPath": "logs/qbxml"         // relative => under C:\ProgramData\Fw3QbAgent
},
"Auth": {
  "Enabled": true,
  "HeaderName": "X-Api-Key",
  "ApiKeys": [ "REPLACE_WITH_A_REAL_SHARED_SECRET" ]  // supports multiple keys for rotation
},
"Kestrel": { "Endpoints": { "HttpsLoopback": { "Url": "https://localhost:8462" } } }
```

**Durable data** (logs, idempotency records) lives under `C:\ProgramData\Fw3QbAgent\`.

**Binding / port.** Defaults to `https://localhost:8462`. To accept calls from the ERP, add the
server's internal interface (and have IT open the matching firewall rule), e.g.:

```jsonc
"Kestrel": { "Endpoints": {
  "Internal": { "Url": "https://10.0.0.5:8462" }   // server's LAN IP; never 0.0.0.0
}}
```

The service is **never** exposed to the public internet.

**Auth.** Starts with a shared API key over the internal network. Auth is a single pluggable
middleware (`ApiKeyMiddleware`) so moving to **mTLS** later is a contained change.

---

## Running

### Development (Fixture mode — no QuickBooks needed)

```powershell
dotnet run --project src\Fw3.QbAgent.Host
# https://localhost:8462  — dev API key is "dev-local-key" (appsettings.Development.json)
```

### As a Windows Service

```powershell
dotnet publish src\Fw3.QbAgent.Host -c Release -r win-x64 --self-contained false -o C:\Fw3QbAgent
sc.exe create Fw3QbAgent binPath= "C:\Fw3QbAgent\Fw3.QbAgent.Host.exe" start= auto
sc.exe description Fw3QbAgent "FormulaWeb QuickBooks integration agent"
sc.exe start Fw3QbAgent
```

The host detects the SCM automatically (`UseWindowsService`); the same binary runs as a console app
for development.

### Tests

```powershell
dotnet test
```

---

## One-time QuickBooks setup (the known gotchas)

These are manual, done once on the QuickBooks server. **The round-trip will fail until they're done.**

1. **Install the SDK.** The QuickBooks Desktop SDK 16.0 installer is gated behind a free Intuit
   developer account, so it can't be scripted:
   - Sign in at **developer.intuit.com** → QuickBooks Desktop → **Download the SDK** → SDK 16.0.
   - Run the installer on the QB server. Verify `QBFC16.dll` and the `QBXMLRP2.RequestProcessor`
     COM component register (e.g. `C:\Program Files\Common Files\Intuit\QuickBooks\QBFC16.dll`).
2. **Authorize the integrated application.** The first time the agent connects, QuickBooks prompts
   to authorize it (Edit → Preferences → Integrated Applications, or the consent dialog on first
   connect). Grant access and, for unattended running:
   - allow the app to **log in automatically**, and
   - allow access **even when QuickBooks is not running** (so the service works while QB is closed).
3. **Company file.** Either keep `C:\QBDATA\afi master.qbw` open in QuickBooks with
   `ConnectToOpenFile: true`, or set `ConnectToOpenFile: false` and point `CompanyFilePath` at it for
   unattended access.

## Going Live

1. Complete the one-time setup above.
2. Set `QbAgent:Mode` to `Live`.
3. `GET /health` should report `qbReachable: true` and the company file.
4. Validate the Customer round-trip against the (safe, duplicate) company file before pointing the
   real ERP at it.

---

## Conventions (everything later copies these)

- **Errors.** One structured type (`QbAgentException` with a stable `QbErrorCode`) → RFC 9457
  **ProblemDetails**. QuickBooks' own `statusCode`/`statusSeverity`/`statusMessage` are surfaced
  verbatim in the response extensions. A QuickBooks error is **never** swallowed or returned as 200.
- **Idempotency.** Writes require an `Idempotency-Key`. The check-create-record sequence runs on the
  single QB worker thread, so a retried request can never create a duplicate. A reused key with a
  different body is a `409 Conflict`.
- **Logging.** App log + a **separate daily-rolling qbXML audit log** (`logs/qbxml/qbxml-*.log`) that
  captures every request/response verbatim — the trail for reconciling QB discrepancies.
- **The QB seam.** Everything above `IQuickBooksGateway` is QuickBooks-agnostic; everything below it
  (qbXML, COM) never leaks past it.

### Note on the live transport (QBFC vs QBXMLRP2)

The live gateway drives the **QBXMLRP2 Request Processor** with the qbXML strings produced by
`CustomerMapper`, rather than building QBFC request objects. Reasons: we need the exact qbXML string
anyway (for the audit log and the fixture tests), this gives a **single** mapping codepath shared by
the Fixture and Live gateways, and QBXMLRP2 is the same SDK COM layer QBFC sits on. The connection is
isolated behind `IQbConnection`, so switching to QBFC objects later is a contained change. This was
**reviewed and approved** as the intended approach (a conscious choice over building QBFC objects).

---

## Adding a new entity

Every entity follows the same pattern the Customer slice established. Keep it a thin translator:
map JSON ↔ qbXML, surface QuickBooks' status, add nothing else. Using `Item` as the example:

1. **Contracts** (`Core/Contracts/`): add `ItemDto` and, for writes, `CreateItemRequest`. Mirror the
   QuickBooks fields you need; keep them flat. Pass identity tokens through verbatim — `ListId` +
   `EditSequence` for **list** entities (customers, items, vendors), or `TxnId` + `EditSequence` for
   **transactions** (invoices, payments). Do **not** invent or compute fields.
2. **Gateway interface** (`Core/Abstractions/IQuickBooksGateway.cs`): add the operations
   (`QueryItems`, `GetItem`, `AddItem`, …). Both gateways must implement them — the compiler enforces it.
3. **Mapping** (`QuickBooks/Mapping/ItemMapper.cs`): `BuildQueryRequest` / `BuildAddRequest` (returning
   qbXML strings via `QbXml.BuildRequest`) and `ParseQueryResponse` / `ParseAddResponse` /
   `ParseItemRet`. **qbXML element order must follow the schema sequence** — wrong order is rejected by
   QuickBooks. Reuse `CustomerMapper.ParseStatus` semantics (statusCode 1 = "no matching records" =
   empty/not-found, not an error).
4. **Live gateway** (`Gateways/LiveQuickBooksGateway.cs`): add the methods using the existing `Run(...)`
   helper so the session lifecycle, single audit-write, and error handling come for free. Transactions
   (e.g. `InvoiceAddRq`) carry line items and use `*Rq`/`*Ret` element names specific to that entity —
   check the qbXML reference (OSR) for the exact shape.
5. **Fixture gateway** (`Gateways/FixtureQuickBooksGateway.cs`): implement the same methods; add a
   `fixtures/ItemQueryRs.xml` response and synthesize the add response (echo the request + a generated
   id). The `*.xml` glob already copies new fixtures to the build output — no csproj change needed.
6. **Endpoints** (`Host/Api/ItemEndpoints.cs`): map `GET`/`POST` mirroring `CustomerEndpoints`.
   **Writes must require the `Idempotency-Key` header** and go through `IIdempotencyStore` +
   `RequestHash` exactly as `POST /customers` does. Register the group in `Program.cs`
   (`app.MapItemEndpoints()`).
7. **Tests** (`tests/`): mapper tests (build emits ordered qbXML; parse reads the fixture; error
   severity is flagged) and API tests (list, get-by-id 404, create 201 with ids, idempotent replay,
   key-reuse conflict, auth required).
8. **Regenerate the contract**: run the agent and save the spec so the committed copy matches —
   `Invoke-WebRequest https://localhost:8462/openapi.json -OutFile openapi.json`. The ERP regenerates
   its TypeScript client from this; never let it drift.

Errors, logging, the queue, the STA worker, idempotency, and auth are all already in place — a new
entity touches only the eight points above.
