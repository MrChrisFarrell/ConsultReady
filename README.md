# ConsultReady

ConsultReady turns scattered project records into a user-reviewed consultation brief. It reconciles document claims, identifies uncertainty, asks clarification questions, and tracks how a brief changes when new evidence arrives. It does not give legal advice or determine liability.

Built for the Meta Global AI Developer Hackathon, starting October 27, 2026. Application code and synthetic fixtures were created for this event.

## Run locally

Prerequisites: .NET 10 SDK and Node.js 24.

```powershell
npm ci --prefix web
npm run build --prefix web
dotnet run --project src/ConsultReady.Api --no-launch-profile --urls http://127.0.0.1:5080
```

Open http://127.0.0.1:5080. Choose **Explore Maya's case**, then **Sample walkthrough**. The walkthrough uses explicitly labeled, human-authored results and makes no model calls. It is not evidence of model accuracy.

For live inference, configure `MODEL_API_KEY` in the server process environment before starting. Do not put it in the frontend, source control, or chat. `.env.example` documents settings; .NET does **not** automatically load `.env` files. The default model is `muse-spark-1.3`, configurable with `Meta__Model`.

Local records are stored in the OS local application-data directory under `ConsultReady`; override with `ConsultReady__DataDirectory`. Local mode accepts only loopback API connections. Live inference sends document text and relevant user clarifications to Meta, even when storage is local.

## The demo

1. Open the conflicting-records sample; inspect its five documents.
2. Analyze with Meta, or explicitly choose the sample walkthrough when credentials are unavailable.
3. Inspect the source for the positive response: it predates version 2 delivery.
4. Answer the clarification and confirm or exclude every proposed fact.
5. Create the brief. Reviewed contract ($6,000) minus documented deposit ($3,000) gives $3,000.
6. Under Sources, add the October 2 receipt. The previous brief becomes historical.
7. Reanalyze, review the additional $1,000 payment, and create revision 2. The brief highlights the change to $2,000.
8. Print the latest reviewed brief or save it as PDF using the browser print dialog.

The missing-evidence and agreed-delivery samples exercise different uncertainty states. The local workflow also supports pasted text and text PDFs (5 MB, 30 pages, 10 documents, 70,000 characters total). Scans require OCR and are explicitly rejected. USD only in this prototype.

## Architecture

- React + TypeScript + Vite interface, served by ASP.NET Core 10.
- PdfPig extracts text and page references from PDFs.
- Meta's Chat Completions API supplies structured facts, conflicts, clarification questions, and a proposed ordering of reviewed fact IDs.
- Exact quotes are checked against stored source pages; monetary amounts must occur in their quoted evidence.
- Deterministic code calculates financial totals, enforces review gates, and creates immutable brief snapshots and change summaries.
- SQLite stores session-isolated cases, optimistic revisions, and a persisted daily model-call budget.
- One Docker service with a persistent disk on Render.

See [architecture](docs/architecture.md), [deployment](docs/deployment.md), [evaluation](docs/evaluation.md), and [demo script](docs/demo-script.md).

## Verification

```powershell
dotnet test ConsultReady.sln
npm run build --prefix web
npx --prefix web playwright test --config web/playwright.config.ts
```

Browser tests use installed Edge locally and Playwright Chromium in CI. They start the API automatically if it is not already running. CI also builds the Docker image. Unit and integration tests do not call paid models.

## Current limits

The live Meta adapter is implemented; model quality and provider compatibility still require a credentialed smoke test. Exact citation checks establish that a quote exists, not that every interpretation is correct. Mandatory human review remains part of the workflow. Potential duplicate payments with identical dates and amounts block the balance until reviewed. Clarifications stay labeled as user recollections, and open issues are not automatically treated as resolved.

Hosted mode accepts only bundled synthetic documents and predefined responses. It is not a service for real client records. Public cases expire after 24 hours of inactivity. Browser session cookies isolate cases but do not provide account recovery. No analytics or document-body logging is intentionally added.
