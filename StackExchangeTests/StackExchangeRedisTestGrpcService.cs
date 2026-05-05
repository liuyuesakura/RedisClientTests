using System.Text;
using Grpc.Core;
using StackExchange.Redis;
using StackExchangeTests.Grpc;

public sealed class StackExchangeRedisTestGrpcService : RedisGetTest.RedisGetTestBase
{
    private static readonly TimeSpan NodeInfoCacheTtl = TimeSpan.FromSeconds(10);
    private readonly RedisRepository _repository;
    private readonly CacheShell _cacheShell;
    private readonly StackExchangeGrpcRuntimeOptions _runtimeOptions;
    private readonly SemaphoreSlim _nodeInfoRefreshLock = new(1, 1);
    private volatile CachedNodeInfos? _nodeInfosCache;

    public StackExchangeRedisTestGrpcService(
        RedisRepository repository,
        CacheShell cacheShell,
        StackExchangeGrpcRuntimeOptions runtimeOptions)
    {
        _repository = repository;
        _cacheShell = cacheShell;
        _runtimeOptions = runtimeOptions;
    }

    public override async Task<TriggerGetReply> TriggerGet(TriggerGetRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "key is required"));
        }

        var value = await _repository.GetStringAsync(request.Key);
        return new TriggerGetReply
        {
            Found = !string.IsNullOrEmpty(value),
            Value = value ?? string.Empty,
            Message = value is null ? "nil" : "ok"
        };
    }

    public override async Task<TriggerPipelineReply> TriggerPipeline(TriggerPipelineRequest request, ServerCallContext context)
    {
        var keyPrefix = string.IsNullOrWhiteSpace(request.KeyPrefix) ? "grpc:pipeline" : request.KeyPrefix.Trim();
        var keyCount = request.KeyCount <= 0 ? 3 : Math.Min(request.KeyCount, 32);
        var expirySeconds = request.ExpirySeconds <= 0 ? 300 : request.ExpirySeconds;
        var sameHashtag = request.SameHashtag;
        var runId = Guid.NewGuid().ToString("N");

        var values = BuildPipelineValues(keyPrefix, runId, keyCount, sameHashtag);
        await _repository.PipelineSetAsync(values, TimeSpan.FromSeconds(expirySeconds));
        var readResult = await _repository.PipelineGetStringAsync(values.Keys.ToList());

        var slots = values.Keys.Select(ComputeRedisClusterSlot).ToArray();
        var distinctSlots = slots.Distinct().OrderBy(x => x).ToArray();
        var scenario = sameHashtag ? "same-hashtag" : "multi-hashtag";

        var reply = new TriggerPipelineReply
        {
            Ok = true,
            Scenario = scenario,
            DistinctSlots = distinctSlots.Length,
            Message = $"pipeline ok, keys={values.Count}, distinctSlots={distinctSlots.Length}"
        };

        reply.Slots.AddRange(distinctSlots);
        foreach (var key in values.Keys)
        {
            reply.Entries.Add(new PipelineEntry
            {
                Key = key,
                Value = readResult.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty,
                Slot = ComputeRedisClusterSlot(key)
            });
        }

        return reply;
    }

    public override async Task<TriggerCacheShellReply> TriggerCacheShell(TriggerCacheShellRequest request, ServerCallContext context)
    {
        var keyPrefix = string.IsNullOrWhiteSpace(request.KeyPrefix)
            ? $"grpc:cacheshell:{Guid.NewGuid():N}"
            : request.KeyPrefix.Trim();
        var hashField = string.IsNullOrWhiteSpace(request.HashField) ? "field1" : request.HashField.Trim();
        var expiry = TimeSpan.FromSeconds(request.ExpirySeconds <= 0 ? 300 : request.ExpirySeconds);

        var stringFactoryCalls = 0;
        var stringKey = $"{keyPrefix}:string";
        var stringFirst = await _cacheShell.GetOrSetAsync(
            stringKey,
            expiry,
            async () =>
            {
                Interlocked.Increment(ref stringFactoryCalls);
                await Task.CompletedTask;
                return "value-from-factory";
            });

        var stringSecond = await _cacheShell.GetOrSetAsync(
            stringKey,
            expiry,
            async () =>
            {
                Interlocked.Increment(ref stringFactoryCalls);
                await Task.CompletedTask;
                return "should-not-be-used";
            });

        var hashFactoryCalls = 0;
        var hashKey = $"{keyPrefix}:hash";
        var hashFirst = await _cacheShell.GetOrSetHashAsync(
            hashKey,
            hashField,
            expiry,
            async () =>
            {
                Interlocked.Increment(ref hashFactoryCalls);
                await Task.CompletedTask;
                return "hash-from-factory";
            });

        var hashSecond = await _cacheShell.GetOrSetHashAsync(
            hashKey,
            hashField,
            expiry,
            async () =>
            {
                Interlocked.Increment(ref hashFactoryCalls);
                await Task.CompletedTask;
                return "should-not-be-used";
            });

        var ok = stringFactoryCalls == 1 && hashFactoryCalls == 1;
        return new TriggerCacheShellReply
        {
            Ok = ok,
            StringFirst = stringFirst ?? string.Empty,
            StringSecond = stringSecond ?? string.Empty,
            StringFactoryCalls = stringFactoryCalls,
            HashFirst = hashFirst ?? string.Empty,
            HashSecond = hashSecond ?? string.Empty,
            HashFactoryCalls = hashFactoryCalls,
            Message = ok ? "cacheShell hit/miss behavior ok" : "cacheShell factory call count mismatch"
        };
    }

    public override Task<GetSlotInfosReply> GetSlotInfos(GetSlotInfosRequest request, ServerCallContext context)
    {
        if (request.Keys.Count == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "keys is required"));
        }

        var reply = new GetSlotInfosReply();
        foreach (var key in request.Keys.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            reply.Infos.Add(new SlotInfo
            {
                Key = key,
                HashInput = ExtractHashTagOrKey(key),
                Slot = ComputeRedisClusterSlot(key)
            });
        }

        return Task.FromResult(reply);
    }

    public override async Task<GetNodeInfosReply> GetNodeInfos(GetNodeInfosRequest request, ServerCallContext context)
    {
        var cache = _nodeInfosCache;
        var now = DateTimeOffset.UtcNow;
        if (cache is not null && cache.ExpiresAt > now)
        {
            return BuildNodeInfosReply(cache.Infos, cache.Message);
        }

        // Node topology is diagnostic data: stale-while-revalidate avoids request pile-up during refresh.
        if (cache is not null)
        {
            var lockTaken = await _nodeInfoRefreshLock.WaitAsync(0, context.CancellationToken);
            if (!lockTaken)
            {
                return BuildNodeInfosReply(cache.Infos, $"{cache.Message} (stale)");
            }
        }
        else
        {
            await _nodeInfoRefreshLock.WaitAsync(context.CancellationToken);
        }

        try
        {
            cache = _nodeInfosCache;
            now = DateTimeOffset.UtcNow;
            if (cache is not null && cache.ExpiresAt > now)
            {
                return BuildNodeInfosReply(cache.Infos, cache.Message);
            }

            var infos = await CollectNodeInfosAsync(_runtimeOptions);
            var message = infos.Count == 0 ? "no node info found" : $"nodes={infos.Count}";
            _nodeInfosCache = new CachedNodeInfos(DateTimeOffset.UtcNow.Add(NodeInfoCacheTtl), infos, message);
            return BuildNodeInfosReply(infos, message);
        }
        finally
        {
            _nodeInfoRefreshLock.Release();
        }
    }

    private static Dictionary<string, RedisValue> BuildPipelineValues(
        string keyPrefix,
        string runId,
        int keyCount,
        bool sameHashtag)
    {
        if (sameHashtag)
        {
            var hashTag = $"{runId}:same";
            return Enumerable.Range(1, keyCount)
                .ToDictionary(
                    idx => $"{keyPrefix}:{{{hashTag}}}:k{idx}",
                    idx => (RedisValue)$"v{idx}");
        }

        var values = new Dictionary<string, RedisValue>(keyCount);
        var usedSlots = new HashSet<int>();
        var index = 1;
        var salt = 0;
        while (values.Count < keyCount)
        {
            var hashTag = $"{runId}:multi:{index}:{salt}";
            var key = $"{keyPrefix}:{{{hashTag}}}:k{index}";
            var slot = ComputeRedisClusterSlot(key);
            if (usedSlots.Add(slot))
            {
                values[key] = $"v{index}";
                index++;
            }
            else
            {
                salt++;
            }
        }

        return values;
    }

    private static async Task<List<NodeInfo>> CollectNodeInfosAsync(StackExchangeGrpcRuntimeOptions options)
    {
        var infos = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
        if (!options.SentinelEnabled)
        {
            var directInfos = await CollectDirectNodesAsync(options.ConnectionString);
            foreach (var info in directInfos)
            {
                infos[info.Endpoint] = info;
            }

            return infos.Values.ToList();
        }

        foreach (var endpoint in options.SentinelEndpoints)
        {
            var sentinelInfo = await ProbeSentinelAsync(endpoint);
            infos[sentinelInfo.Endpoint] = sentinelInfo;
        }

        var firstSentinel = options.SentinelEndpoints.FirstOrDefault();
        if (firstSentinel is null)
        {
            return infos.Values.ToList();
        }

        var sentinelOptions = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = 3000,
            SyncTimeout = 3000,
            TieBreaker = string.Empty,
            CommandMap = CommandMap.Sentinel
        };
        sentinelOptions.EndPoints.Add(firstSentinel);

        try
        {
            await using var mux = await ConnectionMultiplexer.ConnectAsync(sentinelOptions);
            var server = mux.GetServer(mux.GetEndPoints().First());
            var mastersResult = await server.ExecuteAsync("SENTINEL", "MASTERS");
            foreach (var row in ParseSentinelRows(mastersResult))
            {
                if (!row.TryGetValue("ip", out var ip) || !row.TryGetValue("port", out var portStr) || !int.TryParse(portStr, out var port))
                {
                    continue;
                }

                var masterName = row.TryGetValue("name", out var n) ? n : "unknown";
                var masterInfo = await ProbeRedisDataNodeAsync(ip, port, $"master:{masterName}");
                infos[masterInfo.Endpoint] = masterInfo;

                var replicasResult = await server.ExecuteAsync("SENTINEL", "REPLICAS", masterName);
                foreach (var replicaRow in ParseSentinelRows(replicasResult))
                {
                    if (!replicaRow.TryGetValue("ip", out var rip) || !replicaRow.TryGetValue("port", out var rportStr) || !int.TryParse(rportStr, out var rport))
                    {
                        continue;
                    }

                    var flags = replicaRow.TryGetValue("flags", out var fl) ? fl : "?";
                    var replicaInfo = await ProbeRedisDataNodeAsync(rip, rport, $"replica:{masterName} ({flags})");
                    infos[replicaInfo.Endpoint] = replicaInfo;
                }
            }
        }
        catch (Exception ex)
        {
            infos[firstSentinel] = new NodeInfo
            {
                Endpoint = firstSentinel,
                Reachable = false,
                Role = "sentinel",
                Slots = "n/a",
                Message = $"sentinel query failed: {ex.Message}"
            };
        }

        return infos.Values.ToList();
    }

    private static async Task<List<NodeInfo>> CollectDirectNodesAsync(string connectionString)
    {
        var list = new List<NodeInfo>();
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 3000;
        options.SyncTimeout = 3000;

        try
        {
            await using var mux = await ConnectionMultiplexer.ConnectAsync(options);
            foreach (var ep in mux.GetEndPoints())
            {
                try
                {
                    var server = mux.GetServer(ep);
                    var ping = await server.PingAsync();
                    var slotSummary = await FormatClusterSlotsSummaryAsync(server, server.EndPoint.ToString()!);
                    list.Add(new NodeInfo
                    {
                        Endpoint = ep.ToString(),
                        Reachable = true,
                        Role = "direct",
                        Slots = slotSummary,
                        Message = $"ping={ping.TotalMilliseconds:F1}ms"
                    });
                }
                catch (Exception ex)
                {
                    list.Add(new NodeInfo
                    {
                        Endpoint = ep.ToString(),
                        Reachable = false,
                        Role = "direct",
                        Slots = "n/a",
                        Message = ex.Message
                    });
                }
            }
        }
        catch (Exception ex)
        {
            list.Add(new NodeInfo
            {
                Endpoint = "direct-mode",
                Reachable = false,
                Role = "direct",
                Slots = "n/a",
                Message = ex.Message
            });
        }

        return list;
    }

    private static async Task<NodeInfo> ProbeSentinelAsync(string endpoint)
    {
        try
        {
            var options = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ConnectTimeout = 3000,
                SyncTimeout = 3000,
                TieBreaker = string.Empty,
                CommandMap = CommandMap.Sentinel
            };
            options.EndPoints.Add(endpoint);
            await using var mux = await ConnectionMultiplexer.ConnectAsync(options);
            var server = mux.GetServer(mux.GetEndPoints().First());
            var ping = await server.PingAsync();
            return new NodeInfo
            {
                Endpoint = endpoint,
                Reachable = true,
                Role = "sentinel",
                Slots = "n/a",
                Message = $"ping={ping.TotalMilliseconds:F1}ms"
            };
        }
        catch (Exception ex)
        {
            return new NodeInfo
            {
                Endpoint = endpoint,
                Reachable = false,
                Role = "sentinel",
                Slots = "n/a",
                Message = ex.Message
            };
        }
    }

    private static async Task<NodeInfo> ProbeRedisDataNodeAsync(string host, int port, string role)
    {
        var endpoint = $"{host}:{port}";
        try
        {
            var options = ConfigurationOptions.Parse($"{endpoint},connectTimeout=3000,syncTimeout=3000,abortConnect=false");
            await using var mux = await ConnectionMultiplexer.ConnectAsync(options);
            var server = mux.GetServer(host, port);
            var ping = await server.PingAsync();
            var slotSummary = await FormatClusterSlotsSummaryAsync(server, endpoint);
            return new NodeInfo
            {
                Endpoint = endpoint,
                Reachable = true,
                Role = role,
                Slots = slotSummary,
                Message = $"ping={ping.TotalMilliseconds:F1}ms"
            };
        }
        catch (Exception ex)
        {
            return new NodeInfo
            {
                Endpoint = endpoint,
                Reachable = false,
                Role = role,
                Slots = "n/a",
                Message = ex.Message
            };
        }
    }

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

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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

                var endpoint = parts[1].Split('@')[0];
                if (!string.Equals(endpoint, hostPortForMatch, StringComparison.OrdinalIgnoreCase))
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
                    return idx >= 0
                        ? trimmed[(idx + " connected ".Length)..].Trim()
                        : "(master, no slot line)";
                }

                return flags;
            }

            return "(not listed in CLUSTER NODES)";
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

    private static int ComputeRedisClusterSlot(string key)
    {
        var hashInput = ExtractHashTagOrKey(key);
        var bytes = Encoding.UTF8.GetBytes(hashInput);
        ushort crc = 0;
        foreach (var b in bytes)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }

        return crc % 16384;
    }

    private static string ExtractHashTagOrKey(string key)
    {
        var start = key.IndexOf('{');
        if (start >= 0)
        {
            var end = key.IndexOf('}', start + 1);
            if (end > start + 1)
            {
                return key.Substring(start + 1, end - start - 1);
            }
        }

        return key;
    }

    private static GetNodeInfosReply BuildNodeInfosReply(IReadOnlyCollection<NodeInfo> infos, string message)
    {
        var reply = new GetNodeInfosReply
        {
            Message = message
        };
        reply.Infos.AddRange(infos.Select(CloneNodeInfo));
        return reply;
    }

    private static NodeInfo CloneNodeInfo(NodeInfo value) =>
        new()
        {
            Endpoint = value.Endpoint,
            Reachable = value.Reachable,
            Role = value.Role,
            Slots = value.Slots,
            Message = value.Message
        };

    private sealed record CachedNodeInfos(DateTimeOffset ExpiresAt, List<NodeInfo> Infos, string Message);
}
