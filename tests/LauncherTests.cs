using System;
using System.IO;
using System.Threading;
using PersonalKeypad;

internal static class LauncherTests
{
    private static int failures;
    private static string directory;
    private static string firstPath;
    private static string secondPath;

    public static int Main()
    {
        directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PersonalKeypadTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        firstPath = Path.Combine(directory, "Talk with spaces \u05e9\u05dc\u05d5\u05dd.txt");
        secondPath = Path.Combine(directory, "Stop.txt");
        File.WriteAllText(firstPath, "Test fixture only; never launched.");
        File.WriteAllText(secondPath, "Test fixture only; never launched.");
        try
        {
            Run("accepts an existing absolute Unicode file path", delegate {
                Equal(null, TargetPath.ValidationError(firstPath));
            });
            Run("accepts an existing local folder", delegate {
                Equal(null, TargetPath.ValidationError(directory));
            });
            Run("rejects empty, relative, URL, command line and missing targets", delegate {
                foreach (string target in new string[] { "", "   ", "relative.exe", "https://example.com", firstPath + " --flag", "\"" + firstPath + "\"", Path.Combine(directory, "missing.exe") })
                    True(TargetPath.ValidationError(target) != null, "Unexpectedly accepted: " + target);
            });
            Run("rejects network and device paths before checking existence", delegate {
                foreach (string target in new string[] { @"\\server\share\thing.exe", @"\\?\C:\Windows\notepad.exe", @"\\.\C:\Windows\notepad.exe" })
                    True(TargetPath.ValidationError(target) != null, "Unexpectedly accepted: " + target);
            });
            Run("round trips settings without losing Unicode or startup choice", delegate {
                SettingsStore store = new SettingsStore(Path.Combine(directory, "roundtrip"));
                store.Save(new LauncherSettings { Button1Path = firstPath, Button2Path = secondPath, StartWithWindows = true });
                LauncherSettings actual = store.Load();
                Equal(firstPath, actual.Button1Path);
                Equal(secondPath, actual.Button2Path);
                True(actual.StartWithWindows, "Startup preference was lost.");
            });
            Run("invalid save preserves previous valid settings", delegate {
                SettingsStore store = new SettingsStore(Path.Combine(directory, "invalid-save"));
                store.Save(new LauncherSettings { Button1Path = firstPath, Button2Path = secondPath });
                string before = File.ReadAllText(store.FilePath);
                Throws(delegate { store.Save(new LauncherSettings { Button1Path = firstPath, Button2Path = "missing" }); });
                Equal(before, File.ReadAllText(store.FilePath));
            });
            Run("atomic replacement keeps an intact previous settings copy", delegate {
                SettingsStore store = new SettingsStore(Path.Combine(directory, "replacement"));
                store.Save(new LauncherSettings { Button1Path = firstPath, Button2Path = secondPath });
                string before = File.ReadAllText(store.FilePath);
                store.Save(new LauncherSettings { Button1Path = secondPath, Button2Path = firstPath });
                Equal(secondPath, store.Load().Button1Path);
                Equal(before, File.ReadAllText(store.FilePath + ".previous"));
                Equal(0, Directory.GetFiles(Path.GetDirectoryName(store.FilePath), "*.tmp").Length);
            });
            Run("corrupt settings fail clearly without overwriting evidence", delegate {
                SettingsStore store = new SettingsStore(Path.Combine(directory, "corrupt"));
                Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath));
                File.WriteAllText(store.FilePath, "not json");
                Throws(delegate { store.Load(); });
                Equal("not json", File.ReadAllText(store.FilePath));
            });
            Run("unknown settings schema is rejected", delegate {
                SettingsStore store = new SettingsStore(Path.Combine(directory, "schema"));
                Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath));
                File.WriteAllText(store.FilePath, "{\"Version\":98,\"Button1Path\":\"x\",\"Button2Path\":\"y\"}");
                Throws(delegate { store.Load(); });
            });
            Run("Stop dispatch does not wait for blocked Talk dispatch", delegate {
                using (ManualResetEvent talkStarted = new ManualResetEvent(false))
                using (ManualResetEvent releaseTalk = new ManualResetEvent(false))
                using (ManualResetEvent stopStarted = new ManualResetEvent(false))
                using (ManualResetEvent talkFinished = new ManualResetEvent(false))
                {
                    TargetDispatcher dispatcher = new TargetDispatcher(delegate(string path) {
                        if (path == firstPath) { talkStarted.Set(); releaseTalk.WaitOne(5000); }
                        else if (path == secondPath) stopStarted.Set();
                        else throw new Exception("Unexpected dispatch target: " + path);
                    }, delegate(int button, string path, Exception error) { if (button == 1) talkFinished.Set(); });
                    try
                    {
                        dispatcher.Dispatch(1, firstPath, false);
                        True(talkStarted.WaitOne(1500), "Talk did not begin.");
                        dispatcher.Dispatch(2, secondPath, false);
                        True(stopStarted.WaitOne(1500), "Stop was blocked by Talk.");
                    }
                    finally { releaseTalk.Set(); talkFinished.WaitOne(2000); }
                }
            });
            Run("test mode observes a button without launching a target", delegate {
                int launches = 0;
                int observed = 0;
                TargetDispatcher dispatcher = new TargetDispatcher(delegate(string path) { Interlocked.Increment(ref launches); },
                    delegate(int button, string path, Exception error) { observed = button; });
                dispatcher.Dispatch(2, secondPath, true);
                Equal(0, launches);
                Equal(2, observed);
            });
            Run("missing target reports failure without launching", delegate {
                int launches = 0;
                Exception failure = null;
                using (ManualResetEvent done = new ManualResetEvent(false))
                {
                    TargetDispatcher dispatcher = new TargetDispatcher(delegate(string path) { Interlocked.Increment(ref launches); },
                        delegate(int button, string path, Exception error) { failure = error; done.Set(); });
                    dispatcher.Dispatch(1, Path.Combine(directory, "absent.exe"), false);
                    True(done.WaitOne(1500), "No result was reported.");
                    True(failure != null, "Missing target should report an error.");
                    Equal(0, launches);
                }
            });
            Run("launcher failure is reported without crashing the caller", delegate {
                Exception failure = null;
                using (ManualResetEvent done = new ManualResetEvent(false))
                {
                    TargetDispatcher dispatcher = new TargetDispatcher(delegate(string path) { throw new InvalidOperationException("fixture failure"); },
                        delegate(int button, string path, Exception error) { failure = error; done.Set(); });
                    dispatcher.Dispatch(2, secondPath, false);
                    True(done.WaitOne(1500), "No failure result was reported.");
                    True(failure is InvalidOperationException, "Launch exception was lost.");
                }
            });
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine(failures == 0 ? "All launcher tests passed." : failures + " launcher test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.ToString()); }
    }
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(object expected, object actual) { if (!Object.Equals(expected, actual)) throw new Exception("Expected " + (expected ?? "<null>") + ", got " + (actual ?? "<null>")); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected an error."); }
}
