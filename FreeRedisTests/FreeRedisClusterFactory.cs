using FreeRedis;

internal static class FreeRedisClusterFactory
{
    public static RedisClient Create(string connectionString)
    {
        var endpoints = ParseClusterEndpoints(connectionString);
        if (endpoints.Length == 0)
        {
            throw new InvalidOperationException("No host:port endpoints found in Redis:ConnectionString.");
        }

        var opts = ExtractOptionsSuffix(connectionString);
        var builders = endpoints
            .Select(ep => (ConnectionStringBuilder)(string.IsNullOrEmpty(opts) ? ep : $"{ep},{opts}"))
            .ToArray();

        return new RedisClient(builders);
    }

    private static string[] ParseClusterEndpoints(string connectionString)
    {
        return connectionString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !s.Contains('=', StringComparison.Ordinal) && s.Contains(':', StringComparison.Ordinal))
            .ToArray();
    }

    private static string ExtractOptionsSuffix(string connectionString)
    {
        var parts = connectionString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Contains('=', StringComparison.Ordinal));
        return string.Join(",", parts);
    }
}
