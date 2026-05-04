using CsredisTests.Grpc;
using Grpc.Core;

namespace CsredisTests;

public sealed class RedisGetTestGrpcService : RedisGetTest.RedisGetTestBase
{
    private readonly CsRedisRepository _repository;

    public RedisGetTestGrpcService(CsRedisRepository repository)
    {
        _repository = repository;
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
}
