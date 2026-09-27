using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProjectF.HubServer;

// Stage 6 presence server. All host wiring lives in PresenceHost (shared with
// the integration tests); this entry point only pins the dev endpoint.
using IHost host = PresenceHost.BuildHost(web =>
{
    web.UseUrls("http://127.0.0.1:5170");
    web.ConfigureKestrel(options =>
    {
        options.ConfigureEndpointDefaults(lo => lo.Protocols = HttpProtocols.Http2);
    });
});

// Presence registry is in-memory: log the lifecycle so a dev can see it die.
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("ProjectF.HubServer")
    .LogInformation("ProjectF.HubServer listening on http://127.0.0.1:5170 (h2c) — presence state is in-memory");

await host.RunAsync();
