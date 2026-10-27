namespace ConsultReady;

public static class CaseService
{
    public static void Apply(CaseFile c, AnalysisResult result, string engine)
    {
        Evidence.Validate(result, c.Sources);
        foreach (var f in result.Facts)
        {
            var old = c.Facts.FirstOrDefault(o => o.Kind == f.Kind && o.Text == f.Text && o.Amount == f.Amount && o.Date == f.Date && o.Evidence.SequenceEqual(f.Evidence));
            if (old is not null)
                f.Review = old.Review;
        }
        // A new model pass must not silently erase a person's explicit correction.
        foreach (var correction in c.Facts.Where(f => f.Provenance == "user-reported"))
        {
            var matched = result.Facts.FirstOrDefault(f => f.Kind == correction.Kind && f.Evidence.SequenceEqual(correction.Evidence));
            if (matched is not null)
            {
                matched.Text = correction.Text;
                matched.Amount = correction.Amount;
                matched.Date = correction.Date;
                matched.OriginalText = correction.OriginalText;
                matched.Provenance = correction.Provenance;
                matched.Review = correction.Review;
            }
            else
            {
                var retained = System.Text.Json.JsonSerializer.Deserialize<Fact>(System.Text.Json.JsonSerializer.Serialize(correction, JsonDefaults.Options), JsonDefaults.Options)!;
                retained.Id = "correction-" + Guid.NewGuid().ToString("N");
                result.Facts.Add(retained);
                result.Issues.Add(new("retained-" + retained.Id, "A user correction was retained separately because this analysis did not identify the same source claim. Review for duplicate claims.", [retained.Id]));
                retained.Review = "pending";
            }
        }
        c.Facts = result.Facts;
        c.Issues = result.Issues;
        c.Questions = result.Questions;
        c.Engine = engine;
        c.Changed();
        c.AnalyzedRevision = c.Revision;
    }
}
