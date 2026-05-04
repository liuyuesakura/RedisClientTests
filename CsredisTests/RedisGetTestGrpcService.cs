using CsredisTests.Grpc;
using Grpc.Core;
using System.Text;
using CSRedis;

namespace CsredisTests;

public sealed class RedisGetTestGrpcService : RedisGetTest.RedisGetTestBase
{
    private readonly CsRedisRepository _repository;
    private readonly string _connectionString;

    public RedisGetTestGrpcService(CsRedisRepository repository, string connectionString)
    {
        _repository = repository;
        _connectionString = connectionString;
    }

    public override Task<TriggerGetReply> TriggerGet(TriggerGetRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "key is required"));
        }

        var value = _repository.GetString(request.Key);
        return Task.FromResult(new TriggerGetReply
        {
            Found = !string.IsNullOrEmpty(value),
            Value = value ?? string.Empty,
            Message = value is null ? "nil" : "ok"
        });
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

    public override Task<TriggerCacheShellReply> TriggerCacheShell(TriggerCacheShellRequest request, ServerCallContext context)
    {
        var keyPrefix = string.IsNullOrWhiteSpace(request.KeyPrefix)
            ? $"grpc:cacheshell:{Guid.NewGuid():N}"
            : request.KeyPrefix.Trim();
        var hashField = string.IsNullOrWhiteSpace(request.HashField) ? "field1" : request.HashField.Trim();
        var expirySeconds = request.ExpirySeconds <= 0 ? 300 : request.ExpirySeconds;

        var stringFactoryCalls = 0;
        var stringKey = $"{keyPrefix}:string";
        var stringFirst = RedisHelper.CacheShell(stringKey, expirySeconds, () =>
        {
            Interlocked.Increment(ref stringFactoryCalls);
            return "value-from-factory";
        });

        var stringSecond = RedisHelper.CacheShell(stringKey, expirySeconds, () =>
        {
            Interlocked.Increment(ref stringFactoryCalls);
            return "should-not-be-used";
        });

        var hashFactoryCalls = 0;
        var hashKey = $"{keyPrefix}:hash";
        var hashFirst = RedisHelper.CacheShell(hashKey, hashField, expirySeconds, () =>
        {
            Interlocked.Increment(ref hashFactoryCalls);
            return "hash-from-factory";
        });

        var hashSecond = RedisHelper.CacheShell(hashKey, hashField, expirySeconds, () =>
        {
            Interlocked.Increment(ref hashFactoryCalls);
            return "should-not-be-used";
        });

        var ok = stringFactoryCalls == 1 && hashFactoryCalls == 1;
        return Task.FromResult(new TriggerCacheShellReply
        {
            Ok = ok,
            StringFirst = stringFirst ?? string.Empty,
            StringSecond = stringSecond ?? string.Empty,
            StringFactoryCalls = stringFactoryCalls,
            HashFirst = hashFirst ?? string.Empty,
            HashSecond = hashSecond ?? string.Empty,
            HashFactoryCalls = hashFactoryCalls,
            Message = ok ? "cacheShell hit/miss behavior ok" : "cacheShell factory call count mismatch"
        });
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
            var hashInput = ExtractHashTagOrKey(key);
            reply.Infos.Add(new SlotInfo
            {
                Key = key,
                HashInput = hashInput,
                Slot = ComputeRedisClusterSlot(key)
            });
        }

        return Task.FromResult(reply);
    }

    public override Task<GetNodeInfosReply> GetNodeInfos(GetNodeInfosRequest request, ServerCallContext context)
    {
        var infos = CollectNodeInfos(_connectionString);
        var reply = new GetNodeInfosReply
        {
            Message = infos.Count == 0 ? "no node info found" : $"nodes={infos.Count}"
        };

        reply.Infos.AddRange(infos);
        return Task.FromResult(reply);
    }

    private static Dictionary<string, string> BuildPipelineValues(
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
                    idx => $"v{idx}");
        }

        var values = new Dictionary<string, string>(keyCount);
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

    private static List<NodeInfo> CollectNodeInfos(string connectionString)
    {
        var infos = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
        var endpoint = ParseFirstEndpoint(connectionString);
        if (endpoint is null)
        {
            return
            [
                new NodeInfo
                {
                    Endpoint = "unknown",
                    Reachable = false,
                    Role = "unknown",
                    Slots = "n/a",
                    Message = "invalid connection string"
                }
            ];
        }

        try
        {
            using var redis = new CSRedisClient($"{endpoint},connectTimeout=3000,syncTimeout=3000,abortConnect=false");
            var ping = redis.Ping();
            infos[endpoint] = new NodeInfo
            {
                Endpoint = endpoint,
                Reachable = true,
                Role = "direct",
                Slots = "n/a",
                Message = $"ping={ping}"
            };

            const string probeKey = "__grpc_node_info_probe__";
            const string script = "return redis.call('CLUSTER','NODES')";
            var raw = redis.Eval(script, probeKey);
            var text = raw switch
            {
                null => null,
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                _ => raw.ToString()
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var info in ParseClusterNodes(text))
                {
                    infos[info.Endpoint] = info;
                }
            }
        }
        catch (Exception ex)
        {
            infos[endpoint] = new NodeInfo
            {
                Endpoint = endpoint,
                Reachable = false,
                Role = "unknown",
                Slots = "n/a",
                Message = ex.Message
            };
        }

        return infos.Values.ToList();
    }

    private static IEnumerable<NodeInfo> ParseClusterNodes(string clusterNodes)
    {
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

            var endpoint = parts[1].Split('@')[0];
            var flags = parts[2];
            var role = flags.Contains("master", StringComparison.OrdinalIgnoreCase)
                ? "master"
                : flags.Contains("slave", StringComparison.OrdinalIgnoreCase) || flags.Contains("replica", StringComparison.OrdinalIgnoreCase)
                    ? "replica"
                    : flags;
            var connected = trimmed.Contains(" connected ", StringComparison.OrdinalIgnoreCase);
            var slots = "n/a";
            if (role == "master")
            {
                var idx = trimmed.IndexOf(" connected ", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    slots = trimmed[(idx + " connected ".Length)..].Trim();
                }
            }
            else if (role == "replica")
            {
                slots = "(replica, slots on primary)";
            }

            yield return new NodeInfo
            {
                Endpoint = endpoint,
                Reachable = connected,
                Role = role,
                Slots = slots,
                Message = connected ? "connected" : flags
            };
        }
    }

    private static string? ParseFirstEndpoint(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var first = connectionString.Split(',')[0].Trim();
        return string.IsNullOrWhiteSpace(first) ? null : first;
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
}
