using System.Text.Json;

namespace ConsultReady;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public sealed record PageText(int Number, string Text);
public sealed record SourceDocument(string Id, string Title, string Hash, List<PageText> Pages, string? OriginalFile = null);
public sealed record EvidenceRef(string SourceId, int Page, string Quote);
public sealed class Fact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public string? Date
    {
        get; set;
    }
    public string Kind { get; set; } = "event";
    public decimal? Amount
    {
        get; set;
    }
    public string Currency { get; set; } = "USD";
    public List<EvidenceRef> Evidence { get; set; } = [];
    public string Provenance { get; set; } = "document";
    public string Review { get; set; } = "pending";
    public string? OriginalText
    {
        get; set;
    }
}
public sealed record Issue(string Id, string Text, List<string> FactIds);
public sealed record Clarification(string Id, string Question, List<string> FactIds);
public sealed record Answer(string QuestionId, string Question, string Text);
public sealed record AnalysisResult(List<Fact> Facts, List<Issue> Issues, List<Clarification> Questions);
public sealed record BriefDraft(string Purpose, List<string> FactIds, List<string> Questions);
public sealed record FinancialSummary(decimal? ContractTotal, decimal Payments, decimal? Balance, bool Complete, string Explanation);
public sealed record BriefRevision(int Number, int CaseRevision, DateTimeOffset CreatedAt, string Purpose,
    List<Fact> Facts, List<Issue> Issues, List<Answer> Answers, List<string> Questions,
    FinancialSummary Financials, List<string> Changes, string Engine);
public sealed class CaseFile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New consultation";
    public string? Sample
    {
        get; set;
    }
    public int Revision
    {
        get; set;
    }
    public int AnalyzedRevision { get; set; } = -1;
    public string Engine { get; set; } = "none";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SourceDocument> Sources { get; set; } = [];
    public List<Fact> Facts { get; set; } = [];
    public List<Issue> Issues { get; set; } = [];
    public List<Clarification> Questions { get; set; } = [];
    public List<Answer> Answers { get; set; } = [];
    public List<BriefRevision> Briefs { get; set; } = [];
    public FinancialSummary Financials => Evidence.Calculate(Facts);
    public bool NeedsAnalysis => AnalyzedRevision != Revision;

    public bool BriefStale => Briefs.Count == 0 || Briefs[^1].CaseRevision != Revision;
    public void Changed()
    {
        Revision++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
public sealed class AppError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public sealed class AppSettings(IConfiguration config)
{
    public bool Hosted { get; } = config["ConsultReady:Mode"] == "Hosted";
    public string DataDirectory
    {
        get;
    } = config["ConsultReady:DataDirectory"] ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConsultReady");
    public string Model { get; } = config["Meta:Model"] ?? "muse-spark-1.3";
    public string? Key { get; } = config["MODEL_API_KEY"];
    public int DailyCalls { get; } = Math.Clamp(config.GetValue("Meta:DailyCallLimit", 100), 1, 1000);
}
