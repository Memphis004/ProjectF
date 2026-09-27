using System;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Crypto;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace ProjectF.SeedNode;

internal sealed class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Libplanet logs through Serilog's static Log.Logger. Without a sink,
        // every internal trace (ping/pong, peer discovery, block sync) is lost,
        // which makes swarm problems undiagnosable.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddCommandLine(args)
            .AddEnvironmentVariables("PF_")
            .Build();

        var options = new NodeOptions();
        configuration.GetSection(NodeOptions.SectionName).Bind(options);

        // The .NET config binder fills string[] only from indexed subkeys
        // (SeedNode:StaticPeers:0=...); a scalar --SeedNode:StaticPeers=...
        // is silently ignored. Accept the scalar too: '|' separates multiple
        // peers (peer strings themselves contain commas).
        string? rawPeers = configuration[$"{NodeOptions.SectionName}:StaticPeers"];
        if (!string.IsNullOrWhiteSpace(rawPeers))
        {
            options.StaticPeers = rawPeers
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.Contains(','))
                .ToArray();
        }

        // Key: config → ephemeral.
        PrivateKey nodeKey = string.IsNullOrWhiteSpace(options.PrivateKeyHex)
            ? new PrivateKey()
            : new PrivateKey(options.PrivateKeyHex.Trim());

        Console.WriteLine("ProjectF.SeedNode");
        Console.WriteLine("=================");
        Console.WriteLine($"Store path:   {(string.IsNullOrWhiteSpace(options.StorePath) ? "(in-memory)" : options.StorePath)}");
        Console.WriteLine($"Miner:        {options.IsMiner}");
        Console.WriteLine($"StaticPeers:  {options.StaticPeers.Length}");

        using CancellationTokenSource cts = new();
        ConsoleCancelEventHandler onCancel =
            (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
        Console.CancelKeyPress += onCancel;

        await using var runner = new SwarmRunner(options, nodeKey);
        try
        {
            await runner.StartAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Node failed to start: {ex}");
            return 1;
        }

        Task? minerTask = options.IsMiner
            ? new MinerLoop(runner, options.TargetBlockIntervalMs).RunAsync(cts.Token)
            : null;

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Shutting down (Ctrl+C)…");
        }

        Log.CloseAndFlush();

        if (minerTask is { })
        {
            try
            {
                await minerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        return 0;
    }
}
