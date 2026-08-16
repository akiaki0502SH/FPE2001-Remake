using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Fpe2001Remake.Contracts;

namespace Fpe2001Remake.AdapterHost;

/// <summary>
/// 适配器宿主（规格 §11/§12）：独立进程，Named Pipe IPC，一实例一适配器。
/// 崩溃不拖垮 UI；协议帧 AdapterMessage（JSON 行帧）。
/// </summary>
public static class Program
{
    private const string DefaultPipeName = "FPE2001-Remake-Adapter";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<int> Main(string[] args)
    {
        var demo = args.Contains("-demo", StringComparer.OrdinalIgnoreCase);
        var pipeName = GetOption(args, "--pipe") ?? DefaultPipeName;
        IEmulatorAdapter adapter = demo ? new DemoSpeedAdapter() : new GenericAdapter();
        Console.WriteLine($"AdapterHost started: {adapter.AdapterId} ({adapter.EmulatorName}) caps={adapter.Capabilities}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        while (!cts.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cts.Token);
                Console.WriteLine("client connected");
                await ServeAsync(pipe, adapter, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"connection error: {ex.Message}");
            }
        }
        return 0;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        return null;
    }

    private static async Task ServeAsync(NamedPipeServerStream pipe, IEmulatorAdapter adapter, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var pending = new MemoryStream();
        var pendingBytes = new byte[AdapterProtocol.MaxFrameBytes];

        while (!ct.IsCancellationRequested)
        {
            var read = await pipe.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                Console.WriteLine("client closed");
                break;
            }
            pending.Write(buffer, 0, read);

            // 提取完整行（\n 分隔）
            var start = 0;
            while (true)
            {
                var newline = -1;
                for (var i = start; i < (int)pending.Length; i++)
                {
                    if (pending.GetBuffer()[i] == (byte)'\n')
                    {
                        newline = i;
                        break;
                    }
                }
                if (newline < 0)
                {
                    break;
                }
                var lineLen = newline - start;
                var line = System.Text.Encoding.UTF8.GetString(pending.GetBuffer(), start, lineLen).TrimEnd('\r');
                start = newline + 1;
                Console.WriteLine($"got: {line[..Math.Min(100, line.Length)]}");

                AdapterMessage? request = null;
                try
                {
                    request = System.Text.Json.JsonSerializer.Deserialize<AdapterMessage>(line, JsonOptions);
                }
                catch (System.Text.Json.JsonException)
                {
                    Console.WriteLine("bad json frame");
                }
                if (request is not null)
                {
                    var response = await DispatchAsync(adapter, request, ct);
                    var frame = System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(response, JsonOptions) + "\n");
                    await pipe.WriteAsync(frame.AsMemory(0, frame.Length), ct);
                    Console.WriteLine($"sent: {response.Method}");
                }
            }

            // 保留未消费部分
            if (start > 0)
            {
                var remaining = (int)pending.Length - start;
                if (remaining > 0)
                {
                    Buffer.BlockCopy(pending.GetBuffer(), start, pendingBytes, 0, remaining);
                    pending.SetLength(0);
                    pending.Write(pendingBytes, 0, remaining);
                }
                else
                {
                    pending.SetLength(0);
                }
            }
            else if ((int)pending.Length > AdapterProtocol.MaxFrameBytes)
            {
                Console.WriteLine("frame too large");
                break;
            }
        }
    }

    private static async ValueTask<AdapterMessage> DispatchAsync(IEmulatorAdapter adapter, AdapterMessage request, CancellationToken ct)
    {
        var method = request.Method;
        var payload = request.PayloadJson;

        if (method == "hello")
        {
            var hello = JsonSerializer.Serialize(new
            {
                adapterId = adapter.AdapterId,
                emulatorName = adapter.EmulatorName,
                versionToken = adapter.VersionToken,
                capabilities = (int)adapter.Capabilities,
                protocolVersion = AdapterProtocol.ProtocolVersion,
            });
            return Response(request, "hello", hello);
        }

        // 速度能力方法
        if (method == "speed.get" || method == "speed.setEnabled" || method == "speed.setMultiplier")
        {
            try
            {
                var responsePayload = await adapter.InvokeAsync(request, ct);
                return responsePayload;
            }
            catch (SpeedUnsupportedException)
            {
                return Response(request, "error", JsonSerializer.Serialize(new { error = "SpeedUnsupported" }));
            }
            catch (Exception ex)
            {
                return Response(request, "error", JsonSerializer.Serialize(new { error = ex.Message }));
            }
        }

        return Response(request, "error", JsonSerializer.Serialize(new { error = $"unknown method: {method}" }));
    }

    private static AdapterMessage Response(AdapterMessage request, string method, string payloadJson)
        => new(
            AdapterProtocol.ProtocolVersion,
            "resp-" + Guid.NewGuid().ToString("N")[..8],
            request.CorrelationId,
            DateTimeOffset.UtcNow.AddSeconds(10),
            Guid.NewGuid().ToString("N")[..16],
            method,
            payloadJson);
}

/// <summary>通用适配器（无速度能力；仅协议演示）。</summary>
public sealed class GenericAdapter : IEmulatorAdapter
{
    public string AdapterId => "generic-win32";

    public string EmulatorName => "Generic Win32";

    public string VersionToken => "generic-1.0";

    public AdapterCapabilities Capabilities => AdapterCapabilities.None;

    public ValueTask<AdapterMessage> InvokeAsync(AdapterMessage request, CancellationToken ct)
        => throw new SpeedUnsupportedException("通用目标无速度控制能力。");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>演示速度适配器（-demo；模拟 SpeedControl，供 UI/测试验证）。</summary>
public sealed class DemoSpeedAdapter : IEmulatorAdapter
{
    private readonly object _gate = new();
    private bool _enabled;
    private double _multiplier = 1.0;

    public string AdapterId => "demo-speed";

    public string EmulatorName => "Demo Emulator (模拟)";

    public string VersionToken => "demo-1.0";

    public AdapterCapabilities Capabilities =>
        AdapterCapabilities.SpeedControl | AdapterCapabilities.PauseResume;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public ValueTask<AdapterMessage> InvokeAsync(AdapterMessage request, CancellationToken ct)
    {
        return request.Method switch
        {
            "speed.get" => SpeedGet(request),
            "speed.setEnabled" => SpeedSetEnabled(request),
            "speed.setMultiplier" => SpeedSetMultiplier(request),
            _ => ValueTask.FromResult(Error(request, $"unknown: {request.Method}")),
        };
    }

    private ValueTask<AdapterMessage> SpeedGet(AdapterMessage request)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(Ok(request, new { enabled = _enabled, multiplier = _multiplier,
                range = new { min = 0.25, max = 4.0, step = 0.25, recommended = new[] { 0.25, 0.5, 1.0, 2.0, 4.0 } } }));
        }
    }

    private ValueTask<AdapterMessage> SpeedSetEnabled(AdapterMessage request)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(request.PayloadJson ?? "{}");
        var enabled = payload.GetProperty("enabled").GetBoolean();
        lock (_gate)
        {
            _enabled = enabled;
            return ValueTask.FromResult(Ok(request, new { enabled = _enabled, multiplier = _multiplier }));
        }
    }

    private ValueTask<AdapterMessage> SpeedSetMultiplier(AdapterMessage request)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(request.PayloadJson ?? "{}");
        var multiplier = payload.GetProperty("multiplier").GetDouble();
        lock (_gate)
        {
            _multiplier = Math.Clamp(multiplier, 0.25, 4.0);
            return ValueTask.FromResult(Ok(request, new { enabled = _enabled, multiplier = _multiplier }));
        }
    }

    private static AdapterMessage Ok(AdapterMessage request, object payload)
        => new(AdapterProtocol.ProtocolVersion, "resp", request.CorrelationId, DateTimeOffset.UtcNow.AddSeconds(10), "nonce", request.Method, JsonSerializer.Serialize(payload));

    private static AdapterMessage Error(AdapterMessage request, string message)
        => new(AdapterProtocol.ProtocolVersion, "resp", request.CorrelationId, DateTimeOffset.UtcNow.AddSeconds(10), "nonce", "error", JsonSerializer.Serialize(new { error = message }));
}
