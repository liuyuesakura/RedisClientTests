using CSRedis;

public sealed class CsRedisRepository
{
    private readonly Func<CSRedisClient> _clientFactory;

    public CsRedisRepository(CSRedisClient client)
    {
        _clientFactory = () => client;
    }

    public CsRedisRepository(CsRedisConnectionPool pool)
    {
        _clientFactory = () => pool.Client;
    }

    public Task PipelineSetAsync(IReadOnlyDictionary<string, string> values, TimeSpan? expiry = null)
    {
        if (values.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            var redis = _clientFactory();
            var pipe = redis.StartPipe();
            foreach (var pair in values)
            {
                if (expiry.HasValue)
                {
                    pipe.Set(pair.Key, pair.Value, (int)expiry.Value.TotalSeconds);
                }
                else
                {
                    pipe.Set(pair.Key, pair.Value);
                }
            }

            pipe.EndPipe();
        });
    }

    public Task<Dictionary<string, string?>> PipelineGetStringAsync(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
        {
            return Task.FromResult(new Dictionary<string, string?>());
        }

        return Task.Run(() =>
        {
            var redis = _clientFactory();
            var pipe = redis.StartPipe();
            foreach (var key in keys)
            {
                pipe.Get(key);
            }

            var raw = pipe.EndPipe();
            var result = new Dictionary<string, string?>(keys.Count);
            if (raw is object[] parts)
            {
                for (var i = 0; i < keys.Count && i < parts.Length; i++)
                {
                    var v = parts[i];
                    result[keys[i]] = v is null or DBNull ? null : v.ToString();
                }
            }
            else
            {
                for (var i = 0; i < keys.Count; i++)
                {
                    result[keys[i]] = null;
                }
            }

            return result;
        });
    }
}
