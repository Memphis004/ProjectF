using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using _Script.Action;
using _Script.Data;
using _Script.State;
using LibplanetUnity;
using LibplanetUnity.Action;
using Libplanet;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Libplanet.Blockchain.Renderers;
using Libplanet.Action;

namespace _Script
{
    public class Game : MonoBehaviour
    {
        private const float TxProcessInterval = 3.0f;
        private const float AutoClickInterval = 0.2f;

        public Text timerText;
        public Text countText;
        public Text addressText;
        public Text rankingText;
        public Text syncText;
        public Image syncStatusDot;
        public Click click;
        public ScrollRect rankingBoard;
        public RankingRow rankingRow;
        private float _time;
        private bool _autoClick;
        private float _autoClickTimer;
        private long _totalCount = 0;
        private Table<Level> _levelTable;
        private Dictionary<Address, int> _attacks = new Dictionary<Address, int>();
        private const float SyncStatusInterval = 1.0f;
        private float _syncStatusTimer;
        private bool _signedIn;

        private static readonly Color SyncColorRed = new Color(0.95f, 0.36f, 0.36f);
        private static readonly Color SyncColorAmber = new Color(1f, 0.76f, 0.25f);
        private static readonly Color SyncColorGreen = new Color(0.45f, 0.85f, 0.4f);
        private static readonly Color SyncColorGray = new Color(0.85f, 0.85f, 0.85f);
        private const string SyncStateStarting = "STARTING";
        private const string SyncStateRed = "RED";
        private const string SyncStateAmber = "AMBER";
        private const string SyncStateGreen = "GREEN";
        private string _lastSyncState = SyncStateStarting;
        private float _lastPeerSeenTime = -999f;
        private const float PeerGracePeriod = 15f;

        public class CountUpdated : UnityEvent<long>
        {
        }

        public class RankUpdated: UnityEvent<RankingState>
        {
        }

        public static CountUpdated OnCountUpdated = new CountUpdated();

        public static RankUpdated OnRankUpdated = new RankUpdated();

        private void Awake()
        {
            Screen.SetResolution(1024, 768, FullScreenMode.Windowed);
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            _autoClick = System.Environment.GetCommandLineArgs().Contains("--auto-click");

            // Agent initialization is deferred: SignInController calls back into
            // OnSignInCompleted once the player has signed in with a private key.
            SignInController.RegisterCallback(OnSignInCompleted);
            if (Agent.HasPendingPrivateKey || SignInController.AlreadySignedIn)
            {
                OnSignInCompleted();
            }
        }

        private void OnSignInCompleted()
        {
            _signedIn = true;

            Agent.Initialize(
                new[]
                {
                    new AnonymousActionRenderer<PolymorphicAction<ActionBase>>()
                    {
                        ActionRenderer = (action, ctx, nextStates) =>
                        {
                            // Renders only when the count has updated.
                            if (nextStates.GetState(ctx.Signer) is Bencodex.Types.Integer nextCount)
                            {
                                Agent.instance.RunOnMainThread(() =>
                                {
                                    OnCountUpdated.Invoke(nextCount);
                                });
                            }

                            // Renders only when the ranking has changed.
                            if (nextStates.GetState(RankingState.Address) is Bencodex.Types.Dictionary rawRank)
                            {
                                var rankingState = new RankingState(rawRank);
                                Agent.instance.RunOnMainThread(() =>
                                {
                                    OnRankUpdated.Invoke(rankingState);
                                });
                            }
                        }
                    }
                }
            );
            var agent = Agent.instance;
            var hex = agent.Address.ToHex().Substring(0, 4);
            addressText.text = $"My Address: {hex}";

            _time = TxProcessInterval;
            SetTimer(_time);

            _levelTable = new Table<Level>();
            _levelTable.Load(Resources.Load<TextAsset>("level").text);

            OnCountUpdated.AddListener(UpdateTotalCount);
            OnRankUpdated.AddListener(rs =>
            {
                StartCoroutine(UpdateRankingBoard(rs));
            });

            var initialCount = agent.GetState(Agent.instance.Address);
            // Poll the chain tip / peer count once a second so the player can see
            // the sync status at a glance.
            _syncStatusTimer = SyncStatusInterval;
            if (!ReferenceEquals(syncText, null))
            {
                syncText.text = "Sync: starting...";
                syncText.color = SyncColorGray;
            }
            if (!ReferenceEquals(syncStatusDot, null))
            {
                syncStatusDot.color = SyncColorGray;
            }

            var initialRanking = agent.GetState(RankingState.Address);
            if (initialCount is Bencodex.Types.Integer count)
            {
                OnCountUpdated.Invoke(count);
            }

            if (initialRanking is Bencodex.Types.Dictionary bdict)
            {
                OnRankUpdated.Invoke(new RankingState(bdict));
            }
        }

        private void UpdateSyncStatus()
        {
            if (!_signedIn || ReferenceEquals(syncText, null))
            {
                return;
            }

            var agent = Agent.instance;
            var tip = agent.TipIndex;
            var peers = agent.PeerCount;
            var role = agent.IsMiner ? "miner" : "peer";
            var net = agent.IsSwarmRunning ? $", {peers} peer{(peers == 1 ? "" : "s")}" : "";
            var tipStr = tip < 0 ? "-" : tip.ToString();
            syncText.text = $"Block #{tipStr} ({role}{net})";

            // Libplanet's PeerCount flaps while the routing table refreshes, so a
            // raw peers==0 check makes the indicator blink.  Remember the last time
            // we saw any peer and stay "connected" for a grace period.
            if (peers > 0)
            {
                _lastPeerSeenTime = Time.time;
            }
            var peerRecentlySeen = (Time.time - _lastPeerSeenTime) <= PeerGracePeriod;

            // Green: a peer was seen within the grace period — syncing is possible.
            // Amber: the swarm is up but no peer has been seen for a while (a solo
            //        seed node sits here until another instance dials in — not an
            //        error).
            // Red: the swarm is not running or the chain is not ready — sync cannot
            //      happen at all.
            string state;
            Color stateColor;
            if (!agent.IsSwarmRunning || tip < 0)
            {
                state = SyncStateRed;
                stateColor = SyncColorRed;
            }
            else if (!peerRecentlySeen)
            {
                state = SyncStateAmber;
                stateColor = SyncColorAmber;
            }
            else
            {
                state = SyncStateGreen;
                stateColor = SyncColorGreen;
            }

            syncText.color = stateColor;
            if (!ReferenceEquals(syncStatusDot, null))
            {
                syncStatusDot.color = stateColor;
            }

            if (state != _lastSyncState)
            {
                Debug.LogFormat(
                    "[SyncIndicator] {0} (tip: {1}, peers: {2}, role: {3})",
                    state, tipStr, peers, role);
                _lastSyncState = state;
            }
        }

        private void SetTimer(float time)
        {
            timerText.text = $"Remain Time: {Mathf.Ceil(time).ToString(CultureInfo.CurrentCulture)} sec";
        }

        private void ResetTimer()
        {
            SetTimer(0);
            click.ResetCount();
        }

        private void FixedUpdate()
        {
            if (_autoClick)
            {
                _autoClickTimer -= Time.deltaTime;
                if (_autoClickTimer <= 0f)
                {
                    _autoClickTimer = AutoClickInterval;
                    click.Plus();
                }
            }

            _syncStatusTimer -= Time.deltaTime;
            if (_signedIn && _syncStatusTimer <= 0f)
            {
                _syncStatusTimer = SyncStatusInterval;
                UpdateSyncStatus();
            }

            if (_time > 0)
            {
                _time -= Time.deltaTime;
                SetTimer(_time);
            }
            else
            {
                _time = TxProcessInterval;
                var actions = new List<ActionBase>();
                if (click.count > 0)
                {
                    var action = new AddCount(click.count);
                    actions.Add(action);
                }

                actions.AddRange(_attacks.Select(pair => new SubCount(pair.Key, pair.Value)));
                if (actions.Any())
                {
                    Agent.instance.MakeTransaction(actions);
                }
                _attacks = new Dictionary<Address, int>();

                ResetTimer();
            }
        }

        private void UpdateTotalCount(long count)
        {
            _totalCount = count;
            var selected = _levelTable.Values.FirstOrDefault(i => i.exp > _totalCount) ?? _levelTable.Values.Last();
            click.Set(selected.id);
            countText.text = $"Total Count: {_totalCount.ToString()}";
        }

        private IEnumerator UpdateRankingBoard(RankingState rankingState)
        {
            foreach (Transform child in rankingBoard.content.transform)
            {
                Destroy(child.gameObject);
            }
            yield return new WaitForEndOfFrame();

            rankingRow.gameObject.SetActive(true);
            var ranking = rankingState.GetRanking().ToList();
            for (var i = 0; i < ranking.Count; i++)
            {
                var rankingInfo = ranking[i];
                var go = Instantiate(rankingRow, rankingBoard.content.transform);
                var bg = go.GetComponent<Image>();
                if (i % 2 == 1)
                {
                    bg.enabled = false;
                }
                var row = go.GetComponent<RankingRow>();
                var rank = i + 1;
                row.Set(rank, rankingInfo);
                if (rankingInfo.Address == Agent.instance.Address)
                {
                    rankingText.text = $"My Ranking: {rank}";
                }
            }

            rankingRow.gameObject.SetActive(false);
            yield return null;
        }

        public void Attack(RankingRow row)
        {
            var address = row.address;

            if (_attacks.TryGetValue(address, out _))
            {
                _attacks[address] += 1;
            }
            else
            {
                _attacks[address] = 0;
            }
        }
    }
}
