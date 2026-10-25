using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CodexPet {
    // Electron's native tray callback invokes the application's own click handler.
    // Never force a hidden BrowserWindow visible: that skips Electron's state change.
    public static class DshActivation {
        delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct IconIdentifier {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll", SetLastError=true)] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref IconIdentifier identifier, out Rect rect);

        public static IntPtr FindWindow(int desktopPid) {
            IntPtr best=IntPtr.Zero; long bestArea=-1;
            EnumWindows(delegate(IntPtr window, IntPtr parameter) {
                uint pid; GetWindowThreadProcessId(window,out pid);
                if(pid!=(uint)desktopPid || GetWindow(window,4)!=IntPtr.Zero || GetWindowTextLength(window)==0)return true;
                var name=new StringBuilder(256); GetClassName(window,name,name.Capacity);
                if(!name.ToString().StartsWith("Chrome_WidgetWin_",StringComparison.Ordinal))return true;
                Rect rect; if(!GetWindowRect(window,out rect))return true;
                long area=(long)(rect.Right-rect.Left)*(rect.Bottom-rect.Top);
                if(area>bestArea){bestArea=area;best=window;}return true;
            },IntPtr.Zero);
            return best;
        }

        public static IntPtr FindTrayHost(int desktopPid) {
            IntPtr result=IntPtr.Zero;
            EnumWindows(delegate(IntPtr window,IntPtr parameter) {
                uint pid; GetWindowThreadProcessId(window,out pid);
                if(pid!=(uint)desktopPid)return true;
                var name=new StringBuilder(256); GetClassName(window,name,name.Capacity);
                if(name.ToString()!="Electron_NotifyIconHostWindow")return true;
                result=window;return false;
            },IntPtr.Zero);
            return result;
        }

        public static uint FindTrayIcon(IntPtr host) {
            if(host==IntPtr.Zero)return 0;
            uint found=0;
            // DSH constructs one Tray without a GUID. Validate its ID with the
            // Shell rather than sending guessed callbacks to other tray icons.
            for(uint id=2;id<=64;id++) {
                var identifier=new IconIdentifier { cbSize=(uint)Marshal.SizeOf(typeof(IconIdentifier)),hWnd=host,uID=id };
                Rect rect;
                if(Shell_NotifyIconGetRect(ref identifier,out rect)!=0)continue;
                if(found!=0)return 0; // Ambiguous future DSH version: fail safely.
                found=id;
            }
            return found;
        }

        public static async Task<bool> OpenAsync(int desktopPid,Func<bool> ownerAlive,Action<string> log) {
            if(desktopPid<=0 || !ownerAlive())return false;
            IntPtr host=FindTrayHost(desktopPid);
            uint icon=FindTrayIcon(host);
            if(host!=IntPtr.Zero && icon!=0) {
                AllowSetForegroundWindow((uint)desktopPid);
                // WM_APP+1, wParam=icon ID, lParam=WM_LBUTTONDOWN. Electron
                // turns this into Tray 'click', which DSH handles with show().
                if(!ownerAlive() || !PostMessage(host,0x8001,new IntPtr(icon),new IntPtr(0x0201))) {
                    log("desktop-tray-post-failed "+Marshal.GetLastWin32Error());return false;
                }
                log("desktop-tray-click "+host+" icon="+icon);
                for(int attempt=0;attempt<40;attempt++) {
                    await Task.Delay(50);
                    if(!ownerAlive())return false;
                    IntPtr window=FindWindow(desktopPid);
                    if(window==IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window))continue;
                    bool active=SetForegroundWindow(window);
                    log("desktop-restored "+window+(active?" active":" background"));return true;
                }
                log("desktop-tray-restore-timeout");return false;
            }
            // Compatibility fallback is restricted to already visible windows.
            IntPtr visible=FindWindow(desktopPid);
            if(visible==IntPtr.Zero || !IsWindowVisible(visible)) {
                log("desktop-tray-unavailable");return false;
            }
            if(IsIconic(visible))ShowWindowAsync(visible,9);
            bool focused=SetForegroundWindow(visible);
            log("desktop-visible-focus "+visible+(focused?" active":" background"));return true;
        }
    }
}
