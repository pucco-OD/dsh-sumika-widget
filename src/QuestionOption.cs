using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CodexPet {
    public sealed class PixelNumber : FrameworkElement {
        static readonly string[] Digits = {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001010010010", "111101111101111", "111101111001111"
        };
        readonly string label;
        public PixelNumber(int number) {
            label = number.ToString(CultureInfo.InvariantCulture);
            Width = Math.Max(20,label.Length*12-3); Height = 15;
            VerticalAlignment = VerticalAlignment.Center;
            RenderOptions.SetEdgeMode(this,EdgeMode.Aliased);
        }
        protected override void OnRender(DrawingContext drawing) {
            var ink = (Brush)new BrushConverter().ConvertFromString("#A47545");
            for(int d=0;d<label.Length;d++) {
                string pixels = Digits[label[d]-'0'];
                for(int row=0;row<5;row++) for(int col=0;col<3;col++)
                    if(pixels[row*3+col]=='1') drawing.DrawRectangle(ink,null,new Rect(d*12+col*3,row*3,3,3));
            }
        }
    }

    public sealed class QuestionOption : RadioButton {
        public const double ScrollPixelsPerSecond = 42;
        public const string Arrow = "M 0,5 H 7 V 1 H 9 V 3 H 11 V 5 H 13 V 7 H 11 V 9 H 9 V 11 H 7 V 7 H 0 Z";
        readonly Grid viewport;
        readonly TextBlock label;
        readonly TranslateTransform translation = new TranslateTransform();
        public Button SendButton { get; private set; }
        public string OptionText { get; private set; }
        public int Number { get; private set; }
        public double Overflow { get; private set; }
        public double TextOffset { get { return translation.X; } }
        public event Action<QuestionOption> SendRequested;
        public bool MultiSelect { get; set; }
        protected override void OnToggle() {
            if(MultiSelect)IsChecked=IsChecked!=true;
            else base.OnToggle();
        }

        public QuestionOption(int number,string text,Style optionStyle,Style sendStyle) {
            Number=number; OptionText=text; Style=optionStyle;
            HorizontalContentAlignment=HorizontalAlignment.Stretch;
            AutomationProperties.SetName(this,number+". "+text);
            AutomationProperties.SetAutomationId(this,"QuestionOption"+number);
            var content=new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            content.Children.Add(new PixelNumber(number));
            viewport=new Grid { ClipToBounds=true, Margin=new Thickness(5,0,0,0) };
            Grid.SetColumn(viewport,1);content.Children.Add(viewport);
            var canvas=new Canvas();viewport.Children.Add(canvas);
            label=new TextBlock { Text=text,FontSize=14,TextWrapping=TextWrapping.NoWrap,RenderTransform=translation };
            canvas.Children.Add(label);
            SendButton=new Button { Style=sendStyle,Content=CreateArrow(),Margin=new Thickness(8,0,0,0),Visibility=Visibility.Collapsed,ToolTip="发送此选项" };
            AutomationProperties.SetName(SendButton,"发送选项 "+number);
            Grid.SetColumn(SendButton,2);content.Children.Add(SendButton);
            SendButton.Click+=delegate(object sender,RoutedEventArgs e) {
                e.Handled=true;
                if(IsChecked==true && SendRequested!=null)SendRequested(this);
            };
            Content=content;
            Checked+=delegate { SendButton.Visibility=Visibility.Visible;ScheduleMarquee(); };
            Unchecked+=delegate { SendButton.Visibility=Visibility.Collapsed;StopMarquee(); };
            Loaded+=delegate { ScheduleMarquee(); };
            viewport.SizeChanged+=delegate { ScheduleMarquee(); };
            Unloaded+=delegate { StopMarquee(); };
        }
        public static System.Windows.Shapes.Path CreateArrow() {
            var glyph=new System.Windows.Shapes.Path { Width=14,Height=14,Data=Geometry.Parse(Arrow),Stretch=Stretch.Uniform };
            glyph.SetBinding(Shape.FillProperty,new System.Windows.Data.Binding("Foreground") {
                RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor,typeof(Button),1)
            });
            RenderOptions.SetEdgeMode(glyph,EdgeMode.Aliased);return glyph;
        }
        void ScheduleMarquee() {
            Dispatcher.BeginInvoke(new Action(UpdateMarquee),System.Windows.Threading.DispatcherPriority.Loaded);
        }
        void StopMarquee() { translation.BeginAnimation(TranslateTransform.XProperty,null);translation.X=0; }
        void UpdateMarquee() {
            StopMarquee();
            label.Measure(new Size(Double.PositiveInfinity,Double.PositiveInfinity));
            Canvas.SetTop(label,Math.Max(0,(viewport.ActualHeight-label.DesiredSize.Height)/2));
            Overflow=Math.Max(0,label.DesiredSize.Width-viewport.ActualWidth);
            if(!IsLoaded || IsChecked!=true || Overflow<1 || viewport.ActualWidth<=0)return;
            double travel=Overflow/ScrollPixelsPerSecond;
            var cycle=new DoubleAnimationUsingKeyFrames { Duration=TimeSpan.FromSeconds(.5+travel+1),RepeatBehavior=RepeatBehavior.Forever };
            cycle.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            cycle.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.5))));
            cycle.KeyFrames.Add(new LinearDoubleKeyFrame(-Overflow,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.5+travel))));
            // A discrete reset holds the last characters for a full second.
            cycle.KeyFrames.Add(new DiscreteDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromSeconds(.5+travel+1))));
            translation.BeginAnimation(TranslateTransform.XProperty,cycle);
        }
    }
}
