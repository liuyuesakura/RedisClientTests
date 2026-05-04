using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Net.Client;
using CsGrpc = CsredisTests.Grpc;
using SeGrpc = StackExchangeTests.Grpc;

var options = ParseArgs(args);
Console.WriteLine(
    $"[GrpcStress] suite={options.Suite}, method={options.Method}, target={options.Target}, total={options.TotalRequests}, concurrency={options.Concurrency}");

var handler = new SocketsHttpHandler
{
    EnableMultipleHttp2Connections = true
};
using var channel = GrpcChannel.ForAddress(options.Target, new GrpcChannelOptions
{
    HttpHandler = handler
});

var csClient = options.Suite is "csredis" ? new CsGrpc.RedisGetTest.RedisGetTestClient(channel) : null;
var seClient = options.Suite is "stackexchange" ? new SeGrpc.RedisGetTest.RedisGetTestClient(channel) : null;
if (csClient is null && seClient is null)
{
    throw new InvalidOperationException("No grpc client available for suite.");
}

var overall = new StressStats(options.TotalRequests);
var methodStats = new ConcurrentDictionary<string, StressStats>(StringComparer.OrdinalIgnoreCase);

var globalStopwatch = Stopwatch.StartNew();
var requestCounter = 0;
var workers = Enumerable.Range(0, options.Concurrency)
    .Select(_ => Task.Run(async () =>
    {
        while (true)
        {
            var idx = Interlocked.Increment(ref requestCounter);
            if (idx > options.TotalRequests)
            {
                break;
            }

            var method = ResolveMethod(options.Method, idx);
            var sw = Stopwatch.StartNew();
            try
            {
                var ok = options.Suite switch
                {
                    "csredis" => await ExecuteCsredisAsync(csClient!, options, method, idx),
                    "stackexchange" => await ExecuteStackExchangeAsync(seClient!, options, method, idx),
                    _ => false
                };
                sw.Stop();

                overall.Add(sw.Elapsed.TotalMilliseconds, ok);
                methodStats.GetOrAdd(method, _ => new StressStats()).Add(sw.Elapsed.TotalMilliseconds, ok);
            }
            catch
            {
                sw.Stop();
                overall.Add(sw.Elapsed.TotalMilliseconds, false);
                methodStats.GetOrAdd(method, _ => new StressStats()).Add(sw.Elapsed.TotalMilliseconds, false);
            }
        }
    }))
    .ToArray();

await Task.WhenAll(workers);
globalStopwatch.Stop();

overall.SortLatencies();
var elapsedSeconds = Math.Max(globalStopwatch.Elapsed.TotalSeconds, 0.0001);
var qps = options.TotalRequests / elapsedSeconds;

Console.WriteLine("[GrpcStress] Completed");
Console.WriteLine(
    $"[GrpcStress] Overall Success={overall.SuccessCount}, Failed={overall.FailCount}, Duration={globalStopwatch.Elapsed.TotalMilliseconds:F0}ms, QPS={qps:F2}");
Console.WriteLine(
    $"[GrpcStress] Overall Latency(ms): p50={Percentile(overall.LatencyMs, 0.50):F2}, p95={Percentile(overall.LatencyMs, 0.95):F2}, p99={Percentile(overall.LatencyMs, 0.99):F2}, max={Max(overall.LatencyMs):F2}");

foreach (var item in methodStats.OrderBy(x => x.Key))
{
    item.Value.SortLatencies();
    Console.WriteLine(
        $"[GrpcStress:{item.Key}] Success={item.Value.SuccessCount}, Failed={item.Value.FailCount}, p95={Percentile(item.Value.LatencyMs, 0.95):F2}ms, p99={Percentile(item.Value.LatencyMs, 0.99):F2}ms");
}

static async Task<bool> ExecuteCsredisAsync(
    CsGrpc.RedisGetTest.RedisGetTestClient client,
    Options options,
    string method,
    int idx)
{
    switch (method)
    {
        case "get":
        {
            var key = $"{options.KeyPrefix}:{idx}";
            var reply = await client.TriggerGetAsync(new CsGrpc.TriggerGetRequest { Key = key });
            return !string.IsNullOrEmpty(reply.Message);
        }
        case "pipeline":
        {
            var reply = await client.TriggerPipelineAsync(new CsGrpc.TriggerPipelineRequest
            {
                KeyPrefix = $"{options.KeyPrefix}:pipeline:{idx}",
                KeyCount = options.PipelineKeyCount,
                ExpirySeconds = options.ExpirySeconds,
                SameHashtag = options.SameHashtag
            });
            return reply.Ok && reply.Entries.Count > 0;
        }
        case "cacheshell":
        {
            var reply = await client.TriggerCacheShellAsync(new CsGrpc.TriggerCacheShellRequest
            {
                KeyPrefix = $"{options.KeyPrefix}:cacheshell:{idx}",
                ExpirySeconds = options.ExpirySeconds,
                HashField = options.HashField
            });
            return reply.Ok;
        }
        case "slot":
        {
            var reply = await client.GetSlotInfosAsync(new CsGrpc.GetSlotInfosRequest
            {
                Keys =
                {
                    $"{options.KeyPrefix}:slot:{{u:{idx}}}:k1",
                    $"{options.KeyPrefix}:slot:{{u:{idx}}}:k2",
                    $"{options.KeyPrefix}:slot:{idx}:k3"
                }
            });
            return reply.Infos.Count >= 3;
        }
        case "node":
        {
            var reply = await client.GetNodeInfosAsync(new CsGrpc.GetNodeInfosRequest());
            return reply.Infos.Count > 0;
        }
        default:
            throw new ArgumentOutOfRangeException(nameof(method), method, "unsupported method");
    }
}

static async Task<bool> ExecuteStackExchangeAsync(
    SeGrpc.RedisGetTest.RedisGetTestClient client,
    Options options,
    string method,
    int idx)
{
    switch (method)
    {
        case "get":
        {
            var key = $"{options.KeyPrefix}:{idx}";
            var reply = await client.TriggerGetAsync(new SeGrpc.TriggerGetRequest { Key = key });
            return !string.IsNullOrEmpty(reply.Message);
        }
        case "pipeline":
        {
            var reply = await client.TriggerPipelineAsync(new SeGrpc.TriggerPipelineRequest
            {
                KeyPrefix = $"{options.KeyPrefix}:pipeline:{idx}",
                KeyCount = options.PipelineKeyCount,
                ExpirySeconds = options.ExpirySeconds,
                SameHashtag = options.SameHashtag
            });
            return reply.Ok && reply.Entries.Count > 0;
        }
        case "cacheshell":
        {
            var reply = await client.TriggerCacheShellAsync(new SeGrpc.TriggerCacheShellRequest
            {
                KeyPrefix = $"{options.KeyPrefix}:cacheshell:{idx}",
                ExpirySeconds = options.ExpirySeconds,
                HashField = options.HashField
            });
            return reply.Ok;
        }
        case "slot":
        {
            var reply = await client.GetSlotInfosAsync(new SeGrpc.GetSlotInfosRequest
            {
                Keys =
                {
                    $"{options.KeyPrefix}:slot:{{u:{idx}}}:k1",
                    $"{options.KeyPrefix}:slot:{{u:{idx}}}:k2",
                    $"{options.KeyPrefix}:slot:{idx}:k3"
                }
            });
            return reply.Infos.Count >= 3;
        }
        case "node":
        {
            var reply = await client.GetNodeInfosAsync(new SeGrpc.GetNodeInfosRequest());
            return reply.Infos.Count > 0;
        }
        default:
            throw new ArgumentOutOfRangeException(nameof(method), method, "unsupported method");
    }
}

static string ResolveMethod(string configured, int idx)
{
    if (!string.Equals(configured, "all", StringComparison.OrdinalIgnoreCase))
    {
        return configured;
    }

    return (idx % 5) switch
    {
        0 => "get",
        1 => "pipeline",
        2 => "cacheshell",
        3 => "slot",
        _ => "node"
    };
}

static double Percentile(List<double> sortedValues, double percentile)
{
    if (sortedValues.Count == 0)
    {
        return 0;
    }

    var index = (int)Math.Ceiling(sortedValues.Count * percentile) - 1;
    index = Math.Clamp(index, 0, sortedValues.Count - 1);
    return sortedValues[index];
}

static double Max(List<double> sortedValues) => sortedValues.Count == 0 ? 0 : sortedValues[^1];

static Options ParseArgs(string[] args)
{
    var target = "http://127.0.0.1:50051";
    var total = 10000;
    var concurrency = 100;
    var keyPrefix = "grpc:stress:test";
    var plaintext = true;
    var suite = "csredis";
    var method = "all";
    var pipelineKeyCount = 3;
    var expirySeconds = 300;
    var sameHashtag = true;
    var hashField = "field1";

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--target":
                target = GetArgValue(args, ref i, "--target");
                break;
            case "--total":
                total = int.Parse(GetArgValue(args, ref i, "--total"));
                break;
            case "--concurrency":
                concurrency = int.Parse(GetArgValue(args, ref i, "--concurrency"));
                break;
            case "--key-prefix":
                keyPrefix = GetArgValue(args, ref i, "--key-prefix");
                break;
            case "--suite":
                suite = GetArgValue(args, ref i, "--suite").ToLowerInvariant();
                break;
            case "--method":
                method = GetArgValue(args, ref i, "--method").ToLowerInvariant();
                break;
            case "--pipeline-key-count":
                pipelineKeyCount = int.Parse(GetArgValue(args, ref i, "--pipeline-key-count"));
                break;
            case "--expiry-seconds":
                expirySeconds = int.Parse(GetArgValue(args, ref i, "--expiry-seconds"));
                break;
            case "--hash-field":
                hashField = GetArgValue(args, ref i, "--hash-field");
                break;
            case "--multi-hashtag":
                sameHashtag = false;
                break;
            case "--https":
                plaintext = false;
                break;
            case "--help":
            case "-h":
                PrintUsage();
                Environment.Exit(0);
                break;
        }
    }

    if (total <= 0 || concurrency <= 0 || pipelineKeyCount <= 0 || expirySeconds <= 0)
    {
        throw new ArgumentOutOfRangeException("total/concurrency/pipeline-key-count/expiry-seconds must be > 0");
    }

    if (suite is not ("csredis" or "stackexchange"))
    {
        throw new ArgumentOutOfRangeException(nameof(suite), "suite must be csredis or stackexchange");
    }

    if (method is not ("get" or "pipeline" or "cacheshell" or "slot" or "node" or "all"))
    {
        throw new ArgumentOutOfRangeException(nameof(method), "method must be get/pipeline/cacheshell/slot/node/all");
    }

    if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
        target = $"{(plaintext ? "http" : "https")}://{target}";
    }

    return new Options(
        target,
        total,
        concurrency,
        keyPrefix,
        plaintext,
        suite,
        method,
        pipelineKeyCount,
        expirySeconds,
        sameHashtag,
        hashField);
}

static string GetArgValue(string[] args, ref int i, string name)
{
    if (i + 1 >= args.Length)
    {
        throw new ArgumentException($"{name} requires a value");
    }

    i++;
    return args[i];
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project CsredisGrpcStressTests -- [options]");
    Console.WriteLine("Options:");
    Console.WriteLine("  --suite <csredis|stackexchange>      proto suite, default: csredis");
    Console.WriteLine("  --target <url>                       gRPC target, default: http://127.0.0.1:50051");
    Console.WriteLine("  --method <get|pipeline|cacheshell|slot|node|all>  default: all");
    Console.WriteLine("  --total <n>                          total requests, default: 10000");
    Console.WriteLine("  --concurrency <n>                    concurrent workers, default: 100");
    Console.WriteLine("  --key-prefix <value>                 redis key prefix, default: grpc:stress:test");
    Console.WriteLine("  --pipeline-key-count <n>             default: 3");
    Console.WriteLine("  --expiry-seconds <n>                 default: 300");
    Console.WriteLine("  --hash-field <value>                 default: field1");
    Console.WriteLine("  --multi-hashtag                      pipeline uses multi hashtag mode");
    Console.WriteLine("  --https                              force https scheme when target has no scheme");
}

internal sealed class StressStats
{
    private int _successCount;
    private int _failCount;

    public StressStats(int capacity = 0)
    {
        LatencyMs = capacity > 0 ? new List<double>(capacity) : [];
    }

    public List<double> LatencyMs { get; }

    public int SuccessCount => _successCount;

    public int FailCount => _failCount;

    public void Add(double latencyMs, bool ok)
    {
        lock (LatencyMs)
        {
            LatencyMs.Add(latencyMs);
        }

        if (ok)
        {
            Interlocked.Increment(ref _successCount);
        }
        else
        {
            Interlocked.Increment(ref _failCount);
        }
    }

    public void SortLatencies()
    {
        lock (LatencyMs)
        {
            LatencyMs.Sort();
        }
    }
}

internal sealed record Options(
    string Target,
    int TotalRequests,
    int Concurrency,
    string KeyPrefix,
    bool Plaintext,
    string Suite,
    string Method,
    int PipelineKeyCount,
    int ExpirySeconds,
    bool SameHashtag,
    string HashField);
