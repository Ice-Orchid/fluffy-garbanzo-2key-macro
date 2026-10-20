using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace PersonalKeypad
{
    public sealed class LauncherSettings
    {
        public int Version = 1;
        public string Button1Path;
        public string Button2Path;
        public bool StartWithWindows;

        public static LauncherSettings Defaults()
        {
            string helpers = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                @"ChatGPT\PROJECTS\Project Galatea\repo\favorite-keys");
            return new LauncherSettings {
                Button1Path = Path.Combine(helpers, "ButlerTalk.exe"),
                Button2Path = Path.Combine(helpers, "ButlerStop.exe")
            };
        }

        public LauncherSettings Copy()
        {
            return new LauncherSettings { Version = Version, Button1Path = Button1Path,
                Button2Path = Button2Path, StartWithWindows = StartWithWindows };
        }
    }

    public static class TargetPath
    {
        public static string ValidationError(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return "Choose a local file or folder.";
            if (path.Length < 3 || !Char.IsLetter(path[0]) || path[1] != ':' ||
                (path[2] != '\\' && path[2] != '/'))
                return "Use a full local path, such as C:\\Folder\\Program.exe.";
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOf('"') >= 0 ||
                path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0 || path.IndexOf(':', 2) >= 0)
                return "Choose one file or folder. Do not add quotes, arguments, or commands.";
            try
            {
                string full = Path.GetFullPath(path);
                if (new DriveInfo(Path.GetPathRoot(full)).DriveType == DriveType.Network)
                    return "Choose a path on this computer, rather than a network drive.";
                if (!File.Exists(full) && !Directory.Exists(full))
                    return "This path does not exist or cannot be accessed. Browse to its current location.";
            }
            catch (Exception ex)
            {
                if (!(ex is ArgumentException) && !(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is NotSupportedException) && !(ex is System.Security.SecurityException)) throw;
                return "This path cannot be used. Choose an accessible local file or folder.";
            }
            return null;
        }

        public static void ValidateSettings(LauncherSettings settings)
        {
            if (settings == null || settings.Version != 1) throw new InvalidDataException("These settings use an unsupported format.");
            string error = ValidationError(settings.Button1Path);
            if (error != null) throw new ArgumentException("Button 1: " + error);
            error = ValidationError(settings.Button2Path);
            if (error != null) throw new ArgumentException("Button 2: " + error);
        }
    }

    public sealed class SettingsStore
    {
        public readonly string FilePath;
        public SettingsStore(string directory) { FilePath = Path.Combine(directory, "settings.json"); }

        public LauncherSettings Load()
        {
            if (!File.Exists(FilePath)) return LauncherSettings.Defaults();
            try
            {
                FileInfo info = new FileInfo(FilePath);
                if (info.Length > 65536) throw new InvalidDataException("The settings file is larger than expected.");
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 65536;
                object raw = serializer.DeserializeObject(json);
                System.Collections.Generic.Dictionary<string, object> fields = raw as System.Collections.Generic.Dictionary<string, object>;
                if (fields == null || !fields.ContainsKey("Version")) throw new InvalidDataException("The settings version is missing.");
                LauncherSettings loaded = serializer.Deserialize<LauncherSettings>(json);
                if (loaded == null || loaded.Version != 1 || loaded.Button1Path == null || loaded.Button2Path == null)
                    throw new InvalidDataException("The settings format is unsupported or incomplete.");
                // Targets may have moved since the last save. Keep them visible for repair;
                // dispatch validates their current existence every time.
                return loaded;
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException || ex is StackOverflowException || ex is ThreadAbortException) throw;
                throw new InvalidDataException("Could not read settings. The original file was kept at " + FilePath + ". " + ex.Message, ex);
            }
        }

        public void Save(LauncherSettings settings)
        {
            TargetPath.ValidateSettings(settings);
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(settings));
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".previous");
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public sealed class TargetDispatcher
    {
        private readonly Action<string> start;
        private readonly Action<int, string, Exception> completed;
        public TargetDispatcher(Action<string> start, Action<int, string, Exception> completed)
        {
            if (start == null || completed == null) throw new ArgumentNullException();
            this.start = start;
            this.completed = completed;
        }

        public void Dispatch(int button, string path, bool testMode)
        {
            if (button != 1 && button != 2) throw new ArgumentOutOfRangeException("button");
            if (testMode) { completed(button, path, null); return; }
            // One thread per request: a slow shell launch for Talk cannot queue Stop
            // behind it. Never wait for the target process to exit.
            Thread worker = new Thread(delegate() {
                Exception failure = null;
                try
                {
                    string error = TargetPath.ValidationError(path);
                    if (error != null) throw new InvalidOperationException(error);
                    start(path);
                }
                catch (Exception ex) { failure = ex; }
                completed(button, path, failure);
            });
            worker.IsBackground = true;
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        public static void OpenTarget(string path)
        {
            ProcessStartInfo info = new ProcessStartInfo {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path)
            };
            using (Process process = Process.Start(info)) { }
        }
    }

    public static class StartupShortcut
    {
        private static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Personal Keypad.lnk"); }
        }

        public static bool IsEnabled(string executable)
        {
            if (!File.Exists(ShortcutPath)) return false;
            return IsOwned(ShortcutPath, executable);
        }

        public static void SetEnabled(bool enabled, string executable)
        {
            string path = ShortcutPath;
            if (File.Exists(path) && !IsOwned(path, executable))
                throw new IOException("A different Personal Keypad startup shortcut already exists. No shortcut was changed: " + path);
            if (!enabled)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = Path.Combine(Path.GetDirectoryName(path), "Personal Keypad-" + Guid.NewGuid().ToString("N") + ".lnk");
            try
            {
                WithShortcut(temporary, delegate(object shortcut) {
                    SetProperty(shortcut, "TargetPath", executable);
                    SetProperty(shortcut, "Arguments", "--minimized");
                    SetProperty(shortcut, "WorkingDirectory", Path.GetDirectoryName(executable));
                    SetProperty(shortcut, "Description", "Personal Keypad button launcher");
                    shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                });
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static bool IsOwned(string path, string executable)
        {
            bool owned = false;
            WithShortcut(path, delegate(object shortcut) {
                string target = (string)shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                owned = String.Equals(Path.GetFullPath(target), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
            });
            return owned;
        }

        private static void SetProperty(object shortcut, string name, object value)
        {
            shortcut.GetType().InvokeMember(name, BindingFlags.SetProperty, null, shortcut, new object[] { value });
        }

        private static void WithShortcut(string path, Action<object> action)
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type type = Type.GetTypeFromProgID("WScript.Shell", true);
                shell = Activator.CreateInstance(type);
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                action(shortcut);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
