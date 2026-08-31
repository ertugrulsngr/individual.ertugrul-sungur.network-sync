using Unity.Netcode;
using UnityEngine;

namespace NetworkSync.GameTime
{
    /// <summary>
    /// Networked scaled game clock. After spawn, advances each EarlyUpdate by <see cref="Time.deltaTime"/>.
    /// Server periodically syncs samples; clients reconcile via RPC and late-join <see cref="NetworkBehaviour.OnSynchronize"/>.
    /// </summary>
    public class NetworkGameTime : NetworkBehaviour, INetworkUpdateSystem
    {
        public static NetworkGameTime Instance { get; private set; }

        private static readonly SyncedClock s_clock = new SyncedClock();

        /// <summary>Monotonic scaled seconds since session start.</summary>
        public static double Seconds => s_clock.Now;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
            s_clock.SnapTo(0d);
        }

        [Tooltip("How often the server sends a time sync RPC to clients.")]
        [SerializeField] private float _timeSyncIntervalSeconds = 1f;

        private double _nextTimeSyncAtUnscaled;

        private void Awake()
        {
            Debug.Assert(Instance == null, $"[{nameof(NetworkGameTime)}] Instance already exists.");
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            s_clock.SnapTo(0d);
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _nextTimeSyncAtUnscaled = Time.unscaledTimeAsDouble;
            this.RegisterNetworkUpdate(NetworkUpdateStage.EarlyUpdate);
        }

        public override void OnNetworkDespawn()
        {
            this.UnregisterNetworkUpdate(NetworkUpdateStage.EarlyUpdate);
            base.OnNetworkDespawn();
        }

        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            double seconds = 0d;
            if (serializer.IsWriter)
            {
                seconds = s_clock.Now;
            }

            serializer.SerializeValue(ref seconds);

            if (serializer.IsReader)
            {
                s_clock.SnapTo(seconds);
            }

            base.OnSynchronize(ref serializer);
        }

        public void NetworkUpdate(NetworkUpdateStage updateStage)
        {
            if (updateStage != NetworkUpdateStage.EarlyUpdate) return;

            s_clock.Advance(Time.deltaTime);

            if (IsServer)
            {
                HandlePeriodicTimeSync();
            }
        }

        private void HandlePeriodicTimeSync()
        {
            double now = Time.unscaledTimeAsDouble;
            if (now < _nextTimeSyncAtUnscaled) return;

            _nextTimeSyncAtUnscaled = now + _timeSyncIntervalSeconds;
            BroadcastTimeSyncRpc(s_clock.Now);
        }

        [Rpc(SendTo.NotServer)]
        private void BroadcastTimeSyncRpc(double serverSeconds)
        {
            s_clock.Reconcile(serverSeconds);
        }
    }
}
