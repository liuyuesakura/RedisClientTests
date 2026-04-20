using FreeRedis;
using StackExchange.Redis;

public static class RedisReachableNodesReport
{
    /// <summary>仅直连模式拓扑探测（与 CsredisTests 一致；CLUSTER NODES 通过 StackExchange 辅助读取）。</summary>
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
            using var redis = FreeRedisClusterFactory.Create(
                $"{connectionString.TrimEnd(',')},connectTimeout=3000,syncTimeout=3000");
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
        catch (StackExchange.Redis.RedisServerException)
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
}
