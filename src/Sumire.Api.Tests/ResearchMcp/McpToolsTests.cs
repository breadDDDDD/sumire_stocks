using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Sumire.Api.Data;
using Sumire.Api.Relations;
using Sumire.Api.Research;
using Sumire.Api.Research.FactPacket;
using Sumire.Api.Research.Mcp;
using Sumire.Api.Tests.Research;

namespace Sumire.Api.Tests.ResearchMcp;

sealed class FakeMcpData : IMcpData
{
    public List<ResearchJob> Jobs { get; } = [];
    public List<Insight> Insights { get; } = [];
    public List<EnqueueRequest> Enqueued { get; } = [];
    public int Saves { get; private set; }

    public Task<IReadOnlyList<ResearchJob>> OpenJobsAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ResearchJob>>(Jobs.Where(j => j.Status == "pending" || (j.Status == "running" && j.Runner == "mcp"))
            .OrderByDescending(j => j.Priority).Take(limit).ToList());

    public Task<ResearchJob?> FindJobAsync(long id, CancellationToken ct) => Task.FromResult(Jobs.FirstOrDefault(j => j.Id == id));

    public Task SaveAsync(CancellationToken ct) { Saves++; return Task.CompletedTask; }

    public Task<long> AddInsightAsync(Insight insight, CancellationToken ct)
    {
        insight.Id = Insights.Count + 1;
        Insights.Add(insight);
        return Task.FromResult(insight.Id);
    }

    public Task<EnqueueResult> EnqueueAsync(EnqueueRequest request, CancellationToken ct)
    {
        Enqueued.Add(request);
        return Task.FromResult(new EnqueueResult(100 + Enqueued.Count, "queued"));
    }

    public Task<PacketInputs> InputsAsync(FactPacketRequest request, CancellationToken ct) => Task.FromResult(new PacketInputs
    {
        Now = PacketFixtures.Now,
        Universe = PacketFixtures.Universe,
        Stock = request.Kind == ResearchKinds.Stock ? PacketFixtures.Universe.Stocks.GetValueOrDefault(request.Subject) : null,
    });

    public Task<StockRelations?> RelationsAsync(string code, CancellationToken ct) => Task.FromResult<StockRelations?>(
        code == "PTBA" ? new StockRelations("PTBA", "Bukit Asam", "Energy", null, [], [], [], [], [], null, "2026-10-01") : null);

    public ISet<string> KnownTickers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ADRO", "PTBA", "ITMG", "BBCA", "ZZZZ" };
}

sealed class RecordingNotifier : IInsightNotifier
{
    public List<InsightReady> Sent { get; } = [];
    public Task InsightReadyAsync(InsightReady message, CancellationToken ct) { Sent.Add(message); return Task.CompletedTask; }
}

public class McpToolsTests
{
    readonly FakeMcpData _data = new();
    readonly RecordingNotifier _notifier = new();
    readonly SumireMcpTools _tools;

    public McpToolsTests()
    {
        _tools = new SumireMcpTools(_data, _notifier, new ResearchOptions(), FakeClock.AtWib(2026, 10, 22, 17));
        _data.Jobs.Add(new ResearchJob { Id = 1, Kind = "stock", Subject = "ADRO", Priority = 80, CreatedAt = PacketFixtures.Now });
        // ZZZZ has no price history, so its packet has no ADRO moves for the validator to check numbers against.
        _data.Jobs.Add(new ResearchJob { Id = 2, Kind = "stock", Subject = "ZZZZ", Priority = 60, CreatedAt = PacketFixtures.Now });
        _data.Jobs.Add(new ResearchJob { Id = 3, Kind = "ask", Subject = "Why is coal up?", Priority = 80, Packet = """{"question":"Why is coal up?"}""", CreatedAt = PacketFixtures.Now });
        _data.Jobs.Add(new ResearchJob { Id = 4, Kind = "stock", Subject = "BBCA", Status = "running", Runner = "agy", CreatedAt = PacketFixtures.Now });
    }

    static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    static string Insight() => ResearchFixtures.Read("insight.json");

    [Fact]
    public async Task List_pending_returns_jobs_by_priority()
    {
        var r = Parse(await _tools.ListPendingResearchAsync(10, default));
        Assert.Equal(3, (int)r["count"]!);
        Assert.Equal(80, (int)r["jobs"]![0]!["priority"]!);
        Assert.Equal("Why is coal up?", (string?)r["jobs"]!.AsArray().First(j => (int)j!["id"]! == 3)!["question"]);
    }

    [Fact]
    public async Task Get_job_claims_it_and_returns_packet_instructions_and_schema()
    {
        var r = Parse(await _tools.GetResearchJobAsync(1, default));

        Assert.Equal("ADRO", (string?)r["subject"]);
        Assert.True(r["packet"]!["moves"]!.AsObject().ContainsKey("ADRO"));
        Assert.Contains("submit_insight", (string?)r["instructions"]);
        Assert.Contains("<<<PACKET", (string?)r["instructions"]);
        Assert.NotNull(r["schema"]!["properties"]);
        var job = _data.Jobs[0];
        Assert.Equal("running", job.Status);
        Assert.Equal("mcp", job.Runner);
        Assert.Equal(1, job.Attempts);
        Assert.Contains("\"asOf\"", job.Packet);
    }

    [Fact]
    public async Task Get_job_twice_keeps_the_same_packet_and_attempt()
    {
        await _tools.GetResearchJobAsync(1, default);
        var packet = _data.Jobs[0].Packet;
        await _tools.GetResearchJobAsync(1, default);
        Assert.Equal(packet, _data.Jobs[0].Packet);
        Assert.Equal(1, _data.Jobs[0].Attempts);
    }

    [Fact]
    public async Task Ask_job_keeps_its_question_in_the_packet()
    {
        var r = Parse(await _tools.GetResearchJobAsync(3, default));
        Assert.Equal("Why is coal up?", (string?)r["question"]);
        Assert.Equal("Why is coal up?", (string?)r["packet"]!["question"]);
    }

    [Fact]
    public async Task Jobs_run_by_another_runner_or_unknown_are_refused()
    {
        Assert.Contains("agy", (string?)Parse(await _tools.GetResearchJobAsync(4, default))["error"]);
        Assert.NotNull(Parse(await _tools.GetResearchJobAsync(999, default))["error"]);
    }

    [Fact]
    public async Task Submit_before_get_is_refused()
    {
        var r = Parse(await _tools.SubmitInsightAsync(1, Insight(), null, default));
        Assert.Contains("get_research_job", (string?)r["error"]);
        Assert.Empty(_data.Insights);
    }

    [Fact]
    public async Task Clean_insight_is_stored_marks_the_job_done_and_notifies()
    {
        await _tools.GetResearchJobAsync(2, default);
        var r = Parse(await _tools.SubmitInsightAsync(2, Insight(), "gemini-3-pro", default));

        Assert.True((bool)r["accepted"]!);
        var insight = Assert.Single(_data.Insights);
        Assert.Equal("mcp", insight.Runner);
        Assert.Equal("gemini-3-pro", insight.Model);
        Assert.Null(insight.Warnings);
        var job = _data.Jobs[1];
        Assert.Equal("done", job.Status);
        Assert.Equal(insight.Id, job.InsightId);
        Assert.Null(job.Packet);
        Assert.Equal(new InsightReady("stock", "ZZZZ", insight.Id), Assert.Single(_notifier.Sent));
    }

    [Fact]
    public async Task First_submission_with_issues_is_rejected_and_the_second_is_stored_with_warnings()
    {
        await _tools.GetResearchJobAsync(1, default);
        // The fixture claims ADRO rose 3.1% today; the synthetic packet disagrees.
        var first = Parse(await _tools.SubmitInsightAsync(1, Insight(), null, default));
        Assert.False((bool)first["accepted"]!);
        Assert.NotEmpty(first["issues"]!.AsArray());
        Assert.Empty(_data.Insights);
        Assert.Equal("running", _data.Jobs[0].Status);

        var second = Parse(await _tools.SubmitInsightAsync(1, Insight(), null, default));
        Assert.True((bool)second["accepted"]!);
        Assert.NotEmpty(second["warnings"]!.AsArray());
        Assert.NotNull(Assert.Single(_data.Insights).Warnings);
        Assert.Equal("done", _data.Jobs[0].Status);
    }

    [Fact]
    public async Task Malformed_json_is_rejected_without_using_up_the_retry()
    {
        await _tools.GetResearchJobAsync(2, default);
        Assert.False((bool)Parse(await _tools.SubmitInsightAsync(2, "{not json", null, default))["accepted"]!);
        Assert.False((bool)Parse(await _tools.SubmitInsightAsync(2, """{"summary":"x"}""", null, default))["accepted"]!);
        Assert.True((bool)Parse(await _tools.SubmitInsightAsync(2, Insight(), null, default))["accepted"]!);
    }

    [Fact]
    public async Task Done_job_cannot_be_submitted_again()
    {
        await _tools.GetResearchJobAsync(2, default);
        await _tools.SubmitInsightAsync(2, Insight(), null, default);
        Assert.Contains("already done", (string?)Parse(await _tools.SubmitInsightAsync(2, Insight(), null, default))["error"]);
    }

    [Theory]
    [InlineData("stock", "bbca", null, "stock", "bbca")]
    [InlineData("open", "", null, "brief_open", "IHSG")]
    [InlineData("ask", "", "What moves banks?", "ask", "What moves banks?")]
    [InlineData("sector", "Energy", null, "sector", "Energy")]
    public async Task Queue_research_normalizes_kinds(string kind, string subject, string? question, string expectedKind, string expectedSubject)
    {
        var r = Parse(await _tools.QueueResearchAsync(kind, subject, question, default));
        Assert.Equal("queued", (string?)r["status"]);
        var req = Assert.Single(_data.Enqueued);
        Assert.Equal(expectedKind, req.Kind);
        Assert.Equal(expectedSubject, req.Subject);
    }

    [Theory]
    [InlineData("bogus", "BBCA", null)]
    [InlineData("stock", "not a ticker", null)]
    [InlineData("ask", "", null)]
    public async Task Queue_research_rejects_bad_input(string kind, string subject, string? question)
    {
        Assert.NotNull(Parse(await _tools.QueueResearchAsync(kind, subject, question, default))["error"]);
        Assert.Empty(_data.Enqueued);
    }

    [Fact]
    public async Task Snapshot_overview_and_relations_read_without_headlines()
    {
        var snap = Parse(await _tools.GetStockSnapshotAsync("adro", default));
        Assert.NotNull(snap["technicals"]);
        Assert.Null(snap["headlines"]);
        Assert.NotNull(Parse(await _tools.GetStockSnapshotAsync("ZZZZ", default))["error"]);

        var overview = Parse(await _tools.GetMarketOverviewAsync(default));
        Assert.NotNull(overview["market"]);
        Assert.NotNull(overview["topMovers"]);

        Assert.Equal("PTBA", (string?)Parse(await _tools.GetRelationsAsync("ptba", default))["code"]);
        Assert.NotNull(Parse(await _tools.GetRelationsAsync("NOPE", default))["error"]);
    }

    [Fact]
    public void Server_exposes_the_seven_tools()
    {
        var tools = McpCommand.Tools(new ServiceCollection().BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>());
        Assert.Equal(
            ["get_market_overview", "get_relations", "get_research_job", "get_stock_snapshot", "list_pending_research", "queue_research", "submit_insight"],
            tools.Select(t => t.ProtocolTool.Name).Order());
    }
}
