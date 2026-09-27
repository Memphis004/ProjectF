using System;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Crypto;
using Microsoft.Extensions.Configuration;

namespace ProjectF.SeedNode;

internal sealed class Program
{
    private static async Task<int> Main(string[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddCommandLine(args)
            .AddEnvironmentVariables("PF_")
            .Build();

        var options = new NodeOptions();
        configuration.GetSection(NodeOptions.SectionName).Bind(options);

        // Key: config → ephemeral.
        PrivateKey nodeKey = string.IsNullOrWhiteSpace(options.PrivateKeyHex)
            ? new PrivateKey()
            : new PrivateKey(options.PrivateKeyHex.Trim());

        Console.WriteLine("ProjectF.SeedNode");
        Console.WriteLine("=================");
        Console.WriteLine($"Store path:   {(string.IsNullOrWhiteSpace(options.StorePath) ? "(in-memory)" : options.StorePath)}");
        Console.WriteLine($"Miner:        {options.IsMiner}");

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
