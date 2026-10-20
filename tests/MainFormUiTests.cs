using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PersonalKeypad;

internal static class MainFormUiTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    public static int Main()
    {
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-test-data-" + Guid.NewGuid().ToString("N"));
        string captures = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui-captures");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(captures);
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (EventWaitHandle test = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (MainForm form = new MainForm(directory, true, false, show, test))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(10, 10);
                form.Show();
                Application.DoEvents();
                SaveCapture(form, Path.Combine(captures, "normal.png"));
                VerifyControls(form);
                SendMessage(form.Handle, 0x0312, new IntPtr(0x5A11), IntPtr.Zero);
                SendMessage(form.Handle, 0x0312, new IntPtr(0x5A12), IntPtr.Zero);
                string log = File.ReadAllText(Path.Combine(directory, "physical-test.log"));
                if (!log.Contains("F13 / Button 1") || !log.Contains("F14 / Button 2"))
                    throw new Exception("Synthetic test-mode events were not recorded.");
                if (HardwareService.Calls != 0) throw new Exception("Rendering the form unexpectedly accessed hardware.");
                if (File.Exists(Path.Combine(directory, "settings.json"))) throw new Exception("Rendering unexpectedly saved settings.");
                form.Size = form.MinimumSize;
                Application.DoEvents();
                SaveCapture(form, Path.Combine(captures, "minimum.png"));
                VerifyControls(form);
                form.Hide();
            }
            Console.WriteLine("PASS native form renders at normal/minimum size; controls have accessible names; synthetic test mode records both keys; no hardware calls or settings writes.");
            Console.WriteLine("UI captures: " + captures);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL native form: " + ex); return 1; }
        finally { Directory.Delete(directory, true); }
    }

    private static void SaveCapture(Form form, string path)
    {
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(path, ImageFormat.Png);
        }
    }

    private static void VerifyControls(Control control)
    {
        foreach (Control child in control.Controls)
        {
            if (child is Button && String.IsNullOrEmpty(child.Text)) throw new Exception("A button has no label.");
            if (child is TextBox && String.IsNullOrEmpty(child.AccessibleName)) throw new Exception("A text box has no accessible name.");
            if (child is TextBox && child.AccessibleName == "Status and button activity")
            {
                TableLayoutPanel layout = child.Parent as TableLayoutPanel;
                Control footer = layout.GetControlFromPosition(0, 9);
                if (child.Bottom > footer.Top) throw new Exception("The status area overlaps the footer at this window size.");
            }
            if (child.Visible && (child is Button || child is TextBox) &&
                (child.Width < 10 || child.Height < 10)) throw new Exception("A control has collapsed: " + child.Text);
            VerifyControls(child);
        }
    }
}

// The rendering harness cannot reach a real device.
namespace PersonalKeypad
{
    public sealed class HardwareStatus { public bool Prepared; public string Message; public string BackupPath; }
    public static class HardwareService
    {
        public static int Calls;
        private static HardwareStatus Block() { Calls++; throw new InvalidOperationException("Hardware is forbidden in the UI rendering harness."); }
        public static HardwareStatus Inspect(string directory) { return Block(); }
        public static HardwareStatus Prepare(string directory) { return Block(); }
        public static HardwareStatus Restore(string directory) { return Block(); }
    }
}
