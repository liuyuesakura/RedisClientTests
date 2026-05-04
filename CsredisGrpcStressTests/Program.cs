using System.Diagnostics;
using CsredisTests.Grpc;
using Grpc.Net.Client;

var options = ParseArgs(args);
Console.WriteLine($"[GrpcStress] target={options.Target}, plaintext={options.Plaintext}, total={options.TotalRequests}, concurrency={options.Concurrency}");

var handler = new SocketsHttpHandler
{
    EnableMultipleHttp2Connections = true
};
using var channel = GrpcChannel.ForAddress(options.Target, new GrpcChannelOptions
{
    HttpHandler = handler
});
var client = new RedisGetTest.RedisGetTestClient(channel);

var globalStopwatch = Stopwatch.StartNew();
var latencyMs = new List<double>(options.TotalRequests);
var successCount = 0;
var failCount = 0;

var requestCounter = 0;
var workers = Enumerable.Range(0, options.Concurrency)
    .Select(workerId => Task.Run(async () =>
    {
        while (true)
        {
            var idx = Interlocked.Increment(ref requestCounter);
            if (idx > options.TotalRequests)
            {
                break;
            }

            var key = $"{options.KeyPrefix}:{idx}";
            var sw = Stopwatch.StartNew();
            try
            {
                var reply = await client.TriggerGetAsync(new TriggerGetRequest { Key = key });
                sw.Stop();
                lock (latencyMs)
                {
                    latencyMs.Add(sw.Elapsed.TotalMilliseconds);
                }

                if (reply.Found || !string.IsNullOrEmpty(reply.Message))
                {
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    Interlocked.Increment(ref failCount);
                }
            }
            catch
            {
                sw.Stop();
                lock (latencyMs)
                {
                    latencyMs.Add(sw.Elapsed.TotalMilliseconds);
                }

                Interlocked.Increment(ref failCount);
            }
        }
    }))
    .ToArray();

await Task.WhenAll(workers);
globalStopwatch.Stop();

latencyMs.Sort();
var elapsedSeconds = Math.Max(globalStopwatch.Elapsed.TotalSeconds, 0.0001);
var qps = options.TotalRequests / elapsedSeconds;

Console.WriteLine("[GrpcStress] Completed");
Console.WriteLine($"[GrpcStress] Success={successCount}, Failed={failCount}, Duration={globalStopwatch.Elapsed.TotalMilliseconds:F0}ms, QPS={qps:F2}");
Console.WriteLine($"[GrpcStress] Latency(ms): p50={Percentile(latencyMs, 0.50):F2}, p95={Percentile(latencyMs, 0.95):F2}, p99={Percentile(latencyMs, 0.99):F2}, max={latencyMs[^1]:F2}");

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

static Options ParseArgs(string[] args)
{
    var target = "http://127.0.0.1:50051";
    var total = 10000;
    var concurrency = 100;
    var keyPrefix = "grpc:stress:test";
    var plaintext = true;

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

    if (total <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(total), "total must be > 0");
    }

    if (concurrency <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(concurrency), "concurrency must be > 0");
    }

    if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
        target = $"{(plaintext ? "http" : "https")}://{target}";
    }

    return new Options(target, total, concurrency, keyPrefix, plaintext);
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
    Console.WriteLine("  --target <url>        gRPC target, default: http://127.0.0.1:50051");
    Console.WriteLine("  --total <n>           total requests, default: 10000");
    Console.WriteLine("  --concurrency <n>     concurrent workers, default: 100");
    Console.WriteLine("  --key-prefix <value>  redis key prefix, default: grpc:stress:test");
    Console.WriteLine("  --https               force https scheme when target has no scheme");
}

internal sealed record Options(
    string Target,
    int TotalRequests,
    int Concurrency,
    string KeyPrefix,
    bool Plaintext);
