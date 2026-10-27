using System.Globalization;
using System.Text.RegularExpressions;

namespace ConsultReady;

public static partial class Evidence
{
    public static string Normalize(string value) => WhiteSpace().Replace(value, " ").Trim();
    [GeneratedRegex(@"\s+")] private static partial Regex WhiteSpace();
    [GeneratedRegex(@"(?<![\d.])\d[\d,]*(?:\.\d{1,2})?(?![\d.])")] private static partial Regex Numbers();

    public static void Validate(AnalysisResult result, IReadOnlyList<SourceDocument> sources)
    {
        if (result.Facts is null || result.Issues is null || result.Questions is null || result.Facts.Count > 80 || result.Questions.Count > 8 || result.Issues.Count > 12)
            throw new AppError(502, "The model returned an invalid analysis. Your documents have not changed.");
        var ids = new HashSet<string>();
        foreach (var fact in result.Facts)
        {
            if (string.IsNullOrWhiteSpace(fact.Id) || !ids.Add(fact.Id) || string.IsNullOrWhiteSpace(fact.Text) || fact.Text.Length > 1200 || fact.Evidence is null || fact.Evidence.Count is < 1 or > 6)
                throw new AppError(502, "A proposed fact is missing a unique identifier or source evidence.");
            if (fact.Kind is not ("event" or "contract_total" or "payment" or "invoice_amount") || fact.Currency != "USD")
                throw new AppError(502, "Unsupported fact type or currency. This prototype supports USD cases only.");
            if (fact.Date is not null && !DateOnly.TryParseExact(fact.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new AppError(502, "A proposed date is invalid.");
            foreach (var reference in fact.Evidence)
            {
                var page = sources.FirstOrDefault(s => s.Id == reference.SourceId)?.Pages.FirstOrDefault(p => p.Number == reference.Page);
                if (page is null || string.IsNullOrWhiteSpace(reference.Quote) || reference.Quote.Length > 1500 || !Normalize(page.Text).Contains(Normalize(reference.Quote), StringComparison.Ordinal))
                    throw new AppError(502, "A model citation could not be matched to the source. Analysis was not saved; retry or inspect the document.");
            }
            if (fact.Kind != "event" && (fact.Amount is null or < 0 or > 10000000 || decimal.Round(fact.Amount.Value, 2) != fact.Amount))
                throw new AppError(502, "A monetary fact has an invalid amount.");
            if (fact.Amount is not null && !fact.Evidence.Any(e => Numbers().Matches(e.Quote).Any(m => decimal.TryParse(m.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n == fact.Amount)))
                throw new AppError(502, "A proposed monetary amount does not occur in its cited excerpt.");
            fact.Provenance = "document";
            fact.Review = "pending";
            fact.OriginalText = null;
        }
        if (result.Issues.Any(i => string.IsNullOrWhiteSpace(i.Text) || i.FactIds is null || i.FactIds.Count == 0 || i.FactIds.Any(id => !ids.Contains(id))) ||
            result.Questions.Any(q => string.IsNullOrWhiteSpace(q.Question) || q.FactIds is null || q.FactIds.Count == 0 || q.FactIds.Any(id => !ids.Contains(id))) ||
            result.Questions.Select(q => q.Id).Distinct().Count() != result.Questions.Count)
            throw new AppError(502, "A model question or issue references an unknown fact.");
    }

    public static FinancialSummary Calculate(IEnumerable<Fact> input)
    {
        var facts = input.Where(f => f.Review == "confirmed").ToList();
        var totals = facts.Where(f => f.Kind == "contract_total").Select(f => f.Amount).Distinct().ToList();
        var payments = facts.Where(f => f.Kind == "payment").ToList();
        // Multiple evidence references to a payment are not independent receipts. Ambiguous duplicates block the total.
        var duplicate = payments.GroupBy(f => (f.Date, f.Amount)).Any(g => g.Count() > 1);
        var pendingMoney = input.Any(f => f.Review == "pending" && f.Kind != "event");
        var contract = totals.Count == 1 ? totals[0] : null;
        var paid = payments.Sum(f => f.Amount ?? 0);
        var complete = contract is not null && !duplicate && !pendingMoney;
        return new(contract, paid, complete ? contract - paid : null, complete,
            duplicate ? "Possible duplicate payments: review and reject the duplicate before calculating." :
            pendingMoney ? "Review all monetary facts before calculating a balance." :
            contract is null ? "A single reviewed contract total is required." :
            "Reviewed contract total minus reviewed payments. Not a finding of legal liability; undocumented payments may exist.");
    }

    public static BriefRevision CreateBrief(CaseFile item, BriefDraft draft)
    {
        if (item.NeedsAnalysis || item.Facts.Count == 0 || item.Facts.Any(f => f.Review == "pending"))
            throw new AppError(409, "Analyze the current evidence and review every fact before creating a brief.");
        var accepted = item.Facts.Where(f => f.Review == "confirmed").ToList();
        if (draft.FactIds is null || draft.Questions is null || string.IsNullOrWhiteSpace(draft.Purpose) || draft.Questions.Count > 8 || draft.Questions.Any(q => q.Length > 1000) ||
            draft.FactIds.Count != accepted.Count || draft.FactIds.Distinct().Count() != accepted.Count || draft.FactIds.Any(id => accepted.All(f => f.Id != id)))
            throw new AppError(502, "The proposed brief did not preserve the reviewed facts.");
        var financials = Calculate(item.Facts);
        var ordered = draft.FactIds.Select(id => accepted.Single(f => f.Id == id)).ToList();
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<List<Fact>>(System.Text.Json.JsonSerializer.Serialize(ordered, JsonDefaults.Options), JsonDefaults.Options)!;
        var changes = new List<string>();
        if (item.Briefs.LastOrDefault() is { } previous)
        {
            if (financials.Balance != previous.Financials.Balance)
                changes.Add($"Documented balance changed from {Money(previous.Financials.Balance)} to {Money(financials.Balance)}.");
            foreach (var fact in snapshot.Where(f => previous.Facts.All(p => p.Text != f.Text)))
                changes.Add($"Added or revised: {fact.Text}");
            foreach (var fact in previous.Facts.Where(f => snapshot.All(p => p.Text != f.Text)))
                changes.Add($"Removed or replaced: {fact.Text}");
            foreach (var answer in item.Answers.Where(a => !previous.Answers.Contains(a)))
                changes.Add($"Clarification: {answer.Text}");
        }
        return new(item.Briefs.Count + 1, item.Revision, DateTimeOffset.UtcNow,
            "Prepare for a consultation about a freelance project and payment records.", snapshot,
            [.. item.Issues], [.. item.Answers], draft.Questions, financials, changes, item.Engine);
    }
    private static string Money(decimal? value) => value?.ToString("C2", CultureInfo.GetCultureInfo("en-US")) ?? "unresolved";
}
