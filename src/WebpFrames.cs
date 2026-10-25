using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexPet {
    public static class WebpFrames {
        [StructLayout(LayoutKind.Sequential)]struct Data { public IntPtr bytes;public UIntPtr size; }
        [StructLayout(LayoutKind.Sequential)]struct Info { public uint width,height,loop,bg,frames,p0,p1,p2,p3; }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr reserved,uint flags);
        [DllImport("libwebpdemux.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr WebPAnimDecoderNewInternal(ref Data data,IntPtr options,int version);
        [DllImport("libwebpdemux.dll",CallingConvention=CallingConvention.Cdecl)]static extern int WebPAnimDecoderGetInfo(IntPtr decoder,out Info info);
        [DllImport("libwebpdemux.dll",CallingConvention=CallingConvention.Cdecl)]static extern int WebPAnimDecoderGetNext(IntPtr decoder,out IntPtr pixels,out int timestamp);
        [DllImport("libwebpdemux.dll",CallingConvention=CallingConvention.Cdecl)]static extern void WebPAnimDecoderDelete(IntPtr decoder);
        static readonly object gate=new object();static bool loaded;
        static void Load() {
            lock(gate) {
                if(loaded)return;
                string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native");
                foreach(string name in new[]{"libwebp.dll","libwebpdemux.dll"})
                    if(LoadLibraryEx(Path.Combine(directory,name),IntPtr.Zero,0x1100)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot load WebP decoder");
                loaded=true;
            }
        }
        public static BitmapSource[] Decode(string assets,ClipInfo clip,int targetHeight,double dpi,Func<bool> cancelled) {
            Load();
            byte[] bytes=File.ReadAllBytes(Path.Combine(assets,clip.source));
            var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);IntPtr decoder=IntPtr.Zero;
            try {
                var data=new Data { bytes=pin.AddrOfPinnedObject(),size=(UIntPtr)bytes.Length };
                decoder=WebPAnimDecoderNewInternal(ref data,IntPtr.Zero,0x0107);
                Info info;
                if(decoder==IntPtr.Zero || WebPAnimDecoderGetInfo(decoder,out info)==0 || info.width!=clip.width || info.height!=clip.height || info.frames!=clip.frames.Length)throw new InvalidDataException("WebP manifest mismatch: "+clip.source);
                int width=(int)info.width,height=(int)info.height;
                int scaledWidth=Math.Min(width,Math.Max(1,(int)Math.Ceiling(width*(double)targetHeight/clip.stageHeight*dpi)));
                int scaledHeight=Math.Max(1,(int)Math.Round(height*(double)scaledWidth/width));
                var frames=new BitmapSource[info.frames];var rgba=new byte[width*height*4];int previous=0;
                for(int frame=0;frame<frames.Length;frame++) {
                    if(cancelled!=null && cancelled())throw new OperationCanceledException();
                    IntPtr pixels;int timestamp;
                    if(WebPAnimDecoderGetNext(decoder,out pixels,out timestamp)==0 || timestamp-previous!=clip.frames[frame].durationMs)throw new InvalidDataException("WebP timing mismatch: "+clip.source);
                    previous=timestamp;Marshal.Copy(pixels,rgba,0,rgba.Length);
                    if(clip.id=="idle")ClearExteriorBlack(rgba,width,height);
                    var bgra=new byte[scaledWidth*scaledHeight*4];
                    for(int y=0;y<scaledHeight;y++) for(int x=0;x<scaledWidth;x++) {
                        int source=((y*height/scaledHeight)*width+x*width/scaledWidth)*4,at=(y*scaledWidth+x)*4;
                        bgra[at]=rgba[source+2];bgra[at+1]=rgba[source+1];bgra[at+2]=rgba[source];bgra[at+3]=rgba[source+3];
                    }
                    var bitmap=BitmapSource.Create(scaledWidth,scaledHeight,96,96,PixelFormats.Bgra32,null,bgra,scaledWidth*4);
                    bitmap.Freeze();frames[frame]=bitmap;
                }
                return frames;
            } finally {if(decoder!=IntPtr.Zero)WebPAnimDecoderDelete(decoder);pin.Free();}
        }
        static void ClearExteriorBlack(byte[] data,int width,int height) {
            var seen=new bool[width*height];var queue=new int[width*height];int head=0,tail=0;
            Action<int> add=index=>{
                if(seen[index])return;seen[index]=true;int at=index*4;
                if(data[at]<=8 && data[at+1]<=8 && data[at+2]<=8)queue[tail++]=index;
            };
            for(int x=0;x<width;x++){add(x);add((height-1)*width+x);}
            for(int y=0;y<height;y++){add(y*width);add(y*width+width-1);}
            while(head<tail){int at=queue[head++],x=at%width;data[at*4+3]=0;if(x>0)add(at-1);if(x<width-1)add(at+1);if(at>=width)add(at-width);if(at+width<width*height)add(at+width);}
        }
    }
}
