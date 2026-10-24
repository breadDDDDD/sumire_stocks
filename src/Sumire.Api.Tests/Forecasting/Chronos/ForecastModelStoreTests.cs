using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sumire.Api.Forecasting;

namespace Sumire.Api.Tests.Forecasting.Chronos;

public sealed class ForecastModelStoreTests : IDisposable
{
    readonly string _cache = Path.Combine(Path.GetTempPath(), "sumire-model-tests-" + Guid.NewGuid().ToString("N"));
    static readonly byte[] Payload = Encoding.UTF8.GetBytes("fake onnx bytes");
    static readonly string PayloadSha = Convert.ToHexStringLower(SHA256.HashData(Payload));

    public void Dispose()
    {
        if (Directory.Exists(_cache)) Directory.Delete(_cache, recursive: true);
    }

    sealed class Handler(byte[] body) : HttpMessageHandler
    {
        public int Requests;
        public string? Auth;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Auth = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    ForecastModelOptions Options(string sha, string revision = ForecastModelOptions.DefaultRevision, string? token = null) =>
        new("owner/model", revision, "model.onnx", sha, _cache, token);

    [Fact]
    public async Task Downloads_verifies_and_caches()
    {
        var handler = new Handler(Payload);
        var store = new ForecastModelStore(Options(PayloadSha, token: "hf_x"), new HttpClient(handler), NullLogger.Instance);

        var path = await store.EnsureAsync();
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.Contains(Path.Combine("owner--model", ForecastModelOptions.DefaultRevision), path);
        Assert.Equal("Bearer hf_x", handler.Auth);

        await store.EnsureAsync();
        Assert.Equal(1, handler.Requests); // second call served from the cache
    }

    [Fact]
    public async Task Refuses_a_download_with_the_wrong_checksum()
    {
        var options = Options(new string('0', 64));
        var store = new ForecastModelStore(options, new HttpClient(new Handler(Payload)), NullLogger.Instance);

        var ex = await Assert.ThrowsAsync<ForecastModelException>(() => store.EnsureAsync());
        Assert.Contains("checksum mismatch", ex.Message);
        Assert.False(File.Exists(options.LocalPath));
        Assert.False(File.Exists(options.LocalPath + ".part"));
    }

    [Fact]
    public async Task Refuses_and_deletes_a_tampered_cached_file()
    {
        var options = Options(PayloadSha);
        Directory.CreateDirectory(Path.GetDirectoryName(options.LocalPath)!);
        await File.WriteAllTextAsync(options.LocalPath, "tampered");
        var handler = new Handler(Payload);
        var store = new ForecastModelStore(options, new HttpClient(handler), NullLogger.Instance);

        await Assert.ThrowsAsync<ForecastModelException>(() => store.EnsureAsync());
        Assert.False(File.Exists(options.LocalPath));
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("84282421")]
    public async Task Refuses_unpinned_revisions(string revision)
    {
        var store = new ForecastModelStore(Options(PayloadSha, revision), new HttpClient(new Handler(Payload)), NullLogger.Instance);
        await Assert.ThrowsAsync<ForecastModelException>(() => store.EnsureAsync());
    }

    [Fact]
    public void Defaults_point_at_the_pinned_chronos2_export()
    {
        var o = new ForecastModelOptions(ForecastModelOptions.DefaultRepo, ForecastModelOptions.DefaultRevision, ForecastModelOptions.DefaultFile,
            ForecastModelOptions.DefaultSha256, _cache, null);
        o.Validate();
        Assert.Equal("https://huggingface.co/sktime/chronos2-onnx/resolve/84282421a54142dc689c0a71bc9876285b60bdae/chronos2-ctx2048-h64.onnx", o.Url);
    }
}
