using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;
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

        private GrpcChannel? _channel;
        private IPlayerHub? _hub;

        public IPlayerHub? Hub => _hub;

        public bool IsConnected => _hub is { };

        public PresenceConnection(NetworkSettings settings, PresenceReceiver receiver)
        {
            _settings = settings;
            _receiver = receiver;
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

                _hub = await StreamingHubClient.ConnectAsync<IPlayerHub, IPlayerHubReceiver>(
                    _channel, _receiver, cancellationToken: ct);

                // Monitor disconnection (API: WaitForDisconnectAsync returns
                // Task<DisconnectionReason>). Ditched on the threadpool: a
                // dead hub must never touch main-thread state directly — the
                // StatusChanged event drives the HUD dot instead.
                _ = Task.Run(async () =>
                {
                    await _hub.WaitForDisconnectAsync();
                    _hub = null;
                    Debug.LogWarning("[presence] disconnected from hub — single-player visual mode.");
                });

                await _hub.JoinAsync(new PlayerJoinRequest
                {
                    PlayerName = playerName,
                    SceneId = sceneId,
                    X = x,
                    Y = y,
                });

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

        private async UniTask DisposeCoreAsync()
        {
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
            }
        }

        public void Dispose()
        {
            _ = DisposeCoreAsync();
        }
    }
}
