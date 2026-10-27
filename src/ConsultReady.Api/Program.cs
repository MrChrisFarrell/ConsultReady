using ConsultReady;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 6 * 1024 * 1024);
builder.Services.AddSingleton<AppSettings>();
builder.Services.AddSingleton<CaseStore>();
builder.Services.AddHostedService<CleanupService>();
builder.Services.AddHttpClient<MetaProvider>(h => h.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); o.AddConcurrencyLimiter("model", x => { x.PermitLimit = 2; x.QueueLimit = 0; }); });
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    var settings = ctx.RequestServices.GetRequiredService<AppSettings>();
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (ctx.Request.Method is not ("GET" or "HEAD") && ctx.Request.Headers["X-ConsultReady"] != "1")
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "Missing request protection header."
            });
            return;
        }
        if (!settings.Hosted && ctx.Connection.RemoteIpAddress is { } ip && !System.Net.IPAddress.IsLoopback(ip))
        {
            ctx.Response.StatusCode = 403;
            return;
        }
    }
    var owner = ctx.Request.Cookies["cr-session"];
    if (owner is null || owner.Length != 64 || !owner.All(Uri.IsHexDigit))
    {
        owner = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ctx.Response.Cookies.Append("cr-session", owner, new()
        {
            HttpOnly = true,
            Secure = settings.Hosted || ctx.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromDays(7)
        });
    }
    ctx.Items["owner"] = owner;
    try
    {
        await next();
    }
    catch (AppError e) { ctx.Response.StatusCode = e.Status; await ctx.Response.WriteAsJsonAsync(new { error = e.Message }); }
    catch (BadHttpRequestException) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = "Invalid request." }); }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
    catch (Exception e) { app.Logger.LogError("Request failed: {Type}", e.GetType().Name); ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { error = "Operation failed. Previously saved case is unchanged." }); }
});
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/capabilities", (AppSettings s) => new { mode = s.Hosted ? "Hosted" : "Local", liveAvailable = !string.IsNullOrWhiteSpace(s.Key), model = s.Model, samples = Samples.Names });
app.MapGet("/api/cases", (HttpContext h, CaseStore s) => { s.Cleanup(); return s.List(Owner(h)); });
app.MapPost("/api/cases", (HttpContext h, CaseStore s, AppSettings a, CreateRequest r) => { if (a.Hosted && r.Sample is null) throw new AppError(403, "Public demo accepts bundled synthetic cases only."); var c = r.Sample is null ? new CaseFile { Title = Clean(r.Title ?? "New consultation", 120) } : Samples.Create(r.Sample); s.Add(Owner(h), c); return c; });
app.MapGet("/api/cases/{id}", (string id, HttpContext h, CaseStore s) => s.Get(Owner(h), id));
app.MapDelete("/api/cases/{id}", (string id, HttpContext h, CaseStore s) => { s.Delete(Owner(h), id); return Results.NoContent(); });
app.MapPost("/api/cases/{id}/sample-payment", (string id, HttpContext h, CaseStore s) => { var c = s.Get(Owner(h), id); var rev = c.Revision; if (c.Sample is null || c.Sources.Any(x => x.Id == "later-payment")) throw new AppError(400, "Additional receipt unavailable."); c.Sources.Add(Samples.LaterPayment()); c.Changed(); s.Save(Owner(h), c, rev); return c; });
app.MapPost("/api/cases/{id}/sources/text", (string id, HttpContext h, CaseStore s, AppSettings a, TextRequest r) => { Local(a); var c = s.Get(Owner(h), id); var rev = c.Revision; Add(c, Samples.Source(Guid.NewGuid().ToString("N"), Clean(r.Title, 120), Clean(r.Text, 30000))); c.Sample = null; c.Changed(); s.Save(Owner(h), c, rev); return c; });
app.MapPost("/api/cases/{id}/sources/pdf", async (string id, HttpContext h, CaseStore s, AppSettings a) => { Local(a); var c = s.Get(Owner(h), id); var rev = c.Revision; var form = await h.Request.ReadFormAsync(); var file = form.Files.GetFile("file") ?? throw new AppError(400, "Choose a PDF."); if (file.Length is 0 or > 5242880) throw new AppError(400, "PDF must be under 5 MB."); using var ms = new MemoryStream(); await file.CopyToAsync(ms); var bytes = ms.ToArray(); List<PageText> pages = []; try { using var pdf = PdfDocument.Open(bytes); if (pdf.NumberOfPages > 30) throw new AppError(400, "Maximum 30 pages."); foreach (var p in pdf.GetPages()) { var text = ContentOrderTextExtractor.GetText(p); if (string.IsNullOrWhiteSpace(text)) throw new AppError(400, "Scanned or blank pages need OCR, not supported in this version."); pages.Add(new(p.Number, text)); } } catch (AppError) { throw; } catch { throw new AppError(400, "Use an unencrypted text PDF."); } var source = new SourceDocument(Guid.NewGuid().ToString("N"), Clean(Path.GetFileName(file.FileName), 120), Convert.ToHexString(SHA256.HashData(bytes)), pages, "pdf"); Add(c, source); c.Sample = null; c.Changed(); var path = s.OriginalPath(c.Id, source.Id); await File.WriteAllBytesAsync(path, bytes); try { s.Save(Owner(h), c, rev); } catch { File.Delete(path); throw; } return c; });
app.MapGet("/api/cases/{id}/sources/{sourceId}/original", (string id, string sourceId, HttpContext h, CaseStore s) => { var c = s.Get(Owner(h), id); var source = c.Sources.FirstOrDefault(x => x.Id == sourceId) ?? throw new AppError(404, "Source not found."); return source.OriginalFile is null ? Results.Text(string.Join("\n\n", source.Pages.Select(p => p.Text))) : Results.File(s.OriginalPath(id, sourceId), "application/pdf", source.Title); });
app.MapPost("/api/cases/{id}/analyze", async (string id, HttpContext h, CaseStore s, MetaProvider meta, AnalyzeRequest r, CancellationToken ct) => { var c = s.Get(Owner(h), id); var rev = c.Revision; if (c.Sources.Count == 0) throw new AppError(400, "Add evidence first."); var result = r.Engine switch { "walkthrough" => Samples.Walkthrough(c), "meta" => await meta.Analyze(c, ct), _ => throw new AppError(400, "Unknown engine.") }; CaseService.Apply(c, result, r.Engine); s.Save(Owner(h), c, rev); return c; }).RequireRateLimiting("model");
app.MapPatch("/api/cases/{id}/facts/{factId}", (string id, string factId, HttpContext h, CaseStore s, AppSettings a, ReviewRequest r) => { var c = s.Get(Owner(h), id); var rev = c.Revision; if (c.NeedsAnalysis) throw new AppError(409, "Analyze current sources first."); var f = c.Facts.SingleOrDefault(x => x.Id == factId) ?? throw new AppError(404, "Fact not found."); if (r.Review is not ("pending" or "confirmed" or "rejected")) throw new AppError(400, "Invalid review state."); if (r.Text is not null) { Local(a); f.OriginalText ??= f.Text; f.Text = Clean(r.Text, 1200); f.Provenance = "user-reported"; if (f.Kind != "event") { if (r.Amount is null or < 0 or > 10000000 || decimal.Round(r.Amount.Value, 2) != r.Amount) throw new AppError(400, "Enter a valid amount."); f.Amount = r.Amount; } } f.Review = r.Review; c.Changed(); c.AnalyzedRevision = c.Revision; s.Save(Owner(h), c, rev); return c; });
app.MapPost("/api/cases/{id}/answers", (string id, HttpContext h, CaseStore s, AppSettings a, AnswerRequest r) => { var c = s.Get(Owner(h), id); var rev = c.Revision; var current = !c.NeedsAnalysis; var q = c.Questions.FirstOrDefault(x => x.Id == r.QuestionId) ?? throw new AppError(404, "Question not found."); if (r.Choice is null) Local(a); var answer = r.Choice is null ? Clean(r.Text ?? "", 1500) : Samples.Answer(r.Choice); c.Answers.RemoveAll(x => x.QuestionId == q.Id); c.Answers.Add(new(q.Id, q.Question, answer)); c.Changed(); if (current) c.AnalyzedRevision = c.Revision; s.Save(Owner(h), c, rev); return c; });
app.MapPost("/api/cases/{id}/brief", async (string id, HttpContext h, CaseStore s, MetaProvider meta, CancellationToken ct) => { var c = s.Get(Owner(h), id); var rev = c.Revision; if (c.NeedsAnalysis || c.Facts.Count == 0 || c.Facts.Any(f => f.Review == "pending")) throw new AppError(409, "Analyze and review every fact first."); if (c.Briefs.Count >= 30) throw new AppError(400, "Maximum 30 brief revisions."); var draft = c.Engine == "walkthrough" ? Samples.Draft(c) : await meta.Draft(c, ct); c.Changed(); c.AnalyzedRevision = c.Revision; c.Briefs.Add(Evidence.CreateBrief(c, draft)); s.Save(Owner(h), c, rev); return c; }).RequireRateLimiting("model");
app.MapFallbackToFile("index.html");
app.Run();
static string Owner(HttpContext h) => (string)h.Items["owner"]!;
static void Local(AppSettings a) { if (a.Hosted) throw new AppError(403, "Public demo accepts synthetic samples and predefined clarifications only."); }
static string Clean(string s, int max) { if (string.IsNullOrWhiteSpace(s) || s.Length > max) throw new AppError(400, $"Text required, maximum {max} characters."); return s.Trim(); }
static void Add(CaseFile c, SourceDocument s) { if (c.Sources.Count >= 10 || c.Sources.Sum(x => x.Pages.Sum(p => p.Text.Length)) + s.Pages.Sum(p => p.Text.Length) > 70000) throw new AppError(400, "Case limit: 10 documents and 70,000 characters."); if (c.Sources.Any(x => x.Hash == s.Hash)) throw new AppError(400, "Document already included."); c.Sources.Add(s); }
public sealed record CreateRequest(string? Sample, string? Title);
public sealed record TextRequest(string Title, string Text);
public sealed record AnalyzeRequest(string Engine);
public sealed record ReviewRequest(string Review, string? Text = null, decimal? Amount = null);
public sealed record AnswerRequest(string QuestionId, string? Choice = null, string? Text = null);
public partial class Program;
