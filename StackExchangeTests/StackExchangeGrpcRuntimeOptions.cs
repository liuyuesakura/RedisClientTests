public sealed class StackExchangeGrpcRuntimeOptions
{
    public required string ConnectionString { get; init; }

    public required bool SentinelEnabled { get; init; }

    public required string[] SentinelEndpoints { get; init; }
}
