using System.Collections.Concurrent;
using Grpc.Net.Client;
using MagicOnion.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProjectF.HubServer;
using ProjectF.HubServer.Presence;
using ProjectF.Shared.Hubs;
using ProjectF.Shared.Presence;
using Xunit;

namespace ProjectF.Lib.Tests.HubServer;

/// <summary>
/// Stage 6 integration proof: two fake clients over REAL gRPC — join scene 4,
/// move one, the other receives OnMove; kill a connection, the survivor
/// receives OnLeave.
///
/// Runs in one of two modes:
///  - DEFAULT: an in-process Kestrel host on a dynamic loopback port
///    (parallel-test safe, no fixed-port contention).
///  - PF_HUB_URL set (e.g. http://127.0.0.1:5170): connect to that ALREADY
///    RUNNING server (scripts/run-two-nodes.ps1 starts HubServer alongside the
///    seed node and drives these tests against it). Registry-count assertions
///    are skipped here — the live server hosts other sessions too.
/// </summary>
public sealed class PresenceHubIntegrationTests
{
    private static string? ExternalHubUrl =>
        Environment.GetEnvironmentVariable("PF_HUB_URL") is { Length: > 0 } url ? url : null;

    private static string TestRunId { get; } = Guid.NewGuid().ToString("N")[..8];

    private sealed record HubEvent(string Name, string SessionId, PlayerSnapshot? Snapshot, int EmoteId);

    /// <summary>Thread-safe receiver recording every server callback.</summary>
    private sealed class RecordingReceiver : IPlayerHubReceiver
    {
        public ConcurrentQueue<HubEvent> Events { get; } = new();

        public void OnSceneSnapshot(PlayerSnapshot[] players)
        {
            foreach (var p in players)
            {
                Events.Enqueue(new HubEvent("snapshot", p.SessionId, p, 0));
            }
        }

        public void OnJoin(PlayerSnapshot player) =>
            Events.Enqueue(new HubEvent("join", player.SessionId, player, 0));

        public void OnLeave(string sessionId) =>
            Events.Enqueue(new HubEvent("leave", sessionId, null, 0));

        public void OnMove(PlayerSnapshot player) =>
            Events.Enqueue(new HubEvent("move", player.SessionId, player, 0));

        public void OnEmote(string sessionId, int emoteId) =>
            Events.Enqueue(new HubEvent("emote", sessionId, null, emoteId));

        public void OnPondHint(PondHintMessage hint)
        {
        }
    }

    private static async Task<HubEvent> WaitForAsync(
        ConcurrentQueue<HubEvent> events, Func<HubEvent, bool> predicate,
        string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var match = events.FirstOrDefault(predicate);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        var seen = string.Join(", ", events.Select(e => e.Name));
        throw new TimeoutException($"presence test: never received {what} (saw: {seen})");
    }

    [Fact]
    public async Task Two_clients_scene4_move_and_disconnect_flow()
    {
        await using var running = await TestHostAsync();

        var receiverA = new RecordingReceiver();
        var receiverB = new RecordingReceiver();
        var hubA = await StreamingHubClient.ConnectAsync<IPlayerHub, IPlayerHubReceiver>(
            running.Channel, receiverA);
        var hubB = await StreamingHubClient.ConnectAsync<IPlayerHub, IPlayerHubReceiver>(
            running.Channel, receiverB);

        var alice = $"{TestRunId}-Alice";
        var bob = $"{TestRunId}-Bob";

        // A joins scene 4 first: empty roster, nothing to see yet.
        var rosterA = await hubA.JoinAsync(new PlayerJoinRequest
        {
            PlayerName = alice, SceneId = 4, X = 1f, Y = 2f,
        });
        Assert.Empty(rosterA);

        var registry = running.Registry;
        if (registry is not null)
        {
            Assert.Equal(1, registry.Count(4));
        }

        // B joins scene 4: A must receive OnJoin(B); B must see Alice in its roster.
        var rosterB = await hubB.JoinAsync(new PlayerJoinRequest
        {
            PlayerName = bob, SceneId = 4, X = 5f, Y = 6f,
        });
        if (registry is not null)
        {
            Assert.Equal(2, registry.Count(4));
        }

        var joinA = await WaitForAsync(
            receiverA.Events,
            e => e.Name == "join" && e.Snapshot!.PlayerName == bob,
            "OnJoin(B)");
        string bobSessionId = joinA.SessionId; // A learned Bob's id via OnJoin

        // B's roster comes back as the JoinAsync RESULT (OnSceneSnapshot is a
        // separate push the server may use later for reconnect sync).
        Assert.Contains(rosterB, p => p.PlayerName == alice);
        Assert.DoesNotContain(rosterB, p => p.PlayerName == bob);

        // B moves: A receives OnMove with B's session id and new coords.
        await hubB.MoveAsync(new PlayerMoveRequest
        {
            SceneId = 4, X = 10.5f, Y = 20.5f, Facing = Direction.Left, Animation = AnimationState.Walk,
        });
        var move = await WaitForAsync(
            receiverA.Events,
            e => e.Name == "move" && e.Snapshot!.X == 10.5f && e.Snapshot.Y == 20.5f,
            "OnMove(B @ 10.5, 20.5)");
        Assert.Equal(bobSessionId, move.SessionId);
        Assert.Equal(AnimationState.Walk, move.Snapshot!.Animation);

        // B emotes: A receives OnEmote.
        await hubB.EmoteAsync(7);
        await WaitForAsync(receiverA.Events, e => e.Name == "emote" && e.EmoteId == 7, "OnEmote(7)");

        // KILL B's connection (no LeaveAsync — the crash path): the survivor
        // must receive OnLeave for B and the registry must drop B.
        await hubB.DisposeAsync();

        await WaitForAsync(receiverA.Events, e => e.Name == "leave" && e.SessionId == bobSessionId, "OnLeave(B)");
        if (registry is not null)
        {
            Assert.Equal(1, registry.Count(4)); // only Alice remains — no ghost player
        }

        await hubA.DisposeAsync();
        await running.DisposeAsync();
    }

    [Fact]
    public async Task Change_scene_leaves_old_group_and_joins_new()
    {
        await using var running = await TestHostAsync();

        var receiverA = new RecordingReceiver();
        var receiverB = new RecordingReceiver();
        var hubA = await StreamingHubClient.ConnectAsync<IPlayerHub, IPlayerHubReceiver>(
            running.Channel, receiverA);
        var hubB = await StreamingHubClient.ConnectAsync<IPlayerHub, IPlayerHubReceiver>(
            running.Channel, receiverB);

        var alice = $"{TestRunId}-Alice";
        var bob = $"{TestRunId}-Bob";

        _ = await hubA.JoinAsync(new PlayerJoinRequest { PlayerName = alice, SceneId = 4, X = 0f, Y = 0f });
        _ = await hubB.JoinAsync(new PlayerJoinRequest { PlayerName = bob, SceneId = 5, X = 1f, Y = 1f });

        var registry = running.Registry;
        if (registry is not null)
        {
            Assert.Equal(1, registry.Count(4));
            Assert.Equal(1, registry.Count(5));
        }

        // Bob walks from scene 5 into scene 4: Alice must see him join,
        // scene 5 must be empty afterwards.
        await hubB.ChangeSceneAsync(4, 9f, 9f);

        await WaitForAsync(
            receiverA.Events,
            e => e.Name == "join" && e.Snapshot!.PlayerName == bob,
            "OnJoin(Bob) in scene 4");
        if (registry is not null)
        {
            Assert.Equal(2, registry.Count(4));
            Assert.Equal(0, registry.Count(5));
        }

        // And Bob's moves now reach scene 4, not scene 5.
        await hubB.MoveAsync(new PlayerMoveRequest { SceneId = 4, X = 11f, Y = 12f, Facing = Direction.Up, Animation = AnimationState.Idle });
        await WaitForAsync(receiverA.Events, e => e.Name == "move" && e.Snapshot!.X == 11f, "OnMove(Bob) after scene change");

        await hubA.DisposeAsync();
        await hubB.DisposeAsync();
        await running.DisposeAsync();
    }

    /// <summary>In-process host (default) or channel to the external PF_HUB_URL
    /// server. Registry is only available in-process.</summary>
    private static async Task<TestEnvironment> TestHostAsync()
    {
        if (ExternalHubUrl is { } url)
        {
            return new TestEnvironment(host: null, GrpcChannel.ForAddress(url), registry: null);
        }

        IHost host = PresenceHost.BuildHost(web =>
        {
            // Port 0: Kestrel binds a free loopback port (parallel-test safe).
            web.UseUrls("http://127.0.0.1:0");
        });
        await host.StartAsync();

        var addresses = host.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        var port = new Uri(addresses.First()).Port;

        var registry = host.Services.GetRequiredService<PresenceRegistry>();
        return new TestEnvironment(host, GrpcChannel.ForAddress($"http://127.0.0.1:{port}"), registry);
    }

    private sealed class TestEnvironment : IAsyncDisposable
    {
        public TestEnvironment(IHost? host, GrpcChannel channel, PresenceRegistry? registry)
        {
            Host = host;
            Channel = channel;
            Registry = registry;
        }

        public IHost? Host { get; }

        public GrpcChannel Channel { get; }

        /// <summary>Null in external-server mode (global counts would include
        /// other sessions; the message-flow assertions stay exact).</summary>
        public PresenceRegistry? Registry { get; }

        public async ValueTask DisposeAsync()
        {
            Channel.Dispose();
            if (Host is not null)
            {
                await Host.StopAsync(TimeSpan.FromSeconds(5));
                Host.Dispose();
            }
        }
    }
}
