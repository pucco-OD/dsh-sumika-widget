using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Web.Script.Serialization;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.Generic;
using System.Windows.Controls.Primitives;
using CodexPet;

public static class QuestionWindowInteractionTests {
    static int passed;
    static void Check(bool ok,string label) { if(!ok)throw new Exception("FAIL "+label);passed++;Console.WriteLine("PASS "+label); }
    static async Task At(Stopwatch clock,double seconds) {
        int wait=(int)Math.Ceiling(seconds*1000-clock.Elapsed.TotalMilliseconds);
        if(wait>0)await Task.Delay(wait);
    }
    [STAThread]public static void Main() {
        string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".."));
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var window=new QuestionWindow(root) { Opacity=0,IsHitTestVisible=false,Topmost=false };
        window.Loaded+=async delegate {
            try {
                await Task.Delay(150);
                var layout=(Grid)window.Content;
                var text=(TextBox)layout.FindName("ReplyText");
                var send=(Button)layout.FindName("SendButton");
                var options=((StackPanel)layout.FindName("Options")).Children.Cast<QuestionOption>().ToArray();
                Check(options.Length==6 && options.Select((option,index)=>option.Number==index+1).All(value=>value),"six options keep their original sequential numbers");
                Check(!send.IsEnabled,"empty reply disables input send button");
                int count=window.ReplyPreviewCount;
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.ReplyPreviewCount==count,"empty reply cannot submit even through a synthetic click");
                options[1].IsChecked=true;
                Check(window.ReplyPreviewCount==count,"selecting an option does not submit it");
                Check(options[1].SendButton.Visibility==Visibility.Visible && options[0].SendButton.Visibility==Visibility.Collapsed,"only the selected option exposes its send button");
                options[1].SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.ReplyPreviewCount==count+1 && window.LastPreviewKind=="option" && window.LastPreviewText==options[1].OptionText,"option arrow submits the selected full option text locally");
                text.Text="手动输入的回复";
                Check(options.All(option=>option.IsChecked!=true && option.SendButton.Visibility==Visibility.Collapsed),"typing clears every option selection and hides option send buttons");
                Check(send.IsEnabled,"typing enables input send button");
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.ReplyPreviewCount==count+2 && window.LastPreviewKind=="text" && window.LastPreviewText==text.Text,"input arrow uses typed text instead of a previous option");
                text.Text="   ";
                Check(!send.IsEnabled,"whitespace-only input is disabled");
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.ReplyPreviewCount==count+2,"whitespace cannot submit");
                text.Text="保留这份未发送的草稿";
                options[1].IsChecked=true;
                Check(text.Text=="保留这份未发送的草稿" && !send.IsEnabled,"selecting an option preserves the draft and disables its send button");
                Check(((SolidColorBrush)text.Foreground).Color==(Color)ColorConverter.ConvertFromString("#AAA194"),"inactive draft text becomes gray");
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.ReplyPreviewCount==count+2,"retained draft cannot send while an option is selected");
                options[1].SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.ReplyPreviewCount==count+3 && window.LastPreviewKind=="option" && window.LastPreviewText==options[1].OptionText,"option reply excludes the retained draft");
                text.Text+="，继续编辑";
                Check(options.All(option=>option.IsChecked!=true) && send.IsEnabled && ((SolidColorBrush)text.Foreground).Color==(Color)ColorConverter.ConvertFromString("#514236"),"editing the retained draft restores text mode and its original color");
                options[1].IsChecked=true;
                await Task.Delay(150);
                Check(options[1].Overflow<1 && Math.Abs(options[1].TextOffset)<.1,"short selected text remains still");
                options[0].IsChecked=true;
                var clock=Stopwatch.StartNew();
                await At(clock,.22);
                Check(options[0].Overflow>20 && Math.Abs(options[0].TextOffset)<.5,"overflow text holds at its beginning for half a second");
                double overflow=options[0].Overflow;
                await At(clock,.9);double firstOffset=options[0].TextOffset;
                await At(clock,1.4);double distance=firstOffset-options[0].TextOffset;
                Check(firstOffset< -8 && distance>17 && distance<25,"selected text scrolls left at the increased forty-two pixels per second");
                double end=.5+overflow/QuestionOption.ScrollPixelsPerSecond;
                await At(clock,end+.15);double endOffset=options[0].TextOffset;
                await At(clock,end+.7);
                Check(Math.Abs(endOffset+overflow)<1 && Math.Abs(options[0].TextOffset+overflow)<1,"last characters stay visible during the one-second end pause");
                await At(clock,end+1.2);
                Check(Math.Abs(options[0].TextOffset)<1,"marquee returns to the beginning and pauses before repeating");
                await At(clock,end+1.9);
                Check(options[0].TextOffset< -5,"marquee repeats after the beginning pause");
                text.Text="停止滚动";
                Check(options[0].IsChecked!=true && Math.Abs(options[0].TextOffset)<.1,"typing immediately stops and resets the old selected marquee");
                var scroll=(ScrollViewer)layout.FindName("OptionsScroll");
                scroll.ScrollToVerticalOffset(0);await Task.Delay(100);
                Action<int> wheel=delta=>scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta) { RoutedEvent=UIElement.PreviewMouseWheelEvent });
                var samples=new List<double>();
                wheel(-120);
                for(int i=0;i<24;i++){await Task.Delay(16);samples.Add(scroll.VerticalOffset);}
                Check(samples.Distinct().Count()>8 && samples.Any(offset=>offset>0 && offset<46),"wheel movement renders continuous intermediate pixel offsets");
                Check(Math.Abs(scroll.VerticalOffset-48)<.2,"one notch settles at its pixel destination without snapping to an option");
                double previous=scroll.VerticalOffset;
                wheel(-15);await Task.Delay(450);
                Check(Math.Abs(scroll.VerticalOffset-previous-6)<.2,"high-resolution fractional wheel input preserves its full precision");
                wheel(-120);await Task.Delay(40);double reverseStart=scroll.VerticalOffset;wheel(30);await Task.Delay(450);
                Check(scroll.VerticalOffset<reverseStart,"reversing the wheel immediately reverses the current motion");
                for(int i=0;i<8;i++)wheel(-120);
                await Task.Delay(550);
                Check(Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<.2,"rapid wheel input accumulates and stops at the lower edge");
                wheel(120);await Task.Delay(30);
                var scrollbar=(ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar",scroll);
                scrollbar.Value=64;
                ScrollBar.ScrollToVerticalOffsetCommand.Execute(64.0,scrollbar);
                scrollbar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack,64) { RoutedEvent=ScrollBar.ScrollEvent });
                await Task.Delay(350);
                Check(Math.Abs(scroll.VerticalOffset-64)<.2,"dragging the scrollbar cancels wheel easing and keeps the thumb destination (actual "+scroll.VerticalOffset+")");
                int submitted=0,jumpInvoked=0;QuestionAnswer[] received=null;
                var receipt=new TaskCompletionSource<bool>();
                var real=new QuestionWindow(root,new [] {
                    new QuestionData {id="first",question="First real question",options=new [] {new QuestionChoice {label="Real choice"}}},
                    new QuestionData {id="second",question="Second real question",options=new QuestionChoice[0]}
                },answer=>{submitted++;received=answer;return receipt.Task;},delegate {jumpInvoked++;}) {Opacity=0,Topmost=false};
                real.Show();await Task.Delay(120);
                var realLayout=(Grid)real.Content;
                var jump=(Button)realLayout.FindName("SessionButton");
                Check(jump.IsEnabled && jump.Opacity==1,"DSH jump remains enabled for the shared tray restore path");
                jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(jumpInvoked==1,"DSH jump invokes its shared open callback once");
                var realOptions=(StackPanel)realLayout.FindName("Options");
                Check(realOptions.Children.Count==1&&((QuestionOption)realOptions.Children[0]).IsChecked!=true,"real questions begin without a sample or automatically selected answer");
                ((Button)realLayout.FindName("NextPage")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var realText=(TextBox)realLayout.FindName("ReplyText");realText.Text="second answer";
                ((Button)realLayout.FindName("SendButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(submitted==0&&((TextBlock)realLayout.FindName("PageNumber")).Text=="1 / 2","an unanswered first page is revisited without submitting a partial batch");
                var realOption=(QuestionOption)realOptions.Children[0];realOption.IsChecked=true;
                realOption.SendButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(submitted==1&&received.Length==2&&received[0].selected[0]=="Real choice"&&received[1].custom=="second answer","real window submits the complete structured batch with retained page drafts");
                Check(!((Grid)realLayout.FindName("CardContent")).IsEnabled,"pending network submission disables duplicate UI interaction");
                receipt.SetResult(true);await Task.Delay(50);
                File.WriteAllText(Path.Combine(root,"tests/question-window-interaction-results.json"),new JavaScriptSerializer().Serialize(new { passed=passed,failed=0,scrollPixelsPerSecond=QuestionOption.ScrollPixelsPerSecond,marqueeOverflow=overflow }));
                Console.WriteLine(passed+" question-window checks passed.");
            } catch(Exception error) {Console.Error.WriteLine(error);Environment.ExitCode=1;}
            finally {window.Close();app.Shutdown();}
        };
        app.Run(window);
    }
}
