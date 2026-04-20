using StackExchange.Redis;

public static class RedisReachableNodesReport
{
    /// <returns>是否至少探测到一个可达的 Sentinel 或数据节点（用于决定是否立刻跑功能测试）。</returns>
    public static async Task<bool> PrintAsync(
        bool sentinelEnabled,
        string? connectionString,
        string[] sentinelEndpoints)
    {
        Console.WriteLine("[RedisTopology] Reachable nodes (before tests):");
        if (!sentinelEnabled)
        {
            var ok = await PrintDirectModeAsync(connectionString!);
            Console.Out.Flush();
            return ok;
        }

        var anyOk = false;
        foreach (var ep in sentinelEndpoints)
        {
            anyOk |= await ProbeSentinelEndpointAsync(ep);
        }

        var firstSentinel = sentinelEndpoints.FirstOrDefault();
        if (firstSentinel is null)
        {
            Console.WriteLine("  (no SentinelEndpoints configured)");
            Console.Out.Flush();
            return false;
        }

        try
        {
            var opts = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectTimeout = 3000,
                SyncTimeout = 3000,
                TieBreaker = string.Empty,
                CommandMap = CommandMap.Sentinel
            };
            opts.EndPoints.Add(firstSentinel);
            await using var mux = await ConnectionMultiplexer.ConnectAsync(opts);
            var server = mux.GetServer(mux.GetEndPoints().First());

            var mastersResult = await server.ExecuteAsync("SENTINEL", "MASTERS");
            foreach (var row in ParseSentinelRows(mastersResult))
            {
                if (!row.TryGetValue("ip", out var ip) || !row.TryGetValue("port", out var portStr) ||
                    !row.TryGetValue("name", out var masterName))
                {
                    continue;
                }

                if (!int.TryParse(portStr, out var port))
                {
                    continue;
                }

                anyOk |= await ProbeRedisDataNodeAsync(ip, port, $"master:{masterName}");

                var replicasResult = await server.ExecuteAsync("SENTINEL", "REPLICAS", masterName);
                foreach (var replicaRow in ParseSentinelRows(replicasResult))
                {
                    if (!replicaRow.TryGetValue("ip", out var rip) || !replicaRow.TryGetValue("port", out var rportStr))
                    {
                        continue;
                    }

                    if (!int.TryParse(rportStr, out var rport))
                    {
                        continue;
                    }

                    var flags = replicaRow.TryGetValue("flags", out var fl) ? fl : "?";
                    anyOk |= await ProbeRedisDataNodeAsync(rip, rport, $"replica:{masterName} ({flags})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [RedisTopology] Sentinel query failed: {ex.Message}");
        }

        Console.Out.Flush();
        return anyOk;
    }

    private static async Task<bool> PrintDirectModeAsync(string connectionString)
    {
        var opts = ConfigurationOptions.Parse(connectionString);
        opts.AbortOnConnectFail = false;
        opts.ConnectTimeout = 3000;
        opts.SyncTimeout = 3000;
        var anyOk = false;
        try
        {
            await using var mux = await ConnectionMultiplexer.ConnectAsync(opts);
            foreach (var ep in mux.GetEndPoints())
            {
                try
                {
                    var server = mux.GetServer(ep);
                    var ping = await server.PingAsync();
                    anyOk = true;
                    var slots = await FormatClusterSlotsSummaryAsync(server, server.EndPoint.ToString()!);
                    Console.WriteLine(
                        $"  OK   redis://{ep}  ping={ping.TotalMilliseconds:F1}ms  slots={slots}  (standalone/cluster endpoint)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  FAIL redis://{ep}  ({ex.Message})");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [RedisTopology] Connect failed: {ex.Message}");
            return false;
        }

        return anyOk;
    }

    private static async Task<bool> ProbeSentinelEndpointAsync(string hostAndPort)
    {
        try
        {
            var opts = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectTimeout = 3000,
                SyncTimeout = 3000,
                TieBreaker = string.Empty,
                CommandMap = CommandMap.Sentinel
            };
            opts.EndPoints.Add(hostAndPort);
            await using var mux = await ConnectionMultiplexer.ConnectAsync(opts);
            var server = mux.GetServer(mux.GetEndPoints().First());
            var ping = await server.PingAsync();
            Console.WriteLine($"  OK   sentinel://{hostAndPort}  ping={ping.TotalMilliseconds:F1}ms  slots=n/a (sentinel)");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL sentinel://{hostAndPort}  ({ex.Message})");
            return false;
        }
    }

    private static async Task<bool> ProbeRedisDataNodeAsync(string host, int port, string label)
    {
        var ep = $"{host}:{port}";
        try
        {
            var opts = ConfigurationOptions.Parse($"{ep},connectTimeout=3000,syncTimeout=3000,abortConnect=false");
            await using var mux = await ConnectionMultiplexer.ConnectAsync(opts);
            var db = mux.GetDatabase();
            var ping = await db.PingAsync();
            var server = mux.GetServer(host, port);
            var slots = await FormatClusterSlotsSummaryAsync(server, $"{host}:{port}");
            Console.WriteLine($"  OK   redis://{ep}  {label}  ping={ping.TotalMilliseconds:F1}ms  slots={slots}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL redis://{ep}  {label}  ({ex.Message})");
            return false;
        }
    }

    /// <summary>解析 CLUSTER NODES 中与当前 host:port 匹配的一行，输出槽位摘要（仅 Redis Cluster 模式有槽；主从复制无 cluster 时返回 n/a）。</summary>
    private static async Task<string> FormatClusterSlotsSummaryAsync(IServer server, string hostPortForMatch)
    {
        try
        {
            var raw = await server.ExecuteAsync("CLUSTER", "NODES");
            if (raw.IsNull)
            {
                return "n/a";
            }

            var text = raw.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "n/a";
            }

            return ParseSlotsForNodeLine(text, hostPortForMatch);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("cluster", StringComparison.OrdinalIgnoreCase))
        {
            return "n/a (non-cluster)";
        }
        catch (Exception ex)
        {
            return $"n/a ({ex.Message})";
        }
    }

    /// <summary>匹配 CLUSTER NODES 中 address 为 host:port 的行，提取 master 的槽区间或 replica 标记。</summary>
    private static string ParseSlotsForNodeLine(string clusterNodes, string hostPortForMatch)
    {
        var needle = hostPortForMatch.Trim();
        foreach (var line in clusterNodes.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            var addrPort = parts[1].Split('@')[0];
            if (!string.Equals(addrPort, needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var flags = parts[2];
            if (flags.Contains("slave", StringComparison.OrdinalIgnoreCase) ||
                flags.Contains("replica", StringComparison.OrdinalIgnoreCase))
            {
                return "(replica, slots on primary)";
            }

            if (flags.Contains("master", StringComparison.OrdinalIgnoreCase))
            {
                var idx = trimmed.IndexOf(" connected ", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    return trimmed[(idx + " connected ".Length)..].Trim();
                }

                return "(master, no slot line)";
            }

            return flags;
        }

        return "(not listed in CLUSTER NODES)";
    }

    private static IEnumerable<Dictionary<string, string>> ParseSentinelRows(RedisResult result)
    {
        if (result.IsNull || result.Resp2Type != ResultType.Array)
        {
            yield break;
        }

        var outer = (RedisResult[])result!;
        foreach (var row in outer)
        {
            if (row.IsNull || row.Resp2Type != ResultType.Array)
            {
                continue;
            }

            var inner = (RedisResult[])row!;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < inner.Length - 1; i += 2)
            {
                dict[inner[i].ToString()!] = inner[i + 1].ToString()!;
            }

            yield return dict;
        }
    }
}
