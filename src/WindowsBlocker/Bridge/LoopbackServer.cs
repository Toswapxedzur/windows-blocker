using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WindowsBlocker.Bridge;

// Kestrel binds a user-owned loopback socket. Unlike HTTP.sys listeners it
// needs no administrator privilege or system-wide URL reservation.
internal static class LoopbackServer
{
    public static WebApplication Start(int port,RequestDelegate handler,bool sockets=false)
    {
        var builder=WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args=[],ContentRootPath=AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options=> { options.Listen(IPAddress.Loopback,port); options.Limits.MaxRequestBodySize=1_048_576; });
        var app=builder.Build(); if(sockets) app.UseWebSockets();
        app.Run(async context=>
        {
            if(context.Connection.RemoteIpAddress is not { } address || !IPAddress.IsLoopback(address) || context.Request.Host.Host is not ("127.0.0.1" or "localhost" or "::1")) { context.Response.StatusCode=403; return; }
            await handler(context);
        });
        app.StartAsync().GetAwaiter().GetResult(); return app;
    }
    public static void Stop(WebApplication? app)
        => StopAsync(app).GetAwaiter().GetResult();
    public static async Task StopAsync(WebApplication? app)
    {
        if(app==null) return;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await app.StopAsync(timeout.Token).ConfigureAwait(false); } catch { }
        try { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
    }
}
