# Claude Code bootstrap prompt — QuickBooks Desktop integration agent (C#)

> Paste everything below the line into Claude Code (in the **separate C# repo**, not the
> ERP repo) as your first message. It defines a small Windows service that is the *only*
> thing in the system allowed to touch the QuickBooks SDK. Read it fully, then ask the
> clarifying questions at the bottom before scaffolding. Build only the first vertical
> slice described under "First deliverable."

---

## Context

I'm replacing a painful QuickBooks Web Connector (QBWC) integration with a small,
in-house **integration agent**. This agent is a separate project from my main ERP (which
is TypeScript: NestJS + Vue + SQL Server, in its own repo). The agent's entire job is to
be a **thin translator** between a clean REST/JSON API that my ERP calls, and the
QuickBooks Desktop SDK.

The agent runs as a **Windows service on the company-controlled Windows server that hosts
QuickBooks Desktop Enterprise 2024**. It talks to QuickBooks **locally** via the SDK — no
web connector, no polling. My ERP calls the agent **on demand** over the internal network.

Do **not** start scaffolding yet. Read this, then ask me the questions at the bottom.

## The one rule that defines this project: thin translator, no business logic

This is the most important constraint. The agent does exactly three things:

1. Accept a typed REST/JSON request from the ERP.
2. Translate it to/from **qbXML**, talk to the QuickBooks SDK, manage the session.
3. Return JSON (or a structured error) to the ERP.

It does **not** contain accounting rules, validation of business meaning, decisions about
*what* to sync or *when*, or any ERP domain logic. All of that lives in NestJS. If you
ever feel tempted to add "while we're here, let's also compute/decide X" in the agent —
stop, that belongs in the ERP. qbXML and COM must never leak past this agent into the ERP;
the ERP only ever sees clean JSON.

## Locked technical decisions

- **Language/runtime:** C# on the current .NET LTS. (I'll confirm the exact version —
  verify what the current LTS is, don't assume.)
- **Why C#:** the QuickBooks SDK is a **Windows COM component** (the `QBXMLRP2` Request
  Processor, with **QBFC** as the recommended object wrapper). .NET has first-class native
  COM interop, and essentially all SDK documentation/examples are C#. This is the reason
  for the language; respect it.
- **REST seam:** ASP.NET Core **Minimal API**. Keep it small. Generate an **OpenAPI spec**
  — it's the source-of-truth contract my ERP will generate its TypeScript client from, so
  keep it accurate and stable.
- **Hosting:** run as a **Windows Service** (`Microsoft.Extensions.Hosting.WindowsServices`)
  — starts on boot, restarts on failure, logs to a durable location.
- **QB access:** local connection via QBFC `SessionManager` against the QB SDK, talking to
  the company file on the same machine.
- **Binding/exposure:** bind to localhost or the internal interface only. This service is
  **never** exposed to the public internet.

## QuickBooks SDK realities to design around (these cause most integration pain)

- **Session lifecycle:** `OpenConnection` → `BeginSession` (company file path, or `""` to
  use the file currently open in QB) → do requests → `EndSession` → `CloseConnection`.
  Wrap this so sessions are always cleanly closed even on error.
- **Serialize all QB access.** QuickBooks Desktop does not tolerate concurrent sessions
  well. Put a **single-consumer in-process queue** in front of the SDK so only one request
  hits QuickBooks at a time, even if multiple REST calls arrive at once. Incoming HTTP
  requests enqueue work and await the result.
- **One-time app authorization:** the first time the agent connects, QuickBooks must
  authorize it (the integrated-application / certificate grant, done once in the QB UI,
  ideally set to allow access while QB is closed / "automatic login" for unattended
  running). Document this manual setup step in the README — it's a known gotcha.
- **QB identity fields:** QuickBooks assigns a permanent **ListID** (for list entities like
  customers/items) or **TxnID** (for transactions), plus an **EditSequence** used as an
  optimistic-concurrency token on updates. The agent must surface these in its JSON
  responses. **The ERP owns the mapping** between its own record IDs and QB ListIDs/TxnIDs;
  the agent just passes these values through faithfully.
- **Error handling:** qbXML responses carry `statusCode` / `statusSeverity` /
  `statusMessage`. Map these to meaningful structured HTTP errors — never swallow a QB
  error or return a 200 on a failed operation. A wrong/missing error here becomes a
  silent data problem later.

## Financial-safety guardrails

This agent moves accounting data, so:

- **Idempotency on writes.** The ERP may retry a request after a network blip; the agent
  must not create a duplicate invoice/customer. Accept an idempotency key from the ERP and
  ensure replays are safe.
- **Log every qbXML request and response** (to a durable, rotating log) for audit and
  debugging — this is invaluable when reconciling QuickBooks discrepancies.
- **No partial-success ambiguity.** A REST call either fully succeeded (with the resulting
  QB IDs returned) or it failed with a clear reason. Don't return states the ERP can't act
  on confidently.

## REST contract — starting shape (resource-oriented, JSON in/out)

Keep it RESTful and typed. Rough starting endpoints (we'll refine):

- `GET  /health` → `{ qbReachable, companyFileOpen, sdkVersion, agentVersion }`
- `GET  /customers?updatedSince={iso}` → list of customers as JSON
- `GET  /customers/{listId}` → one customer
- `POST /customers` (JSON body + idempotency key) → creates in QB, returns the created
  record including its new `ListId` and `EditSequence`

The agent internally maps these JSON shapes to/from qbXML (e.g. `CustomerQueryRq`,
`CustomerAddRq`) via QBFC. The ERP never sees qbXML.

## qbXML round-trip flow (for POST /customers)

1. ERP sends `POST /customers` with JSON + idempotency key.
2. Agent enqueues the job on the single-consumer QB queue and awaits it.
3. Worker maps JSON → qbXML `CustomerAddRq` using QBFC.
4. Worker opens/uses a QB session, submits the request, receives qbXML response.
5. Worker inspects status; on success parses the response → JSON (including `ListId`,
   `EditSequence`); on failure builds a structured error.
6. Agent returns JSON (or error) to the ERP. Request + response are logged.

## First deliverable — ONE thin vertical slice (do this first, nothing more)

Prove the whole pipe before adding breadth. Start with the **Customer** entity because
it's simple and carries no money — ideal for proving COM interop, session management, the
queue, and the REST seam before we touch invoices/items/payments. Deliver:

1. The Windows-service host skeleton + ASP.NET Core Minimal API + OpenAPI generation.
2. The single-consumer QB request queue.
3. A QBFC session wrapper (open/begin/end/close, always cleanly torn down).
4. `GET /health` (proves the agent can reach QB and see the company file).
5. `GET /customers` and `POST /customers` (proves a full qbXML read and write round-trip),
   with idempotency on the POST and structured error mapping.
6. Structured logging of qbXML requests/responses, and a README documenting the one-time
   QB app-authorization setup.

Establish the project conventions (structure, error model, logging, config, test setup)
as part of this slice — everything later will copy these patterns. Stop for review when
the Customer round-trip works.

## Before you write any code — confirm these with me

1. **.NET version:** confirm the current .NET LTS to target (verify, don't assume).
2. **QuickBooks SDK version:** which QBSDK / QBFC version is installed or should be
   installed on the QB server, and is it already present?
3. **Auth between ERP and agent:** since this is internal-network-only — start with a
   shared API key/secret over the internal interface, or go straight to mTLS? Any network
   constraints (which interface/port, firewall rules)?
4. **Company file:** path to the company file, and should the agent connect to the file
   currently open in QB, or open a specific file? Is QB configured for unattended access?
5. **Test strategy:** is there a QB Desktop instance (or sample company file) I can develop
   against on a dev box, or do we need recorded qbXML fixtures to test against without a
   live QuickBooks?
6. **Contract sharing:** confirm we'll publish the OpenAPI spec as the source of truth and
   generate the ERP's TypeScript client from it (so the two repos can't drift silently).

Ask these, wait for my answers, then build only the Customer vertical slice and stop.
