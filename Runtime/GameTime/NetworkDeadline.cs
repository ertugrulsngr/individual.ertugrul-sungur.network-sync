using System;
using Unity.Netcode;
using UnityEngine;

namespace NetworkSync.GameTime
{
    public delegate void NetworkDeadlineValueChangedHandler(NetworkDeadline deadline, double previousValue, double currentValue);

    /// <summary>
    /// Absolute deadline on the <see cref="NetworkGameTime"/> axis.
    /// Only <see cref="Value"/> is networked; <see cref="RemainingSeconds"/> is derived locally.
    /// </summary>
    [Serializable]
    public class NetworkDeadline : NetworkVariableBase
    {
        public const double InactiveValue = double.PositiveInfinity;

        private static double NowSeconds => NetworkGameTime.Seconds;
        private double _value;

        public NetworkDeadline(
            double value = InactiveValue,
            NetworkVariableReadPermission readPerm = DefaultReadPerm,
            NetworkVariableWritePermission writePerm = DefaultWritePerm)
            : base(readPerm, writePerm)
        {
            _value = value;
        }

        public event NetworkDeadlineValueChangedHandler ValueChanged;

        /// <summary>Absolute time on the <see cref="NetworkGameTime"/> axis; <see cref="InactiveValue"/> when off.</summary>
        public double Value
        {
            get => _value;
            set
            {
                if (!CanLocalClientWrite()) return;

                double previous = _value;
                if (previous == value) return;

                _value = value;
                SetDirty(true);
                ValueChanged?.Invoke(this, previous, _value);
            }
        }

        public bool IsActive => !double.IsPositiveInfinity(_value);

        public bool IsExpired => IsActive && NowSeconds >= _value;

        public double RemainingSeconds
        {
            get
            {
                if (!IsActive) return 0d;

                double remaining = _value - NowSeconds;
                return remaining > 0d ? remaining : 0d;
            }
        }

        public void SetFromDuration(double durationSeconds)
        {
            if (durationSeconds < 0d) durationSeconds = 0d;
            Value = NowSeconds + durationSeconds;
        }

        public void Extend(double additionalSeconds)
        {
            if (additionalSeconds < 0d) additionalSeconds = 0d;
            Value = Value + additionalSeconds;
        }

        public void SetInactive()
        {
            Value = InactiveValue;
        }

        public override void WriteField(FastBufferWriter writer)
        {
            writer.WriteValueSafe(_value);
        }

        public override void ReadField(FastBufferReader reader)
        {
            double previous = _value;
            reader.ReadValueSafe(out _value);
            if (previous != _value)
            {
                ValueChanged?.Invoke(this, previous, _value);
            }
        }

        public override void WriteDelta(FastBufferWriter writer)
        {
            WriteField(writer);
        }

        public override void ReadDelta(FastBufferReader reader, bool keepDirtyDelta)
        {
            ReadField(reader);
        }

        public bool CanLocalClientWrite()
        {
            NetworkManager networkManager = GetBehaviour()?.NetworkManager;
            if (networkManager == null) return true;
            return CanClientWrite(networkManager.LocalClientId);
        }
    }
}
