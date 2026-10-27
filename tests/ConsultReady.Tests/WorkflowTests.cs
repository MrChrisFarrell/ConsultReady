using System.Net;
using System.Net.Http.Json;
using ConsultReady;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
namespace ConsultReady.Tests;

public class WorkflowTests
{
    sealed class App(string mode = "Hosted") : WebApplicationFactory<Program>
    {
        readonly string path = Path.Combine(Path.GetTempPath(), "consultready-tests", Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder b) => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["ConsultReady:Mode"] = mode, ["ConsultReady:DataDirectory"] = path, ["MODEL_API_KEY"] = "" }));
    }
    static HttpClient Client(App app)
    {
        var h = app.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        h.DefaultRequestHeaders.Add("X-ConsultReady", "1");
        return h;
    }
    static async Task<CaseFile> Case(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<CaseFile>())!;
    }
    [Fact]
    public async Task RevisedReceiptRequiresReviewAndUpdatesBrief()
    {
        using var app = new App();
        using var h = Client(app);
        var c = await Case(await h.PostAsJsonAsync("/api/cases", new
        {
            sample = "conflicting"
        }));
        var url = "/api/cases/" + c.Id;
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsync(url + "/brief", null)).StatusCode);
        c = await Case(await h.PostAsJsonAsync(url + "/analyze", new
        {
            engine = "walkthrough"
        }));
        Assert.Equal(6, c.Facts.Count);
        Assert.Single(c.Issues);
        foreach (var f in c.Facts)
            c = await Case(await h.PatchAsJsonAsync(url + "/facts/" + f.Id, new
            {
                review = "confirmed"
            }));
        c = await Case(await h.PostAsync(url + "/brief", null));
        Assert.Equal(3000, c.Briefs[0].Financials.Balance);
        c = await Case(await h.PostAsync(url + "/sample-payment", null));
        Assert.True(c.BriefStale);
        Assert.True(c.NeedsAnalysis);
        Assert.Equal(HttpStatusCode.Conflict, (await h.PostAsync(url + "/brief", null)).StatusCode);
        c = await Case(await h.PostAsJsonAsync(url + "/analyze", new
        {
            engine = "walkthrough"
        }));
        Assert.Single(c.Facts, f => f.Review == "pending");
        c = await Case(await h.PatchAsJsonAsync(url + "/facts/later-paid", new
        {
            review = "confirmed"
        }));
        c = await Case(await h.PostAsync(url + "/brief", null));
        Assert.Equal(2000, c.Briefs[1].Financials.Balance);
        Assert.Equal(3000, c.Briefs[0].Financials.Balance);
        Assert.Contains(c.Briefs[1].Changes, x => x.Contains("$3,000.00") && x.Contains("$2,000.00"));
        Assert.False(c.BriefStale);
    }
    [Fact]
    public async Task HostedModeIsIsolatedAndRejectsArbitraryInput()
    {
        using var app = new App();
        using var a = Client(app);
        using var b = Client(app);
        var c = await Case(await a.PostAsJsonAsync("/api/cases", new
        {
            sample = "missing"
        }));
        var url = "/api/cases/" + c.Id;
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync("/api/cases", new
        {
            title = "custom"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync(url + "/sources/text", new
        {
            title = "x",
            text = "private"
        })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await a.PostAsJsonAsync(url + "/analyze", new
        {
            engine = "meta"
        })).StatusCode);
        c = await Case(await a.GetAsync(url));
        Assert.Empty(c.Facts);
        c = await Case(await a.PostAsJsonAsync(url + "/analyze", new
        {
            engine = "walkthrough"
        }));
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PatchAsJsonAsync(url + "/facts/price", new
        {
            review = "confirmed",
            text = "arbitrary",
            amount = 1
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync(url + "/answers", new
        {
            questionId = "approval-version",
            text = "arbitrary"
        })).StatusCode);
        a.DefaultRequestHeaders.Remove("X-ConsultReady");
        Assert.Equal(HttpStatusCode.Forbidden, (await a.DeleteAsync(url)).StatusCode);
    }
    [Theory]
    [InlineData("conflicting")]
    [InlineData("straightforward")]
    [InlineData("missing")]
    public void FixturesHaveExactCitationsAndConsistentMoney(string name)
    {
        var c = Samples.Create(name);
        CaseService.Apply(c, Samples.Walkthrough(c), "walkthrough");
        foreach (var f in c.Facts)
            f.Review = "confirmed";
        Assert.Equal(3000, Evidence.Calculate(c.Facts).Balance);
        var brief = Evidence.CreateBrief(c, Samples.Draft(c));
        Assert.Equal(c.Facts.Count, brief.Facts.Count);
    }
    [Fact]
    public void UnsupportedCitationRejected()
    {
        var c = Samples.Create("conflicting");
        var r = Samples.Walkthrough(c);
        r.Facts[0].Evidence = [new("contract", 1, "This is invented")];
        Assert.Throws<AppError>(() => Evidence.Validate(r, c.Sources));
    }
    [Fact]
    public void UnsupportedMoneyRejected()
    {
        var c = Samples.Create("conflicting");
        var r = Samples.Walkthrough(c);
        r.Facts[0].Amount = 999;
        Assert.Throws<AppError>(() => Evidence.Validate(r, c.Sources));
    }
    [Fact]
    public void DuplicatePaymentsBlockBalance()
    {
        var c = Samples.Create("conflicting");
        var r = Samples.Walkthrough(c);
        foreach (var f in r.Facts)
            f.Review = "confirmed";
        r.Facts.Add(r.Facts.Single(f => f.Kind == "payment"));
        Assert.Null(Evidence.Calculate(r.Facts).Balance);
    }
    [Fact]
    public void BriefRejectsRepeatedFactIds()
    {
        var c = Samples.Create("missing");
        CaseService.Apply(c, Samples.Walkthrough(c), "walkthrough");
        foreach (var f in c.Facts)
            f.Review = "confirmed";
        var draft = Samples.Draft(c);
        draft.FactIds.Add(draft.FactIds[0]);
        Assert.Throws<AppError>(() => Evidence.CreateBrief(c, draft));
    }
    [Fact]
    public void OptimisticConcurrencyPreservesNewerCase()
    {
        var settings = new AppSettings(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConsultReady:DataDirectory"] = Path.Combine(Path.GetTempPath(), "consultready-tests", Guid.NewGuid().ToString("N")) }).Build());
        var store = new CaseStore(settings);
        var c = Samples.Create("missing");
        store.Add("owner", c);
        var stale = store.Get("owner", c.Id);
        c.Changed();
        store.Save("owner", c, 0);
        stale.Changed();
        Assert.Throws<AppError>(() => store.Save("owner", stale, 0));
    }
    [Fact]
    public async Task LocalPdfRoundTripAndDuplicateDetection()
    {
        using var app = new App("Local");
        using var h = Client(app);
        var c = await Case(await h.PostAsJsonAsync("/api/cases", new
        {
            title = "Local PDF case"
        }));
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(600, 800);
        page.AddText("Receipt: received $1000 on October 2, 2026.", 12, new UglyToad.PdfPig.Core.PdfPoint(30, 700), font);
        var bytes = builder.Build();
        MultipartFormDataContent Upload()
        {
            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(bytes), "file", "receipt.pdf");
            return form;
        }
        using var form = Upload();
        c = await Case(await h.PostAsync("/api/cases/" + c.Id + "/sources/pdf", form));
        Assert.Contains("$1000", c.Sources[0].Pages[0].Text);
        Assert.Equal(bytes, await h.GetByteArrayAsync("/api/cases/" + c.Id + "/sources/" + c.Sources[0].Id + "/original"));
        using var duplicate = Upload();
        Assert.Equal(HttpStatusCode.BadRequest, (await h.PostAsync("/api/cases/" + c.Id + "/sources/pdf", duplicate)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await h.DeleteAsync("/api/cases/" + c.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.GetAsync("/api/cases/" + c.Id)).StatusCode);
    }
}
