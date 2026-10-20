using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PersonalKeypad
{
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint processId);
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            string dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            if (args.Length == 1 && (args[0] == "--inspect" || args[0] == "--prepare" || args[0] == "--restore"))
            {
                AttachConsole(0xFFFFFFFF);
                try
                {
                    HardwareStatus status = args[0] == "--inspect" ? HardwareService.Inspect(dataDirectory) :
                        args[0] == "--prepare" ? HardwareService.Prepare(dataDirectory) : HardwareService.Restore(dataDirectory);
                    Console.WriteLine(new JavaScriptSerializer().Serialize(status));
                    return status.Prepared || args[0] == "--inspect" || args[0] == "--restore" ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(new JavaScriptSerializer().Serialize(new { Prepared = false, Message = ex.Message, BackupPath = (string)null }));
                    return 1;
                }
            }
            bool testMode = Array.IndexOf(args, "--test-mode") >= 0;
            bool minimized = Array.IndexOf(args, "--minimized") >= 0;
            foreach (string arg in args)
            {
                if (arg != "--test-mode" && arg != "--minimized")
                {
                    AttachConsole(0xFFFFFFFF);
                    Console.WriteLine("PersonalKeypad.exe [--test-mode] [--minimized]\r\nPersonalKeypad.exe --inspect | --prepare | --restore");
                    return arg == "--help" ? 0 : 2;
                }
            }

            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string owner = WindowsIdentity.GetCurrent().User.Value;
            bool firstInstance;
            using (Mutex singleton = new Mutex(true, @"Local\PersonalKeypad-" + owner, out firstInstance))
            using (EventWaitHandle showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PersonalKeypad-Show-" + owner))
            using (EventWaitHandle startTest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PersonalKeypad-Test-" + owner))
            {
                if (!firstInstance)
                {
                    if (testMode) startTest.Set();
                    showSettings.Set();
                    return 0;
                }
                try
                {
                    using (MainForm form = new MainForm(dataDirectory, testMode, minimized, showSettings, startTest))
                        Application.Run(form);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Personal Keypad could not start.\r\n\r\n" + ex.Message,
                        "Personal Keypad", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { singleton.ReleaseMutex(); }
            }
            return 0;
        }
    }
}
