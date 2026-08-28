using System.Collections.Generic;
using Unity.Netcode;

namespace NetworkSync.Core
{
    /// <summary>Synchronizes state over the network via encode/send/decode/receive.</summary>
    [GenerateSerializationForGenericParameter(1)]
    public abstract class NetworkStateSync<TState, TPayload> : NetworkBehaviour
        where TState : struct
        where TPayload : struct, INetworkSerializable
    {
        /// <summary>Payloads received via RPC before synchronization completes, in arrival order.</summary>
        private readonly List<TPayload> _initialPendingPayloads = new List<TPayload>();

        /// <summary>Synchronization payload kept until synchronization completes, so it can be decoded again.</summary>
        private TPayload? _initialSynchronizationPayload;

        /// <summary>Whether initial synchronization is complete.</summary>
        public bool IsSynchronizationComplete { get; private set; } = false;

        /// <summary>Last state that was successfully synced (sent or received).</summary>
        protected TState? LastSyncedState { get; set; }

        /// <summary>Client id that has authority over this object. Defaults to the network object owner.</summary>
        public virtual ulong AuthoritativeClientId => OwnerClientId;

        /// <summary>Whether the given client has authority over this object.</summary>
        public bool IsAuthority(ulong clientId) => clientId == AuthoritativeClientId;

        /// <summary>Whether the local client has authority.</summary>
        public bool IsLocalAuthority => IsAuthority(NetworkManager.LocalClientId);

        /// <summary>Returns the current authoritative state.</summary>
        public abstract TState GetState();

        /// <summary>Encodes a state into a network payload.</summary>
        /// <param name="forSynchronize">True when encoding for <see cref="OnSynchronize"/></param>
        protected abstract TPayload EncodeState(in TState current, bool forSynchronize = false);

        /// <summary>Decodes a network payload into a state.</summary>
        protected abstract TState DecodePayload(in TPayload payload);

        /// <summary>Applies a state.</summary>
        public virtual void SetState(in TState state)
        {
        }

        /// <summary>Called when a new state is received. Default applies via <see cref="SetState"/>.</summary>
        protected virtual void OnStateReceived(in TState state)
        {
            SetState(state);
        }

        /// <summary>Called after initial synchronization is complete.</summary>
        protected virtual void OnSynchronizationComplete()
        {
        }

        private void InternalOnSynchronizationComplete()
        {
            if (IsSynchronizationComplete) return;

            IsSynchronizationComplete = true;

            // Decode again now that the session is fully synchronized.
            if (_initialSynchronizationPayload.HasValue)
            {
                LastSyncedState = DecodePayload(_initialSynchronizationPayload.Value);
                _initialSynchronizationPayload = null;
            }

            for (int i = 0; i < _initialPendingPayloads.Count; i++)
            {
                TState state = DecodePayload(_initialPendingPayloads[i]);
                LastSyncedState = state;
                OnStateReceived(state);
            }

            _initialPendingPayloads.Clear();

            OnSynchronizationComplete();
        }

        /// <summary>Whether the payload should be sent. Defaults to true.</summary>
        protected virtual bool ShouldSendPayload(in TPayload payload)
        {
            return true;
        }

        /// <summary>Writes or reads late-join sync data from <see cref="LastSyncedState"/>.</summary>
        protected sealed override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
        {
            bool hasState = false;
            TPayload payload = default;

            if (serializer.IsWriter)
            {
                if (LastSyncedState.HasValue)
                {
                    hasState = true;
                    payload = EncodeState(LastSyncedState.Value, forSynchronize: true);
                }
            }
            serializer.SerializeValue(ref hasState);
            if (hasState)
            {
                serializer.SerializeNetworkSerializable(ref payload);
            }
            
            if (serializer.IsReader)
            {
                if (hasState)
                {
                    LastSyncedState = DecodePayload(payload);
                    _initialSynchronizationPayload = payload;
                }
            }

            base.OnSynchronize(ref serializer);
        }

        public sealed override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            OnSpawned();
            if (NetworkSyncManager.Instance.IsSessionSynchronized)
            {
                InternalOnSynchronizationComplete();
            }
        }

        public sealed override void OnNetworkDespawn()
        {
            OnDespawning();

            IsSynchronizationComplete = false;
            _initialSynchronizationPayload = null;
            _initialPendingPayloads.Clear();
            LastSyncedState = null;

            base.OnNetworkDespawn();
        }

        /// <summary>Called during network spawn.</summary>
        protected virtual void OnSpawned()
        {
        }

        /// <summary>Called during network despawn.</summary>
        protected virtual void OnDespawning()
        {
        }

        protected override void OnNetworkSessionSynchronized()
        {
            InternalOnSynchronizationComplete();
            base.OnNetworkSessionSynchronized();
        }

        /// <summary>Encodes and sends the current state.</summary>
        public void SendState()
        {
            if (!IsLocalAuthority) return;

            TState current = GetState();
            TPayload payload = EncodeState(current);

            if (!ShouldSendPayload(payload)) return;

            if (IsServer)
            {
                BroadcastStateRpc(payload, RpcTarget.NotServer);
            }
            else
            {
                SubmitStateToServerRpc(payload);
            }

            LastSyncedState = DecodePayload(payload);
        }

        /// <summary>Server validates an authority payload before relay. Mutate payload if needed; return false to drop.</summary>
        protected virtual bool ServerValidatePayload(ref TPayload payload, ulong senderClientId)
        {
            return true;
        }

        /// <summary>Client authority submits state to the server for relay.</summary>
        [Rpc(SendTo.Server)]
        private void SubmitStateToServerRpc(TPayload payload, RpcParams rpcParams = default)
        {
            if (!IsAuthority(rpcParams.Receive.SenderClientId)) return;

            if (!ServerValidatePayload(ref payload, rpcParams.Receive.SenderClientId)) return;

            LastSyncedState = DecodePayload(payload);
            BroadcastStateRpc(payload, RpcTarget.Not(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        /// <summary>Applies a received payload and calls <see cref="OnStateReceived"/>.</summary>
        [Rpc(SendTo.SpecifiedInParams)]
        private void BroadcastStateRpc(TPayload payload, RpcParams rpcParams = default)
        {
            if (!IsSynchronizationComplete)
            {
                _initialPendingPayloads.Add(payload);
                return;
            }

            TState state = DecodePayload(payload);
            LastSyncedState = state;
            OnStateReceived(state);
        }
    }
}
