using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CodexPet {
    public sealed class QuestionChoice {
        public string label { get; set; }
        public string description { get; set; }
    }
    public sealed class QuestionData {
        public string id { get; set; }
        public string question { get; set; }
        public string header { get; set; }
        public string detail { get; set; }
        public bool multiSelect { get; set; }
        public bool allowCustom { get; set; }
        public QuestionChoice[] options { get; set; }
        public QuestionData(){allowCustom=true;}
    }
    public sealed class QuestionAnswer {
        public string id { get; set; }
        public string[] selected { get; set; }
        public string custom { get; set; }
    }
    public sealed class QuestionWindow : Window {
        readonly string root;
        readonly Grid layout;
        readonly DispatcherTimer follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        double pointerY = 105;
        readonly List<QuestionOption> options = new List<QuestionOption>();
        readonly SmoothPixelScroll optionScrolling;
        readonly SmoothPixelScroll questionScrolling;
        QuestionData[] questions;
        QuestionAnswer[] answers;
        string[] drafts;
        int page;
        bool busy, changingPage;
        Func<QuestionAnswer[],Task> submit;
        Action openSession;
        public int ReplyPreviewCount { get; private set; }
        public int SessionPreviewCount { get; private set; }
        public string LastPreviewKind { get; private set; }
        public string LastPreviewText { get; private set; }
        public QuestionWindow(string directory):this(directory,true) { }
        QuestionWindow(string directory,bool preview) {
            root = directory;
            Title = "Sumika · 问题窗口";
            // Logical size and typography stay independent of the pet's size.
            Width = 438; Height = 370;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            string ui=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui/QuestionWindow.xaml");
            if(!File.Exists(ui))ui=System.IO.Path.Combine(root,"ui/QuestionWindow.xaml");
            layout = (Grid)XamlReader.Parse(File.ReadAllText(ui));
            Content = layout;
            optionScrolling = new SmoothPixelScroll(Get<ScrollViewer>("OptionsScroll"));
            questionScrolling = new SmoothPixelScroll(Get<ScrollViewer>("QuestionScroll"));
            AutomationProperties.SetAutomationId(this, "SumikaQuestionPreview");
            Get<Button>("CloseButton").Click += delegate { Close(); };
            Get<Button>("SendButton").Click += delegate { PreviewTypedReply(); };
            Get<Button>("SessionButton").Click += delegate {
                SessionPreviewCount++;
                if(openSession!=null)openSession();
            };
            Get<Button>("PreviousPage").Click+=delegate { if(page>0)ShowPage(page-1); };
            Get<Button>("NextPage").Click+=delegate { if(questions!=null&&page<questions.Length-1)ShowPage(page+1); };
            var text = Get<TextBox>("ReplyText");
            text.TextChanged += delegate {
                Get<TextBlock>("Placeholder").Visibility = String.IsNullOrEmpty(text.Text) ? Visibility.Visible : Visibility.Hidden;
                if(!changingPage)ActivateTypedReply();
            };
            if(preview)PopulateOptions();
            Get<Border>("InputBorder").PreviewMouseDown += delegate(object sender,MouseButtonEventArgs e) {
                if(e.ChangedButton!=MouseButton.Left)return;
                bool entering=!text.IsKeyboardFocusWithin;
                ActivateTypedReply();
                if(!entering)return;
                // Handle the first click so TextBox's default hit testing cannot
                // move the newly focused caret back to the clicked character.
                e.Handled=true;
                text.Focus();
                MoveReplyCaretToEnd();
            };
            text.GotKeyboardFocus += delegate {
                ActivateTypedReply();
                MoveReplyCaretToEnd();
                Get<Border>("InputBorder").BorderBrush = ColorBrush("#B58A57");
            };
            text.LostKeyboardFocus += delegate { Get<Border>("InputBorder").BorderBrush = ColorBrush("#D8C8AF"); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Escape) { Close(); e.Handled = true; }
                else if (e.Key == Key.Enter && (text.IsKeyboardFocused || (Keyboard.Modifiers & ModifierKeys.Control) != 0)) {
                    PreviewTypedReply(); e.Handled = true;
                }
            };
            SizeChanged += delegate { DrawFrame(); };
            Loaded += delegate { FollowPet(); DrawFrame(); follow.Start(); };
            follow.Tick += delegate { FollowPet(); };
            Closed += delegate { follow.Stop(); optionScrolling.Dispose(); questionScrolling.Dispose(); };
        }
        public QuestionWindow(string directory,QuestionData[] data,Func<QuestionAnswer[],Task> sender,Action session):this(directory,false) {
            questions=data;submit=sender;openSession=session;
            answers=new QuestionAnswer[data.Length];drafts=new string[data.Length];
            AutomationProperties.SetAutomationId(this,"SumikaQuestionWindow");
            changingPage=true;
            ShowPage(0);
        }
        void RememberPage() {
            if(questions==null||changingPage)return;
            string text=Get<TextBox>("ReplyText").Text;
            drafts[page]=text;
            string[] selected=options.Where(o=>o.IsChecked==true).Select(o=>o.OptionText).ToArray();
            answers[page]=selected.Length>0||!String.IsNullOrWhiteSpace(text)?new QuestionAnswer {
                id=questions[page].id,selected=selected,custom=selected.Length==0?text:null
            }:null;
        }
        void ShowPage(int index) {
            RememberPage();page=index;changingPage=true;
            var q=questions[page];
            Get<TextBlock>("Question").Text=q.question+(String.IsNullOrWhiteSpace(q.detail)?"":"\n"+q.detail);
            PopulateChoices(q.options??new QuestionChoice[0],q.multiSelect);
            Get<TextBox>("ReplyText").Text=drafts[page]??"";
            Get<Border>("InputBorder").IsEnabled=q.allowCustom;
            Get<TextBlock>("Placeholder").Text=q.allowCustom?"输入回复…":"请选择允许或拒绝";
            if(answers[page]!=null)foreach(var option in options)option.IsChecked=answers[page].selected.Contains(option.OptionText);
            Get<StackPanel>("Pages").Visibility=questions.Length>1?Visibility.Visible:Visibility.Collapsed;
            Get<TextBlock>("PageNumber").Text=(page+1)+" / "+questions.Length;
            Get<Button>("PreviousPage").IsEnabled=page>0;
            Get<Button>("NextPage").IsEnabled=page<questions.Length-1;
            Get<ScrollViewer>("OptionsScroll").ScrollToTop();
            Get<ScrollViewer>("QuestionScroll").ScrollToTop();
            changingPage=false;UpdateReplyInput();
        }
        void ActivateTypedReply() {
            foreach(var option in options)option.IsChecked=false;
            UpdateReplyInput();
        }
        void MoveReplyCaretToEnd() {
            var text=Get<TextBox>("ReplyText");
            text.Select(text.Text.Length,0);
            text.ScrollToHorizontalOffset(Double.MaxValue);
        }
        void PopulateOptions() {
            string[] labels = {
                "先完善动画衔接，确保闲置消散、工作出场和完成提醒之间都能顺畅切换",
                "先完善问题窗口", "调整提示音", "调整挂件外观", "完善工作状态联动", "先按当前方案继续"
            };
            PopulateChoices(labels.Select(label=>new QuestionChoice {label=label}).ToArray(),false);
            options[0].IsChecked=true;
        }
        void PopulateChoices(QuestionChoice[] choices,bool multi) {
            var panel=Get<StackPanel>("Options");panel.Children.Clear();options.Clear();
            for(int i=0;i<choices.Length;i++) {
                var option=new QuestionOption(i+1,choices[i].label,(Style)layout.Resources["AnswerOption"],(Style)layout.Resources["SendArrowButton"]);
                option.MultiSelect=multi;
                if(multi)option.GroupName="SumikaMulti"+i;
                if(!String.IsNullOrWhiteSpace(choices[i].description))option.ToolTip=choices[i].description;
                option.Checked+=delegate { UpdateReplyInput(); };
                option.Unchecked+=delegate { UpdateReplyInput(); };
                option.SendRequested+=delegate(QuestionOption selected) {
                    if(selected.IsChecked!=true)return;
                    PreviewReply("option",selected.OptionText,selected.SendButton);
                };
                if(i==choices.Length-1)option.Margin=new Thickness(0);
                options.Add(option);panel.Children.Add(option);
            }
        }
        bool HasSelectedOption() { return options.Exists(option=>option.IsChecked==true); }
        void UpdateReplyInput() {
            var text=Get<TextBox>("ReplyText");
            bool selected=HasSelectedOption();
            text.Foreground=ColorBrush(selected?"#AAA194":"#514236");
            Get<Button>("SendButton").IsEnabled=!busy && !selected && !String.IsNullOrWhiteSpace(text.Text);
        }
        void PreviewTypedReply() {
            string text=Get<TextBox>("ReplyText").Text;
            if(HasSelectedOption() || String.IsNullOrWhiteSpace(text) || !Get<Button>("SendButton").IsEnabled)return;
            PreviewReply("text",text,Get<Button>("SendButton"));
        }
        void PreviewReply(string kind,string text,Button button) {
            if(submit!=null){SendAnswer();return;}
            ReplyPreviewCount++; LastPreviewKind=kind; LastPreviewText=text;
            var glyph=button.Content as System.Windows.Shapes.Path;
            if(glyph==null)return;
            glyph.Data=Geometry.Parse("M 0,6 H 3 V 9 H 6 V 6 H 9 V 3 H 12 V 0 H 15 V 3 H 12 V 6 H 9 V 9 H 6 V 12 H 3 V 9 H 0 Z");
            var reset=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1.2) };
            reset.Tick+=delegate { reset.Stop();glyph.Data=Geometry.Parse(QuestionOption.Arrow); };
            reset.Start();
        }
        async void SendAnswer() {
            if(busy)return;
            RememberPage();
            int missing=Array.FindIndex(answers,a=>a==null);
            if(missing>=0){ShowPage(missing);return;}
            busy=true;Get<Grid>("CardContent").IsEnabled=false;
            try { await submit(answers);Close(); }
            catch(Exception error) {
                // Keep the draft and selected answers available for retry.
                busy=false;Get<Grid>("CardContent").IsEnabled=true;UpdateReplyInput();
                Get<TextBlock>("Question").Text=questions[page].question+"\n"+error.Message;
            }
        }
        T Get<T>(string name) where T : FrameworkElement { return (T)layout.FindName(name); }
        static Brush ColorBrush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        Geometry Outline(double left, double top, double right, double bottom) {
            return Geometry.Parse(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M {0},{1} H {2} V {3} H {4} V {5} H {6} V {7} H {4} V {8} H {2} V {9} H {0} V {8} H {10} V {7} H {11} V {5} H {10} V {3} H {0} Z",
                left+8,top,right-8,top+4,right-4,top+8,right,bottom-8,bottom-4,bottom,left+4,left));
        }
        void DrawFrame() {
            double right = ActualWidth-25, bottom = ActualHeight-12;
            Get<System.Windows.Shapes.Path>("Shadow").Data = Outline(11,13,right+3,bottom+5);
            Get<System.Windows.Shapes.Path>("Frame").Data = Outline(7,7,right,bottom);
            Get<System.Windows.Shapes.Path>("Paper").Data = Outline(10,10,right-3,bottom-3);
            Get<System.Windows.Shapes.Path>("Highlight").Data = Geometry.Parse(String.Format(System.Globalization.CultureInfo.InvariantCulture,"M 20,13 H {0} M 13,20 V {1}",right-13,bottom-13));
            double y = Math.Max(55,Math.Min(ActualHeight-65,pointerY));
            // A stepped speech pointer, open on the left so it joins the paper.
            Get<System.Windows.Shapes.Path>("Tail").Data = Geometry.Parse(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M {0},{1} H {2} V {3} H {4} V {5} H {2} V {6} H {0}",right-4,y-13,right+6,y-5,right+14,y+5,y+13));
        }
        void FollowPet() {
            try {
                var snapshot = json.DeserializeObject(File.ReadAllText(System.IO.Path.Combine(root,"diagnostics.json"))) as System.Collections.Generic.Dictionary<string,object>;
                var rect = (System.Collections.Generic.Dictionary<string,object>)snapshot["rect"];
                var work = (System.Collections.Generic.Dictionary<string,object>)snapshot["workArea"];
                var source = PresentationSource.FromVisual(this);
                double dpi = source == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
                double petLeft = Convert.ToDouble(rect["Left"])/dpi, petTop = Convert.ToDouble(rect["Top"])/dpi;
                double petHeight = (Convert.ToDouble(rect["Bottom"])-Convert.ToDouble(rect["Top"]))/dpi;
                double workLeft = Convert.ToDouble(work["Left"])/dpi, workTop = Convert.ToDouble(work["Top"])/dpi;
                double workRight = Convert.ToDouble(work["Right"])/dpi, workBottom = Convert.ToDouble(work["Bottom"])/dpi;
                double targetY = petTop + petHeight*.34;
                // The transparent gutter overlaps the pet's window, while the
                // painted speech pointer stops just short of the character.
                Left = Math.Max(workLeft+12,Math.Min(workRight-Width-12,petLeft-Width+8));
                Top = Math.Max(workTop+12,Math.Min(workBottom-Height-12,targetY-105));
                pointerY = targetY-Top; DrawFrame();
            } catch { }
        }
    }
}
