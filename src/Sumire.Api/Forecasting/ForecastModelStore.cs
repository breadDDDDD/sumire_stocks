using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Sumire.Api.Forecasting;

/// <summary>Which ONNX file to use, pinned to a commit and a checksum. Read from FORECAST_MODEL_* env vars.</summary>
public sealed partial record ForecastModelOptions(string Repo, string Revision, string File, string Sha256, string CacheDir, string? HfToken)
{
    public const string DefaultRepo = "sktime/chronos2-onnx";
    public const string DefaultRevision = "84282421a54142dc689c0a71bc9876285b60bdae";
    public const string DefaultFile = "chronos2-ctx2048-h64.onnx";
    public const string DefaultSha256 = "f06aba77b0445061b78639008074d71f45af8c0edf16c9dbe4c9ea513c422c0e";

    public static ForecastModelOptions FromEnvironment() => new(
        Env("FORECAST_MODEL_REPO") ?? DefaultRepo,
        Env("FORECAST_MODEL_REVISION") ?? DefaultRevision,
        Env("FORECAST_MODEL_FILE") ?? DefaultFile,
        (Env("FORECAST_MODEL_SHA256") ?? DefaultSha256).ToLowerInvariant(),
        Env("FORECAST_MODEL_CACHE") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sumire", "models"),
        Env("HF_TOKEN"));

    /// <summary>Where the verified file lives: &lt;cache&gt;/&lt;owner&gt;--&lt;name&gt;/&lt;revision&gt;/&lt;file&gt;.</summary>
    public string LocalPath => Path.Combine(CacheDir, Repo.Replace("/", "--"), Revision, File);

    public string Url => $"https://huggingface.co/{Repo}/resolve/{Revision}/{Uri.EscapeDataString(File)}";

    /// <summary>Throws when the revision is not a full commit hash (e.g. "main") or the checksum is malformed.</summary>
    public void Validate()
    {
        if (!CommitRegex().IsMatch(Revision))
            throw new ForecastModelException($"FORECAST_MODEL_REVISION must be a 40-character commit hash, not '{Revision}'");
        if (!Sha256Regex().IsMatch(Sha256))
            throw new ForecastModelException("FORECAST_MODEL_SHA256 must be a 64-character hex SHA-256");
    }

    static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v.Trim() : null;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitRegex();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Regex();
}

public sealed class ForecastModelException(string message) : Exception(message);

/// <summary>
/// Downloads the pinned model once from the Hugging Face Hub, verifies its SHA-256 and caches it. A file whose
/// checksum does not match is deleted and refused.
/// </summary>
public sealed class ForecastModelStore(ForecastModelOptions options, HttpClient http, ILogger logger)
{
    public async Task<string> EnsureAsync(CancellationToken ct = default)
    {
        options.Validate();
        var path = options.LocalPath;
        if (File.Exists(path))
        {
            var actual = await HashFileAsync(path, ct);
            if (actual == options.Sha256) return path;
            File.Delete(path);
            throw new ForecastModelException($"Cached model checksum mismatch (got {actual[..12]}…, expected {options.Sha256[..12]}…); deleted it, run again to re-download");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = path + ".part";
        logger.LogInformation("Downloading {Repo}@{Revision}/{File}", options.Repo, options.Revision[..8], options.File);
        using var req = new HttpRequestMessage(HttpMethod.Get, options.Url);
        if (options.HfToken is { } token) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new ForecastModelException($"Model download failed ({(int)resp.StatusCode})");

        string hash;
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(part))
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[1 << 20];
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                sha.AppendData(buffer, 0, n);
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            }
            hash = Convert.ToHexStringLower(sha.GetHashAndReset());
        }

        if (hash != options.Sha256)
        {
            File.Delete(part);
            throw new ForecastModelException($"Downloaded model checksum mismatch (got {hash[..12]}…, expected {options.Sha256[..12]}…); refusing it");
        }
        File.Move(part, path, overwrite: true);
        logger.LogInformation("Model cached at {Path}", path);
        return path;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
    }
}
