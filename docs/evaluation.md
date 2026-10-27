# Evaluation record

Run date: October 27, 2026. No paid Meta calls were made in this run.

## Verified locally

- Backend: **16/16 tests passed** (`dotnet test`).
- Browser: **3/3 tests passed** in headless Edge (`playwright test`).
- Frontend TypeScript check and production build passed.
- npm dependency audit: **0 known vulnerabilities** at installation time.
- .NET restore/build: no dependency warnings after upgrading the native SQLite bundle.

Backend checks cover exact citation rejection, unsupported monetary values, duplicate payment blocking, all three synthetic fixtures, mandatory review, immutable brief revisions, optimistic concurrency, separate browser sessions, hosted input restrictions, missing Meta credentials with no fabricated fallback, structured provider requests, malformed/truncated responses, retained user corrections, PDF extraction, original-file retrieval, duplicate upload rejection, and deletion.

Browser checks cover source inspection, $3,000 to $2,000 revision with stale-export prevention, a 390-pixel viewport without horizontal overflow, and local corrections/clarifications surviving reanalysis. They exercise the real ASP.NET API in sample walkthrough mode.

## What these results do not establish

The fixture analyses are human-authored expected results. Their successful validation is not a model benchmark. Mock-provider tests validate the adapter and error handling, not live provider availability. Docker could not run locally because the engine was stopped; the repository CI includes an image build.

## Credentialed evaluation to run before submission

Run the three samples with live Meta, at least three times each. Record model name, commit, latency, successful structured parses, and manual review of:

| Sample | Expected signals |
| --- | --- |
| Conflicting records | Approval predates v2 delivery; invoice is not payment; original documented balance $3,000. |
| Agreed delivery | Explicit approval of v2; no invented version conflict; balance $3,000. |
| Missing evidence | Missing delivery/approval is explicit; no invented acceptance or delivery date. |
| Added receipt | Separate $1,000 payment; updated documented balance $2,000; earlier brief preserved. |

Measure quote validity as matched references / total references, factual support as manually supported claims / total claims, expected issue detection, payment deduplication correctness, and the number of human corrections needed. Publish denominators and individual failures; do not present fixture results as Meta accuracy. Test a local synthetic document containing hostile instructions and verify no instructions are followed or uncited claims accepted.
