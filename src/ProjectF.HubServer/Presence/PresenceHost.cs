using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectF.HubServer.Presence;

namespace ProjectF.HubServer;

/// <summary>
/// Single source of truth for the presence server's host configuration —
/// used by Program.cs and by the integration tests (which bind a dynamic
/// loopback port instead of the fixed 5170).
/// </summary>
public static class PresenceHost
{
    public static IHost BuildHost(Action<IWebHostBuilder>? configure = null)
    {
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureWebHostDefaults(web =>
        {
            // MagicOnion's gRPC transport needs HTTP/2; cleartext h2c keeps the
            // dev setup certificate-free (Unity uses YetAnotherHttpHandler).
            web.ConfigureKestrel(options =>
            {
                options.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http2);
            });
            web.ConfigureServices(services =>
            {
                services.AddSingleton<PresenceRegistry>();
                services.AddMagicOnion();
            });
            web.Configure(app =>
            {
                // MapMagicOnionService is a routing extension: with the classic
                // Startup-style pipeline we need UseRouting + UseEndpoints.
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapMagicOnionService();
                });
            });
            configure?.Invoke(web);
        });

        return builder.Build();
    }
}
