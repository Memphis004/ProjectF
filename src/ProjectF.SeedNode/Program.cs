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
        //
        // Filter: NetMQTransport logs a TimeoutException/TaskCanceledException
        // ERR line for EVERY message aimed at a peer that stopped responding
        // (the dead-peer window before the routing table drops it) — pure
        // noise at volume. ONLY cancel/timeout exceptions from that one source
        // context are dropped; any other error (ChannelClosedException,
        // serialization, send-failures with other causes, all other contexts)
        // still logs.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Filter.ByExcluding(evt =>
            {
                if (evt.Properties.TryGetValue("SourceContext", out Serilog.Events.LogEventPropertyValue? sc) &&
                    sc is Serilog.Events.ScalarValue { Value: string ctx } &&
                    ctx != "Libplanet.Net.Transports.NetMQTransport")
                {
                    return false;
                }

                for (Exception? ex = evt.Exception; ex is not null; ex = ex.InnerException)
                {
                    if (ex is TaskCanceledException or OperationCanceledException or TimeoutException)
                    {
                        return true;
                    }
                }

                return false;
            })
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

        // run-local.ps1 helper: print a fresh key hex and exit (the script
        // pins it into {store}/privkey.txt so genesis never drifts).
        if (args.Contains("--SeedNode:GenerateKeyHexOnly", StringComparer.OrdinalIgnoreCase))
        {
            // ByteArray is ImmutableArray<byte> — copy via indexer (same
            // pattern as the Unity KeyStore; ToArray() depends on which
            // System.Collections.Immutable the compile resolves).
            System.Collections.Immutable.ImmutableArray<byte> raw = new PrivateKey().ByteArray;
            var bytes = new byte[raw.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = raw[i];
            }

            // Convert.ToHexString (net8) — lowercase to match key file format.
            Console.WriteLine(Convert.ToHexString(bytes).ToLowerInvariant());
            return 0;
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
