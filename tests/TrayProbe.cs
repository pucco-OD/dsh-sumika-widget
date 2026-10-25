using System;
using System.Threading;
using CodexPet;

class TrayProbe {
    static int Main(string[] args) {
        int desktopPid=int.Parse(args[0]);
        IntPtr window=DshActivation.FindWindow(desktopPid),host=DshActivation.FindTrayHost(desktopPid);
        Console.WriteLine("desktop="+desktopPid+" window="+window+" visible="+DshActivation.IsWindowVisible(window)+" tray="+host+" icon="+DshActivation.FindTrayIcon(host));
        if(args.Length<2 || args[1]!="open")return 0;
        return DshActivation.OpenAsync(desktopPid,()=>true,Console.WriteLine).GetAwaiter().GetResult()?0:1;
    }
}
