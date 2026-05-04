using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CsredisTests;

internal static class CsRedisGrpcHost
{
    public static WebApplication Start(CsRedisRepository repository, string connectionString, int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http2);
        });

        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton(connectionString);
        builder.Services.AddGrpc();

        var app = builder.Build();
        app.MapGrpcService<RedisGetTestGrpcService>();
        app.MapGet("/", () => "Use a gRPC client to call RedisGetTest/TriggerGet.");

        _ = app.RunAsync();
        return app;
    }
}
