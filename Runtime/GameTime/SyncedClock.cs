using System;

namespace NetworkSync.GameTime
{
    /// <summary>
    /// Local clock that advances each frame and reconciles drift from remote time samples.
    /// Linear mode (period 0): monotonic unbounded time. Periodic mode (period &gt; 0): time stays in [0, period).
    /// Networking is the caller's responsibility; this type only stores time and applies correction.
    /// </summary>
    public sealed class SyncedClock
    {
        private readonly double _period;

        private double _now;
        private double _timeError;

        /// <summary>
        /// Current clock time. Linear mode: elapsed seconds. Periodic mode: position in [0, <see cref="Period"/>).
        /// </summary>
        public double Now => _now;

        /// <summary>
        /// Remaining drift to dissipate. Positive means local time is behind the last remote sample.
        /// </summary>
        public double TimeError => _timeError;

        /// <summary>When |<see cref="TimeError"/>| exceeds this, the full error is applied instantly. In clock units.</summary>
        public double SnapThreshold { get; private set; }

        /// <summary>Soft correction speed in clock-units resolved per real second.</summary>
        public double CorrectionSpeed { get; private set; }

        /// <summary>Zero for linear clocks; one full cycle length for periodic clocks (e.g. 1 for normalized day time).</summary>
        public double Period => _period;

        public bool IsPeriodic => _period > 0d;

        public SyncedClock(double snapThreshold = 0.2d, double correctionSpeed = 0.01d, double period = 0d)
        {
            SnapThreshold = snapThreshold;
            CorrectionSpeed = correctionSpeed;
            _period = period;
        }

        /// <summary>Advances the clock by <paramref name="deltaTime"/> and applies pending soft correction.</summary>
        public void Advance(double deltaTime)
        {
            _now += deltaTime;
            _now = Wrap(_now, _period);
            ApplyCorrectionStep(deltaTime);
            _now = Wrap(_now, _period);
        }

        /// <summary>Queues soft correction toward the authoritative clock time.</summary>
        public void Reconcile(double authoritativeNow)
        {
            if (IsPeriodic)
            {
                _timeError = CircularError(Wrap(authoritativeNow, _period), _now, _period);
                return;
            }

            _timeError = authoritativeNow - _now;
        }

        /// <summary>Hard-sets the clock and clears any pending correction.</summary>
        public void SnapTo(double newNow)
        {
            _now = Wrap(newNow, _period);
            _timeError = 0d;
        }

        private void ApplyCorrectionStep(double deltaTime)
        {
            if (_timeError == 0d) return;

            double absRemaining = Math.Abs(_timeError);

            if (absRemaining > SnapThreshold)
            {
                _now += _timeError;
                _timeError = 0d;
                return;
            }

            double step = deltaTime * CorrectionSpeed;

            if (step >= absRemaining)
            {
                _now += _timeError;
                _timeError = 0d;
                return;
            }

            if (_timeError > 0d)
            {
                _now += step;
                _timeError -= step;
            }
            else
            {
                _now -= step;
                _timeError += step;
            }
        }

        private static double Wrap(double time, double period)
        {
            if (period <= 0d) return time;

            time %= period;
            if (time < 0d) time += period;
            return time;
        }

        private static double CircularError(double authoritativeNow, double localNow, double period)
        {
            double diff = authoritativeNow - localNow;
            double halfPeriod = period * 0.5d;
            if (diff > halfPeriod) diff -= period;
            if (diff < -halfPeriod) diff += period;
            return diff;
        }
    }
}
