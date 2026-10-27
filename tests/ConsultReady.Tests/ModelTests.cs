using ConsultReady;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Text;
using System.Text.Json;
namespace ConsultReady.Tests;

public class ModelTests
{
    sealed class Handler(string payload) : HttpMessageHandler
    {
        public string Request = ""; protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
    static AppSettings Settings() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MODEL_API_KEY"] = "test-only-not-a-real-key", ["ConsultReady:DataDirectory"] = Path.Combine(Path.GetTempPath(), "consultready-tests", Guid.NewGuid().ToString("N")) }).Build());
    [Fact]
    public async Task StructuredResponseUsesMetaAndTreatsDocumentsAsData()
    {
        var c = Samples.Create("missing");
        var result = Samples.Walkthrough(c);
        var handler = new Handler(JsonSerializer.Serialize(new
        {
            choices = new[] { new { finish_reason = "stop", message = new { content = JsonSerializer.Serialize(result, JsonDefaults.Options) } } }
        }));
        var settings = Settings();
        var provider = new MetaProvider(new HttpClient(handler), settings, new CaseStore(settings));
        var output = await provider.Analyze(c, default);
        Evidence.Validate(output, c.Sources);
        using var body = JsonDocument.Parse(handler.Request);
        Assert.Equal("muse-spark-1.3", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Contains("untrusted data", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.False(body.RootElement.TryGetProperty("tools", out _));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\"}]}")]
    public async Task BrokenResponsesAreExplicitFailures(string payload)
    {
        var settings = Settings();
        var provider = new MetaProvider(new HttpClient(new Handler(payload)), settings, new CaseStore(settings));
        var error = await Assert.ThrowsAsync<AppError>(() => provider.Analyze(Samples.Create("missing"), default));
        Assert.Equal(502, error.Status);
    }
    [Fact]
    public void CorrectionSurvivesReanalysis()
    {
        var c = Samples.Create("missing");
        CaseService.Apply(c, Samples.Walkthrough(c), "walkthrough");
        var f = c.Facts[0];
        f.OriginalText = f.Text;
        f.Text = "I recall a negotiated price of $5,000.";
        f.Amount = 5000;
        f.Provenance = "user-reported";
        f.Review = "confirmed";
        CaseService.Apply(c, Samples.Walkthrough(c), "walkthrough");
        Assert.Equal(5000, c.Facts[0].Amount);
        Assert.Equal("user-reported", c.Facts[0].Provenance);
        Assert.NotNull(c.Facts[0].OriginalText);
    }
}
