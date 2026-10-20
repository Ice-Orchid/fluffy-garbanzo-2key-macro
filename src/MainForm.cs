using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PersonalKeypad
{
    public sealed class MainForm : Form
    {
        private const int WmHotkey = 0x0312;
        private const int Hotkey1 = 0x5A11;
        private const int Hotkey2 = 0x5A12;
        private const uint ModNoRepeat = 0x4000;
        private readonly string dataDirectory;
        private readonly SettingsStore store;
        private readonly TargetDispatcher dispatcher;
        private readonly EventWaitHandle showSettings;
        private readonly EventWaitHandle startTest;
        private readonly bool startMinimized;
        private LauncherSettings saved;
        private bool settingsReadable = true;
        private bool exiting;
        private bool registered1;
        private bool registered2;
        private bool hardwareBusy;
        private bool testMode;
        private int count1;
        private int count2;
        private string loadError;
        private readonly TextBox path1 = new TextBox();
        private readonly TextBox path2 = new TextBox();
        private readonly CheckBox startup = new CheckBox();
        private readonly CheckBox testModeBox = new CheckBox();
        private readonly StatusBox activity = new StatusBox();
        private readonly Button inspect = new Button();
        private readonly Button prepare = new Button();
        private readonly Button restore = new Button();
        private readonly Button save = new Button();
        private readonly Label hotkeyStatus = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly System.Windows.Forms.Timer instanceTimer = new System.Windows.Forms.Timer();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        public MainForm(string dataDirectory, bool testMode, bool minimized, EventWaitHandle showSettings, EventWaitHandle startTest)
        {
            this.dataDirectory = dataDirectory;
            this.testMode = testMode;
            this.startMinimized = minimized && !testMode;
            this.showSettings = showSettings;
            this.startTest = startTest;
            store = new SettingsStore(dataDirectory);
            try { saved = store.Load(); }
            catch (Exception ex)
            {
                saved = LauncherSettings.Defaults();
                settingsReadable = false;
                loadError = ex.Message + "\r\nReview the paths and Save to repair the settings. Button launches are paused until then.";
            }
            dispatcher = new TargetDispatcher(TargetDispatcher.OpenTarget, OnDispatchCompleted);
            BuildForm();
            path1.Text = saved.Button1Path;
            path2.Text = saved.Button2Path;
            startup.Checked = saved.StartWithWindows;
            testModeBox.Checked = testMode;
            ConfigureTray();
            instanceTimer.Interval = 200;
            instanceTimer.Tick += delegate {
                if (startTest.WaitOne(0)) { testModeBox.Checked = true; ShowSettings(); }
                if (showSettings.WaitOne(0)) ShowSettings();
            };
            instanceTimer.Start();
        }

        private void BuildForm()
        {
            Text = "Personal Keypad";
            Name = "PersonalKeypadSettings";
            AccessibleName = "Personal Keypad settings";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(6F, 13F);
            ClientSize = new Size(830, 590);
            MinimumSize = new Size(680, 650);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            AutoScroll = true;

            TableLayoutPanel layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1,
                RowCount = 10, AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 10; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles[8] = new RowStyle(SizeType.Percent, 100);
            Label heading = new Label {
                Text = "Choose what your buttons open", AutoSize = true,
                Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6)
            };
            Label guidance = new Label {
                Text = "Browse to a local program, shortcut, file or folder, then Save.",
                AutoSize = true, Margin = new Padding(0, 0, 0, 14)
            };
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(guidance, 0, 1);
            layout.Controls.Add(CreateAssignment(1, path1), 0, 2);
            layout.Controls.Add(CreateAssignment(2, path2), 0, 3);

            FlowLayoutPanel preferences = new FlowLayoutPanel {
                AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Margin = new Padding(0, 8, 0, 8)
            };
            startup.Text = "Start with &Windows";
            startup.AutoSize = true;
            startup.AccessibleDescription = "Applied when you select Save. Creates or removes a startup shortcut for this copy of Personal Keypad.";
            testModeBox.Text = "&Physical button test — record presses without opening targets";
            testModeBox.AutoSize = true;
            testModeBox.CheckedChanged += delegate {
                this.testMode = testModeBox.Checked;
                count1 = count2 = 0;
                ShowStatus(this.testMode
                    ? "Physical test is on. Press each keypad button, then hold a button. F13/F14 events appear here; no targets will open."
                    : "Physical test is off. F13/F14 now open the saved targets.");
            };
            preferences.Controls.Add(startup);
            preferences.Controls.Add(testModeBox);
            layout.Controls.Add(preferences, 0, 4);

            GroupBox hardware = new GroupBox {
                Text = "Keypad setup", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 4, 0, 10)
            };
            FlowLayoutPanel hardwareButtons = new FlowLayoutPanel {
                Dock = DockStyle.Fill, AutoSize = true, WrapContents = true
            };
            ConfigureButton(inspect, "&Inspect keypad", delegate { RunHardware("Inspect", HardwareService.Inspect); });
            ConfigureButton(prepare, "&Prepare keypad…", delegate { RunHardware("Prepare", HardwareService.Prepare); });
            ConfigureButton(restore, "&Restore previous bindings…", delegate { RunHardware("Restore", HardwareService.Restore); });
            hardwareButtons.Controls.Add(inspect);
            hardwareButtons.Controls.Add(prepare);
            hardwareButtons.Controls.Add(restore);
            hardware.Controls.Add(hardwareButtons);
            layout.Controls.Add(hardware, 0, 5);

            hotkeyStatus.AutoSize = true;
            hotkeyStatus.Margin = new Padding(0, 0, 0, 8);
            hotkeyStatus.AccessibleName = "Global hotkey registration status";
            layout.Controls.Add(hotkeyStatus, 0, 6);
            layout.SizeChanged += delegate {
                hotkeyStatus.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal - 8), 0);
            };
            Label statusLabel = new Label { Text = "Status and button activity:", AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            layout.Controls.Add(statusLabel, 0, 7);
            activity.Multiline = true;
            activity.ReadOnly = true;
            activity.ScrollBars = ScrollBars.Vertical;
            activity.Dock = DockStyle.Fill;
            activity.MinimumSize = new Size(100, 66);
            activity.AccessibleName = "Status and button activity";
            activity.BackColor = SystemColors.Window;
            activity.ForeColor = SystemColors.WindowText;
            layout.Controls.Add(activity, 0, 8);

            TableLayoutPanel footer = new TableLayoutPanel {
                ColumnCount = 2, RowCount = 2, AutoSize = true, Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label closeNote = new Label {
                Text = "Test opens the saved target. Closing this window keeps the launcher running.\r\nUse the notification-area menu to reopen Settings or Exit.",
                AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0)
            };
            ConfigureButton(save, "&Save", SaveSettings);
            save.MinimumSize = new Size(96, 30);
            footer.Controls.Add(closeNote, 0, 0);
            footer.Controls.Add(save, 1, 0);
            layout.Controls.Add(footer, 0, 9);
            Controls.Add(layout);
            AcceptButton = save;
        }

        private Control CreateAssignment(int number, TextBox target)
        {
            GroupBox box = new GroupBox {
                Text = number == 1 ? "Button 1 — Talk (F13)" : "Button 2 — Stop (F14)",
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10), Margin = new Padding(0, 0, 0, 10),
                TabIndex = number - 1
            };
            TableLayoutPanel row = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, AutoSize = true
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label label = new Label {
                Text = number == 1 ? "Button &1 target:" : "Button &2 target:", AutoSize = true,
                Margin = new Padding(0, 0, 0, 4), TabIndex = 0
            };
            target.Dock = DockStyle.Fill;
            target.AccessibleName = "Button " + number + " target path";
            target.AccessibleDescription = "An existing local file or folder. Save applies changes.";
            target.TabIndex = 1;
            target.Margin = new Padding(0, 4, 8, 0);
            Button browse = new Button();
            ConfigureButton(browse, number == 1 ? "&Browse…" : "B&rowse…", delegate { BrowseTarget(target, browse); });
            browse.AccessibleName = "Browse for Button " + number + " target";
            browse.TabIndex = 2;
            Button test = new Button();
            ConfigureButton(test, number == 1 ? "&Test" : "Tes&t", delegate { DispatchButton(number, false); });
            test.AccessibleName = "Test Button " + number + " saved target";
            test.TabIndex = 3;
            row.Controls.Add(label, 0, 0);
            row.SetColumnSpan(label, 3);
            row.Controls.Add(target, 0, 1);
            row.Controls.Add(browse, 1, 1);
            row.Controls.Add(test, 2, 1);
            box.Controls.Add(row);
            return box;
        }

        private static void ConfigureButton(Button button, string text, Action action)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(70, 28);
            button.Margin = new Padding(0, 0, 8, 0);
            button.UseVisualStyleBackColor = true;
            button.Click += delegate { action(); };
        }

        private void BrowseTarget(TextBox target, Button button)
        {
            ContextMenuStrip choice = new ContextMenuStrip();
            choice.Items.Add("Choose a &file…", null, delegate {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Choose a button target";
                    dialog.Filter = "Programs and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*";
                    dialog.CheckFileExists = true;
                    dialog.CheckPathExists = true;
                    dialog.RestoreDirectory = true;
                    if (File.Exists(target.Text)) dialog.FileName = target.Text;
                    else if (Directory.Exists(target.Text)) dialog.InitialDirectory = target.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
                }
                target.Focus();
            });
            choice.Items.Add("Choose a f&older…", null, delegate {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Choose the local folder this button should open.";
                    dialog.ShowNewFolderButton = false;
                    if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
                }
                target.Focus();
            });
            choice.Closed += delegate { BeginInvoke(new Action(choice.Dispose)); };
            choice.Show(button, new Point(0, button.Height));
        }

        private void SaveSettings()
        {
            LauncherSettings candidate = new LauncherSettings {
                Button1Path = path1.Text.Trim(), Button2Path = path2.Text.Trim(), StartWithWindows = startup.Checked
            };
            bool previousStartup = false;
            bool changedStartup = false;
            try
            {
                TargetPath.ValidateSettings(candidate);
                previousStartup = StartupShortcut.IsEnabled(Application.ExecutablePath);
                if (candidate.StartWithWindows != previousStartup)
                {
                    StartupShortcut.SetEnabled(candidate.StartWithWindows, Application.ExecutablePath);
                    changedStartup = true;
                }
                store.Save(candidate);
                saved = candidate.Copy();
                settingsReadable = true;
                path1.Text = saved.Button1Path;
                path2.Text = saved.Button2Path;
                ShowStatus("Assignments saved. " + (testMode ? "Physical test remains on; targets will not open." : "The buttons now use these saved paths."));
            }
            catch (Exception ex)
            {
                string message = "Settings were not saved. " + ex.Message;
                if (changedStartup)
                {
                    try { StartupShortcut.SetEnabled(previousStartup, Application.ExecutablePath); }
                    catch (Exception rollback) { message += "\r\nCould not restore the startup shortcut: " + rollback.Message; }
                }
                ShowStatus(message);
                MessageBox.Show(this, message, "Check the assignments", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RunHardware(string action, Func<string, HardwareStatus> operation)
        {
            if (hardwareBusy) return;
            hardwareBusy = true;
            inspect.Enabled = prepare.Enabled = restore.Enabled = false;
            ShowStatus(action + " is in progress. Keep the keypad connected.");
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs args) { args.Result = operation(dataDirectory); };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs args) {
                hardwareBusy = false;
                if (!IsDisposed)
                {
                    inspect.Enabled = prepare.Enabled = restore.Enabled = true;
                    if (args.Error != null) ShowStatus(action + " could not complete. " + args.Error.Message);
                    else
                    {
                        HardwareStatus result = (HardwareStatus)args.Result;
                        ShowStatus(result.Message + (String.IsNullOrEmpty(result.BackupPath) ? "" : "\r\nBackup: " + result.BackupPath));
                    }
                }
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }

        private void ConfigureTray()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("&Settings", null, delegate { ShowSettings(); });
            menu.Items.Add("E&xit", null, delegate { ExitLauncher(); });
            tray.Icon = SystemIcons.Application;
            tray.Text = "Personal Keypad";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowSettings(); };
            tray.Visible = true;
        }

        public void ShowSettings()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }

        private void ExitLauncher()
        {
            if (hardwareBusy)
            {
                ShowSettings();
                ShowStatus("Wait for the current keypad operation to finish before exiting.");
                return;
            }
            exiting = true;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            registered1 = RegisterHotKey(Handle, Hotkey1, ModNoRepeat, (uint)Keys.F13);
            int error1 = registered1 ? 0 : Marshal.GetLastWin32Error();
            registered2 = RegisterHotKey(Handle, Hotkey2, ModNoRepeat, (uint)Keys.F14);
            int error2 = registered2 ? 0 : Marshal.GetLastWin32Error();
            hotkeyStatus.Text = registered1 && registered2
                ? "F13 and F14 are reserved for your two buttons while Personal Keypad runs."
                : "Could not reserve " + (!registered1 ? "F13 (Windows error " + error1 + ") " : "") +
                    (!registered2 ? "F14 (Windows error " + error2 + ") " : "") + ". " +
                    (error1 == 1409 || error2 == 1409
                        ? "Close the other hotkey app, then exit and reopen Personal Keypad."
                        : "Exit and reopen Personal Keypad in your normal Windows desktop session.");
            if (loadError != null) ShowStatus(loadError);
            else if (testMode) ShowStatus("Physical test is on. Press each keypad button. No targets will open.");
            else ShowStatus("Ready for saved button assignments. Use Inspect keypad to check its current configuration.");
            if (startMinimized && registered1 && registered2 && loadError == null) Hide();
        }

        private void DispatchButton(int number, bool physical)
        {
            if (testMode)
            {
                if (physical)
                {
                    if (number == 1) count1++; else count2++;
                    string observation = DateTime.Now.ToString("HH:mm:ss.fff") + " — observed " +
                        (number == 1 ? "F13 / Button 1" : "F14 / Button 2") +
                        ". Counts: Button 1 = " + count1 + ", Button 2 = " + count2 + ".";
                    ShowStatus(observation + "\r\nPhysical test is on. No target was opened.");
                    RecordObservation(observation);
                }
                else ShowStatus("Physical test is on. Press a physical keypad button to test it, or turn physical test off to open a saved target.");
                return;
            }
            if (!settingsReadable)
            {
                ShowSettings();
                ShowStatus("Button launches are paused because settings could not be read. Review both paths and Save.");
                return;
            }
            dispatcher.Dispatch(number, number == 1 ? saved.Button1Path : saved.Button2Path, false);
        }

        private void RecordObservation(string observation)
        {
            try
            {
                Directory.CreateDirectory(dataDirectory);
                string log = Path.Combine(dataDirectory, "physical-test.log");
                if (File.Exists(log) && new FileInfo(log).Length > 65536)
                {
                    string previous = log + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(log, previous);
                }
                File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd") + " " + observation + Environment.NewLine);
            }
            catch (Exception ex) { ShowStatus(observation + "\r\nNo target was opened. Could not save the test log: " + ex.Message); }
        }

        private void OnDispatchCompleted(int button, string path, Exception error)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(delegate {
                    if (error == null) ShowStatus("Button " + button + ": Windows accepted the request to open the saved target.\r\n" + path);
                    else
                    {
                        ShowStatus("Button " + button + " could not open its target. " + error.Message + "\r\n" + path);
                        tray.ShowBalloonTip(4000, "Personal Keypad", "Button " + button + " could not open its target. Open Settings to review the path.", ToolTipIcon.Warning);
                    }
                }));
            }
            catch (InvalidOperationException) { }
        }

        private void ShowStatus(string message)
        {
            activity.Announce(message);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey)
            {
                int id = message.WParam.ToInt32();
                if (id == Hotkey1) DispatchButton(1, true);
                else if (id == Hotkey2) DispatchButton(2, true);
            }
            base.WndProc(ref message);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if (hardwareBusy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                ShowStatus("Wait for the current keypad operation to finish before exiting.");
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                instanceTimer.Stop();
                instanceTimer.Dispose();
                if (registered1) UnregisterHotKey(Handle, Hotkey1);
                if (registered2) UnregisterHotKey(Handle, Hotkey2);
                tray.Visible = false;
                if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                tray.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class StatusBox : TextBox
        {
            public void Announce(string value)
            {
                Text = value;
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
    }
}
