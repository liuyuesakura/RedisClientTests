using CSRedis;
using StackExchange.Redis;

public static class RedisReachableNodesReport
{
    /// <summary>仅直连模式拓扑探测（哨兵分支已注释，见文件末尾 #if false）。</summary>
    public static void Print(string connectionString)
    {
        Console.WriteLine("[RedisTopology] Reachable nodes (before tests):");
        PrintDirectMode(connectionString);
        Console.Out.Flush();
    }

    private static void PrintDirectMode(string connectionString)
    {
        try
        {
            using var redis = new CSRedisClient($"{connectionString.TrimEnd(',')},connectTimeout=3000,syncTimeout=3000");
            var pong = redis.Ping();
            var raw = TryClusterNodesRawFromConnectionString(connectionString);
            var slots = raw is null
                ? "n/a (non-cluster)"
                : SummarizeMasterSlotsFromClusterNodes(raw);
            Console.WriteLine($"  OK   redis (direct)  ping={pong}  slots={slots}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL redis (direct)  ({ex.Message})");
        }
    }

    private static string? TryClusterNodesRawFromConnectionString(string connectionString)
    {
        var first = connectionString.Split(',')[0].Trim();
        var colon = first.LastIndexOf(':');
        if (colon <= 0 || colon == first.Length - 1)
        {
            return null;
        }

        var host = first[..colon];
        if (!int.TryParse(first[(colon + 1)..], out var port))
        {
            return null;
        }

        return TryClusterNodesRaw(host, port);
    }

    private static string? TryClusterNodesRaw(string host, int port)
    {
        try
        {
            var opts = ConfigurationOptions.Parse($"{host}:{port},connectTimeout=3000,syncTimeout=3000,abortConnect=false");
            using var mux = ConnectionMultiplexer.Connect(opts);
            var server = mux.GetServer(host, port);
            var raw = server.Execute("CLUSTER", "NODES");
            return raw.IsNull ? null : raw.ToString();
        }
        catch (RedisServerException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string SummarizeMasterSlotsFromClusterNodes(string clusterNodes)
    {
        var parts = new List<string>();
        foreach (var line in clusterNodes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.Contains("master", StringComparison.Ordinal) || !trimmed.Contains("connected", StringComparison.Ordinal))
            {
                continue;
            }

            var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3)
            {
                continue;
            }

            var addr = tokens[1].Split('@')[0];
            var idx = trimmed.IndexOf(" connected ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var ranges = trimmed[(idx + " connected ".Length)..].Trim();
            parts.Add($"{addr}={ranges}");
        }

        return parts.Count == 0 ? "(no master slot line)" : string.Join("; ", parts);
    }

#if false
    // --- 以下为原「哨兵拓扑」实现，保留供恢复；当前入口仅使用 Print(string) 直连。 ---
    /*
    public static void Print(bool sentinelEnabled, string? connectionString, string[] sentinelEndpoints)
    {
        Console.WriteLine("[RedisTopology] Reachable nodes (before tests):");
        if (!sentinelEnabled)
        {
            PrintDirectMode(connectionString!);
            Console.Out.Flush();
            return;
        }

        foreach (var ep in sentinelEndpoints)
        {
            ProbeSentinelEndpoint(ep);
        }

        var first = sentinelEndpoints.FirstOrDefault();
        if (first is null)
        {
            Console.WriteLine("  (no SentinelEndpoints configured)");
            Console.Out.Flush();
            return;
        }

        try
        {
            var (host, port) = ParseEndpoint(first);
            using var sentinel = new RedisSentinelClient(host, port);
            if (!sentinel.Connect(5000))
            {
                Console.WriteLine("  [RedisTopology] Sentinel connect failed for topology query.");
                Console.Out.Flush();
                return;
            }

            var masters = sentinel.Masters();
            foreach (var m in masters)
            {
                ProbeRedisDataNode(m.Ip, m.Port, $"master:{m.Name}");

                var slaves = sentinel.Slaves(m.Name);
                foreach (var s in slaves)
                {
                    ProbeRedisDataNode(s.Ip, s.Port, $"replica:{m.Name} ({s.Flags})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [RedisTopology] Sentinel query failed: {ex.Message}");
        }

        Console.Out.Flush();
    }

    private static void ProbeSentinelEndpoint(string hostAndPort)
    {
        try
        {
            var (host, port) = ParseEndpoint(hostAndPort);
            using var sentinel = new RedisSentinelClient(host, port);
            if (!sentinel.Connect(3000))
            {
                Console.WriteLine($"  FAIL sentinel://{hostAndPort}  (connect timeout)");
                return;
            }

            var pong = sentinel.Ping();
            Console.WriteLine($"  OK   sentinel://{hostAndPort}  ping={pong}  slots=n/a (sentinel)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL sentinel://{hostAndPort}  ({ex.Message})");
        }
    }

    private static void ProbeRedisDataNode(string host, int port, string label)
    {
        var ep = $"{host}:{port}";
        try
        {
            using var redis = new CSRedisClient($"{ep},connectTimeout=3000,syncTimeout=3000,abortConnect=false");
            var pong = redis.Ping();
            var raw = TryClusterNodesRaw(host, port);
            var slots = raw is null
                ? "n/a (non-cluster)"
                : ParseSlotsForNodeLine(raw, $"{host}:{port}");
            Console.WriteLine($"  OK   redis://{ep}  {label}  ping={pong}  slots={slots}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL redis://{ep}  {label}  ({ex.Message})");
        }
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        var parts = endpoint.Split(':');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid endpoint: {endpoint}", nameof(endpoint));
        }

        return (parts[0], int.Parse(parts[1]));
    }
    */
#endif
}
