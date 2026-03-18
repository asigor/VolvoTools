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
        private sealed class DeviceItem
        {
            public DeviceItem(string name, string library)
            {
                Name = name;
                Library = library;
            }

            public string Name { get; }
            public string Library { get; }

            public override string ToString() => Name;
        }

        private readonly ComboBox _deviceList;
        private readonly Button _refreshDevices;
        private readonly Button _connectDevice;
        private readonly Label _driverLabel;
        private readonly ComboBox _platform;
        private readonly ComboBox _baudrate;
        private readonly TextBox _ecuId;
        private readonly TextBox _pin;
        private readonly CheckBox _pinDown;
        private readonly ComboBox _targetModule;

        private readonly TextBox _flashInput;
        private readonly TextBox _flashSbl;
        private readonly TextBox _readOutput;
        private readonly ProgressBar _progress;
        private readonly Button _clearLog;

        private readonly TextBox _loggerVars;
        private readonly TextBox _loggerOutput;
        private readonly NumericUpDown _loggerPrintCount;
        private readonly TextBox _loggerEcuId;

        private readonly TextBox _cemFlashInput;
        private readonly TextBox _cemFlashSbl;
        private readonly TextBox _cemReadOutput;
        private readonly TextBox _cemEcuId;
        private readonly TextBox _cemPin;
        private readonly CheckBox _cemPinDown;

        private readonly TextBox _logBox;
        private Process? _loggerProcess;
        private readonly List<DeviceItem> _devices = new();

        public MainForm()
        {
            Text = "VolvoTools";
            Width = 1100;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
            Controls.Add(main);

            var connectionGroup = new GroupBox { Text = "Connection", Dock = DockStyle.Fill };
            main.Controls.Add(connectionGroup, 0, 0);

            var connLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 4
            };
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            connLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            connectionGroup.Controls.Add(connLayout);

            _deviceList = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            _refreshDevices = new Button { Text = "Refresh", Dock = DockStyle.Left, Width = 80 };
            _connectDevice = new Button { Text = "Connect", Dock = DockStyle.Left, Width = 80 };
            _driverLabel = new Label { Text = "Driver: -", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            _platform = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _baudrate = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _ecuId = new TextBox { Dock = DockStyle.Fill, Text = "7A" };
            _pin = new TextBox { Dock = DockStyle.Fill, Text = "0" };
            _pinDown = new CheckBox { Text = "Scan down (PIN)", Dock = DockStyle.Left };
            _targetModule = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _targetModule.Items.AddRange(new object[] { "ECU", "CEM" });
            _targetModule.SelectedIndex = 0;

            _platform.Items.AddRange(new object[]
            {
                "P80", "P1", "P1_UDS", "P2", "P2_250", "P2_UDS", "P3", "SPA",
                "FORD_KWP", "FORD_UDS", "HAVAL_UDS", "VAG", "VAG_MED91", "VAG_MED912"
            });
            _platform.SelectedIndex = 3; // P2
            _baudrate.Items.AddRange(new object[] { "500000", "250000", "125000" });
            _baudrate.SelectedIndex = 0;

            connLayout.Controls.Add(new Label { Text = "Device", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            connLayout.Controls.Add(_deviceList, 1, 0);
            var deviceButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            deviceButtons.Controls.Add(_refreshDevices);
            deviceButtons.Controls.Add(_connectDevice);
            connLayout.Controls.Add(deviceButtons, 2, 0);
            connLayout.Controls.Add(_driverLabel, 3, 0);

            _refreshDevices.Click += async (_, _) => await RefreshDevicesAsync();
            _connectDevice.Click += async (_, _) => await ConnectDeviceAsync();
            _deviceList.SelectedIndexChanged += (_, _) => UpdateDriverLabel();
            _deviceList.TextChanged += (_, _) => UpdateDriverLabel();

            connLayout.Controls.Add(new Label { Text = "Baudrate", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            connLayout.Controls.Add(_baudrate, 1, 1);
            connLayout.Controls.Add(new Label { Text = "ECU ID (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 2, 1);
            connLayout.Controls.Add(_ecuId, 3, 1);

            connLayout.Controls.Add(new Label { Text = "PIN (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            connLayout.Controls.Add(_pin, 1, 2);
            connLayout.Controls.Add(new Label { Text = "Module", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 2, 2);
            connLayout.Controls.Add(_targetModule, 3, 2);
            connLayout.Controls.Add(_pinDown, 1, 3);
            connLayout.SetColumnSpan(_pinDown, 3);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            main.Controls.Add(tabs, 0, 1);

            var flasherTab = new TabPage("Flasher");
            var loggerTab = new TabPage("Logger");
            var cemTab = new TabPage("CEM");
            tabs.TabPages.Add(flasherTab);
            tabs.TabPages.Add(loggerTab);
            tabs.TabPages.Add(cemTab);

            _flashInput = new TextBox { Dock = DockStyle.Fill };
            _flashSbl = new TextBox { Dock = DockStyle.Fill };
            _readOutput = new TextBox { Dock = DockStyle.Fill };
            _deviceList.Text = "Auto";
            _driverLabel.Text = "Driver: auto";

            BuildFlasherTab(flasherTab);

            _loggerVars = new TextBox { Dock = DockStyle.Fill };
            _loggerOutput = new TextBox { Dock = DockStyle.Fill };
            _loggerPrintCount = new NumericUpDown { Dock = DockStyle.Left, Minimum = 1, Maximum = 50, Value = 5 };
            _loggerEcuId = new TextBox { Dock = DockStyle.Fill, Text = "7A" };

            BuildLoggerTab(loggerTab);

            _cemFlashInput = new TextBox { Dock = DockStyle.Fill };
            _cemFlashSbl = new TextBox { Dock = DockStyle.Fill };
            _cemReadOutput = new TextBox { Dock = DockStyle.Fill };
            _cemEcuId = new TextBox { Dock = DockStyle.Fill, Text = "7A" };
            _cemPin = new TextBox { Dock = DockStyle.Fill, Text = "0" };
            _cemPinDown = new CheckBox { Text = "Scan down (PIN)", Dock = DockStyle.Left };

            BuildCemTab(cemTab);

            _logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical
            };
            _clearLog = new Button { Text = "Clear", Dock = DockStyle.Right, Width = 80 };
            _clearLog.Click += (_, _) => _logBox.Clear();
            _progress = new ProgressBar { Dock = DockStyle.Bottom, Style = ProgressBarStyle.Marquee, Visible = false };

            var logGroup = new GroupBox { Text = "Output", Dock = DockStyle.Fill };
            var logPanel = new Panel { Dock = DockStyle.Fill };
            logPanel.Controls.Add(_logBox);
            logPanel.Controls.Add(_progress);
            logPanel.Controls.Add(_clearLog);
            logGroup.Controls.Add(logPanel);
            main.Controls.Add(logGroup, 0, 2);

            _ = RefreshDevicesAsync();
        }

        private void BuildFlasherTab(TabPage tab)
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

            var hints = new Label
            {
                Text = "Inputs are hex without 0x prefix.",
                Dock = DockStyle.Fill,
                ForeColor = System.Drawing.Color.DimGray
            };
            layout.Controls.Add(hints, 0, 3);
            layout.SetColumnSpan(hints, 4);

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

        private void BuildCemTab(TabPage tab)
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

            layout.Controls.Add(new Label { Text = "CEM ECU ID (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            layout.Controls.Add(_cemEcuId, 1, 0);
            layout.Controls.Add(new Label { Text = "CEM PIN (hex)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 2, 0);
            layout.Controls.Add(_cemPin, 3, 0);

            layout.Controls.Add(new Label { Text = "Flash input", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            layout.Controls.Add(_cemFlashInput, 1, 1);
            var pickFlash = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickFlash.Click += (_, _) => PickFile(_cemFlashInput, "BIN files|*.bin|All files|*.*");
            layout.Controls.Add(pickFlash, 2, 1);

            layout.Controls.Add(new Label { Text = "SBL (optional)", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            layout.Controls.Add(_cemFlashSbl, 1, 2);
            var pickSbl = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickSbl.Click += (_, _) => PickFile(_cemFlashSbl, "BIN files|*.bin|All files|*.*");
            layout.Controls.Add(pickSbl, 2, 2);

            layout.Controls.Add(new Label { Text = "Read output", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
            layout.Controls.Add(_cemReadOutput, 1, 3);
            var pickRead = new Button { Text = "...", Dock = DockStyle.Left, Width = 30 };
            pickRead.Click += (_, _) => PickSaveFile(_cemReadOutput, "BIN files|*.bin|All files|*.*");
            layout.Controls.Add(pickRead, 2, 3);

            layout.Controls.Add(_cemPinDown, 1, 4);
            layout.SetColumnSpan(_cemPinDown, 3);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            var flashBtn = new Button { Text = "Flash CEM", Width = 120 };
            var readBtn = new Button { Text = "Read CEM", Width = 120 };
            var pinBtn = new Button { Text = "Find PIN", Width = 100 };
            var wakeBtn = new Button { Text = "Wakeup", Width = 100 };

            flashBtn.Click += async (_, _) => await RunFlasherAsync("flash", "CEM", _cemEcuId.Text, _cemPin.Text, _cemFlashInput.Text, _cemFlashSbl.Text, _cemReadOutput.Text, _cemPinDown.Checked);
            readBtn.Click += async (_, _) => await RunFlasherAsync("read", "CEM", _cemEcuId.Text, _cemPin.Text, _cemFlashInput.Text, _cemFlashSbl.Text, _cemReadOutput.Text, _cemPinDown.Checked);
            pinBtn.Click += async (_, _) => await RunFlasherAsync("pin", "CEM", _cemEcuId.Text, _cemPin.Text, _cemFlashInput.Text, _cemFlashSbl.Text, _cemReadOutput.Text, _cemPinDown.Checked);
            wakeBtn.Click += async (_, _) => await RunFlasherAsync("wakeup", "CEM", _cemEcuId.Text, _cemPin.Text, _cemFlashInput.Text, _cemFlashSbl.Text, _cemReadOutput.Text, _cemPinDown.Checked);

            buttons.Controls.Add(flashBtn);
            buttons.Controls.Add(readBtn);
            buttons.Controls.Add(pinBtn);
            buttons.Controls.Add(wakeBtn);

            layout.Controls.Add(buttons, 0, 5);
            layout.SetColumnSpan(buttons, 4);
        }

        private async Task RunFlasherAsync(string mode, string? moduleOverride = null, string? ecuText = null, string? pinText = null,
            string? flashInput = null, string? flashSbl = null, string? readOutput = null, bool pinDown = false)
        {
            var exe = ResolveToolPath("VolvoFlasher.exe");
            if (exe == null)
            {
                AppendLog("VolvoFlasher.exe not found next to GUI executable.");
                return;
            }

            var ecuInput = ecuText ?? _ecuId.Text;
            var pinInput = pinText ?? _pin.Text;

            if (!TryGetHexByte(ecuInput, out var ecuId))
            {
                AppendLog("ECU ID must be a hex byte (00-FF).");
                return;
            }
            if (!TryGetHexUInt64(pinInput, out var pin))
            {
                AppendLog("PIN must be hex.");
                return;
            }

            var args = new List<string>();
            AppendCommonArgs(args, ecuId, pin, moduleOverride);

            switch (mode)
            {
                case "flash":
                    var flashPath = flashInput ?? _flashInput.Text;
                    var sblPath = flashSbl ?? _flashSbl.Text;
                    if (string.IsNullOrWhiteSpace(flashPath))
                    {
                        AppendLog("Flash input file is required.");
                        return;
                    }
                    args.Add("flash");
                    args.AddRange(new[] { "-i", Quote(flashPath) });
                    if (!string.IsNullOrWhiteSpace(sblPath))
                    {
                        args.AddRange(new[] { "-s", Quote(sblPath) });
                    }
                    break;
                case "read":
                    var outputPath = readOutput ?? _readOutput.Text;
                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        AppendLog("Read output file is required.");
                        return;
                    }
                    args.Add("read");
                    args.AddRange(new[] { "-o", Quote(outputPath) });
                    break;
                case "pin":
                    args.Add("pin");
                    if (pinDown || _pinDown.Checked)
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

            if (!TryGetHexByte(_loggerEcuId.Text, out var ecuId))
            {
                AppendLog("Logger ECU ID must be a hex byte (00-FF).");
                return;
            }

            var args = new List<string>();
            AppendCommonArgs(args, ecuId, 0);
            args.AddRange(new[] { "-v", Quote(_loggerVars.Text) });
            args.AddRange(new[] { "-o", Quote(_loggerOutput.Text) });
            args.AddRange(new[] { "-p", _loggerPrintCount.Value.ToString(CultureInfo.InvariantCulture) });

            _loggerProcess = await RunProcessAsync(exe, string.Join(' ', args), keepProcess: true);
            SetBusy(_loggerProcess != null && !_loggerProcess.HasExited);
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
                SetBusy(false);
            }
            catch (Exception ex)
            {
                AppendLog("Failed to stop logger: " + ex.Message);
            }
        }

        private void AppendCommonArgs(List<string> args, int ecuId, ulong pin, string? moduleOverride = null)
        {
            var deviceName = GetSelectedDeviceName();
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                args.AddRange(new[] { "-d", Quote(deviceName) });
            }

            args.AddRange(new[] { "-b", _baudrate.SelectedItem!.ToString()! });
            args.AddRange(new[] { "-f", _platform.SelectedItem!.ToString()! });
            var module = moduleOverride ?? _targetModule.SelectedItem!.ToString()!;
            args.AddRange(new[] { "-m", module });

            args.AddRange(new[] { "-e", ecuId.ToString("X2") });
            args.AddRange(new[] { "-p", pin.ToString("X") });
        }

        private static string Quote(string value)
        {
            return value.Contains(' ') ? "\"" + value + "\"" : value;
        }

        private string GetSelectedDeviceName()
        {
            if (_deviceList.SelectedItem is DeviceItem item)
            {
                return item.Name;
            }
            return _deviceList.Text.Trim();
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
            SetBusy(true);

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
                try
                {
                    await Task.Run(() => process.WaitForExit());
                    AppendLog($"Process exited with code {process.ExitCode}.");
                }
                finally
                {
                    process.Dispose();
                    SetBusy(false);
                }
                return null;
            }

            return process;
        }

        private async Task RefreshDevicesAsync()
        {
            var exe = ResolveToolPath("VolvoFlasher.exe");
            if (exe == null)
            {
                AppendLog("VolvoFlasher.exe not found next to GUI executable.");
                return;
            }

            var output = await RunProcessCaptureAsync(exe, "devices");
            if (output == null)
            {
                return;
            }

            _devices.Clear();
            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 2)
                {
                    _devices.Add(new DeviceItem(parts[0].Trim(), parts[1].Trim()));
                }
            }

            _deviceList.BeginUpdate();
            _deviceList.Items.Clear();
            foreach (var device in _devices)
            {
                _deviceList.Items.Add(device);
            }
            _deviceList.EndUpdate();

            if (_deviceList.Items.Count > 0)
            {
                _deviceList.SelectedIndex = 0;
            }
            UpdateDriverLabel();
        }

        private async Task ConnectDeviceAsync()
        {
            var exe = ResolveToolPath("VolvoFlasher.exe");
            if (exe == null)
            {
                AppendLog("VolvoFlasher.exe not found next to GUI executable.");
                return;
            }

            var deviceName = GetSelectedDeviceName();
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                AppendLog("Select a device before connecting.");
                return;
            }

            var args = new List<string> { "connect" };
            args.AddRange(new[] { "-d", Quote(deviceName) });
            await RunProcessAsync(exe, string.Join(' ', args));
        }

        private void UpdateDriverLabel()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateDriverLabel));
                return;
            }

            var device = _deviceList.SelectedItem as DeviceItem;
            if (device != null)
            {
                _driverLabel.Text = $"Driver: {device.Library}";
                return;
            }

            var typed = _deviceList.Text.Trim();
            var match = _devices.FirstOrDefault(d => d.Name.Equals(typed, StringComparison.OrdinalIgnoreCase));
            _driverLabel.Text = match != null ? $"Driver: {match.Library}" : "Driver: auto";
        }

        private async Task<string?> RunProcessCaptureAsync(string exePath, string arguments)
        {
            AppendLog($"> {Path.GetFileName(exePath)} {arguments}");
            SetBusy(true);

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

            var output = new StringBuilder();
            var process = new Process { StartInfo = startInfo };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
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

            await Task.Run(() => process.WaitForExit());
            SetBusy(false);
            return output.ToString();
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

        private void SetBusy(bool busy)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetBusy), busy);
                return;
            }
            _progress.Visible = busy;
        }

        private static bool TryGetHexByte(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= 0xFF;
        }

        private static bool TryGetHexUInt64(string text, out ulong value)
        {
            return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
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
