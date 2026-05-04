using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

internal static class StackExchangeGrpcHost
{
    public static WebApplication Start(
        RedisRepository repository,
        CacheShell cacheShell,
        StackExchangeGrpcRuntimeOptions options,
        int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(port, listen => listen.Protocols = HttpProtocols.Http2);
        });

        builder.Services.AddSingleton(repository);
        builder.Services.AddSingleton(cacheShell);
        builder.Services.AddSingleton(options);
        builder.Services.AddGrpc();

        var app = builder.Build();
        app.MapGrpcService<StackExchangeRedisTestGrpcService>();
        app.MapGet("/", () => "Use a gRPC client to call RedisGetTest methods.");

        _ = app.RunAsync();
        return app;
    }
}
