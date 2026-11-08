using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MouseSideLab;

internal static class GestureTests
{
    private sealed class FakePetHost : IPetWindowHost
    {
        public readonly List<PetWindowSnapshot> Windows = new List<PetWindowSnapshot>();
        public int Shows, Hides;
        public bool Refuse, Ignore, DeferShow;
        public PetWindowSnapshot[] Snapshot() { return Windows.ToArray(); }
        public PetWindowSnapshot Add(int handle, bool visible=true)
        {
            var w = new PetWindowSnapshot { Identity = new WindowBookmark { Handle = new IntPtr(handle), ProcessId = 100, ProcessStarted = 1000, IsCodex = true },
                ClassName = "Chrome_WidgetWin_1", Title = "ChatGPT", Style = 0x14000000, ExtendedStyle = 0x2800a8,
                Visible = visible, Width = 721, Height = 181, DocumentUrl = "app://-/index.html?initialRoute=%2Favatar-overlay" };
            Windows.Add(w); return w;
        }
        public bool RequestVisibility(WindowBookmark window, bool visible)
        {
            if(Refuse)return false;
            var target=Windows.SingleOrDefault(w=>w.IsPetCandidate && w.Identity.SameWindow(window));
            if(target==null)return false;
            if(visible)Shows++;else Hides++;
            if(!Ignore && !(visible && DeferShow))target.Visible=visible;
            return true;
        }
    }
    private sealed class FakeAppHost : IAppWindowHost
    {
        public readonly List<WindowBookmark> Windows = new List<WindowBookmark>();
        public readonly List<IntPtr> Activated = new List<IntPtr>();
        public WindowBookmark Current, Codex;
        public bool Discoverable = true, LaunchReveals = true, ActivationFails;
        public int Launches;
        public WindowBookmark Add(int handle, bool codex = false, long started = 100)
        {
            var window = new WindowBookmark { Handle = new IntPtr(handle), ProcessId = handle + 100, ProcessStarted = started, IsCodex = codex };
            Windows.Add(window); if(codex) Codex = window; return window;
        }
        public WindowBookmark Foreground() { return Current; }
        public WindowBookmark FindCodex(WindowBookmark preferred) { return Discoverable && IsValid(Codex) ? Codex : null; }
        public bool IsValid(WindowBookmark window) { return window != null && Windows.Any(w => w.SameWindow(window)); }
        public Task<bool> Activate(WindowBookmark window, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if(ActivationFails || !IsValid(window))return Task.FromResult(false);
            Activated.Add(window.Handle); Current=window; return Task.FromResult(true);
        }
        public void LaunchCodex() { Launches++; if(LaunchReveals)Discoverable=true; }
    }
    public sealed class TraceEvent { public int Button, Flags; public string Kind; public long TimeMs; }
    private static int passed, failed;
    private static GestureEngine engine;
    private static List<string> actual;
    private static void New()
    { engine = new GestureEngine(300, 650, 150, false); actual = new List<string>(); engine.Recognized += g => actual.Add(g.Button + "." + g.Kind); }
    private static void Click(int button, long at) { engine.Input(button, true, at); engine.Input(button, false, at + 40); }
    private static void Pump(NativeMouse input)
    {
        MousePacket packet;
        while (input.TryRead(out packet))
            if (!packet.ObservedOnly && packet.Generation == input.Generation)
            {
                if (packet.Kind == "reset") engine.Reset();
                else if (packet.Kind == null) engine.Input(packet.Button, packet.Down, packet.TimeMs);
                else engine.Combo(packet.Button, packet.Kind, packet.TimeMs);
            }
    }
    private static void Expect(params string[] expected)
    { if (!actual.SequenceEqual(expected)) throw new Exception("Expected [" + string.Join(",", expected) + "] but got [" + string.Join(",", actual) + "]"); }
    private static void RepeatInput(DoubleHoldBackspace repeat, int front, int button, bool down, long time, string kind = null, bool enabled = true)
    { repeat.Input(engine, front, new MousePacket { Button = button, Down = down, TimeMs = time, Kind = kind }, new IntPtr(42), enabled); }
    private static DoubleHoldBackspace ArmRepeat(int front)
    {
        var repeat = new DoubleHoldBackspace();
        RepeatInput(repeat, front, front, true, 0); RepeatInput(repeat, front, front, false, 40); RepeatInput(repeat, front, front, true, 160);
        if (!repeat.Armed) throw new Exception("second press did not arm");
        return repeat;
    }
    private static void Test(string name, Action test)
    {
        New();
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + " -- " + ex.Message); }
    }
    private static int Main()
    {
        foreach (int button in new[] { 1, 2 })
        {
            int b = button;
            Test("single waits for multi-click window " + b, () => { Click(b, 0); engine.Advance(339); Expect(); engine.Advance(340); Expect(b + ".single"); });
            Test("double is exclusive " + b, () => { Click(b, 0); Click(b, 160); engine.Advance(499); Expect(); engine.Advance(500); Expect(b + ".double"); });
            Test("triple is exclusive and immediate " + b, () => { Click(b, 0); Click(b, 120); Click(b, 240); Expect(b + ".triple"); engine.Advance(2000); Expect(b + ".triple"); });
            Test("hold fires once, never clicks " + b, () => { engine.Input(b, true, 0); engine.Advance(649); Expect(); engine.Advance(650); engine.Advance(2000); engine.Input(b, false, 2500); engine.Advance(3000); Expect(b + ".hold"); });
            Test("release exactly at hold threshold " + b, () => { engine.Input(b, true, 0); engine.Input(b, false, 650); engine.Advance(1000); Expect(b + ".hold"); });
            Test("next press before click timeout does not leak a single " + b, () => { Click(b, 0); engine.Input(b, true, 330); engine.Advance(700); Expect(); engine.Input(b, false, 750); engine.Advance(1100); Expect(b + ".double"); });
            Test("click then long hold resolves to hold " + b, () => { Click(b, 0); engine.Input(b, true, 200); engine.Advance(850); engine.Input(b, false, 900); engine.Advance(1500); Expect(b + ".hold"); });
            Test("slow double becomes two singles " + b, () => { Click(b, 0); Click(b, 500); engine.Advance(1000); Expect(b + ".single", b + ".single"); });
            Test("four clicks become triple then single " + b, () => { Click(b, 0); Click(b, 100); Click(b, 200); Click(b, 300); engine.Advance(1000); Expect(b + ".triple", b + ".single"); });
            Test("duplicate and orphan edges ignored " + b, () => { engine.Input(b, false, 0); engine.Input(b, true, 10); engine.Input(b, true, 15); engine.Input(b, false, 50); engine.Input(b, false, 60); engine.Advance(500); Expect(b + ".single"); });
            foreach (string kind in new[] { "left", "right", "middle", "wheelup", "wheeldown" })
            {
                string k = kind;
                Test("modifier combination cancels click/hold " + b + "." + k, () => { engine.Input(b, true, 0); engine.Combo(b, k, 80); engine.Advance(700); engine.Input(b, false, 900); engine.Advance(1300); Expect(b + "." + k); });
            }
            Test("multiple wheel detents " + b, () => { engine.Input(b, true, 0); engine.Combo(b, "wheelup", 80); engine.Combo(b, "wheelup", 120); engine.Combo(b, "wheeldown", 160); engine.Input(b, false, 200); engine.Advance(1000); Expect(b + ".wheelup", b + ".wheelup", b + ".wheeldown"); });
            Test("hold hint permits later modifier " + b, () => { engine.Input(b, true, 0); engine.Advance(700); engine.Combo(b, "left", 900); engine.Input(b, false, 1000); engine.Advance(2000); Expect(b + ".hold", b + ".left"); });
            Test("combo without side press ignored " + b, () => { engine.Combo(b, "left", 10); Expect(); });
        }
        foreach (int first in new[] { 1, 2 })
        foreach (int releaseFirst in new[] { 1, 2 })
        {
            int f = first, r = releaseFirst;
            Test("chord suppresses clicks and holds " + f + "/" + r, () => {
                engine.Input(f, true, 0); engine.Input(3 - f, true, 80); engine.Advance(1000);
                engine.Input(r, false, 1100); engine.Input(3 - r, false, 1200); engine.Advance(2000); Expect("0.chord");
            });
        }
        Test("chord allows exact tolerance", () => { engine.Input(1, true, 0); engine.Input(2, true, 150); Expect("0.chord"); });
        Test("outside chord tolerance is independent", () => { engine.Input(1, true, 0); engine.Input(2, true, 151); engine.Input(1, false, 200); engine.Input(2, false, 220); engine.Advance(1000); Expect("1.single", "2.single"); });
        Test("chord cancels both pending click sequences", () => { Click(1, 0); Click(2, 80); engine.Input(1, true, 160); engine.Input(2, true, 180); engine.Input(1, false, 220); engine.Input(2, false, 240); engine.Advance(2000); Expect("0.chord"); });
        Test("chord needs full release before reuse", () => { engine.Input(1, true, 0); engine.Input(2, true, 50); engine.Input(1, false, 90); engine.Input(1, true, 120); engine.Combo(1, "left", 130); engine.Input(2, false, 160); engine.Input(1, false, 180); engine.Advance(1000); Expect("0.chord"); Click(1, 1200); engine.Advance(1800); Expect("0.chord", "1.single"); });
        Test("independent nonoverlapping alternating clicks", () => { Click(1, 0); Click(2, 100); engine.Advance(1000); Expect("1.single", "2.single"); });
        Test("reset cancels pending click", () => { Click(1, 0); engine.Reset(); engine.Advance(1000); Expect(); });
        Test("reset cancels held key; late release ignored", () => { engine.Input(1, true, 0); engine.Reset(); engine.Input(1, false, 700); engine.Advance(2000); Expect(); });
        Test("invalid button rejected", () => { bool ok = false; try { engine.Input(3, true, 0); } catch (ArgumentOutOfRangeException) { ok = true; } if (!ok) throw new Exception("accepted invalid button"); });
        Test("all configured shortcut tokens parse", () => { foreach (string s in new[] { "Win+H", "PlayPause", "Win+Ctrl+O", "Alt+Left", "Alt+Right", "Ctrl+Shift+T", "hint", "panel", "Ctrl+C", "Ctrl+V", "Mute", "VolumeUp", "VolumeDown", "Enter", "Esc", "Win+Tab", "Ctrl+Shift+Tab", "Ctrl+Tab", "Ctrl+Plus", "Win+Shift+S" }) Hotkeys.Parse(s); });
        Test("unknown shortcut rejected", () => { bool ok = false; try { Hotkeys.Parse("not-a-key"); } catch (ArgumentException) { ok = true; } if (!ok) throw new Exception("unknown key accepted"); });
        foreach (int first in new[] { 1, 2 })
        {
            int f = first;
            Test("overlap chord accepts slow second press " + f, () => {
                engine = new GestureEngine(300, 650, 150); engine.Recognized += g => actual.Add(g.Button + "." + g.Kind);
                engine.Input(f, true, 0); engine.Input(3-f, true, 500); engine.Input(f, false, 700); engine.Input(3-f, false, 800); engine.Advance(1500); Expect("0.chord");
            });
            Test("overlap chord works after hold hint " + f, () => {
                engine = new GestureEngine(300, 650, 150); engine.Recognized += g => actual.Add(g.Button + "." + g.Kind);
                engine.Input(f, true, 0); engine.Advance(1000); engine.Input(3-f, true, 1800); engine.Input(f, false, 2000); engine.Input(3-f, false, 2200); engine.Advance(3000); Expect(f + ".hold", "0.chord");
            });
        }
        Test("overlap chord takes precedence over an earlier modifier action", () => {
            engine = new GestureEngine(300, 650, 150); engine.Recognized += g => actual.Add(g.Button + "." + g.Kind);
            engine.Input(1, true, 0); engine.Combo(1, "left", 100); engine.Input(2, true, 200); engine.Input(1, false, 250); engine.Input(2, false, 300); engine.Advance(800); Expect("1.left", "0.chord");
        });
        Test("diagnostic detects consecutive short pulses", () => { var d = new InputDiagnostics(); for (int i=0;i<3;i++) { d.Record(1,true,i*100); d.Record(1,false,i*100+1); } if (!d.SuspectedReleaseOnly(1) || d.SuspectedReleaseOnly(2)) throw new Exception("wrong pulse diagnosis"); });
        Test("normal hold clears pulse suspicion", () => { var d = new InputDiagnostics(); for (int i=0;i<3;i++) { d.Record(1,true,i*100); d.Record(1,false,i*100+1); } d.Record(1,true,500); if(d.Record(1,false,2500)!=2000 || d.SuspectedReleaseOnly(1)) throw new Exception("hold duration lost"); });
        Test("diagnostic ignores orphan release and duplicate down", () => { var d = new InputDiagnostics(); if(d.Record(1,false,10)!=-1) throw new Exception("orphan"); d.Record(1,true,20); d.Record(1,true,50); if(d.Record(1,false,1020)!=1000) throw new Exception("duplicate changed start time"); });
        foreach (uint flags in new uint[] { 0, 1 })
        foreach (int button in new[] { 1, 2 })
        {
            uint f = flags; int b = button;
            Test("input pipeline preserves 2-second hold source=" + f + " button=" + b, () => {
                using (var input = new NativeMouse()) {
                    input.Configure(true, true, true);
                    if (!input.ProcessSide(b, true, f, 0)) throw new Exception("down not suppressed"); Pump(input);
                    engine.Advance(650); Expect(b + ".hold");
                    if (!input.ProcessSide(b, false, f, 2000)) throw new Exception("up not suppressed"); Pump(input);
                    engine.Advance(3000); Expect(b + ".hold");
                }
            });
            Test("input pipeline overlapping chord source=" + f + " first=" + b, () => {
                engine = new GestureEngine(300,650,150); engine.Recognized += g => actual.Add(g.Button + "." + g.Kind);
                using (var input = new NativeMouse()) {
                    input.Configure(true,true,true); input.ProcessSide(b,true,f,0); Pump(input);
                    input.ProcessSide(3-b,true,f,500); Pump(input);
                    input.ProcessSide(b,false,f,900); input.ProcessSide(3-b,false,f,1000); Pump(input);
                    engine.Advance(2000); Expect("0.chord");
                }
            });
        }
        Test("old filter reproduces missing forwarded hold; compatibility fixes it", () => {
            using (var input = new NativeMouse()) {
                input.Configure(true,true,false); input.ProcessSide(1,true,1,0); Pump(input); engine.Advance(650);
                input.ProcessSide(1,false,1,2000); Pump(input); Expect();
                input.Configure(true,true,true); input.ProcessSide(1,true,1,3000); Pump(input); engine.Advance(3650);
                input.ProcessSide(1,false,1,5000); Pump(input); Expect("1.hold");
            }
        });
        Test("ignored forwarded packets are still available for diagnosis", () => {
            using (var input = new NativeMouse()) {
                input.Configure(true,true,false); if(input.ProcessSide(1,true,1,0))throw new Exception("filtered input blocked");
                MousePacket p; if(!input.TryRead(out p) || !p.ObservedOnly || !p.Injected)throw new Exception("missing rejected event");
            }
        });
        Test("pause retains swallowed release and prevents stale gesture", () => {
            using (var input = new NativeMouse()) {
                input.Configure(true,true); input.ProcessSide(1,true,1,0); Pump(input);
                input.Configure(false,false); engine.Reset(); if(!input.ProcessSide(1,false,1,900))throw new Exception("release leaked");
                Pump(input); engine.Advance(2000); Expect();
            }
        });
        Test("physical input remains usable with compatibility disabled", () => {
            using (var input = new NativeMouse()) {
                input.Configure(true,true,false); input.ProcessSide(2,true,0,0); Pump(input);
                input.ProcessSide(2,false,0,50); Pump(input); engine.Advance(400); Expect("2.single");
            }
        });
        Test("driver echo cannot prematurely release physical hold", () => {
            using (var input = new NativeMouse()) {
                input.Configure(true,true); input.ProcessSide(1,true,0,0); Pump(input);
                input.ProcessSide(1,true,1,10); input.ProcessSide(1,false,1,20); Pump(input);
                engine.Advance(650); Expect("1.hold"); input.ProcessSide(1,false,0,2000); Pump(input); engine.Advance(3000); Expect("1.hold");
            }
        });
        Test("changing compatibility mid-press still releases held source", () => {
            using (var input = new NativeMouse()) {
                input.Configure(true,true,true); input.ProcessSide(1,true,1,0); Pump(input);
                input.Configure(true,true,false); engine.Reset(); input.ProcessSide(1,false,1,900); Pump(input);
                input.ProcessSide(1,true,0,1000); input.ProcessSide(1,false,0,1040); Pump(input); engine.Advance(1400); Expect("1.single");
            }
        });
        Test("hook starts and stops on actual Windows message thread", () => {
            using (var input = new NativeMouse()) { input.Configure(false,false); input.Start(); }
        });
        foreach (uint flags in new uint[] { 0, 1 })
        foreach (int message in new[] { 0x201, 0x204, 0x207, 0x20A })
        {
            uint f = flags; int m = message;
            Test("test mode passes normal input with held side source=" + f + " message=" + m, () => {
                using (var input = new NativeMouse()) {
                    input.Configure(true,true,true,false); input.ProcessSide(1,true,1,0); Pump(input);
                    if(input.ProcessMain(m,f,120,100)) throw new Exception("test mode blocked primary input");
                    if(m!=0x20A && input.ProcessMain(m+1,f,0,110))throw new Exception("test mode blocked primary release");
                }
            });
        }
        foreach (int releaseButton in new[] {1,2})
        {
            int r=releaseButton;
            Test("chord first release clears both modifiers even if other up is lost " + r, () => {
                engine = new GestureEngine(300,650,150); engine.Recognized += g=>actual.Add(g.Button+"."+g.Kind);
                using(var input=new NativeMouse()) {
                    input.Configure(true,true,true,true); input.ProcessSide(1,true,1,0); input.ProcessSide(2,true,1,2); Pump(input);
                    input.ProcessSide(r,false,1,261); Pump(input); Expect("0.chord");
                    foreach(int m in new[]{0x201,0x204,0x207,0x20A}) {
                        if(input.ProcessMain(m,0,120,300))throw new Exception("primary blocked after chord release");
                        if(m!=0x20A && input.ProcessMain(m+1,0,0,310))throw new Exception("primary up blocked");
                    }
                    Pump(input); engine.Advance(1000); Expect("0.chord");
                    if(!input.ProcessSide(3-r,false,1,1100))throw new Exception("late swallowed side up leaked");
                    Pump(input); engine.Advance(2000); Expect("0.chord");
                }
            });
        }
        Test("primary clicks pass while both sides are held", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,true); input.ProcessSide(1,true,1,0); input.ProcessSide(2,true,1,10);
                if(input.ProcessMain(0x201,0,0,100) || input.ProcessMain(0x202,0,0,110))throw new Exception("chord blocked panel click");
            }
        });
        Test("lost single release expires before next primary input", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,true); input.ProcessSide(1,true,1,0); Pump(input);
                if(!input.ProcessMain(0x201,0,0,100))throw new Exception("valid active combo did not capture");
                input.ProcessMain(0x202,0,0,110); Pump(input); Expect("1.left");
                if(input.ProcessMain(0x204,0,0,5000) || input.ProcessMain(0x205,0,0,5010))throw new Exception("stale side blocked right click");
                Pump(input); engine.Advance(6000); Expect("1.left");
            }
        });
        Test("idle watchdog cancels hold without waiting for another click", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,true); input.ProcessSide(2,true,1,0); Pump(input); input.ExpireStaleHolds(5000); Pump(input);
                if(engine.IsDown(2))throw new Exception("engine remained held after timeout");
            }
        });
        Test("lost primary up also recovers after side timeout", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,true); input.ProcessSide(1,true,1,0); input.ProcessMain(0x201,0,0,100);
                if(input.ProcessMain(0x201,0,0,5001) || input.ProcessMain(0x202,0,0,5010))throw new Exception("lost primary up remained stuck");
            }
        });
        Test("own windows remain clickable and do not dispatch combos", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,true); input.ProcessSide(1,true,1,0); Pump(input);
                if(input.ProcessMain(0x201,0,0,100,true) || input.ProcessMain(0x202,0,0,110,true) || input.ProcessMain(0x20A,0,120,120,true))throw new Exception("own UI blocked");
                Pump(input); Expect();
            }
        });
        Test("active combo keeps primary down-up pairing after side release", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,true); input.ProcessSide(1,true,1,0);
                if(!input.ProcessMain(0x201,0,0,100))throw new Exception("down leaked");
                input.ProcessSide(1,false,1,120);
                if(!input.ProcessMain(0x202,0,0,150))throw new Exception("up leaked");
                if(input.ProcessMain(0x201,0,0,200) || input.ProcessMain(0x202,0,0,220))throw new Exception("next click blocked");
            }
        });
        foreach(bool capture in new[]{false,true})
        {
            bool c=capture;
            Test("replay captured 153-event failure trace; no ordinary input blocked; capture="+c, () => {
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"missing-release-trace.json");
                TraceEvent[] trace=new JavaScriptSerializer().Deserialize<TraceEvent[]>(File.ReadAllText(path));
                int ordinary=0;
                using(var input=new NativeMouse()) {
                    input.Configure(true,true,true,c);
                    foreach(TraceEvent t in trace) {
                        if(t.Kind=="按下" || t.Kind=="松开") input.ProcessSide(t.Button,t.Kind=="按下",(uint)t.Flags,t.TimeMs);
                        else {
                            int m=t.Kind=="left"?0x201:t.Kind=="right"?0x204:t.Kind=="middle"?0x207:0x20A;
                            short delta=(short)(t.Kind=="wheelup"?120:-120);
                            if(input.ProcessMain(m,(uint)t.Flags,delta,t.TimeMs))throw new Exception("blocked captured "+t.Kind+" at "+t.TimeMs);
                            if(m!=0x20A && input.ProcessMain(m+1,(uint)t.Flags,0,t.TimeMs))throw new Exception("blocked paired release");
                            ordinary++;
                        }
                        Pump(input);
                    }
                }
                if(trace.Length!=153 || ordinary==0)throw new Exception("wrong fixture");
                Console.WriteLine("  replay ordinary inputs forwarded: "+ordinary);
            });
        }
        foreach(int front in new[]{1,2})
        {
            int f=front;
            Test("dedicated volume captures chosen side wheel but preserves buttons " + f, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true,true,false,f); input.ProcessSide(f,true,1,0); Pump(input);
                    if(!input.ProcessMain(0x20A,0,120,50,true))throw new Exception("volume wheel did not capture inside host");
                    Pump(input); Expect(f+".wheelup");
                    foreach(int m in new[]{0x201,0x204,0x207})
                        if(input.ProcessMain(m,0,0,100,true)||input.ProcessMain(m+1,0,0,110,true))throw new Exception("volume mode blocked ordinary button");
                    Pump(input); Expect(f+".wheelup");
                    if(!input.ProcessMain(0x20A,0,-120,150,true))throw new Exception("volume down did not capture"); Pump(input);
                    input.ProcessSide(f,false,1,200); Pump(input); engine.Advance(600); Expect(f+".wheelup",f+".wheeldown");
                    if(input.ProcessMain(0x20A,0,120,650))throw new Exception("wheel stayed blocked after release");
                }
            });
        }
        Test("volume wheel leaves other side and unmodified scrolling intact", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,false,2);
                if(input.ProcessMain(0x20A,0,120,0))throw new Exception("plain scroll blocked");
                input.ProcessSide(1,true,1,10);
                if(input.ProcessMain(0x20A,0,120,50))throw new Exception("other side captured by volume");
            }
        });
        Test("high resolution wheel accumulates one volume step at 120 units", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,false,2); input.ProcessSide(2,true,1,0); Pump(input);
                input.ProcessMain(0x20A,0,40,10); input.ProcessMain(0x20A,0,40,20); Pump(input); Expect();
                input.ProcessMain(0x20A,0,40,30); Pump(input); Expect("2.wheelup");
            }
        });
        Test("volume does not capture wheel during chord or after missing-release recovery", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,false,2); input.ProcessSide(1,true,1,0); input.ProcessSide(2,true,1,5);
                if(input.ProcessMain(0x20A,0,120,100))throw new Exception("chord wheel blocked");
                input.ProcessSide(1,false,1,200);
                if(input.ProcessMain(0x20A,0,120,300))throw new Exception("recovered chord wheel blocked");
            }
        });
        Test("volume respects five-second recovery and pause", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,false,2); input.ProcessSide(2,true,1,0);
                if(input.ProcessMain(0x20A,0,120,5000))throw new Exception("expired volume modifier captured");
                input.ProcessSide(2,false,1,5010); input.Configure(false,false,true,false,2); input.ProcessSide(2,true,1,5100);
                if(input.ProcessMain(0x20A,0,120,5200))throw new Exception("pause did not disable volume");
            }
        });
        foreach (int front in new[] { 1, 2 })
        {
            int f = front;
            Test("sofa mute is paired once, preserves left/right and unmodified middle " + f, () => {
                using (var input = new NativeMouse()) {
                    input.Configure(true,true,true,false,f,f,3-f);
                    if(input.ProcessMain(0x207,0,0,0,true) || input.ProcessMain(0x208,0,0,1,true))throw new Exception("plain middle blocked");
                    input.ProcessSide(f,true,1,10); Pump(input);
                    if(!input.ProcessMain(0x207,0,0,30,true) || !input.ProcessMain(0x207,1,0,31,true))throw new Exception("mute down leaked");
                    Pump(input); Expect(f+".middle");
                    input.ProcessSide(f,false,1,40); Pump(input);
                    if(!input.ProcessMain(0x208,0,0,50,true))throw new Exception("paired up leaked after side released");
                    foreach(int message in new[]{0x201,0x204,0x207})
                        if(input.ProcessMain(message,0,0,60,true)||input.ProcessMain(message+1,0,0,70,true))throw new Exception("ordinary button blocked");
                    engine.Advance(1000); Expect(f+".middle");
                }
            });
            Test("back wheel cycles windows and releases without phantom back click " + f, () => {
                using (var input = new NativeMouse()) {
                    input.Configure(true,true,true,false,f,f,3-f); input.ProcessSide(3-f,true,1,0); Pump(input);
                    foreach(short delta in new short[]{120,120,-120}) {
                        if(!input.ProcessMain(0x20A,0,delta,100,true))throw new Exception("window wheel leaked"); Pump(input);
                    }
                    input.ProcessSide(3-f,false,1,200); Pump(input); engine.Advance(1000);
                    Expect((3-f)+".wheelup",(3-f)+".wheelup",(3-f)+".wheeldown");
                    if(input.ProcessMain(0x20A,0,120,1010))throw new Exception("scroll remained blocked");
                }
            });
        }
        Test("sofa bindings enable only requested actions", () => {
            if(SofaBindings.Shortcut("front.middle")!="Mute" || SofaBindings.Shortcut("back.triple")!="Enter" ||
                SofaBindings.Shortcut("back.wheelup")!="WindowPrevious" || SofaBindings.Shortcut("back.wheeldown")!="WindowNext" ||
                SofaBindings.Shortcut("both.hold")!="ShowHelp")throw new Exception("incorrect binding");
            foreach(string code in new[]{"front.left","front.right","back.left","back.right","both.chord"})
                if(SofaBindings.Shortcut(code)!=null)throw new Exception("unrequested action enabled: "+code);
            var desktop=Hotkeys.Parse(SofaBindings.Shortcut("back.middle"));
            if(!desktop.SequenceEqual(new ushort[]{0x5B,0x44}))throw new Exception("desktop shortcut incorrect");
            if(Hotkeys.Parse(SofaBindings.Shortcut("back.single")).Length!=0)throw new Exception("pet action must not blindly toggle the input box");
        });
        Test("window cycle retains Alt across multiple forward and backward steps", () => {
            var sent=new List<string>();
            using(var cycle=new WindowCycle(k=>false,(k,d)=>sent.Add(d?"AltDown":"AltUp"),s=>sent.Add(s))) {
                cycle.Step(false); cycle.Step(false); cycle.Step(true);
                if(!cycle.Active || !sent.SequenceEqual(new[]{"AltDown","Tab","Tab","Shift+Tab"}))throw new Exception("session not retained");
                cycle.Finish(); cycle.Finish();
            }
            if(!sent.SequenceEqual(new[]{"AltDown","Tab","Tab","Shift+Tab","AltUp"}))throw new Exception("unpaired Alt");
        });
        Test("window cycle preserves physically held Alt", () => {
            var sent=new List<string>();
            using(var cycle=new WindowCycle(k=>true,(k,d)=>sent.Add("unexpected"),s=>sent.Add(s)))cycle.Step(true);
            if(!sent.SequenceEqual(new[]{"Shift+Tab"}))throw new Exception("physical Alt modified");
        });
        Test("window cycle sends release after failed Tab", () => {
            var sent=new List<bool>(); bool failedAsExpected=false;
            using(var cycle=new WindowCycle(k=>false,(k,d)=>sent.Add(d),s=>{throw new Exception("transport failure");})) {
                try { cycle.Step(false); } catch { failedAsExpected=true; }
                if(cycle.Active)throw new Exception("session stuck");
            }
            if(!failedAsExpected || !sent.SequenceEqual(new[]{true,false}))throw new Exception("failed session did not release Alt");
        });
        Test("window cycle attempts release if initial key down fails", () => {
            var sent=new List<bool>();
            using(var cycle=new WindowCycle(k=>false,(k,d)=>{sent.Add(d);if(d)throw new Exception("down failed");},s=>{})) {
                try { cycle.Step(false); } catch { }
                if(cycle.Active)throw new Exception("session stuck");
            }
            if(!sent.SequenceEqual(new[]{true,false}))throw new Exception("cleanup not attempted");
        });
        Test("front and rear triples select Escape and Enter only", () => {
            if(SofaBindings.Shortcut("front.triple")!="Esc" || SofaBindings.Shortcut("back.triple")!="Enter")throw new Exception("wrong mapping");
            Click(2,0);Click(2,100);Click(2,200);engine.Advance(600);Expect("2.triple");
            Click(1,1000);Click(1,1100);Click(1,1200);engine.Advance(1600);Expect("2.triple","1.triple");
            if(!Hotkeys.Parse("Esc").SequenceEqual(new ushort[]{0x1B})||!Hotkeys.Parse("Enter").SequenceEqual(new ushort[]{0x0D}))throw new Exception("wrong key codes");
        });
        Test("back triple repeats without single/double leakage", () => {
            Click(1,0);Click(1,100);Click(1,200);engine.Advance(600);
            Click(1,1000);Click(1,1100);Click(1,1200);engine.Advance(2000);
            Expect("1.triple","1.triple");
        });
        foreach(int first in new[]{1,2}) {
            int f=first;
            Test("dual hold starts on second press and fires once at three seconds " + f, () => {
                engine.Input(f,true,0);engine.Input(3-f,true,100);engine.Advance(3099);Expect("0.chord");
                engine.Advance(3100);engine.Advance(4000);Expect("0.chord","0.chordhold");
                engine.Input(f,false,4100);engine.Input(3-f,false,4200);engine.Advance(4500);Expect("0.chord","0.chordhold");
                engine.Input(f,true,5000);engine.Input(3-f,true,5010);engine.Advance(8010);Expect("0.chord","0.chordhold","0.chord","0.chordhold");
            });
            Test("either early side release cancels three-second hold " + f, () => {
                engine.Input(1,true,0);engine.Input(2,true,100);engine.Input(f,false,3099);engine.Advance(4000);Expect("0.chord");
                engine.Input(f,true,4100);engine.Advance(8000);Expect("0.chord");
            });
        }
        Test("native hold survives late second press and exits immediately on release", () => {
            engine=new GestureEngine(300,650,150);engine.Recognized+=g=>actual.Add(g.Button+"."+g.Kind);
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,false,2,2,1);
                input.ProcessSide(1,true,1,0);Pump(input);engine.Advance(1000);
                input.ProcessSide(2,true,1,2500);Pump(input);
                input.ExpireStaleHolds(5500);Pump(input);engine.Advance(5500);Expect("1.hold","0.chord","0.chordhold");
                input.ProcessSide(1,false,1,5510);Pump(input);
                foreach(int m in new[]{0x201,0x204,0x207,0x20A})
                    if(input.ProcessMain(m,0,120,5600,true))throw new Exception("normal input blocked after chord release");
            }
        });
        Test("both-side hold takes priority over earlier modifier use", () => {
            engine=new GestureEngine(300,650,150);engine.Recognized+=g=>actual.Add(g.Button+"."+g.Kind);
            engine.Input(1,true,0);engine.Combo(1,"wheelup",30);engine.Input(2,true,100);engine.Advance(3100);
            Expect("1.wheelup","0.chord","0.chordhold");
        });
        Test("HID++ side report validates device feature event and all four controls", () => {
            var r=new byte[20];r[0]=0x11;r[1]=0xff;r[2]=9;r[3]=0;r[5]=83;r[11]=86;
            bool front,back;
            if(!LogitechChordReport.TryRead(r,20,9,out front,out back)||!front||!back)throw new Exception("both controls lost");
            r[3]=0x2f;if(LogitechChordReport.TryRead(r,20,9,out front,out back))throw new Exception("query reply became key event");
            r[3]=0;if(LogitechChordReport.TryRead(r,19,9,out front,out back)||LogitechChordReport.TryRead(r,20,8,out front,out back))throw new Exception("invalid packet accepted");
            r[5]=r[11]=0;if(!LogitechChordReport.TryRead(r,20,9,out front,out back)||front||back)throw new Exception("release not decoded");
        });
        Test("HID++ chord triggers help once while suppressing forwarded echoes", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,false,2,2,1,1);
                input.ProcessSide(1,true,1,0);input.RecordHardwareChord(true,true,100);
                input.ProcessSide(2,true,1,110);input.RecordHardwareChord(true,true,2000);
                input.ExpireStaleHolds(3099);
                MousePacket p;int fired=0;while(input.TryRead(out p))if(p.Kind=="hardwarechordhold")fired++;
                if(fired!=0)throw new Exception("early help");
                input.ExpireStaleHolds(3100);input.ExpireStaleHolds(4000);
                while(input.TryRead(out p))if(p.Kind=="hardwarechordhold")fired++;
                if(fired!=1)throw new Exception("missing or repeated help");
                foreach(int msg in new[]{0x201,0x202,0x204,0x205,0x207,0x208,0x20A})
                    if(input.ProcessMain(msg,0,120,4010))throw new Exception("ordinary input blocked");
                input.RecordHardwareChord(false,false,4100);input.ProcessSide(1,false,1,4110);input.ProcessSide(2,false,1,4120);
                while(input.TryRead(out p))if(p.Kind==null&&!p.ObservedOnly)throw new Exception("release echo became action");
                input.RecordHardwareChord(true,true,4200);input.ExpireStaleHolds(7200);
                while(input.TryRead(out p))if(p.Kind=="hardwarechordhold")fired++;
                if(fired!=2)throw new Exception("second chord not rearmed");
            }
        });
        foreach(int cancellation in new[]{0,1,2}) {
            int mode=cancellation;
            Test("HID++ chord cancellation before threshold "+mode, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true);input.RecordHardwareChord(true,true,0);
                    if(mode==0){input.RecordHardwareChord(false,true,2999);input.RecordHardwareChord(true,true,3000);}
                    if(mode==1)input.HardwareChordDisconnected(2999);
                    if(mode==2)input.Configure(false,false);
                    input.ExpireStaleHolds(4500);MousePacket p;
                    while(input.TryRead(out p))if(p.Kind=="hardwarechordhold")throw new Exception("cancelled chord fired");
                }
            });
        }
        Test("HID++ missing release cannot leave clicks blocked or restart help", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,true,2,2,1,1);input.RecordHardwareChord(true,true,0);
                input.ExpireStaleHolds(5001);input.RecordHardwareChord(true,true,5100);input.ExpireStaleHolds(9000);
                MousePacket p;while(input.TryRead(out p))if(p.Kind=="hardwarechordhold")throw new Exception("stale chord fired");
                if(input.ProcessMain(0x201,0,0,9001)||input.ProcessMain(0x204,0,0,9002))throw new Exception("buttons blocked");
            }
        });
        Test("reset cancels pending display toggle", () => {
            engine.Input(1,true,0);engine.Input(2,true,100);engine.Advance(2000);engine.Reset();engine.Advance(6000);Expect("0.chord");
        });
        Test("pause and stale state disable dedicated mute and window wheel", () => {
            using(var input=new NativeMouse()) {
                input.Configure(true,true,true,false,2,2,1);input.ProcessSide(2,true,1,0);
                if(input.ProcessMain(0x207,0,0,5000))throw new Exception("expired mute captured");input.ProcessMain(0x208,0,0,5010);
                input.ProcessSide(2,false,1,5020);input.ProcessSide(1,true,1,5100);
                if(input.ProcessMain(0x20A,0,120,10100))throw new Exception("expired window modifier captured");
                input.Configure(false,false,true,false,2,2,1);input.ProcessSide(2,true,1,10200);
                if(input.ProcessMain(0x207,0,0,10300))throw new Exception("paused mute captured");
            }
        });
        foreach(int front in new[]{1,2}) {
            int f=front;
            Test("dedicated back middle shows desktop, pairs release, preserves ordinary clicks " + f, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true,true,false,f,f,3-f,3-f);input.ProcessSide(3-f,true,1,0);Pump(input);
                    if(!input.ProcessMain(0x207,0,0,50,true) || !input.ProcessMain(0x207,1,0,51,true))throw new Exception("desktop middle leaked");
                    Pump(input);Expect((3-f)+".middle");
                    input.ProcessSide(3-f,false,1,70);Pump(input);
                    if(!input.ProcessMain(0x208,0,0,80,true))throw new Exception("release leaked");
                    foreach(int m in new[]{0x201,0x204,0x207})
                        if(input.ProcessMain(m,0,0,100,true)||input.ProcessMain(m+1,0,0,110,true))throw new Exception("ordinary click blocked");
                    engine.Advance(1000);Expect((3-f)+".middle");
                }
            });
        }
        Test("back middle respects pause, chord and five-second recovery", () => {
            using(var input=new NativeMouse()) {
                input.Configure(false,false,true,false,2,2,1,1);input.ProcessSide(1,true,1,0);
                if(input.ProcessMain(0x207,0,0,10))throw new Exception("paused desktop combo captured");input.ProcessMain(0x208,0,0,20);input.ProcessSide(1,false,1,30);
                input.Configure(true,true,true,false,2,2,1,1);input.ProcessSide(1,true,1,40);input.ProcessSide(2,true,1,50);
                if(input.ProcessMain(0x207,0,0,60))throw new Exception("chord middle captured");input.ProcessMain(0x208,0,0,70);input.ProcessSide(2,false,1,80);input.ProcessSide(1,false,1,90);
                input.ProcessSide(1,true,1,100);
                if(input.ProcessMain(0x207,0,0,5100))throw new Exception("expired desktop modifier captured");
            }
        });
        Test("new default bindings are Codex toggle and desktop", () => {
            if(SofaBindings.Shortcut("front.single")!="CodexToggle" || SofaBindings.Shortcut("front.double")!="Delete" || SofaBindings.Shortcut("back.double")!="Ctrl+Z" || SofaBindings.Shortcut("back.single")!="PetToggle" || SofaBindings.Shortcut("back.middle")!="Win+D")throw new Exception("bindings missing");
            Hotkeys.Parse("CodexToggle");
            if(!Hotkeys.Parse(SofaBindings.Shortcut("front.double")).SequenceEqual(new ushort[]{0x2E}) || !Hotkeys.Parse(SofaBindings.Shortcut("back.double")).SequenceEqual(new ushort[]{0x11,0x5A}))throw new Exception("delete or undo key incorrect");
        });
        Test("Codex round trip returns to exact originating window repeatedly", () => {
            var host=new FakeAppHost();var a=host.Add(1);var c=host.Add(2,true);host.Current=a;
            var toggle=new AppToggle(host);toggle.Toggle().GetAwaiter().GetResult();toggle.Toggle().GetAwaiter().GetResult();toggle.Toggle().GetAwaiter().GetResult();toggle.Toggle().GetAwaiter().GetResult();
            if(!host.Activated.SequenceEqual(new[]{c.Handle,a.Handle,c.Handle,a.Handle})||host.Launches!=0)throw new Exception("wrong round trip");
        });
        Test("Codex cold launch remembers source before launcher exists", () => {
            var host=new FakeAppHost();var a=host.Add(1);var c=host.Add(2,true);host.Current=a;host.Discoverable=false;
            var toggle=new AppToggle(host,(ms,ct)=>Task.FromResult(0));toggle.Toggle().GetAwaiter().GetResult();toggle.Toggle().GetAwaiter().GetResult();
            if(host.Launches!=1||!host.Activated.SequenceEqual(new[]{c.Handle,a.Handle}))throw new Exception("lost cold-start bookmark");
        });
        Test("switching from a different application refreshes Codex return target", () => {
            var host=new FakeAppHost();var a=host.Add(1);var c=host.Add(2,true);var b=host.Add(3);host.Current=a;
            var toggle=new AppToggle(host);toggle.Toggle().GetAwaiter().GetResult();host.Current=b;toggle.ObserveForeground();toggle.Toggle().GetAwaiter().GetResult();toggle.Toggle().GetAwaiter().GetResult();
            if(host.Current!=b)throw new Exception("returned to stale app");
        });
        Test("closed return window never falls back to an unrelated app", () => {
            var host=new FakeAppHost();var a=host.Add(1);var c=host.Add(2,true);var b=host.Add(3);host.Current=a;
            var toggle=new AppToggle(host);toggle.Toggle().GetAwaiter().GetResult();host.Windows.Remove(a);host.Current=b;toggle.ObserveForeground();host.Current=c;
            string message=toggle.Toggle().GetAwaiter().GetResult();
            if(host.Current!=c||host.Activated.Count!=1||!message.Contains("已关闭"))throw new Exception("invalid window restored");
        });
        Test("window handle reuse does not restore a different process instance", () => {
            var host=new FakeAppHost();var a=host.Add(1);var c=host.Add(2,true);host.Current=a;
            var toggle=new AppToggle(host);toggle.Toggle().GetAwaiter().GetResult();host.Windows.Remove(a);host.Add(1,false,200);
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("已关闭")||host.Activated.Count!=1)throw new Exception("reused handle accepted");
        });
        Test("Codex can return to last observed external window when manually opened", () => {
            var host=new FakeAppHost();var a=host.Add(1);var c=host.Add(2,true);host.Current=a;
            var toggle=new AppToggle(host);toggle.ObserveForeground();host.Current=c;toggle.ObserveForeground();toggle.Toggle().GetAwaiter().GetResult();
            if(host.Current!=a)throw new Exception("manual-entry origin not observed");
        });
        Test("first run in Codex without a bookmark does not guess another window", () => {
            var host=new FakeAppHost();host.Current=host.Add(2,true);
            string message=new AppToggle(host).Toggle().GetAwaiter().GetResult();
            if(host.Activated.Count!=0||!message.Contains("尚未记住"))throw new Exception("unrelated window chosen");
        });
        Test("failed activation preserves the last successful bookmark", () => {
            var host=new FakeAppHost();var a=host.Add(1);var c=host.Add(2,true);var b=host.Add(3);host.Current=a;
            var toggle=new AppToggle(host);toggle.Toggle().GetAwaiter().GetResult();host.Current=b;host.ActivationFails=true;toggle.Toggle().GetAwaiter().GetResult();
            host.ActivationFails=false;host.Current=c;toggle.Toggle().GetAwaiter().GetResult();if(host.Current!=a)throw new Exception("failed toggle overwrote source");
        });
        Test("duplicate request while opening does not launch twice, pause cancels", () => {
            var host=new FakeAppHost();host.Current=host.Add(1);host.Add(2,true);host.Discoverable=false;host.LaunchReveals=false;
            var toggle=new AppToggle(host,(ms,ct)=>Task.Delay(Timeout.Infinite,ct));var pending=toggle.Toggle();
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("正在切换")||host.Launches!=1)throw new Exception("duplicate launch");
            toggle.CancelPending();if(!pending.GetAwaiter().GetResult().Contains("取消")||toggle.Busy||host.Activated.Count!=0)throw new Exception("cancel ineffective");
        });
        Test("new user window selection during launch is respected", () => {
            var host=new FakeAppHost();var a=host.Add(1);host.Add(2,true);var b=host.Add(3);host.Current=a;host.Discoverable=false;
            var toggle=new AppToggle(host,(ms,ct)=>{host.Current=b;return Task.FromResult(0);});
            string result=toggle.Toggle().GetAwaiter().GetResult();if(host.Activated.Count!=0||!result.Contains("焦点"))throw new Exception("stole focus after user navigation");
        });
        Test("pet hides and shows the same retained window without touching main", () => {
            var host=new FakePetHost();var pet=host.Add(1);var main=host.Add(2);main.Style=0x15070000;main.ExtendedStyle=0x240100;
            var identity=pet.Identity;var toggle=new PetToggle(host,(ms,ct)=>Task.FromResult(0));
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("已隐藏整个")||pet.Visible||!main.Visible)throw new Exception("did not hide exclusively");
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("已显示")||!pet.Visible||host.Shows!=1||host.Hides!=1||!identity.SameWindow(pet.Identity))throw new Exception("window was not retained");
            if(pet.Width!=721||pet.Height!=181)throw new Exception("input layout changed");
        });
        Test("pet uses observed visibility after a manual hide", () => {
            var host=new FakePetHost();var pet=host.Add(1,false);var toggle=new PetToggle(host,(ms,ct)=>Task.FromResult(0));
            toggle.Toggle().GetAwaiter().GetResult();pet.Visible=false;toggle.Toggle().GetAwaiter().GetResult();
            if(host.Shows!=2||host.Hides!=0)throw new Exception("visibility desynchronized");
        });
        Test("pet excludes main foreign owned and utility windows", () => {
            for(int variant=0;variant<9;variant++){
                var host=new FakePetHost();var w=host.Add(1);
                if(variant==0)w.Identity.IsCodex=false;
                if(variant==1){w.Style=0x15070000;w.ExtendedStyle=0x240100;}
                if(variant==2)w.Owner=new IntPtr(2);
                if(variant==3)w.ClassName="Chrome_WidgetWin_0";
                if(variant==4)w.ExtendedStyle=0x80;
                if(variant==5)w.Width=0;
                if(variant==6)w.Style|=0x40000000;
                if(variant==7)w.ExtendedStyle|=0x40000;
                if(variant==8)w.Title="Dictation";
                new PetToggle(host,(ms,ct)=>Task.FromResult(0)).Toggle().GetAwaiter().GetResult();
                if(host.Hides!=0||host.Shows!=0||!w.Visible)throw new Exception("unsafe target "+variant);
            }
        });
        Test("pet excludes dictation even with identical titles and native styles", () => {
            var host=new FakePetHost();var pet=host.Add(1);var dictation=host.Add(2);
            dictation.DocumentUrl="app://-/index.html?initialRoute=%2Fglobal-dictation";
            var toggle=new PetToggle(host,(ms,ct)=>Task.FromResult(0));
            for(int i=0;i<6;i++)toggle.Toggle().GetAwaiter().GetResult();
            if(!pet.Visible||!dictation.Visible||host.Hides!=3||host.Shows!=3)throw new Exception("dictation confused with pet");
        });
        Test("hidden avatar remains identifiable without layered style", () => {
            var host=new FakePetHost();var pet=host.Add(1,false);pet.ExtendedStyle=0x00200088;
            new PetToggle(host,(ms,ct)=>Task.FromResult(0)).Toggle().GetAwaiter().GetResult();
            if(!pet.Visible||host.Shows!=1)throw new Exception("hidden avatar excluded");
        });
        Test("avatar requires unambiguous local document route", () => {
            foreach(var url in new[]{null,"","https://-/index.html?initialRoute=%2Favatar-overlay","app://other/index.html?initialRoute=%2Favatar-overlay",
                "app://-/index.html?initialRoute=%2Fglobal-dictation","app://-/index.html?initialRoute=%2Favatar-overlay&initialRoute=%2Fglobal-dictation",
                "app://-/index.html?initialRoute=%2Favatar-overlay-extra","app://-/index.html#initialRoute=%2Favatar-overlay"})
                if(PetWindowSnapshot.IsAvatarDocument(url))throw new Exception("unverified route accepted: "+url);
        });
        Test("pet refuses multiple visible overlays", () => {
            var host=new FakePetHost();host.Add(1);host.Add(2);
            if(!new PetToggle(host,(ms,ct)=>Task.FromResult(0)).Toggle().GetAwaiter().GetResult().Contains("多个")||host.Hides!=0||host.Shows!=0)throw new Exception("ambiguous target changed");
        });
        Test("pet refuses multiple hidden overlays without remembered identity", () => {
            var host=new FakePetHost();host.Add(1,false);host.Add(2,false);
            if(!new PetToggle(host,(ms,ct)=>Task.FromResult(0)).Toggle().GetAwaiter().GetResult().Contains("多个")||host.Hides!=0||host.Shows!=0)throw new Exception("ambiguous hidden target changed");
        });
        Test("pet reuses exact previously hidden target among hidden siblings", () => {
            var host=new FakePetHost();var pet=host.Add(1);var sibling=host.Add(2,false);var toggle=new PetToggle(host,(ms,ct)=>Task.FromResult(0));
            toggle.Toggle().GetAwaiter().GetResult();toggle.Toggle().GetAwaiter().GetResult();
            if(!pet.Visible||sibling.Visible||host.Shows!=1||host.Hides!=1)throw new Exception("wrong hidden sibling selected");
        });
        Test("pet refuses changed target at mutation boundary", () => {
            var host=new FakePetHost();host.Add(1);host.Refuse=true;
            if(!new PetToggle(host,(ms,ct)=>Task.FromResult(0)).Toggle().GetAwaiter().GetResult().Contains("窗口已变化")||host.Hides!=0||host.Shows!=0)throw new Exception("stale target used");
        });
        Test("pet reports unfulfilled hide without false success", () => {
            var host=new FakePetHost();host.Add(1);host.Ignore=true;
            if(!new PetToggle(host,(ms,ct)=>Task.FromResult(0)).Toggle().GetAwaiter().GetResult().Contains("隐藏未成功")||host.Hides!=1||host.Shows!=0)throw new Exception("false success");
        });
        Test("pet verifies hide stays stable rather than temporary disappearance", () => {
            var host=new FakePetHost();var pet=host.Add(1);int checks=0;
            var toggle=new PetToggle(host,(ms,ct)=>{if(++checks>=2)pet.Visible=true;return Task.FromResult(0);});
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("隐藏未成功"))throw new Exception("unstable visibility accepted");
        });
        Test("pet verifies show stays stable rather than temporary appearance", () => {
            var host=new FakePetHost();var pet=host.Add(1,false);int checks=0;
            var toggle=new PetToggle(host,(ms,ct)=>{if(++checks>=2)pet.Visible=false;return Task.FromResult(0);});
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("显示未成功"))throw new Exception("unstable show accepted");
        });
        Test("pet duplicate press and pause do not repeat visibility requests", () => {
            var host=new FakePetHost();host.Add(1);var toggle=new PetToggle(host,(ms,ct)=>Task.Delay(Timeout.Infinite,ct));
            var pending=toggle.Toggle();
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("正在切换")||host.Hides!=1)throw new Exception("duplicate request");
            toggle.CancelPending();if(!pending.GetAwaiter().GetResult().Contains("取消")||toggle.Busy||host.Shows!=0)throw new Exception("pause ignored");
        });
        Test("pet missing window never launches or substitutes keyboard shortcuts", () => {
            var host=new FakePetHost();var result=new PetToggle(host,(ms,ct)=>Task.FromResult(0)).Toggle().GetAwaiter().GetResult();
            if(!result.Contains("宠物菜单")||host.Shows!=0||host.Hides!=0)throw new Exception("missing target fallback");
        });
        Test("pet delayed native show submits exactly one request", () => {
            var host=new FakePetHost();var pet=host.Add(1,false);host.DeferShow=true;int elapsed=0;
            var toggle=new PetToggle(host,(ms,ct)=>{elapsed+=ms;if(elapsed>=1500)pet.Visible=true;return Task.FromResult(0);});
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("已显示")||host.Shows!=1)throw new Exception("delayed native show failed");
        });
        Test("pet detects destruction during a pending request", () => {
            var host=new FakePetHost();host.Add(1);
            var toggle=new PetToggle(host,(ms,ct)=>{host.Windows.Clear();return Task.FromResult(0);});
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("失效"))throw new Exception("destroyed window reported retained");
        });
        Test("pet rejects reused remembered handle among hidden candidates", () => {
            var host=new FakePetHost();var pet=host.Add(1);var toggle=new PetToggle(host,(ms,ct)=>Task.FromResult(0));
            toggle.Toggle().GetAwaiter().GetResult();host.Windows.Clear();var reused=host.Add(1,false);reused.Identity.ProcessStarted=2000;host.Add(2,false);
            if(!toggle.Toggle().GetAwaiter().GetResult().Contains("多个")||host.Shows!=0)throw new Exception("stale handle reused");
        });
        foreach (int front in new[] { 1, 2 })
        {
            int f = front;
            Test("double-hold repeats at 500ms, release leaves no clicks " + f, () => {
                var repeat=ArmRepeat(f);
                if(repeat.Advance(engine,f,659,new IntPtr(42),true)!=RepeatDecision.None)throw new Exception("early repeat");
                if(repeat.Advance(engine,f,660,new IntPtr(42),true)!=RepeatDecision.Backspace)throw new Exception("first repeat missing");
                if(repeat.Advance(engine,f,1159,new IntPtr(42),true)!=RepeatDecision.None || repeat.Advance(engine,f,1160,new IntPtr(42),true)!=RepeatDecision.Backspace)throw new Exception("wrong interval");
                RepeatInput(repeat,f,f,false,1170);
                if(repeat.Advance(engine,f,5000,new IntPtr(42),false)!=RepeatDecision.None)throw new Exception("repeat after release");
                engine.Advance(5000);Expect();
            });
            Test("quick double remains exclusive Delete gesture " + f, () => {
                var repeat=ArmRepeat(f);RepeatInput(repeat,f,f,false,200);engine.Advance(500);Expect(f+".double");
                if(repeat.Armed)throw new Exception("still armed");
            });
            Test("triple remains triple and never arms on third press " + f, () => {
                var repeat=ArmRepeat(f);RepeatInput(repeat,f,f,false,200);RepeatInput(repeat,f,f,true,260);
                if(repeat.Armed)throw new Exception("third press armed repeat");
                RepeatInput(repeat,f,f,false,300);engine.Advance(900);Expect(f+".triple");
            });
            Test("delayed UI drains release before repeat without late Delete " + f, () => {
                var repeat=ArmRepeat(f);RepeatInput(repeat,f,f,false,900);
                if(repeat.Advance(engine,f,1500,new IntPtr(42),false)!=RepeatDecision.None)throw new Exception("late repeat");
                engine.Advance(1500);Expect();
            });
            Test("foreground change cancels repeat and cannot resume " + f, () => {
                var repeat=ArmRepeat(f);
                if(repeat.Advance(engine,f,400,new IntPtr(99),true)!=RepeatDecision.Cancelled)throw new Exception("focus ignored");
                if(repeat.Advance(engine,f,1200,new IntPtr(42),true)!=RepeatDecision.None)throw new Exception("repeat resumed");
                RepeatInput(repeat,f,f,false,1300);engine.Advance(2000);Expect();
            });
            Test("main button or wheel cancels pending repeat " + f, () => {
                var repeat=ArmRepeat(f);RepeatInput(repeat,f,f,false,250,"wheelup");
                if(repeat.Armed)throw new Exception("combo still repeats");
                RepeatInput(repeat,f,f,false,300);engine.Advance(1000);Expect(f+".wheelup");
            });
            Test("other side cancels repeat but keeps three second help " + f, () => {
                engine=new GestureEngine(300,650,150);engine.Recognized+=g=>actual.Add(g.Button+"."+g.Kind);
                var repeat=ArmRepeat(f);RepeatInput(repeat,f,3-f,true,300);
                if(repeat.Armed)throw new Exception("chord still repeats");
                engine.Advance(3299);Expect("0.chord");engine.Advance(3300);engine.Advance(6000);Expect("0.chord","0.chordhold");
            });
            Test("no repeat from a single long press or expired first click " + f, () => {
                var repeat=new DoubleHoldBackspace();RepeatInput(repeat,f,f,true,0);engine.Advance(650);
                if(repeat.Armed)throw new Exception("single hold armed");RepeatInput(repeat,f,f,false,1000);Expect(f+".hold");
                RepeatInput(repeat,f,f,true,1100);RepeatInput(repeat,f,f,false,1140);RepeatInput(repeat,f,f,true,1440);
                if(repeat.Armed)throw new Exception("expired click armed");
            });
        }
        Test("repeat does not catch up with a destructive burst after delayed tick", () => {
            var repeat=ArmRepeat(2);
            if(repeat.Advance(engine,2,4000,new IntPtr(42),true)!=RepeatDecision.Backspace)throw new Exception("missing key");
            if(repeat.Advance(engine,2,4001,new IntPtr(42),true)!=RepeatDecision.None || repeat.Advance(engine,2,4499,new IntPtr(42),true)!=RepeatDecision.None)throw new Exception("catch-up burst");
            if(repeat.Advance(engine,2,4500,new IntPtr(42),true)!=RepeatDecision.Backspace)throw new Exception("next interval");
        });
        Test("repeat reservation works with short configured hold threshold", () => {
            engine=new GestureEngine(300,300,150);engine.Recognized+=g=>actual.Add(g.Button+"."+g.Kind);
            var repeat=ArmRepeat(2);engine.Advance(600);Expect();
            if(repeat.Advance(engine,2,660,new IntPtr(42),true)!=RepeatDecision.Backspace)throw new Exception("hold stole repeat");
            engine.Advance(1000);Expect();
        });
        Test("pause/reset cancels repeat and late release cannot become Delete", () => {
            var repeat=ArmRepeat(2);RepeatInput(repeat,2,0,false,300,"reset");RepeatInput(repeat,2,2,false,800);
            if(repeat.Armed)throw new Exception("pause ignored");engine.Advance(1500);Expect();
        });
        Test("disabled sofa mode keeps original double gesture", () => {
            var repeat=new DoubleHoldBackspace();
            foreach(long t in new long[]{0,160}) {RepeatInput(repeat,2,2,true,t,null,false);RepeatInput(repeat,2,2,false,t+40,null,false);}
            if(repeat.Armed)throw new Exception("disabled repeat armed");engine.Advance(500);Expect("2.double");
        });
        Test("repeat sends Backspace and help is not a keyboard/display action", () => {
            if(!Hotkeys.Parse("Backspace").SequenceEqual(new ushort[]{8}) || Hotkeys.Parse("ShowHelp").Length!=0 || SofaBindings.Shortcut("both.hold")!="ShowHelp")throw new Exception("wrong action");
        });
        foreach(int front in new[]{1,2})
        {
            int f=front; ushort down=(ushort)(f==1?0x40:0x100), up=(ushort)(down*2);
            Test("verified physical repeat survives five seconds and ends on raw release " + f, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true,true,false);input.RecordRawButtons(new IntPtr(7),down,160);input.ProcessSide(f,true,1,160);input.SetRepeatHold(f,160);
                    input.ExpireStaleHolds(10160);if(!input.IsRepeatHeld(f,160))throw new Exception("verified hold expired");
                    if(input.ProcessMain(0x201,0,0,10200)||input.ProcessMain(0x202,0,0,10210))throw new Exception("ordinary click blocked");
                    input.RecordRawButtons(new IntPtr(7),up,10300);if(input.IsRepeatHeld(f,160))throw new Exception("raw release ignored");
                    input.ExpireStaleHolds(10400);if(input.IsRepeatHeld(f,160))throw new Exception("stale state persisted");
                }
            });
            Test("repeat without raw evidence retains missing-release protection " + f, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true,true,false);input.ProcessSide(f,true,1,0);input.SetRepeatHold(f,0);input.ExpireStaleHolds(5000);
                    if(input.IsRepeatHeld(f,0))throw new Exception("unverified indefinite hold");
                }
            });
            Test("old or multiple raw devices cannot bypass five-second protection " + f, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true,true,false);input.RecordRawButtons(new IntPtr(7),down,0);input.ProcessSide(f,true,1,1000);input.SetRepeatHold(f,1000);input.ExpireStaleHolds(6000);
                    if(input.IsRepeatHeld(f,1000))throw new Exception("old evidence reused");
                    input.ProcessSide(f,false,1,6010);input.ProcessSide(f,true,1,7000);input.SetRepeatHold(f,7000);
                    input.RecordRawButtons(new IntPtr(8),down,7000);input.RecordRawButtons(new IntPtr(9),down,7000);input.ExpireStaleHolds(12000);
                    if(input.IsRepeatHeld(f,7000))throw new Exception("ambiguous evidence trusted");
                }
            });
            Test("raw device removal stops repeat even if low level release is lost " + f, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true);input.RecordRawButtons(new IntPtr(7),down,0);input.ProcessSide(f,true,1,0);input.SetRepeatHold(f,0);input.RawDeviceRemoved(new IntPtr(7));
                    if(input.IsRepeatHeld(f,0))throw new Exception("removed device still held");
                }
            });
            Test("first click raw evidence cannot verify the second held press " + f, () => {
                using(var input=new NativeMouse()) {
                    input.Configure(true,true);input.RecordRawButtons(new IntPtr(7),down,0);input.ProcessSide(f,true,1,0);input.ProcessSide(f,false,1,40);
                    input.ProcessSide(f,true,1,160);input.SetRepeatHold(f,160);input.ExpireStaleHolds(5160);
                    if(input.IsRepeatHeld(f,160))throw new Exception("first click's raw evidence reused for repeat");
                }
            });
        }
        Console.WriteLine("RESULT: " + passed + " passed; " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
