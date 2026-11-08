using System;

namespace MouseSideLab
{
    // Pure, timestamp-driven state machine. No Windows/UI dependencies.
    public sealed class Gesture
    {
        public int Button; // 1/2 = physical XBUTTON; 0 = chord
        public string Kind;
        public long TimeMs;
        public long HeldMs;
        public Gesture(int button, string kind, long time, long held)
        { Button = button; Kind = kind; TimeMs = time; HeldMs = held; }
    }

    public sealed class GestureEngine
    {
        private sealed class KeyState
        {
            public bool Down, LongFired, ModifierUsed, ReservedHold;
            public long DownAt, Deadline;
            public int Clicks;
        }
        private readonly KeyState[] keys = { new KeyState(), new KeyState() };
        private bool chord, chordHoldFired;
        public const int ChordHoldMs = 3000;
        private long lastTime;
        public readonly int ClickMs, HoldMs, ChordMs;
        public readonly bool OverlapChord;
        public event Action<Gesture> Recognized;
        public GestureEngine(int clickMs, int holdMs, int chordMs, bool overlapChord = true)
        {
            if (clickMs < 100 || holdMs < 200 || chordMs < 20 || chordMs >= holdMs)
                throw new ArgumentOutOfRangeException("Invalid timing thresholds");
            ClickMs = clickMs; HoldMs = holdMs; ChordMs = chordMs;
            OverlapChord = overlapChord;
        }
        public bool IsDown(int button) { return keys[button - 1].Down; }
        public bool CanStartDoubleHold(int button, long now)
        {
            KeyState k = keys[button - 1];
            return !chord && !k.Down && !keys[2 - button].Down && k.Clicks == 1 && now < k.Deadline;
        }
        public void ReserveHold(int button) { keys[button - 1].ReservedHold = true; }
        public void Consume(int button)
        {
            KeyState k = keys[button - 1];
            k.ModifierUsed = true; k.Clicks = 0; k.ReservedHold = false;
        }
        public long HeldFor(int button, long now)
        { KeyState k = keys[button - 1]; return k.Down ? Math.Max(0, now - k.DownAt) : 0; }
        private void Emit(int button, string kind, long now, long held)
        { if (Recognized != null) Recognized(new Gesture(button, kind, now, held)); }
        public void Advance(long now)
        {
            now = Math.Max(now, lastTime); lastTime = now;
            if (chord)
            {
                long held = now - Math.Max(keys[0].DownAt, keys[1].DownAt);
                if (!chordHoldFired && keys[0].Down && keys[1].Down && held >= ChordHoldMs)
                { chordHoldFired = true; Emit(0, "chordhold", now, held); }
                return;
            }
            for (int i = 0; i < 2; i++)
            {
                KeyState k = keys[i];
                if (k.Down && !k.LongFired && !k.ModifierUsed && !k.ReservedHold && now - k.DownAt >= HoldMs)
                {
                    k.LongFired = true; k.Clicks = 0;
                    Emit(i + 1, "hold", now, now - k.DownAt);
                }
                else if (!k.Down && k.Clicks > 0 && now >= k.Deadline)
                {
                    int clicks = k.Clicks; k.Clicks = 0;
                    Emit(i + 1, clicks == 1 ? "single" : "double", now, 0);
                }
            }
        }
        public void Input(int button, bool down, long now)
        {
            if (button != 1 && button != 2) throw new ArgumentOutOfRangeException("button");
            now = Math.Max(now, lastTime);
            Advance(now);
            KeyState k = keys[button - 1], other = keys[2 - button];
            if (k.Down == down) return; // duplicate down or orphan up
            k.Down = down;
            if (chord)
            {
                if (!down) chordHoldFired = true;
                if (!keys[0].Down && !keys[1].Down) Reset();
                return; // release order and re-press cannot produce phantom clicks
            }
            if (down)
            {
                k.DownAt = now; k.LongFired = false; k.ModifierUsed = false; k.ReservedHold = false;
                // Hold emits only a hint, so it must not prevent a later overlapping chord.
                if (other.Down && (OverlapChord || !other.ModifierUsed) &&
                    (OverlapChord || (!other.LongFired && now - other.DownAt <= ChordMs)))
                {
                    chord = true;
                    keys[0].Clicks = keys[1].Clicks = 0;
                    Emit(0, "chord", now, now - other.DownAt);
                }
            }
            else if (!k.LongFired && !k.ModifierUsed)
            {
                k.Clicks++;
                if (k.Clicks == 3)
                {
                    k.Clicks = 0;
                    Emit(button, "triple", now, 0);
                }
                else k.Deadline = now + ClickMs;
            }
        }
        public void Combo(int button, string kind, long now)
        {
            if (button != 1 && button != 2) throw new ArgumentOutOfRangeException("button");
            if (kind != "left" && kind != "right" && kind != "middle" && kind != "wheelup" && kind != "wheeldown")
                throw new ArgumentException("Unknown modifier combination");
            now = Math.Max(now, lastTime); Advance(now);
            KeyState k = keys[button - 1];
            if (chord || !k.Down) return;
            k.ModifierUsed = true; k.Clicks = 0;
            Emit(button, kind, now, now - k.DownAt);
        }
        public void Reset()
        {
            chord = false; chordHoldFired = false;
            foreach (KeyState k in keys)
            { k.Down = false; k.LongFired = false; k.ModifierUsed = false; k.ReservedHold = false; k.Clicks = 0; k.Deadline = 0; }
        }
    }
}
