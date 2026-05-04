using FreeRedis;
using System.Reflection;

internal static class FreeRedisClusterFactory
{
    public sealed record SlotCacheWarmupResult(
        bool Success,
        string Path,
        int SlotCacheCount,
        string? ErrorMessage);

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

    public static SlotCacheWarmupResult WarmupSlotCache(RedisClient client)
    {
        try
        {
            var adapterProp = typeof(RedisClient).GetProperty(
                "Adapter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var adapter = adapterProp?.GetValue(client);
            if (adapter is not null)
            {
                var refreshClusterNodes = adapter.GetType().GetMethod(
                    "RefershClusterNodes",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (refreshClusterNodes is not null)
                {
                    refreshClusterNodes.Invoke(adapter, null);
                    return new SlotCacheWarmupResult(
                        Success: true,
                        Path: "private:RefershClusterNodes",
                        SlotCacheCount: GetSlotCacheCount(client),
                        ErrorMessage: null);
                }
            }

            // Fallback: trigger cluster topology read via a standard cluster command.
            client.Call(new CommandPacket("CLUSTER", "SLOTS"));
            return new SlotCacheWarmupResult(
                Success: true,
                Path: "command:CLUSTER SLOTS",
                SlotCacheCount: GetSlotCacheCount(client),
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new SlotCacheWarmupResult(
                Success: false,
                Path: "failed",
                SlotCacheCount: GetSlotCacheCount(client),
                ErrorMessage: ex.Message);
        }
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

    private static int GetSlotCacheCount(RedisClient client)
    {
        try
        {
            var adapterProp = typeof(RedisClient).GetProperty(
                "Adapter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var adapter = adapterProp?.GetValue(client);
            if (adapter is null)
            {
                return -1;
            }

            var slotCacheField = adapter.GetType().GetField(
                "_slotCache",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var slotCache = slotCacheField?.GetValue(adapter);
            if (slotCache is null)
            {
                return -1;
            }

            var countProp = slotCache.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            if (countProp is not null && countProp.GetValue(slotCache) is int count)
            {
                return count;
            }

            return slotCache is Array arr ? arr.Length : -2;
        }
        catch
        {
            return -3;
        }
    }
}
