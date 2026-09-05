using System;
using Unity.Netcode;
using UnityEngine;

namespace NetworkSync.GameTime
{
    /// <summary>Lifecycle intent for <see cref="NetworkCountdown"/> state RPCs.</summary>
    public enum CountdownSyncType : byte
    {
        None = 0,
        Started = 1,
        Stopped = 2,
        Paused = 3,
        Finished = 4,
        Extended = 5,
        Resumed = 6,
    }

    public sealed class NetworkCountdown : NetworkBehaviour, INetworkUpdateSystem
    {
        private bool _autoAdvance = true;
        private float _syncIntervalSeconds = 1f;
        private float _timeScale = 1f;
        private bool _isRunning;
        private double _initialRemainingSeconds;

        private readonly SyncedClock _clock = new SyncedClock();
        private double _nextSyncAtUnscaled;

        /// <summary>Occurs when the countdown starts or restarts with a positive remaining duration.</summary>
        public event Action<NetworkCountdown> Started;

        /// <summary>Occurs when the countdown is halted manually via <see cref="Stop"/>.</summary>
        public event Action<NetworkCountdown> Stopped;

        /// <summary>Occurs when the countdown is paused via <see cref="Pause"/>.</summary>
        public event Action<NetworkCountdown> Paused;

        /// <summary>Occurs when the countdown resumes via <see cref="Resume"/>.</summary>
        public event Action<NetworkCountdown> Resumed;

        /// <summary>
        /// Occurs when the countdown ends on this peer: after <see cref="Advance"/> or <see cref="Extend"/>
        /// reaches zero on the server, or when a <see cref="CountdownSyncType.Finished"/> sync message is applied.
        /// </summary>
        public event Action<NetworkCountdown> Finished;

        /// <summary>
        /// Occurs when remaining time crosses from above zero to zero or below while the countdown was running.
        /// Not raised again if a <see cref="CountdownSyncType.Finished"/> sync message arrives after a local zero cross.
        /// </summary>
        public event Action<NetworkCountdown> LocalFinished;

        /// <summary>Occurs after each <see cref="Advance"/> while the countdown is running.</summary>
        public event Action<NetworkCountdown> Tick;

        /// <summary>Gets whether the countdown is actively advancing.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>Gets the multiplier applied to elapsed time each tick.</summary>
        public float TimeScale => _timeScale;

        /// <summary>Gets the remaining duration in seconds.</summary>
        public double RemainingSeconds => _clock.Now;

        private void Awake()
        {
            ApplyState(_initialRemainingSeconds, _timeScale, _isRunning);
            if (RemainingSeconds > 0 && _isRunning)
            {
                Started?.Invoke(this);
            }
        }

        protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            double remainingSeconds = 0d;
            float timeScale = 0f;
            bool isRunning = false;

            if (serializer.IsWriter)
            {
                remainingSeconds = _clock.Now;
                timeScale = _timeScale;
                isRunning = _isRunning;
            }

            serializer.SerializeValue(ref remainingSeconds);
            serializer.SerializeValue(ref timeScale);
            serializer.SerializeValue(ref isRunning);

            if (serializer.IsReader)
            {
                ApplyState(remainingSeconds, timeScale, isRunning);
                if (RemainingSeconds > 0 && isRunning)
                {
                    Started?.Invoke(this);
                }
            }

            base.OnSynchronize(ref serializer);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ScheduleNextSync();
            this.RegisterNetworkUpdate(NetworkUpdateStage.EarlyUpdate);
        }

        public override void OnNetworkDespawn()
        {
            this.UnregisterNetworkUpdate(NetworkUpdateStage.EarlyUpdate);
            base.OnNetworkDespawn();
        }

        public void NetworkUpdate(NetworkUpdateStage updateStage)
        {
            if (updateStage != NetworkUpdateStage.EarlyUpdate) return;
            if (!IsSpawned) return;

            if (_autoAdvance)
            {
                Advance(Time.deltaTime);
            }

            if (IsServer)
            {
                HandlePeriodicSync();
            }
        }

        private void ScheduleNextSync(float delaySeconds = 0f)
        {
            _nextSyncAtUnscaled = Time.unscaledTimeAsDouble + delaySeconds;
        }

        private void HandlePeriodicSync()
        {
            if (!_isRunning) return;
            if (_syncIntervalSeconds <= 0f) return;

            double now = Time.unscaledTimeAsDouble;
            if (now < _nextSyncAtUnscaled) return;

            ScheduleNextSync(_syncIntervalSeconds);
            SendRemainingSecondsClientRpc(_clock.Now);
        }

        private void ApplyState(double remainingSeconds, float timeScale, bool isRunning)
        {
            _isRunning = isRunning;
            _timeScale = timeScale;
            _clock.SnapTo(remainingSeconds);
        }

        [Rpc(SendTo.NotServer)]
        private void SendStateClientRpc(CountdownSyncType syncType, double remainingSeconds, float timeScale, bool isRunning)
        {
            switch (syncType)
            {
                case CountdownSyncType.None:
                    ApplyState(remainingSeconds, timeScale, isRunning);
                    break;
                case CountdownSyncType.Started:
                    InternalStart(remainingSeconds, timeScale);
                    break;
                case CountdownSyncType.Stopped:
                    InternalStop(timeScale);
                    break;
                case CountdownSyncType.Paused:
                    InternalPause(remainingSeconds, timeScale);
                    break;
                case CountdownSyncType.Finished:
                    InternalFinished();
                    break;
                case CountdownSyncType.Extended:
                    InternalExtend(remainingSeconds, timeScale, isRunning);
                    break;
                case CountdownSyncType.Resumed:
                    InternalResume(remainingSeconds, timeScale);
                    break;
            }
        }

        [Rpc(SendTo.NotServer)]
        private void SendRemainingSecondsClientRpc(double remainingSeconds)
        {
            _clock.Reconcile(remainingSeconds);
        }

        private void InternalStart(double remainingSeconds, float timeScale)
        {
            ApplyState(remainingSeconds, timeScale, true);
            Started?.Invoke(this);
        }

        private void InternalPause(double remainingSeconds, float timeScale)
        {
            ApplyState(remainingSeconds, timeScale, false);
            Paused?.Invoke(this);
        }

        private void InternalResume(double remainingSeconds, float timeScale)
        {
            ApplyState(remainingSeconds, timeScale, true);
            Resumed?.Invoke(this);
        }

        private void InternalStop(float timeScale)
        {
            ApplyState(0d, timeScale, false);
            Stopped?.Invoke(this);
        }

        private void InternalFinished()
        {
            bool wasRunning = _isRunning;
            _isRunning = false;
            _clock.SnapTo(0d);

            // LocalFinished: once per zero-crossing while we were still running.
            // Skips a second invoke when the Finished RPC arrives after Advance already crossed zero.
            if (wasRunning)
            {
                LocalFinished?.Invoke(this);
            }

            if (IsServer)
            {
                SendStateClientRpc(CountdownSyncType.Finished, 0d, _timeScale, false);
            }
            Finished?.Invoke(this);
        }

        private void InternalExtend(double remainingSeconds, float timeScale, bool isRunning)
        {
            ApplyState(remainingSeconds, timeScale, isRunning);
        }

        /// <summary>
        /// Advances remaining time by <paramref name="deltaTime"/> scaled by <see cref="TimeScale"/>.
        /// On a zero cross, ends the countdown and raises <see cref="LocalFinished"/> and <see cref="Finished"/>.
        /// </summary>
        /// <param name="deltaTime">Elapsed seconds since the last advance.</param>
        public void Advance(float deltaTime)
        {
            if (!_isRunning) return;

            double before = _clock.Now;
            _clock.Advance(-deltaTime * _timeScale);
            if (_clock.Now < 0d)
            {
                _clock.SnapTo(0d);
            }

            // Edge trigger: fire finish logic once per approach to zero, not every frame at zero.
            if (before > 0d && _clock.Now <= 0d)
            {
                InternalFinished();
            }

            Tick?.Invoke(this);
        }

        /// <summary>
        /// Starts or restarts the countdown. Server only.
        /// </summary>
        /// <param name="remainingSeconds">Initial remaining duration in seconds.</param>
        /// <param name="timeScale">Time multiplier for <see cref="Advance"/>.</param>
        public void StartCountdown(double remainingSeconds, float timeScale = 1f)
        {
            if (!IsServer) return;

            InternalStart(remainingSeconds, timeScale);
            SendStateClientRpc(CountdownSyncType.Started, remainingSeconds, timeScale, true);
            ScheduleNextSync(_syncIntervalSeconds);
        }

        /// <summary>Pauses the countdown at the current remaining time. Server only.</summary>
        public void Pause()
        {
            if (!IsServer) return;

            InternalPause(_clock.Now, _timeScale);
            SendStateClientRpc(CountdownSyncType.Paused, _clock.Now, _timeScale, false);
        }

        /// <summary>Resumes a paused countdown from the current remaining time. Server only.</summary>
        public void Resume()
        {
            if (!IsServer) return;
            if (_isRunning) return;
            if (RemainingSeconds <= 0d) return;

            InternalResume(_clock.Now, _timeScale);
            SendStateClientRpc(CountdownSyncType.Resumed, _clock.Now, _timeScale, true);
            ScheduleNextSync(_syncIntervalSeconds);
        }

        /// <summary>
        /// Halts the countdown and clears remaining time. Server only.
        /// Raises <see cref="Stopped"/>, not <see cref="Finished"/>.
        /// </summary>
        public void Stop()
        {
            if (!IsServer) return;

            InternalStop(_timeScale);
            SendStateClientRpc(CountdownSyncType.Stopped, 0d, _timeScale, false);
        }

        /// <summary>
        /// Adjusts remaining time by a delta (positive or negative). Server only.
        /// When not running and remaining is zero, starts a new countdown.
        /// When paused (remaining above zero), extends without resuming.
        /// If the result is zero or below, ends the countdown via <see cref="Finished"/>.
        /// </summary>
        /// <param name="seconds">Seconds to add; negative values shorten the countdown.</param>
        public void Extend(double seconds)
        {
            if (!IsServer) return;
            if (seconds == 0d) return;

            double newRemaining = RemainingSeconds + seconds;
            // If the new remaining time is zero or below, finish the countdown.
            if (newRemaining <= 0d)
            {
                if (_isRunning || RemainingSeconds > 0d)
                {
                    InternalFinished();
                }
                return;
            }

            // If the countdown is not running and the remaining time is zero, start a new countdown.
            if (!_isRunning && RemainingSeconds <= 0d)
            {
                StartCountdown(newRemaining, _timeScale);
                return;
            }

            // If paused or running, extend the countdown.
            InternalExtend(newRemaining, _timeScale, _isRunning);
            SendStateClientRpc(CountdownSyncType.Extended, newRemaining, _timeScale, _isRunning);
        }

        /// <summary>Updates <see cref="TimeScale"/> on all clients. Server only.</summary>
        /// <param name="timeScale">New time multiplier.</param>
        public void SetTimeScale(float timeScale)
        {
            if (!IsServer) return;

            _timeScale = timeScale;
            SendStateClientRpc(CountdownSyncType.None, _clock.Now, timeScale, _isRunning);
        }
    }
}
