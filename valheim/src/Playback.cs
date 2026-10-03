using System;

namespace Valcraft
{
    /// <summary>
    /// Jitter buffer for Minecraft's tick stream: decides which (fractional) tick to show now.
    ///
    /// Playback runs a little behind the newest tick, at Minecraft's measured tick rate (which can
    /// be under 20/s when it lags), with a small speed correction (±8%) that keeps the gap at a
    /// target size. That absorbs bursty delivery and can't drift into the newest sample. The
    /// target grows with measured arrival jitter. Pure logic (no Unity), so it can be unit tested.
    /// </summary>
    internal sealed class Playback
    {
        public const double TickSeconds = 0.05;
        private const double MinTarget = 1.5, MaxTarget = 4.0;
        private const double MaxRateChange = 0.08, Gain = 0.1, IntegralGain = 0.05;

        private bool _started, _haveArrival;
        private long _latestTick;
        private double _lastArrival;
        private double _pos;     // playback position, in ticks
        private double _bufAvg;  // smoothed (latest tick - playback position)
        private double _integral; // accumulated error, removes any leftover steady offset
        // Measured Minecraft speed relative to 20 ticks/s, from arrivals over a few seconds.
        private double _mcSpeed = 1.0;
        private long _windowTick;
        private double _windowStart;
        private const double SpeedWindow = 3.0;

        /// <summary>Recent worst deviation of arrival spacing from tick spacing, in ticks.</summary>
        public double Jitter { get; private set; }
        /// <summary>How far behind the newest tick we aim to play, in ticks.</summary>
        public double TargetTicks { get; private set; } = MinTarget;
        public double Rate { get; private set; } = 1.0;
        public double McSpeed => _mcSpeed;
        /// <summary>Frames where playback caught up with the newest tick and had to hold.</summary>
        public int Starved { get; private set; }
        public double Buffered => _latestTick - _pos;

        public void Reset()
        {
            _started = _haveArrival = false;
            _integral = 0;
            _mcSpeed = 1.0;
            Jitter = 0;
            TargetTicks = MinTarget;
            Rate = 1.0;
        }

        public void OnSample(long tick, double arrival)
        {
            if (_started && tick < _latestTick - 100)
            {
                Reset(); // Minecraft restarted its tick counter
            }
            else if (_started && tick <= _latestTick)
            {
                return; // duplicate or stale
            }
            if (_haveArrival)
            {
                double expected = (tick - _latestTick) * TickSeconds;
                double dev = Math.Abs(arrival - _lastArrival - expected) / TickSeconds;
                Jitter = Math.Max(dev, Jitter * 0.995);
            }
            if (!_haveArrival)
            {
                _windowTick = tick;
                _windowStart = arrival;
            }
            else if (arrival - _windowStart >= SpeedWindow)
            {
                double measured = (tick - _windowTick) * TickSeconds / (arrival - _windowStart);
                _mcSpeed += (Math.Max(0.5, Math.Min(1.5, measured)) - _mcSpeed) * 0.5;
                _windowTick = tick;
                _windowStart = arrival;
            }
            _latestTick = tick;
            _lastArrival = arrival;
            _haveArrival = true;
            // Buffered time is a sawtooth (full on arrival, -1 tick just before the next), so the
            // average must stay above ~0.5 tick plus jitter to never hit zero.
            TargetTicks = Math.Max(MinTarget, Math.Min(MaxTarget, 1.0 + Jitter * 1.2));
            if (!_started)
            {
                _pos = tick - TargetTicks;
                _bufAvg = TargetTicks;
                _started = true;
            }
        }

        /// <summary>Advance by <paramref name="dt"/> seconds; returns the tick position to show.</summary>
        public double Advance(double dt)
        {
            if (!_started) return 0;
            if (Buffered > TargetTicks + 6)
            {
                // Far behind (e.g. a hitch on our side): jump instead of fast-forwarding.
                _pos = _latestTick - TargetTicks;
                _bufAvg = TargetTicks;
            }
            _bufAvg += (Buffered - _bufAvg) * Math.Min(1.0, dt * 2.0);
            double err = _bufAvg - TargetTicks;
            _integral = Math.Max(-1.0, Math.Min(1.0, _integral + err * IntegralGain * dt));
            Rate = _mcSpeed * (1.0 + Math.Max(-MaxRateChange, Math.Min(MaxRateChange, err * Gain + _integral)));
            _pos += dt / TickSeconds * Rate;
            if (_pos > _latestTick)
            {
                _pos = _latestTick;
                Starved++;
            }
            return _pos;
        }
    }
}
