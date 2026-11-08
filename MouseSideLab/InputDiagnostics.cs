using System;

namespace MouseSideLab
{
    public sealed class InputDiagnostics
    {
        private readonly long[] pressedAt = { -1, -1 };
        private readonly int[] shortPulses = { 0, 0 };
        public long Record(int button, bool down, long now)
        {
            int index = button - 1;
            if (down) { if (pressedAt[index] < 0) pressedAt[index] = now; return -1; }
            if (pressedAt[index] < 0) return -1;
            long duration = Math.Max(0, now - pressedAt[index]); pressedAt[index] = -1;
            shortPulses[index] = duration <= 5 ? shortPulses[index] + 1 : 0;
            return duration;
        }
        public bool SuspectedReleaseOnly(int button) { return shortPulses[button - 1] >= 3; }
        public void Reset() { pressedAt[0] = pressedAt[1] = -1; shortPulses[0] = shortPulses[1] = 0; }
    }
}
