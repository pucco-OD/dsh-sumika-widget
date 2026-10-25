using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using CodexPet;

class PlaybackTests {
    static int count;
    static Dictionary<PetState, ClipInfo> clips;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS " + name); }
    static Playback New() { return new Playback(clips); }
    static Playback Resting() { var p = New(); FinishCycle(p); return p; }
    static Playback Working() { var p = Resting(); p.Request(PetState.Working); FinishCycle(p); FinishCycle(p); return p; }
    static void FinishCycle(Playback p) {
        var initial = p.Current;
        int remaining = p.Clip.frames.Length - p.FrameIndex;
        for (int i = 0; i < remaining; i++) {
            if (p.Current != initial) throw new Exception("Switched before the last frame finished");
            p.Advance(p.Clip.frames[p.FrameIndex].durationMs - p.FrameElapsed);
        }
    }
    static void Main() {
        try {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            var manifest = new JavaScriptSerializer().Deserialize<AnimationManifest>(File.ReadAllText(Path.Combine(root, "assets/animations.json")));
            clips = manifest.clips.ToDictionary(c => (PetState)Enum.Parse(typeof(PetState), c.id, true));
            var p = New();
            Check(p.Current == PetState.IdleEnter && !p.IsWorking, "startup plays idle entrance once");
            p.Request(PetState.Idle); p.Request(PetState.Idle); FinishCycle(p);
            Check(p.Current == PetState.Idle, "idle snapshots allow startup entrance to finish");
            FinishCycle(p); Check(p.Current == PetState.Idle && p.Loops == 1, "idle loops");
            p.Request(PetState.Working);
            Check(p.Current == PetState.IdleExit && p.IsWorking, "new work first dissolves idle scene");
            p.Advance(10); p.Request(PetState.Working); p.Request(PetState.Working);
            Check(p.Current == PetState.IdleExit && p.FrameElapsed == 10, "repeated work does not restart idle dissolve");
            FinishCycle(p);
            Check(p.Current == PetState.WorkingEnter, "idle dissolve finishes before working entrance");
            p.Advance(10); p.Request(PetState.Working);
            Check(p.Current == PetState.WorkingEnter && p.FrameElapsed == 10, "repeated work does not restart working entrance");
            FinishCycle(p);
            Check(p.Current == PetState.Working && p.FrameIndex == 0, "working entrance plays once before work loop");
            FinishCycle(p);
            Check(p.Current == PetState.Working && p.Loops == 1, "work loops");
            p.Advance(10); p.Request(PetState.Waiting);
            Check(p.Current == PetState.Waiting && p.FrameIndex == 0 && p.FrameElapsed == 0, "question interrupts work immediately");
            FinishCycle(p); Check(p.Current == PetState.Waiting && p.Loops == 1, "waiting loops despite source WebP single-play flag");
            p.Advance(20); p.Request(PetState.Working);
            Check(p.Current == PetState.Waiting && p.ResumePending && p.FrameElapsed == 20, "answer does not restart or interrupt current cycle");
            while (p.FrameIndex < p.Clip.frames.Length - 1) p.Advance(p.Clip.frames[p.FrameIndex].durationMs - p.FrameElapsed);
            p.Advance(p.Clip.frames[p.FrameIndex].durationMs - p.FrameElapsed - 1);
            Check(p.Current == PetState.Waiting, "last frame keeps full display duration");
            p.Advance(1); Check(p.Current == PetState.Working && p.FrameIndex == 0, "resumes exactly at loop boundary");
            p.Request(PetState.Waiting); p.Request(PetState.Working); p.Request(PetState.Waiting);
            FinishCycle(p); Check(p.Current == PetState.Waiting && !p.ResumePending, "new question cancels pending resume");
            p.Request(PetState.Working); p.Request(PetState.Completed);
            Check(p.Current == PetState.Completed && !p.ResumePending && p.FrameIndex == 0, "completion interrupts waiting and cancels pending resume");
            FinishCycle(p); Check(p.Current == PetState.IdleEnter && !p.IsWorking, "completion plays once then idle entrance");
            p.Request(PetState.Idle); p.Request(PetState.Idle); FinishCycle(p);
            Check(p.Current == PetState.Idle, "idle entrance plays once then idle loops");
            p.Request(PetState.Completed); p.Request(PetState.Working);
            Check(p.Current == PetState.WorkingEnter && p.IsWorking, "new task interrupts completion with working entrance");
            p.Request(PetState.Completed); p.Request(PetState.Waiting);
            Check(p.Current == PetState.Waiting, "new question interrupts completion reminder");
            p = Resting(); int alerts = 0;
            p.Entered += delegate(PetState s) { if (s == PetState.Waiting || s == PetState.Completed) alerts++; };
            p.Request(PetState.Waiting); FinishCycle(p); FinishCycle(p); p.Request(PetState.Waiting);
            Check(alerts == 1, "looping and repeated snapshots do not repeat waiting sound");
            p.Request(PetState.Completed); p.Request(PetState.Completed); FinishCycle(p);
            Check(alerts == 2, "completion sound triggers once");
            FinishCycle(p); FinishCycle(p);
            Check(alerts == 2 && p.Current == PetState.Idle, "entrances and idle loop never trigger alert sounds");
            p = New(); p.Advance(10); p.Request(PetState.Working);
            Check(p.Current == PetState.IdleEnter && p.FrameElapsed == 10 && p.IsWorking, "work arriving during idle entrance preserves current frames");
            FinishCycle(p); Check(p.Current == PetState.IdleExit, "pending work follows idle entrance with idle dissolve");
            p.Request(PetState.Idle); FinishCycle(p);
            Check(p.Current == PetState.IdleEnter && !p.IsWorking, "cancelled work restores idle scene after dissolve");
            FinishCycle(p); Check(p.Current == PetState.Idle, "cancelled transition returns to idle loop");
            p = Resting(); p.Request(PetState.Working); FinishCycle(p);
            p.Request(PetState.Idle); FinishCycle(p);
            Check(p.Current == PetState.IdleEnter && !p.IsWorking, "work cancelled during entrance returns to idle entrance");
            p = Working(); p.Request(PetState.Idle);
            Check(p.Current == PetState.IdleEnter, "direct idle request also uses idle entrance");
            foreach (PetState transition in new[] { PetState.IdleEnter, PetState.IdleExit, PetState.WorkingEnter }) {
                p = transition == PetState.IdleEnter ? New() : Resting();
                if (transition != PetState.IdleEnter) p.Request(PetState.Working);
                if (transition == PetState.WorkingEnter) FinishCycle(p);
                p.Advance(10); p.Request(PetState.Waiting);
                Check(p.Current == PetState.Waiting && p.FrameIndex == 0 && p.IsWorking, "question immediately preempts " + transition);
                p.Request(PetState.Working); FinishCycle(p);
                Check(p.Current == PetState.Working, "answer resumes desk loop without entrance after interrupting " + transition);
                p = transition == PetState.IdleEnter ? New() : Resting();
                if (transition != PetState.IdleEnter) p.Request(PetState.Working);
                if (transition == PetState.WorkingEnter) FinishCycle(p);
                p.Request(PetState.Completed);
                Check(p.Current == PetState.Completed && !p.IsWorking, "short task completion immediately preempts " + transition);
                FinishCycle(p); Check(p.Current == PetState.IdleEnter, "preempted " + transition + " cannot restart after completion");
            }
            p = Resting(); p.Request(PetState.Working);
            while (p.FrameIndex < p.Clip.frames.Length - 1) p.Advance(p.Clip.frames[p.FrameIndex].durationMs - p.FrameElapsed);
            p.Advance(p.Clip.frames[p.FrameIndex].durationMs - p.FrameElapsed - 1);
            Check(p.Current == PetState.IdleExit, "last dissolve frame keeps full source duration");
            p.Advance(1); Check(p.Current == PetState.WorkingEnter && p.FrameElapsed == 0, "transition changes exactly at last frame boundary");
            foreach (var clip in clips.Values) {
                Check(clip.frames.Sum(f => f.durationMs) == clip.durationMs, clip.id + " exact source timing");
                Check(File.Exists(Path.Combine(root,"assets",clip.source)), clip.id + " WebP source exists");
            }
            File.WriteAllText(Path.Combine(root, "tests/playback-results.json"), "{\"passed\":" + count + ",\"failed\":0}");
            Console.WriteLine(count + " checks passed.");
        } catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
    }
}
