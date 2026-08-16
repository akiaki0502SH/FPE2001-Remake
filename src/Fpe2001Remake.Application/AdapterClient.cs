using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.Application;

/// <summary>
/// 适配器 IPC 客户端（规格 §12）：Named Pipe 连接 AdapterHost，JSON 行帧。
/// correlationId 匹配请求/响应；超时与取消。
/// </summary>
public sealed class AdapterClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _gate = new();
    private NamedPipeClientStream? _pipe;
    private readonly MemoryStream _receiveBuffer = new();
    private bool _disposed;

    public AdapterClient(string pipeName = "FPE2001-Remake-Adapter")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        PipeName = pipeName;
    }

    public string PipeName { get; }

    public bool IsConnected => _pipe?.IsConnected == true;

    /// <summary>连接宿主（已运行则直接连；可等待重试）。</summary>
    public async ValueTask<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (_pipe?.IsConnected == true)
        {
            return true;
        }

        var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeout, ct);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync();
            return false;
        }

        lock (_gate)
        {
            _pipe = pipe;
            _receiveBuffer.SetLength(0);
        }
        return true;
    }

    public async ValueTask<AdapterMessage> InvokeAsync(AdapterMessage request, CancellationToken ct)
    {
        if (_pipe?.IsConnected != true)
        {
            throw new InvalidOperationException("适配器未连接。");
        }

        var frame = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions) + "\n");
        await _pipe.WriteAsync(frame.AsMemory(0, frame.Length), ct);

        // 读取直到匹配 correlationId（容忍乱序）
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await ReadLineAsync(ct);
            if (line is null)
            {
                throw new IOException("适配器连接已关闭。");
            }
            var response = JsonSerializer.Deserialize<AdapterMessage>(line, JsonOptions);
            if (response is not null && response.CorrelationId == request.CorrelationId)
            {
                return response;
            }
        }
    }

    /// <summary>原始字节行读取（\n 分隔，跨调用保留缓冲）。</summary>
    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            var bytes = _receiveBuffer.GetBuffer();
            for (var i = 0; i < (int)_receiveBuffer.Length; i++)
            {
                if (bytes[i] == (byte)'\n')
                {
                    var line = Encoding.UTF8.GetString(bytes, 0, i).TrimEnd('\r');
                    var remaining = (int)_receiveBuffer.Length - i - 1;
                    if (remaining > 0)
                    {
                        Buffer.BlockCopy(bytes, i + 1, bytes, 0, remaining);
                    }
                    _receiveBuffer.SetLength(remaining);
                    return line;
                }
            }

            var buffer = new byte[64 * 1024];
            var read = await _pipe!.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                return null;
            }
            _receiveBuffer.Write(buffer, 0, read);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _pipe?.Dispose();
            _pipe = null;
            _receiveBuffer.Dispose();
        }
        await Task.CompletedTask;
    }
}

/// <summary>速度控制能力客户端（通过 IPC 调用宿主适配器）。</summary>
public sealed class SpeedControlClient : ISpeedControlCapability
{
    private readonly AdapterClient _client;
    private readonly SpeedRange _range = new(0.25, 4.0, 0.25, [0.25, 0.5, 1.0, 2.0, 4.0]);

    public SpeedControlClient(AdapterClient client)
    {
        _client = client;
    }

    public SpeedRange Range => _range;

    public async ValueTask<SpeedState> GetAsync(CancellationToken ct)
    {
        var response = await _client.InvokeAsync(Request("speed.get", null), ct);
        var payload = ParsePayload(response);
        if (payload is JsonElement element &&
            element.TryGetProperty("enabled", out var enabled) &&
            element.TryGetProperty("multiplier", out var multiplier))
        {
            return new SpeedState(enabled.GetBoolean(), multiplier.GetDouble());
        }
        throw new SpeedUnsupportedException("适配器未返回速度状态。");
    }

    public async ValueTask<SpeedState> SetEnabledAsync(bool enabled, CancellationToken ct)
    {
        var response = await _client.InvokeAsync(Request("speed.setEnabled", $"{{ \"enabled\": {enabled.ToString().ToLowerInvariant()} }}"), ct);
        return await GetAsync(ct);
    }

    public async ValueTask<SpeedState> SetMultiplierAsync(double multiplier, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { multiplier });
        var response = await _client.InvokeAsync(Request("speed.setMultiplier", json), ct);
        return await GetAsync(ct);
    }

    private static AdapterMessage Request(string method, string? payload)
        => new(
            AdapterProtocol.ProtocolVersion,
            "req-" + Guid.NewGuid().ToString("N")[..8],
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow.AddSeconds(10),
            Guid.NewGuid().ToString("N")[..16],
            method,
            payload);

    private static JsonElement? ParsePayload(AdapterMessage response)
    {
        if (response.Method == "error" || string.IsNullOrEmpty(response.PayloadJson))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(response.PayloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>适配器能力查询结果。</summary>
public sealed record AdapterHelloInfo(
    string AdapterId,
    string EmulatorName,
    string VersionToken,
    AdapterCapabilities Capabilities,
    int ProtocolVersion);
