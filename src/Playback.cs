using System;
using System.Collections.Generic;

namespace CodexPet {
    public enum PetState { Idle, Working, Waiting, Completed, IdleEnter, IdleExit, WorkingEnter }
    public sealed class FrameInfo {
        public string file { get; set; }
        public int durationMs { get; set; }
    }
    public sealed class ClipInfo {
        public string id { get; set; }
        public string source { get; set; }
        public string sourceSha256 { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public int stageWidth { get; set; }
        public int stageHeight { get; set; }
        public int offsetY { get; set; }
        public int durationMs { get; set; }
        public FrameInfo[] frames { get; set; }
    }
    public sealed class AnimationManifest { public int version { get; set; } public ClipInfo[] clips { get; set; } }

    // A frame finishes only after its full display interval. Wall time does not
    // skip over unseen frames when the UI stalls or the computer resumes.
    public sealed class Playback {
        readonly Dictionary<PetState, ClipInfo> clips;
        public PetState Current { get; private set; }
        public PetState Desired { get; private set; }
        public int FrameIndex { get; private set; }
        public double FrameElapsed { get; private set; }
        public int Loops { get; private set; }
        public bool ResumePending { get { return Current == PetState.Waiting && Desired == PetState.Working; } }
        public bool IsWorking { get { return Desired == PetState.Working || Desired == PetState.Waiting; } }
        public ClipInfo Clip { get { return clips[Current]; } }
        public event Action<PetState> Entered;
        public event Action FrameChanged;
        public event Action<PetState> CycleFinished;

        public Playback(Dictionary<PetState, ClipInfo> source) {
            clips = source;
            foreach (PetState state in Enum.GetValues(typeof(PetState))) {
                if (!clips.ContainsKey(state)) throw new ArgumentException("Missing clip: " + state);
                var clip = clips[state]; int sum = 0;
                if (clip.frames == null || clip.frames.Length == 0) throw new ArgumentException("Empty clip: " + state);
                foreach (var frame in clip.frames) {
                    if (frame.durationMs <= 0) throw new ArgumentException("Invalid frame duration");
                    sum += frame.durationMs;
                }
                if (sum != clip.durationMs) throw new ArgumentException("Frame timing mismatch: " + state);
            }
            Current = PetState.IdleEnter; Desired = PetState.Idle;
        }
        public void Request(PetState state) {
            if (state == PetState.IdleEnter || state == PetState.IdleExit || state == PetState.WorkingEnter)
                throw new ArgumentException("Transitions are selected by playback, not requested as agent states");
            if (state == PetState.Completed) {
                // Deduplication of business events belongs at the input boundary.
                Desired = PetState.Idle;
                if (Current != PetState.Completed) Switch(PetState.Completed);
                return;
            }
            Desired = state;
            if (state == PetState.Waiting) {
                if (Current != PetState.Waiting) Switch(PetState.Waiting);
                return;
            }
            // Repeated snapshots update the destination without restarting a
            // one-shot transition or showing a half-appeared scene at full size.
            if (Current == PetState.IdleEnter || Current == PetState.IdleExit || Current == PetState.WorkingEnter) return;
            if (state == PetState.Working) {
                if (Current == PetState.Idle) Switch(PetState.IdleExit);
                else if (Current == PetState.Completed) Switch(PetState.WorkingEnter);
                // Waiting resumes at its loop boundary in the same desk scene.
                return;
            }
            if (Current != PetState.Idle && Current != PetState.Completed) Switch(PetState.IdleEnter);
        }
        void Switch(PetState state) {
            Current = state; FrameIndex = 0; FrameElapsed = 0; Loops = 0;
            if (Entered != null) Entered(state);
            if (FrameChanged != null) FrameChanged();
        }
        public void Advance(double elapsedMs) {
            if (elapsedMs < 0) throw new ArgumentOutOfRangeException("elapsedMs");
            var duration = Clip.frames[FrameIndex].durationMs;
            FrameElapsed += elapsedMs;
            if (FrameElapsed < duration) return;
            // Advance at most one frame per paint tick. Preserve every pose.
            FrameElapsed -= duration;
            if (FrameIndex < Clip.frames.Length - 1) {
                FrameIndex++;
                if (FrameChanged != null) FrameChanged();
                return;
            }
            var previous = Current;
            Loops++;
            if (CycleFinished != null) CycleFinished(previous);
            if (Current == PetState.Completed) { Switch(PetState.IdleEnter); return; }
            if (Current == PetState.IdleEnter) { Switch(IsWorking ? PetState.IdleExit : PetState.Idle); return; }
            if (Current == PetState.IdleExit) { Switch(IsWorking ? PetState.WorkingEnter : PetState.IdleEnter); return; }
            if (Current == PetState.WorkingEnter) { Switch(IsWorking ? PetState.Working : PetState.IdleEnter); return; }
            if (ResumePending) { Switch(PetState.Working); return; }
            FrameIndex = 0;
            if (FrameChanged != null) FrameChanged();
        }
    }
}
