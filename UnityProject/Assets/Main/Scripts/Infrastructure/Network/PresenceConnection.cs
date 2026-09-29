using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;
using ProjectF.Infrastructure;
using ProjectF.Shared.Hubs;
using ProjectF.Shared.Presence;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Infrastructure.Network
{
    /// <summary>
    /// Owns the gRPC channel + StreamingHubClient for the presence hub.
    /// Transport: YetAnotherHttpHandler (h2c — HubServer is HTTP/2 cleartext on
    /// 5170, so Http2Only = true) per the MagicOnion 7 Unity client guide.
    /// MagicOnion.Unity's GrpcChannelProvider (MagicOnionUnityDefaults) wires
    /// the default channel provider during [RuntimeInitializeOnLoadMethod].
    ///
    /// Everything here is cosmetic-layer: any failure degrades to Offline and
    /// NEVER blocks gameplay (knowledge.md rule 1, UX contract).
    /// </summary>
    public sealed class PresenceConnection : IDisposable
    {
        private readonly NetworkSettings _settings;
        private readonly PresenceReceiver _receiver;
        private readonly BackgroundTaskRegistry _registry;

        private GrpcChannel? _channel;
        private IPlayerHub? _hub;

        // Stage 11.5: one CTS per connection lifetime — cancels the hub
        // disconnect watcher task on shutdown (it used to be a naked
        // fire-and-forget that outlived the disposed channel). Ownership is
        // tracked through a BackedUpCts so teardown awaits ONLY this
        // connection's watcher — never another service's loop.
        private BackedUpCts? _lifetimeCts;

        public IPlayerHub? Hub => _hub;

        public bool IsConnected => _hub is { };

        public PresenceConnection(
            NetworkSettings settings, PresenceReceiver receiver)
            : this(settings, receiver, new BackgroundTaskRegistry())
        {
        }

        public PresenceConnection(
            NetworkSettings settings,
            PresenceReceiver receiver,
            BackgroundTaskRegistry registry)
        {
            _settings = settings;
            _receiver = receiver;
            _registry = registry;
        }

        /// <summary>Connects + joins a scene group. Returns false instead of
        /// throwing when the hub is unreachable (offline visual mode).</summary>
        public async UniTask<bool> ConnectAsync(
            string playerName, int sceneId, float x, float y, CancellationToken ct)
        {
            try
            {
                _channel = GrpcChannel.ForAddress(_settings.HubAddress, new GrpcChannelOptions
                {
                    HttpHandler = new Cysharp.Net.Http.YetAnotherHttpHandler
                    {
                        // HubServer serves HTTP/2 cleartext (h2c) — force h2.
                        Http2Only = true,
                    },
                    DisposeHttpClient = true,
                });

                // Stage 11.5 (spec 3): StreamingHubClient.ConnectAsync can
                // hang against a dead HubServer — run it under a LINKED CTS
                // with a short (5s) timeout so the connect attempt always
                // terminates.
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));
                _hub = await StreamingHubClient.ConnectAsync<IPlayerHub, IPlayerHubReceiver>(
                    _channel, _receiver, cancellationToken: connectCts.Token);

                // Monitor disconnection (API: WaitForDisconnectAsync returns
                // Task<DisconnectionReason>). Ditched on the threadpool: a
                // dead hub must never touch main-thread state directly — the
                // StatusChanged event drives the HUD dot instead.
                // Stage 11.5: the watcher task is now TRACKED and OWNED by
                // the connection-lifetime source — teardown cancels + awaits
                // it (bounded) BEFORE touching the channel (spec 2 order).
                _lifetimeCts ??= new BackedUpCts(_registry);
                CancellationToken lifetime = _lifetimeCts.Token;
                IPlayerHub hubForWatch = _hub;
                _lifetimeCts.Run(
                    "PresenceConnection.DisconnectWatcher",
                    async () =>
                    {
                        try
                        {
                            await hubForWatch.WaitForDisconnectAsync();
                        }
                        catch (OperationCanceledException)
                        {
                            return; // teardown cancelled the watcher
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[presence] disconnect watch ended: {ex.Message}");
                            return;
                        }

                        if (ReferenceEquals(Interlocked.CompareExchange(ref _hub, null, _hub), hubForWatch))
                        {
                            _hub = null;
                        }

                        Debug.LogWarning("[presence] disconnected from hub — single-player visual mode.");
                    });

                // Stage 16 (multi-instance): the JoinAsync RESULT is the
                // roster of players already in the scene (server:
                // PresenceRegistry.OthersIn). MagicOnion delivers OnJoin
                // broadcasts only to the OTHER group members, so a late
                // joiner never sees those joins — without surfacing this
                // roster everyone who joined earlier stays invisible
                // (repro: Editor joined after the built player and its
                // roster stayed empty forever; RemotePlayerRegistry.OnMove
                // only updates known sessions).
                // Upsert semantics (no Clear): an OnJoin broadcast for a
                // player joining while this roster was in flight must
                // survive the merge.
                PlayerSnapshot[] roster = await _hub.JoinAsync(new PlayerJoinRequest
                {
                    PlayerName = playerName,
                    SceneId = sceneId,
                    X = x,
                    Y = y,
                });

                foreach (PlayerSnapshot player in roster)
                {
                    _receiver.OnJoin(player);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[presence] hub unreachable ({_settings.HubAddress}) — " +
                    $"single-player visual mode. {ex.Message}");
                await DisposeCoreAsync();
                return false;
            }
        }

        public async UniTask<bool> ChangeSceneAsync(int sceneId, float x, float y)
        {
            IPlayerHub? hub = _hub;
            if (hub is null)
            {
                return false;
            }

            try
            {
                await hub.ChangeSceneAsync(sceneId, x, y);
                return true;
            }
            catch (Exception ex)
            {
                // RpcException on a dead channel etc. — drop to offline; the
                // reconnect loop (Stage 12) will re-join.
                Debug.LogWarning($"[presence] ChangeSceneAsync failed — {ex.Message}");
                _hub = null;
                return false;
            }
        }

        public void Move(PlayerMoveRequest request)
        {
            try
            {
                _hub?.MoveAsync(request);
            }
            catch (Exception ex)
            {
                // Fire-and-forget must never surface — a dead hub only means
                // nobody sees us move.
                Debug.LogWarning($"[presence] MoveAsync failed — {ex.Message}");
                _hub = null;
            }
        }

        public void Emote(int emoteId)
        {
            try
            {
                _hub?.EmoteAsync(emoteId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[presence] EmoteAsync failed — {ex.Message}");
                _hub = null;
            }
        }

        public async UniTask LeaveAsync()
        {
            IPlayerHub? hub = _hub;
            if (hub is null)
            {
                return;
            }

            try
            {
                await hub.LeaveAsync();
            }
            catch
            {
                // Best effort — the server's OnDisconnected covers ghosts.
            }
        }

        public async UniTask DisposeAsync() => await DisposeCoreAsync();

        /// <summary>Stage 11.5 (spec 2 order for the gRPC side):
        /// a. cancel the lifetime CTS (stops the disconnect watcher),
        /// b. await the watcher task with a bounded timeout,
        /// c. only THEN shut down + dispose the gRPC channel.
        /// (Spec 2c — "dispose the gRPC channel last" — was the exact
        /// inversion that produced the dead-channel logs at teardown.)
        /// ConfigureAwait(false) everywhere: the sync Dispose bridge blocks
        /// the main thread; sync-context continuations deadlock until the
        /// timeout (the EditMode 14s repro).</summary>
        private async UniTask DisposeCoreAsync()
        {
            // --- a. cancel ------------------------------------------------
            _lifetimeCts?.Cancel();

            // --- b. await THIS connection's watcher only (bounded) ----------
            if (_lifetimeCts is { } lifetime)
            {
                await lifetime.AwaitOwnedAsync(TimeSpan.FromSeconds(3));
            }
            // (All awaits here are UniTask/threadpool — no sync-context
            // capture: the sync Dispose bridge blocks the main thread.)

            // --- c. dispose the channel (last) ------------------------------
            _hub = null;
            if (_channel is { } channel)
            {
                _channel = null;
                try
                {
                    await channel.ShutdownAsync();
                }
                catch
                {
                    // Already dead.
                }

                channel.Dispose();
            }

            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }

        public void Dispose()
        {
            // Sync bridge for VContainer/test teardown: run the ordered async
            // shutdown and block briefly — never fire-and-forget (Stage 11.5:
            // the old `Dispose() => _ = DisposeCoreAsync()` disposed the
            // channel while background work was still touching it).
            // Task.Run FIRST: awaiting on the main thread captures the Unity
            // sync context, which is blocked in .Wait — deadlock until the
            // timeout (the EditMode 14s repro).
            try
            {
                Task.Run(() => DisposeCoreAsync().AsTask())
                    .Wait(TimeSpan.FromSeconds(4));
            }
            catch
            {
                // Shutdown must never throw into teardown.
            }
        }
    }
}
