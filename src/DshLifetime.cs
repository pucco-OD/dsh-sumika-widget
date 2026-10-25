using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace CodexPet {
    public sealed class DshOwner {
        public int pid { get; set; }
        public int desktopPid { get; set; }
        public string execPath { get; set; }
        public string instance { get; set; }
        public bool active { get; set; }
    }
    // The renderer belongs to one plugin instance and to both DSH processes.
    // It exits even when bridge polling is paused, disconnected, or hidden.
    public sealed class DshLifetime : IDisposable {
        readonly string file;
        readonly BoundProcess host,desktop;
        readonly DshOwner owner;
        readonly DispatcherTimer timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
        int readFailures;
        bool disposed;
        public int HostPid { get { return owner.pid; } }
        public int DesktopPid { get { return owner.desktopPid; } }
        public string Session { get { return owner.instance; } }
        public string ExecPath { get { return owner.execPath; } }
        sealed class BoundProcess : IDisposable {
            IntPtr handle;
            [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
            [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
            [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
            [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr handle,uint flags,StringBuilder name,ref int length);
            public BoundProcess(int pid) {
                handle=OpenProcess(0x00100000|0x1000,false,pid);
                if(handle==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot open DSH process "+pid);
            }
            public bool IsAlive { get { return handle!=IntPtr.Zero && WaitForSingleObject(handle,0)==258; } }
            public string ImagePath {
                get {var result=new StringBuilder(32768);int length=result.Capacity;if(!QueryFullProcessImageName(handle,0,result,ref length))throw new Win32Exception(Marshal.GetLastWin32Error());return result.ToString();}
            }
            public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
        }
        DshLifetime(string filename,DshOwner record,BoundProcess hostProcess,BoundProcess desktopProcess) {
            file=filename;owner=record;host=hostProcess;desktop=desktopProcess;
        }
        static DshOwner Read(string file) {
            return new JavaScriptSerializer().Deserialize<DshOwner>(File.ReadAllText(file));
        }
        static bool Enabled(string root) {
            if(!File.Exists(Path.Combine(root,"settings.json")))return true;
            var settings=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"settings.json")));
            object value;return settings==null || !settings.TryGetValue("enabled",out value) || !(value is bool) || (bool)value;
        }
        public static DshLifetime TryCreate(string root,string[] args) {
            BoundProcess host=null,desktop=null;
            try {
                if(!Enabled(root))return null;
                string file=Path.Combine(root,".sumika-host.json");
                var record=Read(file);
                if(record==null || !record.active || record.pid<=0 || record.desktopPid<=0 || String.IsNullOrEmpty(record.instance))return null;
                int sessionIndex=Array.IndexOf(args,"--host-session");
                if(sessionIndex>=0 && (sessionIndex+1>=args.Length || args[sessionIndex+1]!=record.instance))return null;
                host=new BoundProcess(record.pid);desktop=new BoundProcess(record.desktopPid);
                // Opening handles binds these exact process instances, so PID
                // reuse after an exit cannot keep an orphaned window alive.
                if(!host.IsAlive || !desktop.IsAlive)throw new InvalidOperationException("DSH exited");
                if(!String.IsNullOrEmpty(record.execPath) && !String.Equals(Path.GetFullPath(host.ImagePath),Path.GetFullPath(record.execPath),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Host process changed");
                return new DshLifetime(file,record,host,desktop);
            } catch(Exception error) {
                try { File.AppendAllText(Path.Combine(root,"runtime.log"),"host-unavailable: "+error.Message+Environment.NewLine); } catch { }
                if(host!=null)host.Dispose();if(desktop!=null)desktop.Dispose();return null;
            }
        }
        public bool IsAlive() {
            if(disposed)return false;
            try { if(!host.IsAlive || !desktop.IsAlive)return false; }
            catch { return false; }
            try {
                var current=Read(file);readFailures=0;
                return current!=null && current.active && current.instance==owner.instance && current.pid==owner.pid && current.desktopPid==owner.desktopPid && Enabled(Path.GetDirectoryName(file));
            } catch { return ++readFailures<4; }
        }
        public void Start(Action onExit) {
            timer.Tick+=delegate { if(!IsAlive()){timer.Stop();onExit();} };
            timer.Start();
        }
        public void Dispose() {
            if(disposed)return;disposed=true;timer.Stop();host.Dispose();desktop.Dispose();
        }
    }
}
