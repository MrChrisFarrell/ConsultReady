namespace ConsultReady;

public static class Samples
{
    public static readonly string[] Names = ["conflicting", "straightforward", "missing"];
    public static SourceDocument Source(string id, string title, string text) => new(id, title,
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))), [new(1, text)]);
    public static CaseFile Create(string name)
    {
        if (!Names.Contains(name))
            throw new AppError(400, "Unknown sample.");
        var c = new CaseFile { Title = "Maya · " + (name == "missing" ? "Incomplete records" : name == "conflicting" ? "Website project" : "Agreed delivery"), Sample = name };
        c.Sources = [Source("contract","Project agreement","SYNTHETIC EXAMPLE\nSeptember 1, 2026: Maya Chen will build a website for Alder Studio. Total project price: $6,000.00 USD. Deposit: $3,000.00 USD. Includes five pages and two revision rounds."),
            Source("deposit","Deposit receipt","SYNTHETIC EXAMPLE\nReceipt DEP-001. September 2, 2026: Maya received $3,000.00 USD from Alder Studio as the website deposit."),
            Source("invoice","Final invoice","SYNTHETIC EXAMPLE\nInvoice INV-002. September 23, 2026: Final invoice amount: $3,000.00 USD. An invoice does not establish payment or acceptance.")];
        if (name != "missing")
            c.Sources.Add(Source("delivery", "Delivery correspondence", "SYNTHETIC EXAMPLE\nSeptember 12, 2026: Maya sent version 1 for review. September 22, 2026: Maya sent version 2 with requested revisions as final delivery."));
        if (name == "conflicting")
            c.Sources.Add(Source("approval", "Client correspondence", "SYNTHETIC EXAMPLE\nSeptember 15, 2026: Alder Studio wrote: Looks good, thanks! September 25, 2026: Alder Studio raised mobile-layout concerns about the latest version."));
        if (name == "straightforward")
            c.Sources.Add(Source("approval", "Client correspondence", "SYNTHETIC EXAMPLE\nSeptember 24, 2026: Alder Studio confirmed review and approval of version 2 delivered September 22."));
        return c;
    }
    public static SourceDocument LaterPayment() => Source("later-payment", "Additional payment receipt", "SYNTHETIC EXAMPLE\nReceipt PAY-002. October 2, 2026: Maya received an additional $1,000.00 USD from Alder Studio. This is separate from deposit DEP-001.");
    public static AnalysisResult Walkthrough(CaseFile c)
    {
        if (c.Sample is null)
            throw new AppError(400, "The walkthrough is available only for bundled samples.");
        Fact F(string id, string text, string date, string source, string kind = "event", decimal? amount = null) => new()
        {
            Id = id,
            Text = text,
            Date = date,
            Kind = kind,
            Amount = amount,
            Evidence = [new(source, 1, c.Sources.Single(s => s.Id == source).Pages[0].Text)]
        };
        List<Fact> facts = [F("price", "The agreement lists a $6,000 project price.", "2026-09-01", "contract", "contract_total", 6000), F("deposit-paid", "A $3,000 deposit is documented.", "2026-09-02", "deposit", "payment", 3000), F("invoice-issued", "The final invoice lists $3,000; it is not proof of payment.", "2026-09-23", "invoice", "invoice_amount", 3000)];
        List<Issue> issues = [];
        List<Clarification> questions = [];
        if (c.Sample != "missing")
            facts.Add(F("delivery-v2", "Maya described version 2 as final delivery.", "2026-09-22", "delivery"));
        if (c.Sample == "conflicting")
        {
            facts.Add(F("approval-v1", "The client wrote ‘Looks good’ before version 2 was delivered.", "2026-09-15", "approval"));
            facts.Add(F("complaint", "The client later raised mobile-layout concerns.", "2026-09-25", "approval"));
            issues.Add(new("version-conflict", "The positive response predates final delivery. It does not establish approval of version 2.", ["approval-v1", "delivery-v2"]));
            questions.Add(new("approval-version", "Which version did the positive response refer to?", ["approval-v1", "delivery-v2"]));
        }
        if (c.Sample == "straightforward")
            facts.Add(F("approval-v2", "The client confirmed approval of version 2.", "2026-09-24", "approval"));
        if (c.Sample == "missing")
        {
            issues.Add(new("missing-delivery", "No delivery or approval correspondence is included.", ["invoice-issued"]));
            questions.Add(new("approval-version", "Do you have delivery or approval correspondence?", ["invoice-issued"]));
        }
        if (c.Sources.Any(s => s.Id == "later-payment"))
            facts.Add(F("later-paid", "A separate additional $1,000 payment is documented.", "2026-10-02", "later-payment", "payment", 1000));
        questions.Add(new("later-payments", "Were there any other payments or credits?", ["deposit-paid"]));
        return new(facts, issues, questions);
    }
    public static BriefDraft Draft(CaseFile c) => new("Consultation preparation", c.Facts.Where(f => f.Review == "confirmed").OrderBy(f => f.Date).Select(f => f.Id).ToList(), ["What additional records would help clarify delivery and acceptance?", "How should the payment records and disputed work be discussed during consultation?"]);
    public static string Answer(string choice) => choice switch { "earlier" => "I recall the response referring to an earlier version. This is my recollection, not independent evidence.", "unknown" => "I am unsure and would like to clarify this during consultation.", "no-other-payments" => "I do not recall other payments or credits beyond the uploaded records.", _ => throw new AppError(400, "Unknown sample clarification.") };
}
