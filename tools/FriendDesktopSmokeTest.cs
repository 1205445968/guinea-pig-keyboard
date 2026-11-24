using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

internal static class FriendDesktopSmokeTest
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
    private static object Field(object o, string name) { return o.GetType().GetField(name, Private).GetValue(o); }
    private static object Get(object o, string name) { return o.GetType().GetProperty(name).GetValue(o, null); }
    private static void Set(object o, string name, object value) { o.GetType().GetProperty(name).SetValue(o, value, null); }
    private static void Call(object o, string method, params object[] args) { o.GetType().GetMethod(method).Invoke(o, args); }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
    private static void Pump(Window owner) { owner.UpdateLayout(); DispatcherFrame frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; })); Dispatcher.PushFrame(frame); }
    private static object First(IDictionary items) { foreach (DictionaryEntry item in items) return item.Value; return null; }

    [STAThread]
    private static int Main(string[] args)
    {
        Assembly app = Assembly.LoadFile(System.IO.Path.GetFullPath(args[0]));
        object source = Activator.CreateInstance(app.GetType("WheekKeys.PetScene"), true);
        object network = Activator.CreateInstance(app.GetType("WheekKeys.LocalPeers"), true);
        object[] skins = (object[])app.GetType("WheekKeys.PetSkin").GetField("All").GetValue(null);
        Set(source,"Skin",skins[6]);
        Viewbox viewport = new Viewbox { Child = (UIElement)source, Stretch = Stretch.Uniform };
        Window owner = new Window { Width = 310, Height = 310.0 * 308 / 400, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, Opacity = 0, Left = 700, Top = 400, Content = viewport };
        object stage = Activator.CreateInstance(app.GetType("WheekKeys.FriendDesktop"), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] {owner,viewport}, null);
        MethodInfo read = network.GetType().GetMethod("ReadPeer",Private);
        const string id = "11111111111111111111111111111111";
        try
        {
            owner.Show(); Pump(owner);
            read.Invoke(network,new object[]{"WK1|"+id+"|tricolor|100|20",1.0});
            read.Invoke(network,new object[]{"WS1|"+id+"|pixel-tricolor|1",1.0});
            Call(stage,"Update",network,1.0,false,true); Pump(owner);
            IDictionary windows = (IDictionary)Field(stage,"windows");
            Check(windows.Count==1,"one peer window created");
            object wrapper = First(windows); Window friend=(Window)Field(wrapper,"window");
            object remote=Field(wrapper,"scene"); Viewbox remoteView=(Viewbox)Field(wrapper,"box");
            foreach(double width in new[]{180.0,310.0,600.0})
            {
                owner.Width=width; owner.Height=width*308/400; Pump(owner);
                Call(stage,"Update",network,1.1,false,true); friend.UpdateLayout(); Pump(owner);
                Rect ownBounds=((Visual)source).TransformToAncestor(viewport).TransformBounds(new Rect(0,0,400,308));
                Rect remoteBounds=((Visual)remote).TransformToAncestor(remoteView).TransformBounds(new Rect(0,0,400,308));
                Check(Math.Abs(ownBounds.Width-remoteBounds.Width)<0.01 && Math.Abs(ownBounds.Height-remoteBounds.Height)<0.01,"matching rendered size "+width);
                Check(friend.IsVisible,"peer visible "+width);
                friend.Opacity=0;
            }
            read.Invoke(network,new object[]{"WA1|"+id+"|1|k30",1.2});
            Call(stage,"Update",network,1.2,true,false); Pump(owner);
            object pixel=Field(remote,"pixelScene");
            object left=Field(pixel,"left"),right=Field(pixel,"right");
            Check((int)left.GetType().GetField("Scan").GetValue(left)==30 || (int)right.GetType().GetField("Scan").GetValue(right)==30,"network action reaches rendered hand");
            Check(!(bool)Get(remote,"ShowKeyLabels"),"bubble toggle propagated");
            Check(((TextBlock)Field(wrapper,"label")).Visibility==Visibility.Visible,"name independent from bubbles");
            IntPtr handle=((HwndSource)PresentationSource.FromVisual(friend)).Handle;
            Check((GetWindowLong(handle,-20)&0x20)!=0,"click-through follows owner");
            owner.Hide(); Pump(owner); Check(!friend.IsVisible,"peer hides with owner");
            owner.Show(); Pump(owner); Check(friend.IsVisible,"peer returns with owner");
            friend.Close(); Call(stage,"Update",network,1.3,false,true); Pump(owner);
            wrapper=First(windows); friend=(Window)Field(wrapper,"window"); Check(friend.IsVisible,"closed peer recreated without exception"); friend.Opacity=0;
            ((IDisposable)network).Dispose(); Call(stage,"Update",network,1.4,false,true); Pump(owner); Check(windows.Count==0,"leave closes peer window");
            ((IDisposable)stage).Dispose(); owner.Close();
            Console.WriteLine("PASS: equal rendered scale at 180/310/600, real WPF key animation, names, click-through, hide/show, recreate, leave cleanup");
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { ((IDisposable)stage).Dispose(); ((IDisposable)network).Dispose(); owner.Close(); }
    }
}
