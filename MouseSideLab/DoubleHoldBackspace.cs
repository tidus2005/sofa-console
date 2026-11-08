using System;

namespace MouseSideLab
{
    public enum RepeatDecision { None, Cancelled, Backspace }

    // Evaluate only after all queued mouse packets have been processed. No action queue:
    // a delayed UI tick emits at most one key, and a queued release always wins.
    public sealed class DoubleHoldBackspace
    {
        public const int IntervalMs = 500;
        public bool Armed { get; private set; }
        public long StartedAt { get; private set; }
        public IntPtr Target { get; private set; }
        private long next;
        public void Input(GestureEngine engine, int front, MousePacket packet, IntPtr foreground, bool enabled)
        {
            if (packet.Kind == "reset") { Cancel(); engine.Reset(); return; }
            if (Armed && (packet.Kind != null || packet.Button != front || !packet.Down))
            {
                if (packet.Kind != null || packet.Button != front || ConsumesRelease(packet.TimeMs)) engine.Consume(front);
                Cancel();
            }
            bool begin = enabled && packet.Kind == null && packet.Down && packet.Button == front && engine.CanStartDoubleHold(front, packet.TimeMs);
            if (packet.Kind == null) engine.Input(packet.Button, packet.Down, packet.TimeMs);
            else engine.Combo(packet.Button, packet.Kind, packet.TimeMs);
            if (begin) { Begin(packet.TimeMs, foreground); engine.ReserveHold(front); }
        }
        public RepeatDecision Advance(GestureEngine engine, int front, long now, IntPtr foreground, bool stillHeld)
        {
            RepeatDecision decision = Tick(now, foreground, stillHeld);
            if (decision != RepeatDecision.None) engine.Consume(front);
            return decision;
        }
        public void Begin(long now, IntPtr target)
        { Armed = true; StartedAt = now; Target = target; next = now + IntervalMs; }
        public bool ConsumesRelease(long now) { return Armed && now - StartedAt >= IntervalMs; }
        public void Cancel() { Armed = false; Target = IntPtr.Zero; }
        public RepeatDecision Tick(long now, IntPtr foreground, bool stillHeld)
        {
            if (!Armed) return RepeatDecision.None;
            if (!stillHeld || Target == IntPtr.Zero || foreground != Target)
            { Cancel(); return RepeatDecision.Cancelled; }
            if (now < next) return RepeatDecision.None;
            next = now + IntervalMs;
            return RepeatDecision.Backspace;
        }
    }
}
