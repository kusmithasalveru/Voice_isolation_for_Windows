using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using IsoVoice.Core;

namespace IsoVoice.Tray
{
    internal class MainForm : Form
    {
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
        private ToolStripMenuItem? _toggleMenuItem;
        private ToolStripMenuItem? _statusMenuItem;
        private ToolStripMenuItem? _autoStartMenuItem;
        private ToolStripSeparator? _separator1;
        private ToolStripMenuItem? _exitMenuItem;
        private EngineWrapper? _engine;
        private AppSettings _settings = new AppSettings();
        private readonly TaskCompletionSource<bool> _mainFormClosed = new TaskCompletionSource<bool>();
        private bool _isToggling;
        private CancellationTokenSource? _initCts;
        
        public MainForm()
        {
            InitializeComponent();
            LoadSettings();
            // Defer RestoreState until after form handle is created
            Shown += MainForm_Shown;
        }
        
        private void MainForm_Shown(object? sender, EventArgs e)
        {
            RestoreState();
        }
        
        private void InitializeComponent()
        {
            Hide();
            ShowInTaskbar = false;
            
            // Create context menu
            _contextMenu = new ContextMenuStrip();
            
            _toggleMenuItem = new ToolStripMenuItem("Enable Voice Isolation");
            _toggleMenuItem.Click += ToggleMenuItem_Click;
            _contextMenu.Items.Add(_toggleMenuItem);
            
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            _statusMenuItem = new ToolStripMenuItem("Status: OFF");
            _statusMenuItem.Enabled = false;
            _contextMenu.Items.Add(_statusMenuItem);
            
            _separator1 = new ToolStripSeparator();
            _contextMenu.Items.Add(_separator1);
            
            _autoStartMenuItem = new ToolStripMenuItem("Auto-start on login");
            _autoStartMenuItem.Checked = _settings.AutoStart;
            _autoStartMenuItem.Click += AutoStartMenuItem_Click;
            _contextMenu.Items.Add(_autoStartMenuItem);
            
            _contextMenu.Items.Add(new ToolStripSeparator());
            
            _exitMenuItem = new ToolStripMenuItem("Exit");
            _exitMenuItem.Click += ExitMenuItem_Click;
            _contextMenu.Items.Add(_exitMenuItem);
            
            // Create notify icon
            _notifyIcon = new NotifyIcon()
            {
                Text = "IsoVoice",
                Visible = true,
                ContextMenuStrip = _contextMenu
            };
            
            UpdateIconAndTooltip(false);
            
            _notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
            _notifyIcon.Click += NotifyIcon_Click;
            
            // Handle engine events
            _engine = new EngineWrapper();
            _engine.LogMessage += Engine_LogMessage;
            _engine.FrameProcessed += Engine_FrameProcessed;
            
            // Handle form closing
            FormClosing += MainForm_FormClosing;
        }
        
        private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
        {
            ToggleMenuItem_Click(null, EventArgs.Empty);
        }
        
        private void NotifyIcon_Click(object? sender, EventArgs e)
        {
            ToggleMenuItem_Click(null, EventArgs.Empty);
        }
        
        private void RestoreState()
        {
            // Restore UI from saved settings
            UpdateUI(_settings.IsEnabled, _settings.IsEnabled ? "Running" : "Stopped");
            if (_autoStartMenuItem != null)
                _autoStartMenuItem.Checked = _settings.AutoStart;
            
            // Start engine if previously enabled
            if (_settings.IsEnabled)
            {
                TryInitializeEngine();
            }
        }
        
        private void LoadSettings()
        {
            _settings = SettingsManager.Load();
            
            // Synchronize AutoStart with actual Registry state
            _settings.AutoStart = WindowsStartup.IsRegistered();



        }
        
        private void SaveSettings()
        {
            _settings.IsEnabled = _engine?.IsRunning ?? false;
            SettingsManager.Save(_settings);
        }
        
        private void TryInitializeEngine()
        {
            if (_engine == null || _engine.IsRunning) return;
            
            try
            {
                // Cancel any previous pending initialization
                _initCts?.Cancel();
                _initCts = new CancellationTokenSource();
                
                _engine.Initialize(_settings);
                UpdateUI(true, "Initializing...");
                
                // Wait briefly for initialization
                Task.Delay(500).ContinueWith(_ =>
                {
                    if (_initCts?.IsCancellationRequested == true) return;
                    
                    Invoke(new Action(() =>
                    {
                        if (_initCts?.IsCancellationRequested == true) return;
                        
                        if (_engine?.IsRunning == true)
                        {
                            UpdateUI(true, "Started successfully");
                        }
                        else
                        {
                            UpdateUI(false, "Failed to initialize");
                            ShowNotification("Voice isolation failed to start", 
                                "Check audio devices and try again.", ToolTipIcon.Error);
                        }
                    }));
                }, _initCts.Token);
            }
            catch (Exception ex)
            {
                Invoke(new Action(() =>
                {
                    UpdateUI(false, "Initialization failed");
                    ShowNotification("Initialization Error", ex.Message, ToolTipIcon.Error);
                }));
            }
        }
        
        private void ToggleMenuItem_Click(object? sender, EventArgs e)
        {
            if (_isToggling) return;
            _isToggling = true;
            
            try
            {
                if (_engine?.IsRunning == true)
                {
                    _engine.Stop();
                    UpdateUI(false, "Stopped");
                    ShowNotification("Voice Isolation Disabled", "Audio processing has been stopped.", ToolTipIcon.Info);
                }
                else
                {
                    TryInitializeEngine();
                }
                
                SaveSettings();
            }
            finally
            {
                _isToggling = false;
            }
        }
        
        private void AutoStartMenuItem_Click(object? sender, EventArgs e)
        {
            _settings.AutoStart = !_settings.AutoStart;
            if (_autoStartMenuItem != null)
                _autoStartMenuItem.Checked = _settings.AutoStart;
            
            if (_settings.AutoStart)
            {
                WindowsStartup.Register();
            }
            else
            {
                WindowsStartup.Unregister();
            }
            
            SaveSettings();
        }
        
        private void ExitMenuItem_Click(object? sender, EventArgs e)
        {
            Close();
        }
        
        private void Engine_LogMessage(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(typeof(MainForm).Assembly.Location),
                        "diagnostic.log"),
                    System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + System.Environment.NewLine);
            }
            catch { }
        }
        
        private void Engine_FrameProcessed(double inputRms, double outputRms)
        {
            // Could track diagnostics if needed
        }
        
        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            // Cancel any pending initialization
            _initCts?.Cancel();
            _initCts?.Dispose();
            _initCts = null;
            
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }
            
            _notifyIcon?.Dispose();
            _notifyIcon = null;
            
            _contextMenu?.Dispose();
            _contextMenu = null;
            
            _mainFormClosed.SetResult(true);
        }
        
        private void UpdateUI(bool isRunning, string statusMsg)
        {
            // Only invoke if handle exists
            if (!this.IsHandleCreated)
            {
                _settings.IsEnabled = isRunning;
                return;
            }
            
            Invoke(new Action(() =>
            {
                if (_toggleMenuItem != null)
                {
                    _toggleMenuItem.Text = isRunning ? "Disable Voice Isolation" : "Enable Voice Isolation";
                }
                
                if (_statusMenuItem != null)
                {
                    _statusMenuItem.Text = $"Status: {(isRunning ? "ON" : "OFF")} - {statusMsg}";
                }
                
                UpdateIconAndTooltip(isRunning);
            }));
        }
        
        private void UpdateIconAndTooltip(bool isRunning)
        {
            if (_notifyIcon == null) return;
            
            _notifyIcon.Text = $"IsoVoice - {(isRunning ? "ON" : "OFF")}";
            
            // Create simple colored icons using DrawIcon
            int size = 16;
            using var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            
            // Draw a simple microphone symbol
            using var pen = new Pen(isRunning ? Color.Green : Color.Gray, 2);
            
            // Microphone body
            g.DrawEllipse(pen, 5, 2, 6, 8);
            // Mic stand
            g.DrawLine(pen, 8, 10, 8, 13);
            // Base
            g.DrawLine(pen, 5, 13, 11, 13);
            
            // Add status indicator circle
            using var brush = new SolidBrush(isRunning ? Color.Green : Color.Red);
            g.FillEllipse(brush, 12, 0, 4, 4);
            
            _notifyIcon.Icon = Icon.FromHandle(bmp.GetHicon());
        }
        
        private void ShowNotification(string title, string message, ToolTipIcon icon)
        {
            if (_notifyIcon == null) return;
            
            _notifyIcon.ShowBalloonTip(
                3000, 
                title, 
                message, 
                icon);
        }
        
        public async Task WaitForCloseAsync()
        {
            await _mainFormClosed.Task;
        }
        
        [STAThread]
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            
            // Check for single instance
            bool createdNew;
            using var mutex = new System.Threading.Mutex(true, "IsoVoiceTrayMutex", out createdNew);
            
            if (!createdNew)
            {
                MessageBox.Show(
                    "IsoVoice is already running.",
                    "IsoVoice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            
            using var form = new MainForm();
            form.Show();
            
            Application.Run();
        }
    }
}

