using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexPet {
    // Wheel deltas remain fractional; the destination is approached every paint
    // frame instead of jumping whole lines. Thumb dragging remains direct.
    public sealed class SmoothPixelScroll : IDisposable {
        readonly ScrollViewer viewer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly ScrollEventHandler barHandler;
        bool animating,disposed;
        double target,lastTick;
        public const double PixelsPerNotch=48;
        public SmoothPixelScroll(ScrollViewer control) {
            viewer=control;
            viewer.PreviewMouseWheel+=OnWheel;
            viewer.ScrollChanged+=OnScrollChanged;
            viewer.PreviewMouseDown+=OnPointerDown;
            viewer.Unloaded+=OnUnloaded;
            barHandler=delegate { Stop(); };
            viewer.AddHandler(ScrollBar.ScrollEvent,barHandler,true);
        }
        void OnWheel(object sender,MouseWheelEventArgs e) {
            if(disposed || viewer.ScrollableHeight<=0 || e.Delta==0)return;
            double movement=-e.Delta*PixelsPerNotch/120.0;
            if(!animating || Math.Sign(movement)!=Math.Sign(target-viewer.VerticalOffset))target=viewer.VerticalOffset;
            target=Clamp(target+movement);
            e.Handled=true;
            if(animating || Math.Abs(target-viewer.VerticalOffset)<.01)return;
            animating=true;lastTick=clock.Elapsed.TotalSeconds;
            CompositionTarget.Rendering+=OnFrame;
        }
        double Clamp(double value) { return Math.Max(0,Math.Min(viewer.ScrollableHeight,value)); }
        void OnPointerDown(object sender,MouseButtonEventArgs e) {
            DependencyObject source=e.OriginalSource as DependencyObject;
            while(source!=null && source!=viewer) {
                if(source is ScrollBar) { Stop();return; }
                source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);
            }
        }
        void OnFrame(object sender,EventArgs e) {
            double now=clock.Elapsed.TotalSeconds;
            double dt=Math.Max(0,Math.Min(.05,now-lastTick));lastTick=now;
            target=Clamp(target);
            double remaining=target-viewer.VerticalOffset;
            if(Math.Abs(remaining)<.08) {
                viewer.ScrollToVerticalOffset(target);Stop();return;
            }
            // 95% of a wheel movement settles in roughly 180 ms. Accumulated
            // wheel events continue from the existing motion without a restart.
            double next=viewer.VerticalOffset+remaining*(1-Math.Exp(-dt/.06));
            viewer.ScrollToVerticalOffset(next);
        }
        void OnScrollChanged(object sender,ScrollChangedEventArgs e) {
            if(!animating)target=viewer.VerticalOffset;
            else if(e.ExtentHeightChange!=0 || e.ViewportHeightChange!=0)target=Clamp(target);
        }
        void OnUnloaded(object sender,RoutedEventArgs e) { Stop(); }
        void Stop() {
            if(animating)CompositionTarget.Rendering-=OnFrame;
            animating=false;target=viewer.VerticalOffset;
        }
        public void Dispose() {
            if(disposed)return;disposed=true;Stop();
            viewer.PreviewMouseWheel-=OnWheel;viewer.ScrollChanged-=OnScrollChanged;viewer.Unloaded-=OnUnloaded;
            viewer.PreviewMouseDown-=OnPointerDown;
            viewer.RemoveHandler(ScrollBar.ScrollEvent,barHandler);
        }
    }
}
