using System;

namespace TravelAgent.Core
{
    public static class SequenceRules
    {
        public static bool IsValid(string sequence)
        {
            if (sequence == null || sequence.Length != 4) return false;
            foreach (char symbol in sequence)
                if (symbol != 'A' && symbol != 'B') return false;
            return true;
        }

        public static bool Validate(string arm, string disarm, out string error)
        {
            if (!IsValid(arm) || !IsValid(disarm))
            {
                error = "Each sequence must contain exactly four A or B presses.";
                return false;
            }
            if (arm == disarm)
            {
                error = "Arm and disarm sequences must differ.";
                return false;
            }
            error = "";
            return true;
        }
    }

    /// <summary>
    /// Parses physical press/release cycles without blocking the alarm loop. Complete taps require
    /// the configured minimum hold. Overlapping buttons cancel the whole entry until both release.
    /// </summary>
    public sealed class ButtonSequenceParser
    {
        public event Action<string> Completed;
        public event Action<int> ProgressChanged;
        public event Action<string> Feedback;
        public double DebounceSeconds { get; set; } = 0.03;
        public double MaximumGapSeconds { get; set; } = 2.0;
        public int Progress { get { return partial.Length; } }
        public bool BothHeld { get { return heldA && heldB; } }
        public double BothHeldSeconds { get { return BothHeld ? now - bothSince : 0; } }

        private string partial = "";
        private bool heldA;
        private bool heldB;
        private bool waitForRelease;
        private double aSince;
        private double bSince;
        private double bothSince;
        private double now;
        private double lastTap;

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds < 0 || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            now += deltaSeconds;
            if (partial.Length > 0 && now - lastTap > MaximumGapSeconds)
            {
                Clear();
                Feedback?.Invoke("Button entry timed out.");
            }
        }

        public void Press(char symbol, bool down)
        {
            if (symbol != 'A' && symbol != 'B') throw new ArgumentException("Use button A or B.", nameof(symbol));
            bool previous = symbol == 'A' ? heldA : heldB;
            if (previous == down) return;
            double heldSince = symbol == 'A' ? aSince : bSince;
            if (symbol == 'A') { heldA = down; if (down) aSince = now; }
            else { heldB = down; if (down) bSince = now; }
            if (heldA && heldB)
            {
                bothSince = now;
                waitForRelease = true;
                Clear();
                Feedback?.Invoke("Overlapping buttons canceled the entry.");
                return;
            }
            if (waitForRelease)
            {
                if (!heldA && !heldB) waitForRelease = false;
                return;
            }
            if (down || now - heldSince + 0.000001 < DebounceSeconds) return;
            if (partial.Length > 0 && now - lastTap > MaximumGapSeconds) Clear();
            partial += symbol;
            lastTap = now;
            ProgressChanged?.Invoke(partial.Length);
            if (partial.Length == 4)
            {
                string complete = partial;
                Clear();
                Completed?.Invoke(complete);
            }
        }

        /// <summary>State transitions clear input and require any held button to be released.</summary>
        public void Reset()
        {
            Clear();
            waitForRelease = heldA || heldB;
        }

        private void Clear()
        {
            partial = "";
            ProgressChanged?.Invoke(0);
        }
    }
}
