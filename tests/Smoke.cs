using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Translater;

internal static class Smoke
{
    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private static object Field(object obj, string name)
    {
        return obj.GetType().GetField(name, BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Forms.Application.EnableVisualStyles();
            using (var host = new Forms.Form { Width=1000, Height=800, Text="划译回归检查" })
            using (var popup = new TranslationPopup())
            {
                host.Show(); Forms.Application.DoEvents();
                var context = new SelectionContext { Window=host.Handle, FontPoints=0, LineHeight=0 };
                popup.BeginTranslation(context);
                Forms.Application.DoEvents();
                var body = (TextBox)Field(popup,"body");
                var paper = (Border)Field(popup,"paper");
                Check(body.FontSize==17,"missing source size uses 17 DIP");
                Check(((SolidColorBrush)paper.Background).Color.A==225 && body.Opacity==1,"paper alpha and opaque text");
                popup.AppendTranslation("温度与反应速率有关，但这一结果尚不能确立因果关系。\n\n当 T = 298 K 时，样品的质量为 2.5 mg [12]。");
                popup.FinishTranslation(); Forms.Application.DoEvents();
                var bounds = (Rectangle)Field(popup,"bounds");
                Check(body.Text.StartsWith("温度") && !body.Text.Contains("正在翻译"),"streamed text replaces waiting state");
                popup.Dismiss();
                popup.BeginTranslation(context);
                Check(bounds == (Rectangle)Field(popup,"bounds"),"placement retained between selections");
                context.LineHeight=1000;
                popup.BeginTranslation(context);
                Check(body.FontSize==26,"large PDF font is clamped");
                context.LineHeight=1;
                popup.BeginTranslation(context);
                Check(body.FontSize==14,"small PDF font is clamped");
                context.LineHeight=0;
                popup.BeginTranslation(context);
                popup.AppendTranslation("温度与反应速率有关，但这一结果尚不能确立因果关系。\n\n当 T = 298 K 时，样品的质量为 2.5 mg [12]。研究结果提示，这一效应可能受实验条件影响。");
                popup.FinishTranslation(); Forms.Application.DoEvents();
                paper.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(paper.ActualWidth),(int)Math.Ceiling(paper.ActualHeight),96,96,PixelFormats.Pbgra32);
                bitmap.Render(paper);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"popup-preview.png"))) encoder.Save(file);
                popup.ShowFailure("连接中断");
                Check(body.Text.Contains("连接中断"),"error feedback visible without heading");
                popup.Dismiss(); Check(!popup.Visible,"dismiss hides popup");
                Check(ClipboardCapture.Copy(host.Handle,host.Handle,delegate{return false;})==null,"cancelled selection never copies");
                host.Close();
            }
            if (args.Length>0 && args[0]=="--api")
            {
                string key=Credentials.Read();
                if(String.IsNullOrEmpty(key)) throw new Exception("No configured key for API check");
                var output=new System.Text.StringBuilder();
                DeepSeek.TranslateAsync(key,"At T = 298 K, the sample mass was 2.5 mg [12]. Higher temperature was associated with faster reaction rates, but this observational result does not establish causality and may depend on experimental conditions.",s=>output.Append(s),CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("TRANSLATION " + output);
                Check(output.ToString().Contains("298") && output.ToString().Contains("2.5") && output.ToString().Contains("[12]"),"API preserves quantities and citation");
            }
            return 0;
        }
        catch(Exception ex) { Console.WriteLine("FAIL " + ex); return 1; }
    }
}
