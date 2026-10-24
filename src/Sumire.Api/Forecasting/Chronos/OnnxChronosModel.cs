using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Sumire.Api.Forecasting.Chronos;

/// <summary>
/// ONNX Runtime session for the Chronos-2 export. Only the <c>forecast</c> verb creates one, so the web server
/// never loads ONNX Runtime or the model.
/// </summary>
public sealed class OnnxChronosModel : IChronosModel, IDisposable
{
    readonly InferenceSession _session;

    public OnnxChronosModel(string path, int threads = 0)
    {
        var options = new Microsoft.ML.OnnxRuntime.SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        if (threads > 0) options.IntraOpNumThreads = threads;
        _session = new InferenceSession(path, options);
    }

    public string Describe() =>
        string.Join("; ", _session.InputMetadata.Select(i => $"in {i.Key} {i.Value.ElementDataType} [{string.Join(",", i.Value.Dimensions)}]")
            .Concat(_session.OutputMetadata.Select(o => $"out {o.Key} {o.Value.ElementDataType} [{string.Join(",", o.Value.Dimensions)}]")));

    public float[] Predict(float[] context, long[] groupIds, int rows)
    {
        var inputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("context", new DenseTensor<float>(context, [rows, ChronosForecaster.ContextLength])),
            NamedOnnxValue.CreateFromTensor("group_ids", new DenseTensor<long>(groupIds, [rows])),
        };
        using var results = _session.Run(inputs);
        return results.First(r => r.Name == "quantiles").AsTensor<float>().ToArray();
    }

    public void Dispose() => _session.Dispose();
}
