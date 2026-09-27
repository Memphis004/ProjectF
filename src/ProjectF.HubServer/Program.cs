using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// MagicOnion server comes in Stage 4; this skeleton only proves Kestrel boots.
// TODO(stage-4): AddMagicOnion() + PlayerHub registration.
builder.Services.AddMagicOnion();

var app = builder.Build();

app.MapGet("/", () => "ProjectF.HubServer — skeleton (Stage 1).");

app.Run();
