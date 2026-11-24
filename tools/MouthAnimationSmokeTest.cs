using System;
using System.Reflection;

internal static class MouthAnimationSmokeTest
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static int Frame(object instance, PropertyInfo property) { return (int)property.GetValue(instance, null); }

    public static int Main(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: MouthAnimationSmokeTest.exe WheekKeys-Mouth.exe"); return 2; }
        Assembly assembly = Assembly.LoadFrom(args[0]);
        Type type = assembly.GetType("WheekKeys.MouthAnimation", true);
        object animation = Activator.CreateInstance(type, true);
        PropertyInfo frame = type.GetProperty("Frame");
        MethodInfo advance = type.GetMethod("Advance"), trigger = type.GetMethod("Trigger"), reset = type.GetMethod("Reset");
        reset.Invoke(animation, new object[] { 0.0 });
        Check(Frame(animation, frame) == 0, "reset opens mouth");
        Check(!(bool)advance.Invoke(animation, new object[] { 1.0, false }), "reset does not move immediately");
        trigger.Invoke(animation, new object[] { 100.0 });
        int[] expected = { 0, 1, 2, 3, 2, 1, 0, 0, 1, 2, 3, 2, 1, 0 };
        for (int i = 0; i < expected.Length; i++)
        {
            bool changed = (bool)advance.Invoke(animation, new object[] { 100.0 + i * 70, false });
            Check(Frame(animation, frame) == expected[i], "episode frame " + i);
            Check(changed == (i == 0 ? false : expected[i] != expected[i - 1]), "change result matches frame");
        }
        advance.Invoke(animation, new object[] { 1080.0, false });
        Check(Frame(animation, frame) == 0, "episode naturally returns to rest");
        Check(!(bool)advance.Invoke(animation, new object[] { 1081.0, false }), "episode waits before repeating");
        trigger.Invoke(animation, new object[] { 2000.0 });
        advance.Invoke(animation, new object[] { 2210.0, false });
        Check(Frame(animation, frame) == 3, "trigger reaches closed frame");
        advance.Invoke(animation, new object[] { 2211.0, true });
        Check(Frame(animation, frame) == 0, "blocked returns to open frame");
        advance.Invoke(animation, new object[] { 2300.0, false });
        Check(Frame(animation, frame) == 0, "startle blocks immediate restart");
        Console.WriteLine("Mouth animation smoke test passed.");
        return 0;
    }
}
