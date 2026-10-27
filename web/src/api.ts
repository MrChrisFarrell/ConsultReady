export type Ref = { sourceId: string; page: number; quote: string };
export type Fact = {
  id: string;
  text: string;
  date: string | null;
  kind: string;
  amount: number | null;
  evidence: Ref[];
  review: string;
  provenance: string;
  originalText: string | null;
};
export type Source = {
  id: string;
  title: string;
  pages: { number: number; text: string }[];
};
export type Financial = {
  contractTotal: number | null;
  payments: number;
  balance: number | null;
  explanation: string;
};
export type Brief = {
  number: number;
  caseRevision: number;
  purpose: string;
  facts: Fact[];
  issues: { text: string }[];
  answers: { question: string; text: string }[];
  questions: string[];
  financials: Financial;
  changes: string[];
  engine: string;
};
export type Case = {
  id: string;
  title: string;
  sample: string | null;
  revision: number;
  needsAnalysis: boolean;
  briefStale: boolean;
  engine: string;
  sources: Source[];
  facts: Fact[];
  issues: { id: string; text: string }[];
  questions: { id: string; question: string }[];
  answers: { questionId: string; text: string }[];
  briefs: Brief[];
  financials: Financial;
};
export async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  const r = await fetch("/api" + path, {
    method,
    headers: {
      "X-ConsultReady": "1",
      ...(body instanceof FormData
        ? {}
        : { "Content-Type": "application/json" }),
    },
    body:
      body instanceof FormData
        ? body
        : body === undefined
          ? undefined
          : JSON.stringify(body),
  });
  if (!r.ok) {
    const e = await r.json().catch(() => ({
      error: `Request failed (${r.status}). Please retry shortly.`,
    }));
    throw new Error(e.error || "Request failed.");
  }
  return r.status === 204 ? (undefined as T) : r.json();
}
