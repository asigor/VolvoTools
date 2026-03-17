using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VolvoToolsGui
{
    public sealed class MainForm : Form
    {
        private readonly TextBox _deviceFilter;
        private readonly ComboBox _platform;
        private readonly ComboBox _baudrate;
        private readonly TextBox _ecuId;
        private readonly TextBox _pin;
        private readonly CheckBox _pinDown;

        private readonly TextBox _flashInput;
        private readonly TextBox _flashSbl;
        private readonly TextBox _readOutput;
        private readonly TextBox _readStart;
        private readonly TextBox _readSize;

        private readonly TextBox _loggerVars;
        private readonly TextBox _loggerOutput;
        private readonly NumericUpDown _loggerPrintCount;
        private readonly TextBox _loggerEcuId;

        private readonly TextBox _logBox;
        private Process? _loggerProcess;

        public MainForm()
        {
            Text = "VolvoTools";
            Width = 900;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            Controls.Add(main);

            var connectionGroup = new GroupBox { Text = "Connection", Dock = DockStyle.Fill };
            main.Controls.Add(connectionGroup, 0, 0);

            var connLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 3
            };
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            connectionGroup.Controls.Add(connLayout);

            _deviceFilter = new TextBox { Dock = DockStyle.Fill };
            _platform = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _baudrate = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _ecuId = new TextBox { Dock = DockStyle.Fill, Text = "7A" };
            _pin = new TextBox { Dock = DockStyle.Fill, Text = "0" };
            _pinDown = new CheckBox { Text = "Scan down (PIN)", Dock = DockStyle.Left };

            _platform.Items.AddRange(new object[] { "P80", "P1", "P1_UDS", "P2", "P2_250", "P2_UDS", "P3", "SPA" });
            _platform.SelectedIndex = 3; // P2
            _baudrate.Items.AddRange(new object[] { "500000", "250000" });
            _baudrate.SelectedIndex = 0;

            connLayout.Controls.Add(new Label { Text = "Device filter", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            connLayout.Controls.Add(_deviceFilter, 1, 0);
            connLayout.Controls.Add(new Label { Text = "Platform", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 2, 0);
            connLayout.Controls.Add(_platform, 3, 0);

            connLayout.Controls.Add(new Label { Text = "Baudrate", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            connLayout.Controls.Add(_baudrate, 1, 1);
            connLayout.Controls.Add(new Label { Text = "ECU ID (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 2, 1);
            connLayout.Controls.Add(_ecuId, 3, 1);

            connLayout.Controls.Add(new Label { Text = "PIN (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            connLayout.Controls.Add(_pin, 1, 2);
            connLayout.Controls.Add(_pinDown, 2, 2);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            main.Controls.Add(tabs, 0, 1);

            var flasherTab = new TabPage("Flasher");
            var loggerTab = new TabPage("Logger");
            tabs.TabPages.Add(flasherTab);
            tabs.TabPages.Add(loggerTab);

            _flashInput = new TextBox { Dock = DockStyle.Fill };
            _flashSbl = new TextBox { Dock = DockStyle.Fill };
            _readOutput = new TextBox { Dock = DockStyle.Fill };
            _readStart = new TextBox { Dock = DockStyle.Fill, Text = "0" };
            _readSize = new TextBox { Dock = DockStyle.Fill, Text = "0" };

            BuildFlasherTab(flasherTab);

            _loggerVars = new TextBox { Dock = DockStyle.Fill };
            _loggerOutput = new TextBox { Dock = DockStyle.Fill };
            _loggerPrintCount = new NumericUpDown { Dock = DockStyle.Left, Minimum = 1, Maximum = 50, Value = 5 };
            _loggerEcuId = new TextBox { Dock = DockStyle.Fill, Text = "7A" };

            BuildLoggerTab(loggerTab);

            _logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            var logGroup = new GroupBox { Text = "Output", Dock = DockStyle.Fill };
            logGroup.Controls.Add(_logBox);
            main.Controls.Add(logGroup, 0, 2);
        }

        private void BuildFlasherTab(TabPage tab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 6
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tab.Controls.Add(layout);

            layout.Controls.Add(new Label { Text = "Flash input", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_flashInput, 1, 0);
            var pickFlash = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickFlash.Click += (_, _) => PickFile(_flashInput, "BIN files|*.bin|All files|*.*");
            layout.Controls.Add(pickFlash, 2, 0);

            layout.Controls.Add(new Label { Text = "SBL (optional)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            layout.Controls.Add(_flashSbl, 1, 1);
            var pickSbl = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickSbl.Click += (_, _) => PickFile(_flashSbl, "BIN files|*.bin|All files|*.*");
            layout.Controls.Add(pickSbl, 2, 1);

            layout.Controls.Add(new Label { Text = "Read output", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            layout.Controls.Add(_readOutput, 1, 2);
            var pickRead = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickRead.Click += (_, _) => PickSaveFile(_readOutput, "BIN files|*.bin|All files|*.*");
            layout.Controls.Add(pickRead, 2, 2);

            layout.Controls.Add(new Label { Text = "Read start (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
            layout.Controls.Add(_readStart, 1, 3);
            layout.Controls.Add(new Label { Text = "Read size (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 2, 3);
            layout.Controls.Add(_readSize, 3, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            var flashBtn = new Button { Text = "Flash", Width = 100 };
            var readBtn = new Button { Text = "Read", Width = 100 };
            var pinBtn = new Button { Text = "Find PIN", Width = 100 };
            var wakeBtn = new Button { Text = "Wakeup", Width = 100 };

            flashBtn.Click += async (_, _) => await RunFlasherAsync("flash");
            readBtn.Click += async (_, _) => await RunFlasherAsync("read");
            pinBtn.Click += async (_, _) => await RunFlasherAsync("pin");
            wakeBtn.Click += async (_, _) => await RunFlasherAsync("wakeup");

            buttons.Controls.Add(flashBtn);
            buttons.Controls.Add(readBtn);
            buttons.Controls.Add(pinBtn);
            buttons.Controls.Add(wakeBtn);

            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 4);
        }

        private void BuildLoggerTab(TabPage tab)
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 5
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tab.Controls.Add(layout);

            layout.Controls.Add(new Label { Text = "Variables file", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_loggerVars, 1, 0);
            var pickVars = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickVars.Click += (_, _) => PickFile(_loggerVars, "Text files|*.txt;*.csv|All files|*.*");
            layout.Controls.Add(pickVars, 2, 0);

            layout.Controls.Add(new Label { Text = "Output log", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            layout.Controls.Add(_loggerOutput, 1, 1);
            var pickOut = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickOut.Click += (_, _) => PickSaveFile(_loggerOutput, "CSV files|*.csv|All files|*.*");
            layout.Controls.Add(pickOut, 2, 1);

            layout.Controls.Add(new Label { Text = "Print count", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            layout.Controls.Add(_loggerPrintCount, 1, 2);
            layout.Controls.Add(new Label { Text = "ECU ID (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 2, 2);
            layout.Controls.Add(_loggerEcuId, 3, 2);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            var startBtn = new Button { Text = "Start", Width = 100 };
            var stopBtn = new Button { Text = "Stop", Width = 100 };
            startBtn.Click += async (_, _) => await RunLoggerAsync();
            stopBtn.Click += (_, _) => StopLogger();
            buttons.Controls.Add(startBtn);
            buttons.Controls.Add(stopBtn);

            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 4);
        }

        private async Task RunFlasherAsync(string mode)
        {
            var exe = ResolveToolPath("VolvoFlasher.exe");
            if (exe == null)
            {
                AppendLog("VolvoFlasher.exe not found next to GUI executable.");
                return;
            }

            var args = new List<string>();
            AppendCommonArgs(args, _ecuId.Text, _pin.Text);

            switch (mode)
            {
                case "flash":
                    if (string.IsNullOrWhiteSpace(_flashInput.Text))
                    {
                        AppendLog("Flash input file is required.");
                        return;
                    }
                    args.Add("flash");
                    args.AddRange(new[] { "-i", Quote(_flashInput.Text) });
                    if (!string.IsNullOrWhiteSpace(_flashSbl.Text))
                    {
                        args.AddRange(new[] { "-s", Quote(_flashSbl.Text) });
                    }
                    break;
                case "read":
                    if (string.IsNullOrWhiteSpace(_readOutput.Text))
                    {
                        AppendLog("Read output file is required.");
                        return;
                    }
                    args.Add("read");
                    args.AddRange(new[] { "-o", Quote(_readOutput.Text) });
                    if (!string.IsNullOrWhiteSpace(_readStart.Text))
                    {
                        args.AddRange(new[] { "-s", _readStart.Text });
                    }
                    if (!string.IsNullOrWhiteSpace(_readSize.Text))
                    {
                        args.AddRange(new[] { "-sz", _readSize.Text });
                    }
                    break;
                case "pin":
                    args.Add("pin");
                    if (_pinDown.Checked)
                    {
                        args.Add("-d");
                    }
                    break;
                case "wakeup":
                    args.Add("wakeup");
                    break;
            }

            await RunProcessAsync(exe, string.Join(' ', args));
        }

        private async Task RunLoggerAsync()
        {
            if (_loggerProcess != null && !_loggerProcess.HasExited)
            {
                AppendLog("Logger is already running.");
                return;
            }

            var exe = ResolveToolPath("VolvoLogger.exe");
            if (exe == null)
            {
                AppendLog("VolvoLogger.exe not found next to GUI executable.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_loggerVars.Text) || string.IsNullOrWhiteSpace(_loggerOutput.Text))
            {
                AppendLog("Variables file and output path are required for logging.");
                return;
            }

            var args = new List<string>();
            AppendCommonArgs(args, _loggerEcuId.Text, "0");
            args.AddRange(new[] { "-v", Quote(_loggerVars.Text) });
            args.AddRange(new[] { "-o", Quote(_loggerOutput.Text) });
            args.AddRange(new[] { "-p", _loggerPrintCount.Value.ToString(CultureInfo.InvariantCulture) });

            _loggerProcess = await RunProcessAsync(exe, string.Join(' ', args), keepProcess: true);
        }

        private void StopLogger()
        {
            if (_loggerProcess == null || _loggerProcess.HasExited)
            {
                AppendLog("Logger is not running.");
                return;
            }

            try
            {
                _loggerProcess.Kill(true);
                AppendLog("Logger stopped.");
            }
            catch (Exception ex)
            {
                AppendLog("Failed to stop logger: " + ex.Message);
            }
        }

        private void AppendCommonArgs(List<string> args, string ecuText, string pinText)
        {
            if (!string.IsNullOrWhiteSpace(_deviceFilter.Text))
            {
                args.AddRange(new[] { "-d", Quote(_deviceFilter.Text) });
            }

            args.AddRange(new[] { "-b", _baudrate.SelectedItem!.ToString()! });
            args.AddRange(new[] { "-f", _platform.SelectedItem!.ToString()! });

            if (!string.IsNullOrWhiteSpace(ecuText))
            {
                args.AddRange(new[] { "-e", ecuText });
            }

            if (!string.IsNullOrWhiteSpace(pinText))
            {
                args.AddRange(new[] { "-p", pinText });
            }
        }

        private static string Quote(string value)
        {
            return value.Contains(' ') ? "\"" + value + "\"" : value;
        }

        private static string? ResolveToolPath(string exeName)
        {
            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, exeName);
            return File.Exists(path) ? path : null;
        }

        private async Task<Process?> RunProcessAsync(string exePath, string arguments, bool keepProcess = false)
        {
            AppendLog($"> {Path.GetFileName(exePath)} {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendLog(e.Data); };

            try
            {
                if (!process.Start())
                {
                    AppendLog("Failed to start process.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                AppendLog("Start failed: " + ex.Message);
                return null;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!keepProcess)
            {
                await Task.Run(() => process.WaitForExit());
                AppendLog($"Process exited with code {process.ExitCode}.");
                process.Dispose();
                return null;
            }

            return process;
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            _logBox.AppendText(message + Environment.NewLine);
        }

        private static void PickFile(TextBox target, string filter)
        {
            using var dialog = new OpenFileDialog { Filter = filter };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                target.Text = dialog.FileName;
            }
        }

        private static void PickSaveFile(TextBox target, string filter)
        {
            using var dialog = new SaveFileDialog { Filter = filter };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                target.Text = dialog.FileName;
            }
        }
    }
}
