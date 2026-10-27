import { useEffect, useState, useRef } from "react";
import { api, type Case, type Source, type Fact, type Brief } from "./api";
const money = (v: number | null) =>
  v === null
    ? "Unresolved"
    : new Intl.NumberFormat("en-US", {
        style: "currency",
        currency: "USD",
        minimumFractionDigits: 2,
      }).format(v);
export default function App() {
  const initialized = useRef(false);
  const modalRef = useRef<HTMLElement | null>(null);
  const [caps, setCaps] = useState<{
      mode: string;
      liveAvailable: boolean;
      model: string;
    } | null>(null),
    [cases, setCases] = useState<{ id: string; title: string }[]>([]),
    [c, setCase] = useState<Case | null>(null),
    [tab, setTab] = useState("review"),
    [source, setSource] = useState<Source | null>(null),
    [quote, setQuote] = useState(""),
    [busy, setBusy] = useState(""),
    [error, setError] = useState(""),
    [title, setTitle] = useState(""),
    [text, setText] = useState(""),
    [edit, setEdit] = useState<Fact | null>(null),
    [editText, setEditText] = useState(""),
    [editAmount, setEditAmount] = useState(""),
    [answerText, setAnswerText] = useState<Record<string, string>>({}),
    [briefNumber, setBriefNumber] = useState<number | null>(null);
  async function list() {
    setCases(await api("/cases"));
  }
  function select(item: Case) {
    setCase(item);
    localStorage.setItem("consultready-case", item.id);
    setSource(null);
    setBriefNumber(null);
  }
  async function run(label: string, fn: () => Promise<void>) {
    setBusy(label);
    setError("");
    try {
      await fn();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Something went wrong.");
    } finally {
      setBusy("");
    }
  }
  useEffect(() => {
    if (initialized.current) return;
    initialized.current = true;
    void run("Opening workspace", async () => {
      setCaps(await api("/capabilities"));
      await list();
      const id = localStorage.getItem("consultready-case");
      if (id) {
        try {
          select(await api("/cases/" + id));
        } catch {
          localStorage.removeItem("consultready-case");
        }
      }
    });
  }, []);
  useEffect(() => {
    if (!source && !edit) return;
    const previous = document.activeElement as HTMLElement | null;
    const dialog = modalRef.current;
    dialog?.querySelector<HTMLElement>("button, textarea, input, a")?.focus();
    function keys(e: KeyboardEvent) {
      if (e.key === "Escape") {
        setSource(null);
        setEdit(null);
      }
      if (e.key !== "Tab" || !dialog) return;
      const nodes = Array.from(
        dialog.querySelectorAll<HTMLElement>(
          "button:not(:disabled), a, input, textarea, select",
        ),
      );
      const first = nodes[0],
        last = nodes.at(-1);
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last?.focus();
      }
      if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first?.focus();
      }
    }
    document.addEventListener("keydown", keys);
    return () => {
      document.removeEventListener("keydown", keys);
      previous?.focus();
    };
  }, [source, edit]);
  async function mutate(path: string, body?: unknown, method = "POST") {
    if (!c) return;
    select(await api<Case>(`/cases/${c.id}${path}`, method, body));
    await list();
  }
  function inspect(id: string, q = "") {
    setSource(c?.sources.find((s) => s.id === id) ?? null);
    setQuote(q);
  }
  const pending = c?.facts.filter((f) => f.review === "pending").length ?? 0;
  const brief =
    c?.briefs.find((b) => b.number === briefNumber) ?? c?.briefs.at(-1);
  function refs(f: Fact) {
    return (
      <div className="refs">
        {f.evidence.map((r, i) => (
          <button
            key={i}
            className="link"
            onClick={() => inspect(r.sourceId, r.quote)}
          >
            {c?.sources.find((s) => s.id === r.sourceId)?.title ?? r.sourceId} ·
            p.{r.page} ↗
          </button>
        ))}
      </div>
    );
  }
  function renderBrief(b: Brief) {
    return (
      <article className="brief">
        <div className="eyebrow">CONSULTATION BRIEF / REVISION {b.number}</div>
        <h2>{c?.title}</h2>
        <p>{b.purpose}</p>
        <p className="muted">
          {b.engine === "walkthrough"
            ? "Sample walkthrough · human-authored fixture"
            : "Meta-assisted · user-reviewed"}{" "}
          · Preparation only; not legal advice.
        </p>
        {b.changes.length > 0 && (
          <section className="changes">
            <h3>What changed</h3>
            {b.changes.map((x, i) => (
              <p key={i}>{x}</p>
            ))}
          </section>
        )}
        <h3>Documented financial position</h3>
        <p>
          Contract {money(b.financials.contractTotal)} − payments{" "}
          {money(b.financials.payments)} ={" "}
          <strong>{money(b.financials.balance)}</strong>
        </p>
        <small>{b.financials.explanation}</small>
        <h3>Reviewed timeline</h3>
        {b.facts.map((f) => (
          <section className="brief-fact" key={f.id}>
            <small>
              {f.date ?? "Date unknown"} · {f.provenance}
            </small>
            <p>{f.text}</p>
            {f.originalText && <p>Original document claim: {f.originalText}</p>}
            {refs(f)}
            {f.evidence.map((r, i) => (
              <blockquote key={i}>{r.quote}</blockquote>
            ))}
          </section>
        ))}
        <h3>Open issues</h3>
        {b.issues.map((x, i) => (
          <p key={i}>{x.text}</p>
        ))}
        {b.answers.length > 0 && (
          <>
            <h3>Your clarifications</h3>
            {b.answers.map((x, i) => (
              <p key={i}>
                <strong>{x.question}</strong>
                <br />
                {x.text}
              </p>
            ))}
          </>
        )}
        <h3>Questions for consultation</h3>
        <ul>
          {b.questions.map((x, i) => (
            <li key={i}>{x}</li>
          ))}
        </ul>
      </article>
    );
  }
  return (
    <div className="app">
      <aside className="sidebar">
        <a className="brand" href="/">
          c<span>ConsultReady</span>
        </a>
        <div className="sidebar-intro">
          A clearer story.
          <br />A better first conversation.
        </div>
        <p className="eyebrow">YOUR WORKSPACE</p>
        <nav>
          {cases.map((x) => (
            <button
              className={x.id === c?.id ? "case active" : "case"}
              key={x.id}
              disabled={!!busy}
              onClick={() =>
                void run("Opening case", async () => {
                  select(await api("/cases/" + x.id));
                  setTab("review");
                })
              }
            >
              {x.title}
              <span>↗</span>
            </button>
          ))}
        </nav>
        <div className="sample-start">
          <p className="eyebrow">START WITH A SAMPLE</p>
          {[
            ["conflicting", "Conflicting records"],
            ["straightforward", "Agreed delivery"],
            ["missing", "Missing evidence"],
          ].map(([id, label]) => (
            <button
              key={id}
              disabled={!!busy}
              onClick={() =>
                void run("Creating sample", async () => {
                  select(await api("/cases", "POST", { sample: id }));
                  await list();
                  setTab("review");
                })
              }
            >
              ＋ {label}
            </button>
          ))}
          {caps?.mode === "Local" && (
            <button
              disabled={!!busy}
              onClick={() =>
                void run("Creating case", async () => {
                  select(
                    await api("/cases", "POST", { title: "My consultation" }),
                  );
                  await list();
                  setTab("sources");
                })
              }
            >
              ＋ Empty local case
            </button>
          )}
        </div>
        <footer>
          <span className="dot" />
          {caps?.mode ?? "Connecting"} workspace
          <p>
            Evidence organization.
            <br />
            Human judgment.
          </p>
        </footer>
      </aside>
      <main>
        <header>
          <span className="eyebrow">EVIDENCE BEFORE ADVICE</span>
          <span className="pill">
            {caps?.mode === "Hosted"
              ? "Synthetic public demo"
              : "Local workspace"}
          </span>
        </header>
        {error && (
          <div role="alert" className="error">
            {error}
            <button onClick={() => setError("")} aria-label="Dismiss error">
              ×
            </button>
          </div>
        )}
        {busy && (
          <div className="status" role="status">
            {busy}…
          </div>
        )}
        {!c ? (
          <section className="welcome">
            <div className="eyebrow">
              FROM SCATTERED RECORDS TO A CLEAR BRIEF
            </div>
            <h1>
              Walk into your consultation
              <br />
              with the facts in order.
            </h1>
            <p>
              Reconcile documents, surface uncertainty, and review a
              source-backed brief before you speak with a professional.
            </p>
            <div className="welcome-grid">
              <article>
                <b>01 / Gather</b>
                <p>
                  Start with an agreement, invoices, receipts, and
                  correspondence.
                </p>
              </article>
              <article>
                <b>02 / Reconcile</b>
                <p>
                  Find conflicting claims and answer the questions that matter.
                </p>
              </article>
              <article>
                <b>03 / Prepare</b>
                <p>Approve each fact. Bring a brief with traceable evidence.</p>
              </article>
            </div>
            <button
              className="primary"
              disabled={!!busy}
              onClick={() =>
                void run("Creating sample", async () => {
                  select(
                    await api("/cases", "POST", { sample: "conflicting" }),
                  );
                  await list();
                })
              }
            >
              Explore Maya’s case →
            </button>
            <p className="muted">
              Bundled examples are fictional. The sample walkthrough makes no AI
              calls.
            </p>
          </section>
        ) : (
          <>
            <div className="case-heading">
              <div>
                <div className="eyebrow">
                  {c.sample ? "SYNTHETIC CASE STUDY" : "CONSULTATION WORKSPACE"}{" "}
                  / REVISION {c.revision}
                </div>
                <h1>{c.title}</h1>
                <p>
                  Build a reliable account of what happened, and what still
                  needs explaining.
                </p>
              </div>
              <button
                className="link danger"
                disabled={!!busy}
                onClick={() => {
                  if (window.confirm("Delete this case and its documents?"))
                    void run("Deleting case", async () => {
                      await api("/cases/" + c.id, "DELETE");
                      setCase(null);
                      localStorage.removeItem("consultready-case");
                      await list();
                    });
                }}
              >
                Delete case
              </button>
            </div>
            <div className="metrics">
              <div>
                <small>Evidence collected</small>
                <strong>
                  {c.sources.length} <span>documents</span>
                </strong>
              </div>
              <div>
                <small>Review progress</small>
                <strong>
                  {c.facts.length - pending}
                  <span> / {c.facts.length} facts</span>
                </strong>
              </div>
              <div>
                <small>Documented balance</small>
                <strong>
                  {c.needsAnalysis
                    ? "Awaiting analysis"
                    : money(c.financials.balance)}
                </strong>
              </div>
            </div>
            <div className="analysis-bar">
              <div>
                <b>
                  {c.needsAnalysis
                    ? "Evidence is ready for reconciliation"
                    : c.engine === "walkthrough"
                      ? "Sample walkthrough"
                      : "Meta-assisted analysis"}
                </b>
                <p>
                  {c.engine === "walkthrough"
                    ? "Human-authored sample results. No model was called."
                    : `Structured extraction with ${caps?.model ?? "Meta"}. Every claim needs your review.`}
                </p>
              </div>
              <div className="actions">
                {c.sample && (
                  <button
                    disabled={!!busy}
                    onClick={() =>
                      void run("Loading sample walkthrough", () =>
                        mutate("/analyze", { engine: "walkthrough" }),
                      )
                    }
                  >
                    Sample walkthrough
                  </button>
                )}
                <button
                  className="primary"
                  disabled={!!busy || !caps?.liveAvailable || !c.sources.length}
                  title={
                    !caps?.liveAvailable
                      ? "Configure MODEL_API_KEY on the server"
                      : "Send documents to Meta for analysis"
                  }
                  onClick={() =>
                    void run("Reconciling with Meta", () =>
                      mutate("/analyze", { engine: "meta" }),
                    )
                  }
                >
                  Analyze with Meta ↗
                </button>
              </div>
            </div>
            {!caps?.liveAvailable && (
              <p className="muted key-note">
                Live Meta is not configured. Sample walkthrough remains
                available for the bundled cases.
              </p>
            )}
            <div className="tabs" role="tablist">
              {[
                ["sources", "01 Sources"],
                ["review", "02 Review & clarify"],
                ["brief", "03 Consultation brief"],
              ].map(([id, label]) => (
                <button
                  role="tab"
                  aria-selected={tab === id}
                  className={tab === id ? "selected" : ""}
                  key={id}
                  onClick={() => setTab(id)}
                >
                  {label}
                </button>
              ))}
            </div>
            {tab === "sources" && (
              <section className="sources">
                <h2>Your evidence library</h2>
                <p>
                  Original source text remains separate from extracted facts and
                  your recollections.
                </p>
                {c.sources.map((s) => (
                  <button
                    className="source-row"
                    key={s.id}
                    onClick={() => inspect(s.id)}
                  >
                    <span className="file-icon">↗</span>
                    <span>
                      <b>{s.title}</b>
                      <small>{s.pages.length} page(s) · View source</small>
                    </span>
                    <span>→</span>
                  </button>
                ))}
                {c.sample &&
                  !c.sources.some((s) => s.id === "later-payment") && (
                    <div className="add-evidence">
                      <h3>A new receipt arrives</h3>
                      <p>
                        Add a separate $1,000 payment. Reconcile again to see
                        how the brief changes.
                      </p>
                      <button
                        disabled={!!busy}
                        onClick={() =>
                          void run("Adding payment receipt", () =>
                            mutate("/sample-payment"),
                          )
                        }
                      >
                        ＋ Add October 2 receipt
                      </button>
                    </div>
                  )}
                {caps?.mode === "Local" && (
                  <form
                    className="upload"
                    onSubmit={(e) => {
                      e.preventDefault();
                      void run("Adding correspondence", async () => {
                        await mutate("/sources/text", { title, text });
                        setTitle("");
                        setText("");
                      });
                    }}
                  >
                    <h3>Add your records</h3>
                    <p>
                      Using live analysis sends document text to Meta. Local
                      storage alone does not make inference private.
                    </p>
                    <label>
                      Document title
                      <input
                        required
                        maxLength={120}
                        value={title}
                        onChange={(e) => setTitle(e.target.value)}
                      />
                    </label>
                    <label>
                      Correspondence or document text
                      <textarea
                        required
                        maxLength={30000}
                        rows={5}
                        value={text}
                        onChange={(e) => setText(e.target.value)}
                      />
                    </label>
                    <button disabled={!!busy} type="submit">
                      Add text document
                    </button>
                    <label className="pdf-label">
                      Or upload a text PDF (5 MB / 30 pages)
                      <input
                        type="file"
                        accept="application/pdf"
                        disabled={!!busy}
                        onChange={(e) => {
                          const file = e.target.files?.[0];
                          if (file) {
                            const form = new FormData();
                            form.append("file", file);
                            void run("Reading PDF", () =>
                              mutate("/sources/pdf", form),
                            );
                          }
                          e.target.value = "";
                        }}
                      />
                    </label>
                  </form>
                )}
              </section>
            )}
            {tab === "review" && (
              <div className="review-grid">
                <section>
                  <div className="section-heading">
                    <h2>The documented timeline</h2>
                    <span className="pill">{pending} to review</span>
                  </div>
                  {c.needsAnalysis && (
                    <div className="notice">
                      Sources have changed. Run analysis before reviewing facts
                      or creating a current brief.
                    </div>
                  )}
                  {!c.facts.length && (
                    <div className="empty">
                      Choose <b>Analyze with Meta</b> or the labeled{" "}
                      <b>Sample walkthrough</b> to begin.
                    </div>
                  )}
                  {[...c.facts]
                    .sort((a, b) => (a.date ?? "").localeCompare(b.date ?? ""))
                    .map((f) => (
                      <article className={"fact " + f.review} key={f.id}>
                        <div className="fact-top">
                          <small>{f.date ?? "Date unknown"}</small>
                          <span>
                            {f.review === "pending"
                              ? "Needs review"
                              : f.review === "confirmed"
                                ? "✓ Confirmed"
                                : "Excluded"}
                          </span>
                        </div>
                        <p>{f.text}</p>
                        {f.provenance === "user-reported" && (
                          <small>
                            User correction · original: {f.originalText}
                          </small>
                        )}
                        {refs(f)}
                        <div className="fact-actions">
                          <button
                            disabled={!!busy || c.needsAnalysis}
                            aria-pressed={f.review === "confirmed"}
                            onClick={() =>
                              void run("Saving review", () =>
                                mutate(
                                  "/facts/" + f.id,
                                  { review: "confirmed" },
                                  "PATCH",
                                ),
                              )
                            }
                          >
                            Confirm
                          </button>
                          <button
                            disabled={!!busy || c.needsAnalysis}
                            aria-pressed={f.review === "rejected"}
                            onClick={() =>
                              void run("Saving review", () =>
                                mutate(
                                  "/facts/" + f.id,
                                  { review: "rejected" },
                                  "PATCH",
                                ),
                              )
                            }
                          >
                            Exclude
                          </button>
                          {caps?.mode === "Local" && (
                            <button
                              disabled={!!busy || c.needsAnalysis}
                              onClick={() => {
                                setEdit(f);
                                setEditText(f.text);
                                setEditAmount(String(f.amount ?? ""));
                              }}
                            >
                              Correct
                            </button>
                          )}
                        </div>
                      </article>
                    ))}
                </section>
                <aside className="insights">
                  <div className="insight-title">
                    <span>◇</span>
                    <h2>What needs clarity</h2>
                  </div>
                  {c.issues.length === 0 ? (
                    <p className="muted">
                      Open issues will appear after analysis.
                    </p>
                  ) : (
                    c.issues.map((i) => (
                      <div className="issue" key={i.id}>
                        {i.text}
                      </div>
                    ))
                  )}
                  {c.questions.map((q) => (
                    <section className="question" key={q.id}>
                      <h3>{q.question}</h3>
                      {c.answers.find((a) => a.questionId === q.id) && (
                        <p className="saved-answer">
                          Your clarification:{" "}
                          {c.answers.find((a) => a.questionId === q.id)?.text}
                        </p>
                      )}
                      <select
                        aria-label={"Clarification: " + q.question}
                        defaultValue=""
                        disabled={!!busy}
                        onChange={(e) => {
                          if (e.target.value)
                            void run("Saving clarification", () =>
                              mutate("/answers", {
                                questionId: q.id,
                                choice: e.target.value,
                              }),
                            );
                          e.target.value = "";
                        }}
                      >
                        <option value="">Choose a sample response…</option>
                        <option value="unknown">I’m unsure</option>
                        <option value="earlier">
                          It referred to an earlier version
                        </option>
                        <option value="no-other-payments">
                          No other payments I recall
                        </option>
                      </select>
                      {caps?.mode === "Local" && (
                        <form
                          onSubmit={(e) => {
                            e.preventDefault();
                            void run("Saving clarification", () =>
                              mutate("/answers", {
                                questionId: q.id,
                                text: answerText[q.id],
                              }),
                            );
                          }}
                        >
                          <textarea
                            aria-label={"Your answer: " + q.question}
                            placeholder="Or add your recollection…"
                            maxLength={1500}
                            required
                            value={answerText[q.id] ?? ""}
                            onChange={(e) =>
                              setAnswerText({
                                ...answerText,
                                [q.id]: e.target.value,
                              })
                            }
                          />
                          <button disabled={!!busy}>Save answer</button>
                        </form>
                      )}
                    </section>
                  ))}
                  <div className="ready-card">
                    <h3>Ready for your consultation?</h3>
                    <p>
                      Review every fact first. Uncertainty stays visible in your
                      brief.
                    </p>
                    <button
                      className="primary"
                      disabled={
                        !!busy ||
                        c.needsAnalysis ||
                        pending > 0 ||
                        !c.facts.length
                      }
                      onClick={() =>
                        void run("Preparing reviewed brief", async () => {
                          await mutate("/brief");
                          setTab("brief");
                        })
                      }
                    >
                      Create reviewed brief →
                    </button>
                  </div>
                </aside>
              </div>
            )}
            {tab === "brief" && (
              <section>
                {!brief ? (
                  <div className="empty">
                    <h2>Your brief starts with your review.</h2>
                    <p>
                      Confirm or exclude every proposed fact, then create the
                      brief from the review tab.
                    </p>
                    <button onClick={() => setTab("review")}>
                      Review facts →
                    </button>
                  </div>
                ) : (
                  <>
                    <div className="brief-toolbar">
                      <select
                        aria-label="Brief revision"
                        value={brief.number}
                        onChange={(e) => setBriefNumber(Number(e.target.value))}
                      >
                        {c.briefs.map((b) => (
                          <option key={b.number} value={b.number}>
                            Revision {b.number}
                          </option>
                        ))}
                      </select>
                      <button
                        disabled={
                          c.briefStale ||
                          brief.number !== c.briefs.at(-1)?.number
                        }
                        onClick={() => window.print()}
                      >
                        Print / save PDF
                      </button>
                    </div>
                    {(c.briefStale ||
                      brief.number !== c.briefs.at(-1)?.number) && (
                      <div className="notice">
                        Historical brief — evidence or review has changed.
                        Create a new brief before exporting.
                      </div>
                    )}
                    {renderBrief(brief)}
                  </>
                )}
              </section>
            )}
          </>
        )}
        <div className="bottom-note">
          ConsultReady organizes evidence and questions. It does not provide
          legal advice or determine liability.
        </div>
      </main>
      {source && (
        <div className="overlay" onClick={() => setSource(null)}>
          <section
            className="drawer"
            ref={(node) => {
              modalRef.current = node;
            }}
            role="dialog"
            aria-modal="true"
            aria-label="Source document"
            onClick={(e) => e.stopPropagation()}
          >
            <button className="close" onClick={() => setSource(null)}>
              Close ×
            </button>
            <div className="eyebrow">ORIGINAL EVIDENCE</div>
            <h2>{source.title}</h2>
            {quote && (
              <>
                <h3>Cited excerpt</h3>
                <blockquote>{quote}</blockquote>
              </>
            )}
            <a
              href={`/api/cases/${c?.id}/sources/${source.id}/original`}
              target="_blank"
              rel="noreferrer"
            >
              Open original ↗
            </a>
            {source.pages.map((p) => (
              <section key={p.number}>
                <h3>Page {p.number}</h3>
                <pre>{p.text}</pre>
              </section>
            ))}
          </section>
        </div>
      )}
      {edit && (
        <div className="overlay">
          <form
            className="edit-dialog"
            ref={(node) => {
              modalRef.current = node;
            }}
            role="dialog"
            aria-modal="true"
            aria-label="Correct fact"
            onSubmit={(e) => {
              e.preventDefault();
              void run("Saving correction", async () => {
                await mutate(
                  "/facts/" + edit.id,
                  {
                    review: "confirmed",
                    text: editText,
                    amount: edit.kind === "event" ? null : Number(editAmount),
                  },
                  "PATCH",
                );
                setEdit(null);
              });
            }}
          >
            <h2>Record your correction</h2>
            <p>
              Your correction is labeled user-reported. The original claim and
              citation remain available.
            </p>
            <label>
              Corrected statement
              <textarea
                value={editText}
                required
                maxLength={1200}
                onChange={(e) => setEditText(e.target.value)}
              />
            </label>
            {edit.kind !== "event" && (
              <label>
                Corrected amount (USD)
                <input
                  type="number"
                  min="0"
                  max="10000000"
                  step="0.01"
                  required
                  value={editAmount}
                  onChange={(e) => setEditAmount(e.target.value)}
                />
              </label>
            )}
            <div className="actions">
              <button type="button" onClick={() => setEdit(null)}>
                Cancel
              </button>
              <button className="primary" disabled={!!busy}>
                Save correction
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
