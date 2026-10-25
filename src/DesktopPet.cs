using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CodexPet {
    public sealed class Settings {
        public bool enabled { get; set; }
        public int height { get; set; }
        public int margin { get; set; }
        public double volume { get; set; }
        public bool muted { get; set; }
        [ScriptIgnore] public bool hidden { get; set; }
        [ScriptIgnore] public bool autoConnect { get; set; }
        public string waitingSound { get; set; }
        public string completedSound { get; set; }
        public double left { get; set; }
        public double top { get; set; }
        public bool hasPosition { get; set; }
        public Settings() { enabled = true; height = 320; margin = 24; volume = .55; autoConnect = true; }
    }
    public sealed class Prompt {
        public string id { get; set; }
        public string sessionId { get; set; }
        public string kind { get; set; }
        public string header { get; set; }
        public string question { get; set; }
        public bool answerable { get; set; }
        public QuestionData[] questions { get; set; }
    }
    public sealed class BridgeEvent {
        public long seq { get; set; }
        public string type { get; set; }
        public string reason { get; set; }
        public string sessionId { get; set; }
        public string promptId { get; set; }
    }
    public sealed class BridgeState {
        public long seq { get; set; }
        public string[] running { get; set; }
        public Prompt[] prompts { get; set; }
        public BridgeEvent[] events { get; set; }
    }
    public sealed class BridgeCredentials {
        public string origin { get; set; }
        public string token { get; set; }
    }

    public static class Native {
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint flags; }
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        public delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        public static Native.MONITORINFO WorkArea(IntPtr hwnd) {
            var m = new Native.MONITORINFO(); m.cbSize = Marshal.SizeOf(m);
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref m)) throw new InvalidOperationException("Cannot read monitor work area");
            return m;
        }
    }

    public sealed class PetWindow : Window {
        readonly string root;
        readonly string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets");
        readonly DshLifetime owner;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        readonly Settings settings;
        readonly AnimationManifest manifest;
        readonly Dictionary<PetState, ClipInfo> clips = new Dictionary<PetState, ClipInfo>();
        readonly Dictionary<PetState, BitmapSource[]> pictures = new Dictionary<PetState, BitmapSource[]>();
        readonly Playback playback;
        readonly Canvas canvas = new Canvas();
        readonly Image sprite = new Image { Stretch = Stretch.Fill };
        readonly MediaPlayer sound = new MediaPlayer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastPressTime = -1000000d, lastPressX, lastPressY;
        readonly DispatcherTimer bridgeTimer = new DispatcherTimer();
        readonly DispatcherTimer maintenanceTimer = new DispatcherTimer();
        readonly DispatcherTimer resolutionTimer = new DispatcherTimer();
        FileSystemWatcher commandWatcher;
        int commandDispatchScheduled;
        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        IntPtr hwnd;
        bool clamping, polling, closing, manual, loaded, openingDesktop;
        bool? previewAutoConnect;
        bool bridgeInitialized;
        long lastSeq = -1;
        int idleSnapshots;
        double lastFrameTime;
        string connection = "正在连接 DeepSeek Harness…", activePrompt = "", promptText = "";
        string bridgeIdentity = "";
        QuestionWindow questionWindow;
        Prompt questionPrompt;
        string dismissedQuestion="";
        readonly HashSet<string> finishedPrompts = new HashSet<string>();
        DispatcherTimer demoTimer;
        double demoStart;
        int demoStep;
        int pictureGeneration;
        int cachedHeight, cacheReloads;
        double cachedDpi;
        long renderTicks;
        int soundStarts;
        string lastSound = "";
        public PetWindow(string directory,DshLifetime lifetime) {
            root = directory;
            owner = lifetime;
            settings = ReadSettings();
            manual = false;
            manifest = json.Deserialize<AnimationManifest>(File.ReadAllText(Path.Combine(assets, "animations.json")));
            foreach (var clip in manifest.clips) clips.Add((PetState)Enum.Parse(typeof(PetState), clip.id, true), clip);
            playback = new Playback(clips);
            Title = "Sumika";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            Content = canvas; canvas.Children.Add(sprite);
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.NearestNeighbor);
            Cursor = Cursors.Hand;
            Width = settings.height * 673.0 / 1230; Height = settings.height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (settings.hasPosition) { Left = settings.left; Top = settings.top; }
            else { Left = SystemParameters.WorkArea.Right - Width - settings.margin; Top = SystemParameters.WorkArea.Bottom - Height - settings.margin; }
            SourceInitialized += OnSourceInitialized;
            Loaded += OnLoaded;
            LocationChanged += delegate { if (loaded) ClampPosition(); };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                double pressTime = clock.Elapsed.TotalMilliseconds;
                Point pressAt = e.GetPosition(this);
                bool second = pressTime - lastPressTime <= 400d && Math.Abs(pressAt.X - lastPressX) <= 4d && Math.Abs(pressAt.Y - lastPressY) <= 4d;
                lastPressTime = pressTime; lastPressX = pressAt.X; lastPressY = pressAt.Y;
                if(second) {
                    lastPressTime = -1000000d;
                    Log("pet-double-click");
                    OpenDesktop();
                    e.Handled=true;return;
                }
                try { DragMove(); ClampPosition(); SaveSettings(); } catch (InvalidOperationException) { }
            };
            Closed += delegate { Shutdown(); };
            playback.FrameChanged += RenderFrame;
            playback.Entered += delegate(PetState state) {
                lastFrameTime = clock.Elapsed.TotalMilliseconds;
                Log("enter " + state);
                if (state == PetState.Waiting || state == PetState.Completed) PlaySound(state);
 WriteDiagnostics();
            };
            playback.CycleFinished += delegate(PetState state) { Log("cycle-finished " + state); };
            sound.MediaFailed += delegate(object s, ExceptionEventArgs e) { Log("sound-error " + e.ErrorException.Message); };
            sound.MediaOpened += delegate { Log("sound-opened " + lastSound); };
            http.DefaultRequestHeaders.Add("x-dsh-pet", "codex-desktop");
            bridgeTimer.Interval = TimeSpan.FromMilliseconds(500);
            bridgeTimer.Tick += async delegate { await PollBridge(); };
            maintenanceTimer.Interval = TimeSpan.FromSeconds(1);
            maintenanceTimer.Tick += delegate {
                if (!Topmost) Topmost = true;
                Native.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
                ClampPosition(); ReadTestCommand(); WriteDiagnostics();
            };
            resolutionTimer.Interval = TimeSpan.FromMilliseconds(800);
            resolutionTimer.Tick += delegate { resolutionTimer.Stop(); RefreshPictureScale(); };
            SystemEventsSubscribe();
        }
        // Both entry points ask DSH's own Tray click handler to restore Electron.
        // onRestored runs on the UI thread and only when the window really came back.
        async void OpenDesktop(Action onRestored=null) {
            if(openingDesktop || closing)return;
            openingDesktop=true;
            try {
                bool restored=await DshActivation.OpenAsync(owner.DesktopPid,owner.IsAlive,Log);
                if(restored) {
                    if(onRestored!=null&&!closing)try { onRestored(); } catch(Exception error) { Log("desktop-restored-error " + error.Message); }
                } else Log("desktop-restore-failed");
            } catch(Exception error) { Log("desktop-open-failed " + error.Message); }
            finally { openingDesktop=false; }
        }
        Settings ReadSettings() {
            try {
                var result = json.Deserialize<Settings>(File.ReadAllText(Path.Combine(root, "settings.json")));
                result.height = Math.Max(180, Math.Min(600, result.height));
                result.margin = Math.Max(8, Math.Min(100, result.margin));
                result.volume = result.muted ? 0 : Math.Max(0, Math.Min(1, result.volume));
                result.muted = false; result.hidden = false; result.autoConnect = true; return result;
            } catch { return new Settings(); }
        }
        void SaveSettings() {
            settings.left = Left; settings.top = Top; settings.hasPosition = true;
            settings.autoConnect = previewAutoConnect ?? !manual;
            try {
                var file = Path.Combine(root, "settings.json");
                File.WriteAllText(file + ".tmp", json.Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(file + ".tmp", file, null); else File.Move(file + ".tmp", file);
            } catch (Exception e) { Log("settings-error " + e.Message); }
        }
        void OnSourceInitialized(object sender, EventArgs e) {
            hwnd = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(hwnd, -20);
            Native.SetWindowLong(hwnd, -20, ex | 0x80 | 0x08000000); // Tool window; do not take focus from the user's work.
        }
        async void OnLoaded(object sender, RoutedEventArgs e) {
            try {
                double dpi = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
                var decoded = await Task.Run(delegate { return LoadPictures(settings.height, dpi, 0); });
                foreach (var pair in decoded) pictures.Add(pair.Key, pair.Value);
                cachedHeight = settings.height; cachedDpi = dpi;
                loaded = true; RenderFrame(); ClampPosition();
                SaveSettings();
                WatchCommands();
                lastFrameTime = clock.Elapsed.TotalMilliseconds;
                CompositionTarget.Rendering += OnRendering;
                bridgeTimer.Start(); maintenanceTimer.Start();
                Log("started; decoded " + clips.Values.Sum(c => c.frames.Length) + " frames");
                WriteDiagnostics();
                if (settings.hidden) Hide();
                await PollBridge();
            } catch (Exception ex) {
                Log("startup-error " + ex);
                MessageBox.Show("挂件启动失败：\n" + ex.Message + "\n请查看 runtime.log。", "桌面伙伴"); Close();
            }
        }
        Dictionary<PetState, BitmapSource[]> LoadPictures(int targetHeight, double dpi, int generation) {
            var result = new Dictionary<PetState, BitmapSource[]>();
            foreach (var pair in clips) {
                result.Add(pair.Key, WebpFrames.Decode(assets,pair.Value,targetHeight,dpi,()=>generation!=pictureGeneration));
            }
            return result;
        }
        async void RefreshPictureScale() {
            double dpi = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
            if (settings.height <= cachedHeight && dpi <= cachedDpi) return;
            int generation = ++pictureGeneration;
            int targetHeight = Math.Max(settings.height, cachedHeight);
            dpi = Math.Max(dpi, cachedDpi);
            try {
                var decoded = await Task.Run(delegate { return LoadPictures(targetHeight, dpi, generation); });
                if (generation != pictureGeneration || closing) return;
                pictures.Clear(); foreach (var pair in decoded) pictures.Add(pair.Key, pair.Value);
                cachedHeight = targetHeight; cachedDpi = dpi; cacheReloads++;
                RenderFrame();
            } catch (OperationCanceledException) { }
            catch (Exception ex) { Log("image-resize-error " + ex.Message); }
        }
        void RenderFrame() {
            if (!loaded) return;
            var clip = playback.Clip;
            double scale = Height / clip.stageHeight;
            sprite.Width = clip.width * scale; sprite.Height = clip.height * scale;
            Canvas.SetLeft(sprite, (Width - sprite.Width) / 2);
            Canvas.SetTop(sprite, clip.offsetY * scale);
            sprite.Source = pictures[playback.Current][playback.FrameIndex];
        }
        void OnRendering(object sender, EventArgs e) {
            renderTicks++;
            double now = clock.Elapsed.TotalMilliseconds, delta = now - lastFrameTime; lastFrameTime = now;
            // Suspend/resume and long UI stalls must not jump through whole animations.
            if (delta > 250 || delta < 0) delta = 0;
            playback.Advance(delta);
        }
        void ClampPosition() {
            if (clamping || hwnd == IntPtr.Zero) return;
            clamping = true;
            try {
                var monitor = Native.WorkArea(hwnd); Native.RECT r; Native.GetWindowRect(hwnd, out r);
                var src = PresentationSource.FromVisual(this);
                double dpi = src == null ? 1 : src.CompositionTarget.TransformToDevice.M11;
                int margin = (int)Math.Ceiling(settings.margin * dpi);
                int width = r.Right - r.Left, height = r.Bottom - r.Top;
                int x = Math.Max(monitor.rcWork.Left + margin, Math.Min(monitor.rcWork.Right - margin - width, r.Left));
                int y = Math.Max(monitor.rcWork.Top + margin, Math.Min(monitor.rcWork.Bottom - margin - height, r.Top));
                if (x != r.Left || y != r.Top) Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0015);
            } finally { clamping = false; }
        }
        void SetSize(int height, bool persist = true) {
            if (height == settings.height && Math.Abs(Height - height) < .01) return;
            double bottom = Top + Height, center = Left + Width / 2;
            settings.height = height; Height = height; Width = height * 673.0 / 1230;
            Left = center - Width / 2; Top = bottom - Height;
            RenderFrame(); ClampPosition(); if (persist) SaveSettings();
            // Reuse the existing frozen frames during a drag. Increase cache
            // quality only once after the user settles on a larger size.
            resolutionTimer.Stop(); resolutionTimer.Start();
        }
        void PlaySound(PetState state) {
            if (settings.volume <= 0) return;
            string custom = state == PetState.Waiting ? settings.waitingSound : settings.completedSound;
            if (!String.IsNullOrEmpty(custom) && !Path.IsPathRooted(custom)) custom = Path.Combine(root, custom);
            string fallback = Path.Combine(assets, "sounds", state == PetState.Waiting ? "waiting.wav" : "completed.wav");
            string file = !String.IsNullOrEmpty(custom) && File.Exists(custom) ? custom : fallback;
            try { sound.Stop(); lastSound = state.ToString(); soundStarts++; sound.Open(new Uri(Path.GetFullPath(file))); sound.Volume = settings.volume; sound.Play(); }
            catch (Exception ex) { Log("sound-error " + ex.Message); }
        }
        string StatusText() {
            return playback.IsWorking ? "工作中" : "休息中";
        }

        void ManualRequest(PetState state) {
            StopDemo(); manual = true; SaveSettings(); playback.Request(state);
        }
        void StartDemo() {
            StopDemo(); manual = true; SaveSettings();
            playback.Request(PetState.Idle); playback.Request(PetState.Working);
            demoStart = clock.Elapsed.TotalMilliseconds; demoStep = 0;
            demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            demoTimer.Tick += delegate {
                double t = clock.Elapsed.TotalMilliseconds - demoStart;
                if (demoStep == 0 && t >= 4200) { playback.Request(PetState.Waiting); demoStep++; }
                if (demoStep == 1 && t >= 6400) { playback.Request(PetState.Working); demoStep++; }
                if (demoStep == 2 && t >= 12500) { playback.Request(PetState.Completed); demoStep++; }
                if (demoStep == 3 && playback.Current == PetState.Idle) { StopDemo(); }
            };
            demoTimer.Start();
        }
        void StopDemo() { if (demoTimer != null) { demoTimer.Stop(); demoTimer = null; } }
        async void SetConnected() {
            previewAutoConnect = null;
            StopDemo(); manual = false; bridgeInitialized = false; lastSeq = -1; activePrompt = ""; promptText = "";
            connection = "正在连接 DeepSeek Harness…"; SaveSettings(); await PollBridge();
        }
        async Task PollBridge() {
            if (manual || polling || closing) return;
            polling = true;
            try {
                string credentialsFile = Path.Combine(root,".sumika-bridge.json");
                if (!File.Exists(credentialsFile)) { connection = "未找到 DSH 桥接插件，稍后自动重连"; return; }
                var credentials = json.Deserialize<BridgeCredentials>(File.ReadAllText(credentialsFile));
                Uri uri;
                if (!Uri.TryCreate(credentials.origin, UriKind.Absolute, out uri) || !uri.IsLoopback || uri.Scheme != "http") throw new InvalidOperationException("Bridge must use local HTTP");
                string identity = credentials.origin + "|" + credentials.token;
                if (bridgeIdentity != identity) { bridgeIdentity = identity; bridgeInitialized = false; lastSeq = -1; activePrompt = ""; }
                using (var request = new HttpRequestMessage(HttpMethod.Get, credentials.origin.TrimEnd('/') + "/sumika-widget/agent-state?after=" + lastSeq)) {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credentials.token);
                    using (var response = await http.SendAsync(request)) {
                        response.EnsureSuccessStatusCode();
                        string body = await response.Content.ReadAsStringAsync();
                        if (manual || closing) return;
                        var state = json.Deserialize<BridgeState>(body);
                        connection = "已连接 DeepSeek Harness";
                        ApplyBridge(state);
                    }
                }
            } catch (Exception ex) {
                connection = "DSH 连接暂不可用，正在自动重连";
                Log("bridge-unavailable " + ex.GetType().Name);
            } finally { polling = false; }
        }
        void ApplyBridge(BridgeState state) {
            bool first = !bridgeInitialized || state.seq < lastSeq;
            var prompts = state.prompts ?? new Prompt[0];
            var running = state.running ?? new string[0];
            var events = first ? new BridgeEvent[0] : (state.events ?? new BridgeEvent[0]).Where(e => e.seq > lastSeq).OrderBy(e => e.seq).ToArray();
            lastSeq = state.seq; bridgeInitialized = true;
            finishedPrompts.RemoveWhere(id => !prompts.Any(p => p.id == id));
            var endings = events.Where(e => e.type == "turn-end" && e.reason == "completed").ToArray();
            foreach (var ending in endings) {
                foreach (var prompt in prompts.Where(p => p.sessionId == ending.sessionId&&!p.answerable)) {
                    bool newerQuestion = events.Any(e => e.type == "prompt" && e.promptId == prompt.id && e.seq > ending.seq);
                    if (!newerQuestion) finishedPrompts.Add(prompt.id);
                }
            }
            prompts = prompts.Where(p => !finishedPrompts.Contains(p.id)).ToArray();
            SyncQuestion(prompts.FirstOrDefault(p=>p.answerable&&p.questions!=null&&p.questions.Length>0));
            running = running.Where(id => !endings.Any(e => e.sessionId == id)).ToArray();
            if (endings.Length > 0 && running.Length == 0 && prompts.Length == 0) {
                activePrompt = ""; promptText = ""; idleSnapshots = 0;
                playback.Request(PetState.Completed); return;
            }
            if (prompts.Length > 0) {
                idleSnapshots = 0;
                bool newPrompt = activePrompt != prompts[0].id;
                bool alreadyWaiting = playback.Current == PetState.Waiting;
                activePrompt = prompts[0].id; promptText = prompts[0].question ?? prompts[0].header;
                playback.Request(PetState.Waiting);
                if (newPrompt && alreadyWaiting) PlaySound(PetState.Waiting);
                return;
            }
            activePrompt = ""; promptText = "";
            if (running.Length > 0) { idleSnapshots = 0; playback.Request(PetState.Working); return; }
            // A completion sequence owns its playback until its last frame ends.
            if (playback.Current == PetState.Completed) return;
            // Prompt resolution can precede the running status by one snapshot.
            if (playback.Current == PetState.Waiting && !first) {
                playback.Request(PetState.Working); return;
            }
            if (++idleSnapshots >= 3 || first) playback.Request(PetState.Idle);
        }
        void SyncQuestion(Prompt prompt) {
            if(questionPrompt==null||prompt==null||questionPrompt.id!=prompt.id) {
                if(questionWindow!=null){var old=questionWindow;questionWindow=null;old.Close();}
                dismissedQuestion="";
            }
            questionPrompt=prompt;
            if(prompt==null)return;
            if(settings.hidden){if(questionWindow!=null)questionWindow.Hide();return;}
            if(dismissedQuestion==prompt.id)return;
            if(questionWindow==null) {
                var current=new QuestionWindow(root,prompt.questions,async answers=>{
                    await ReplyQuestion(prompt.id,answers);
                },delegate {
                    Log("question-session-click");
                    // Jumping to DSH hands the question back to DSH, so the card
                    // closes itself once the window is actually up.
                    OpenDesktop(delegate {
                        if(questionPrompt==null||questionPrompt.id!=prompt.id)return;
                        var window=questionWindow;
                        if(window!=null)try { window.Close(); } catch(Exception error) { Log("question-close-failed " + error.GetType().Name); }
                    });
                });
                questionWindow=current;current.Owner=this;
                current.Closed+=delegate {if(questionWindow==current){questionWindow=null;dismissedQuestion=prompt.id;}};
                current.Show();
            } else if(!questionWindow.IsVisible)questionWindow.Show();
        }
        async Task ReplyQuestion(string promptId,QuestionAnswer[] answers) {
            var credentials=json.Deserialize<BridgeCredentials>(File.ReadAllText(Path.Combine(root,".sumika-bridge.json")));
            Uri uri;
            if(!Uri.TryCreate(credentials.origin,UriKind.Absolute,out uri)||!uri.IsLoopback||uri.Scheme!="http")throw new InvalidOperationException("DSH 连接不可用");
            using(var request=new HttpRequestMessage(HttpMethod.Post,credentials.origin.TrimEnd('/')+"/sumika-widget/reply")) {
                request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",credentials.token);
                request.Content=new StringContent(json.Serialize(new {promptId=promptId,answers=answers}),Encoding.UTF8,"application/json");
                using(var response=await http.SendAsync(request)) {
                    var result=json.Deserialize<Dictionary<string,object>>(await response.Content.ReadAsStringAsync());
                    if(!response.IsSuccessStatusCode||!result.ContainsKey("accepted")||!Convert.ToBoolean(result["accepted"]))
                        throw new InvalidOperationException(result.ContainsKey("error")?Convert.ToString(result["error"]):"回复未成功，请重试");
                }
            }
        }

        void SystemEventsSubscribe() { Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged; }
        void OnDisplayChanged(object sender, EventArgs e) { Dispatcher.BeginInvoke(new Action(delegate { ClampPosition(); SaveSettings(); })); }
        void Log(string message) {
            try {
                string file = Path.Combine(root, "runtime.log");
                if (File.Exists(file) && new FileInfo(file).Length > 1024 * 1024) File.Move(file, file + "." + DateTime.UtcNow.Ticks);
                File.AppendAllText(file, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, new UTF8Encoding(false));
            } catch { }
        }
        void WriteDiagnostics() {
            if (!loaded) return;
            try {
                Native.RECT rect; Native.GetWindowRect(hwnd, out rect);
                var work = Native.WorkArea(hwnd).rcWork;
                var snapshot = new { pid = Process.GetCurrentProcess().Id, hwnd = hwnd.ToInt64(), current = playback.Current.ToString(), desired = playback.Desired.ToString(),
                    frame = playback.FrameIndex, frames = playback.Clip.frames.Length, loops = playback.Loops, resumePending = playback.ResumePending,
                    manual = manual, connection = connection, topmost = Topmost, height = Height, margin = settings.margin, soundStarts = soundStarts, lastSound = lastSound,
                    hidden = settings.hidden, visible = IsVisible, status = StatusText(), cacheReloads = cacheReloads, renderTicks = renderTicks,
                    questionId=questionPrompt==null?null:questionPrompt.id,questionVisible=questionWindow!=null&&questionWindow.IsVisible,
                    dismissedQuestion=dismissedQuestion,activePrompt=activePrompt,
                    hostPid = owner.HostPid, desktopPid = owner.DesktopPid, hostSession = owner.Session,
                    rect = rect, workArea = work, updatedAt = DateTimeOffset.Now.ToString("o") };
                string file = Path.Combine(root, "diagnostics.json");
                File.WriteAllText(file + ".tmp", json.Serialize(snapshot), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(file + ".tmp", file, null); else File.Move(file + ".tmp", file);
            } catch { }
        }
        // Both development checks and the DSH settings plugin use explicit local
        // commands. Each settings request has its own file and acknowledgement.
        void WatchCommands() {
            string directory = Path.Combine(root, ".sumika-commands"); Directory.CreateDirectory(directory);
            // Discard a preview claim left by an interrupted previous process.
            try { File.Delete(Path.Combine(directory, "live-preview.json.processing")); } catch { }
            commandWatcher = new FileSystemWatcher(directory, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            FileSystemEventHandler changed = delegate { ScheduleCommands(); };
            commandWatcher.Created += changed; commandWatcher.Changed += changed;
            commandWatcher.Renamed += delegate { ScheduleCommands(); };
            commandWatcher.EnableRaisingEvents = true;
            ScheduleCommands();
        }
        void ScheduleCommands() {
            if (closing || Interlocked.Exchange(ref commandDispatchScheduled, 1) != 0) return;
            Dispatcher.BeginInvoke(new Action(delegate {
                Interlocked.Exchange(ref commandDispatchScheduled, 0);
                if (!closing) ReadTestCommand();
            }), DispatcherPriority.Background);
        }
        void ReadLivePreview() {
            string file = Path.Combine(root, ".sumika-commands/live-preview.json");
            if (!File.Exists(file)) return;
            string claimed = file + ".processing";
            try {
                File.Move(file, claimed);
                var values = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(claimed));
                string value;
                if (!values.TryGetValue("sentAt", out value) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Int64.Parse(value, CultureInfo.InvariantCulture)) > 2000) return;
                int height = settings.height, margin = settings.margin; double volume = settings.volume;
                if (values.TryGetValue("height", out value)) height = Int32.Parse(value, CultureInfo.InvariantCulture);
                if (values.TryGetValue("margin", out value)) margin = Int32.Parse(value, CultureInfo.InvariantCulture);
                if (values.TryGetValue("volume", out value)) volume = Double.Parse(value, CultureInfo.InvariantCulture);
                if (height < 180 || height > 600 || margin < 8 || margin > 100 || Double.IsNaN(volume) || volume < 0 || volume > 1) return;
                settings.margin = margin; settings.volume = volume; settings.muted = false; sound.Volume = volume;
                SetSize(height, false); ClampPosition();
            } catch (Exception ex) { Log("preview-error " + ex.Message); }
            finally { try { File.Delete(claimed); } catch { } }
        }
        void ReadTestCommand() {
#if !SUMIKA_RELEASE
            string file = Path.Combine(root, "command.json");
            if (File.Exists(file)) {
                try {
                    var command = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)); File.Delete(file);
                    if (RunCommand(command)) { Close(); return; }
                } catch (Exception ex) { Log("command-error " + ex.Message); }
            }
#endif
            string directory = Path.Combine(root, ".sumika-commands");
            if (!Directory.Exists(directory)) return;
            ReadLivePreview();
            foreach (string request in Directory.GetFiles(directory, "*.json").Where(p => Path.GetFileName(p) != "live-preview.json").OrderBy(p => p).Take(16)) {
                string id = Path.GetFileNameWithoutExtension(request); bool quit = false; object result;
                try {
                    var command = json.Deserialize<Dictionary<string, string>>(File.ReadAllText(request));
                    File.Delete(request); quit = RunCommand(command);
                    result = new { ok = true, settings = settings, current = playback.Current.ToString(), manual = manual };
                } catch (Exception ex) {
                    try { File.Delete(request); } catch { }
                    result = new { ok = false, error = ex.Message }; Log("settings-command-error " + ex.Message);
                }
                string replies = Path.Combine(root, ".sumika-results"); Directory.CreateDirectory(replies);
                string reply = Path.Combine(replies, id + ".json");
                WriteDiagnostics();
                File.WriteAllText(reply + ".tmp", json.Serialize(result), new UTF8Encoding(false)); File.Move(reply + ".tmp", reply);
                if (quit) { Close(); return; }
            }
        }
        bool RunCommand(Dictionary<string, string> command) {
            string action; if (!command.TryGetValue("action", out action)) throw new ArgumentException("缺少操作类型");
            if (action == "connect") SetConnected();
#if !SUMIKA_RELEASE
            else if (action == "questionPreview") { previewAutoConnect = !manual; ManualRequest(PetState.Waiting); }
#endif
            else if (action == "configure") { ApplySettings(command); if(!settings.enabled)return true; }
#if !SUMIKA_RELEASE
            else if (action == "demo") StartDemo();
#endif
            else if (action == "previewWaiting") PlaySound(PetState.Waiting);
            else if (action == "previewCompleted") PlaySound(PetState.Completed);
            else if (action == "size") SetSize(Int32.Parse(command["height"], CultureInfo.InvariantCulture));
            else if (action == "move") { Left = Double.Parse(command["left"], CultureInfo.InvariantCulture); Top = Double.Parse(command["top"], CultureInfo.InvariantCulture); ClampPosition(); SaveSettings(); }
            else if (action == "resetPosition") {
                var area = Native.WorkArea(hwnd).rcWork;
                double dpi = PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice.M11;
                Left = area.Right / dpi - Width - settings.margin; Top = area.Bottom / dpi - Height - settings.margin;
                ClampPosition(); SaveSettings();
            }
            else if (action == "quit") return true;
            else if (action == "resetSounds") { settings.waitingSound = settings.completedSound = null; SaveSettings(); }
            else {
#if SUMIKA_RELEASE
                throw new ArgumentException("不支持的操作");
#else
                PetState requested;
                if (!Enum.TryParse<PetState>(action, true, out requested)) throw new ArgumentException("不支持的操作");
                ManualRequest(requested);
#endif
            }
 return false;
        }
        void ApplySettings(Dictionary<string, string> command) {
            string value;
            int height = settings.height, margin = settings.margin;
            double volume = settings.volume; bool autoConnect = !manual, hidden = settings.hidden;
            if (command.TryGetValue("height", out value)) height = Int32.Parse(value, CultureInfo.InvariantCulture);
            if (command.TryGetValue("margin", out value)) margin = Int32.Parse(value, CultureInfo.InvariantCulture);
            if (command.TryGetValue("volume", out value)) volume = Double.Parse(value, CultureInfo.InvariantCulture);
            if (!command.ContainsKey("volume") && command.TryGetValue("muted", out value) && Boolean.Parse(value)) volume = 0;
            if (command.TryGetValue("hidden", out value)) hidden = Boolean.Parse(value);
            if (command.TryGetValue("enabled", out value)) settings.enabled = Boolean.Parse(value);
            if (command.TryGetValue("autoConnect", out value)) { previewAutoConnect = null; autoConnect = Boolean.Parse(value); }
            if (height < 180 || height > 600 || margin < 8 || margin > 100 || Double.IsNaN(volume) || volume < 0 || volume > 1) throw new ArgumentException("设置超出允许范围");
            string waiting = settings.waitingSound, completed = settings.completedSound;
            if (command.TryGetValue("waitingSound", out value)) waiting = ValidateSoundPath(value);
            if (command.TryGetValue("completedSound", out value)) completed = ValidateSoundPath(value);
            settings.margin = margin; settings.volume = volume; settings.muted = false;
            settings.waitingSound = waiting; settings.completedSound = completed;
            sound.Volume = volume;
            if (height != settings.height) SetSize(height, false);
            ClampPosition();
            if (autoConnect && manual) SetConnected();
            else if (!autoConnect) { StopDemo(); manual = true; }
            settings.hidden = hidden;
            if (hidden && IsVisible) Hide();
            else if (!hidden && !IsVisible) { Show(); Topmost = true; lastFrameTime = clock.Elapsed.TotalMilliseconds; ClampPosition(); }
            SyncQuestion(questionPrompt);
            SaveSettings();
        }
        string ValidateSoundPath(string value) {
            if (String.IsNullOrEmpty(value)) return null;
            if (Path.IsPathRooted(value)) throw new ArgumentException("音效必须存放在挂件目录中");
            string full = Path.GetFullPath(Path.Combine(root, value));
            string allowed = Path.GetFullPath(Path.Combine(root, "assets/sounds/custom")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new ArgumentException("未找到自定义音效");
            return value;
        }
        void Shutdown() {
            if (closing) return; closing = true;
            if(questionWindow!=null){var window=questionWindow;questionWindow=null;window.Close();}
            SaveSettings(); bridgeTimer.Stop(); maintenanceTimer.Stop(); StopDemo();
            resolutionTimer.Stop(); if (commandWatcher != null) commandWatcher.Dispose();
            CompositionTarget.Rendering -= OnRendering;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            sound.Close(); http.Dispose();
            Log("stopped"); Application.Current.Shutdown();
        }
    }
    public static class Program {
        [STAThread] public static void Main(string[] args) {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            int dataIndex=Array.IndexOf(args,"--data-dir");
            if(dataIndex>=0 && dataIndex+1<args.Length)root=Path.GetFullPath(args[dataIndex+1]);
            using(var owner=DshLifetime.TryCreate(root,args)) {
            if(owner==null)return;
            try {
                var saved=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(root,"settings.json")));
                if(!saved.enabled)return;
            }catch { }
            bool created;
            using (var mutex = new Mutex(true, "Local\\CodexDesktopPet-" + root.GetHashCode().ToString("X"), out created)) {
                if (!created) return;
                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) {
                    File.AppendAllText(Path.Combine(root, "runtime.log"), "Unhandled: " + e.Exception + Environment.NewLine);
                    MessageBox.Show("挂件遇到错误：" + e.Exception.Message, "桌面伙伴"); e.Handled = true; app.Shutdown(1);
                };
                owner.Start(()=>app.Shutdown());
                try { app.Run(new PetWindow(root,owner)); }
                catch (Exception e) {
                    File.AppendAllText(Path.Combine(root, "runtime.log"), "Startup: " + e + Environment.NewLine);
                    MessageBox.Show("挂件启动失败：" + e.Message, "桌面伙伴");
                }
            }
            }
        }
    }
}
