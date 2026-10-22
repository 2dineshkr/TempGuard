using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;

namespace TempGuard
{
    internal enum ThermalLevel { Unknown, Safe, Warning, Critical }

    internal sealed class CardPanel : Panel
    {
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public CardPanel()
        {
            BorderColor = Color.FromArgb(43, 52, 68);
            CornerRadius = 14;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
            using (Pen pen = new Pen(BorderColor))
                e.Graphics.DrawPath(pen, path);
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            if (bounds.Width <= diameter || bounds.Height <= diameter)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly TemperatureMonitor _monitor;
        private readonly ProcessLoadMonitor _processMonitor;
        private readonly AppSettings _settings;
        private readonly Timer _timer;
        private readonly NotifyIcon _tray;
        private readonly Label _temperatureLabel;
        private readonly Label _statusLabel;
        private readonly Label _adviceLabel;
        private readonly Label _chargingLabel;
        private readonly Label _sourceLabel;
        private readonly Label _updatedLabel;
        private readonly Label _sensorCountLabel;
        private readonly CardPanel _statusCard;
        private readonly ListView _sensorList;
        private readonly Panel _emptyState;
        private readonly NumericUpDown _warningInput;
        private readonly NumericUpDown _criticalInput;
        private readonly CheckBox _startupCheck;
        private readonly ToolStripMenuItem _startupMenu;
        private readonly Button _adminButton;
        private readonly ListView _processList;
        private readonly Label _processCountLabel;
        private readonly Label _processUpdatedLabel;
        private readonly Button _endProcessButton;
        private ThermalLevel _lastLevel = ThermalLevel.Unknown;
        private bool _hasThermalBaseline;
        private bool _syncingStartupUi;
        private Icon _currentIcon;
        private bool _allowClose;

        private static readonly Color Background = Color.FromArgb(10, 14, 22);
        private static readonly Color Card = Color.FromArgb(21, 27, 38);
        private static readonly Color CardRaised = Color.FromArgb(27, 35, 48);
        private static readonly Color Border = Color.FromArgb(43, 53, 70);
        private static readonly Color TextPrimary = Color.FromArgb(244, 247, 251);
        private static readonly Color Muted = Color.FromArgb(148, 162, 184);
        private static readonly Color SafeColor = Color.FromArgb(61, 214, 159);
        private static readonly Color WarningColor = Color.FromArgb(255, 172, 77);
        private static readonly Color CriticalColor = Color.FromArgb(255, 93, 114);
        private static readonly Color UnknownColor = Color.FromArgb(126, 139, 160);
        private static readonly Color AccentColor = Color.FromArgb(91, 140, 255);
        private static readonly int CurrentProcessId = GetCurrentProcessId();
        private static readonly int CurrentSessionId = GetCurrentSessionId();

        public MainForm(bool startMinimized)
        {
            _settings = AppSettings.Load();
            _monitor = new TemperatureMonitor();
            _processMonitor = new ProcessLoadMonitor();

            Text = "TempGuard";
            Icon = SystemIcons.Shield;
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(760, 680);
            MinimumSize = new Size(700, 620);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(24, 20, 24, 22),
                BackColor = Background
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);

            TableLayoutPanel tabBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Background,
                Margin = new Padding(0, 0, 0, 6)
            };
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            tabBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Button temperaturesTabButton = CreateTabButton("TEMPERATURES", true);
            Button cpuLoadTabButton = CreateTabButton("CPU LOAD", false);
            tabBar.Controls.Add(temperaturesTabButton, 0, 0);
            tabBar.Controls.Add(cpuLoadTabButton, 1, 0);
            root.Controls.Add(tabBar, 0, 1);

            Panel contentHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.Controls.Add(contentHost, 0, 2);

            TableLayoutPanel dashboard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 206));
            dashboard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            dashboard.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            contentHost.Controls.Add(dashboard);

            _statusCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Card,
                BorderColor = Border,
                Padding = new Padding(24, 20, 24, 20),
                Margin = new Padding(0, 0, 0, 14)
            };

            TableLayoutPanel statusLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _statusCard.Controls.Add(statusLayout);

            TableLayoutPanel temperatureBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            temperatureBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            temperatureBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            _temperatureLabel = new Label
            {
                Text = "—°",
                Font = new Font("Segoe UI Semibold", 44F, FontStyle.Bold),
                ForeColor = UnknownColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0)
            };
            Label celsiusCaption = new Label
            {
                Text = "CELSIUS  •  HOTTEST",
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(3, 0, 0, 0)
            };
            temperatureBlock.Controls.Add(_temperatureLabel, 0, 0);
            temperatureBlock.Controls.Add(celsiusCaption, 0, 1);
            statusLayout.Controls.Add(temperatureBlock, 0, 0);

            TableLayoutPanel statusDetails = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                Margin = new Padding(12, 0, 0, 0)
            };
            statusDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            statusDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            statusDetails.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

            _statusLabel = new Label
            {
                Text = "●  CHECKING SENSORS",
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = UnknownColor,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            _adviceLabel = new Label
            {
                Text = "Looking for CPU and GPU temperature data…",
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(3, 0, 0, 0)
            };
            _sourceLabel = new Label
            {
                Text = "The hottest available sensor sets the alert color.",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = true,
                Padding = new Padding(3, 6, 10, 0)
            };

            Panel powerPill = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = CardRaised,
                Margin = new Padding(0, 3, 0, 0),
                Padding = new Padding(10, 0, 10, 0)
            };
            _chargingLabel = new Label
            {
                Text = "⚡  Checking power status…",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            powerPill.Controls.Add(_chargingLabel);
            statusDetails.Controls.Add(_statusLabel, 0, 0);
            statusDetails.Controls.Add(_adviceLabel, 0, 1);
            statusDetails.Controls.Add(_sourceLabel, 0, 2);
            statusDetails.Controls.Add(powerPill, 0, 3);
            statusLayout.Controls.Add(statusDetails, 1, 0);
            dashboard.Controls.Add(_statusCard, 0, 0);

            CardPanel sensorsCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Card,
                BorderColor = Border,
                Padding = new Padding(18, 12, 18, 12),
                Margin = new Padding(0, 0, 0, 14)
            };
            TableLayoutPanel sensorsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            sensorsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            sensorsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sensorsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            sensorsCard.Controls.Add(sensorsLayout);

            TableLayoutPanel sensorsHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            sensorsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sensorsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Label sensorsTitle = new Label
            {
                Text = "LIVE SENSORS",
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _sensorCountLabel = new Label
            {
                Text = "SCANNING",
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = Muted,
                AutoSize = true,
                Anchor = AnchorStyles.Right,
                Padding = new Padding(0, 0, 2, 0)
            };
            sensorsHeader.Controls.Add(sensorsTitle, 0, 0);
            sensorsHeader.Controls.Add(_sensorCountLabel, 1, 0);
            sensorsLayout.Controls.Add(sensorsHeader, 0, 0);

            Panel sensorBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
            _sensorList = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Card,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                HideSelection = false,
                Font = new Font("Segoe UI", 9F)
            };
            _sensorList.Columns.Add("DEVICE / SENSOR", 410);
            _sensorList.Columns.Add("TYPE", 82);
            _sensorList.Columns.Add("TEMP", 92, HorizontalAlignment.Right);
            _sensorList.Resize += delegate { ResizeSensorColumns(); };

            _emptyState = new Panel { Dock = DockStyle.Fill, BackColor = Card };
            TableLayoutPanel emptyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                Padding = new Padding(24, 8, 24, 8)
            };
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            Label emptyTitle = new Label
            {
                Text = "No temperature data yet",
                Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomCenter
            };
            Label emptyText = new Label
            {
                Text = "Hardware access may require administrator permission.",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                AutoEllipsis = true
            };
            _adminButton = CreateButton(SensorSupport.IsInstalled ? "Restart as administrator" : "Install sensor support", AccentColor, 232);
            _adminButton.Anchor = AnchorStyles.Top;
            _adminButton.Margin = new Padding(0, 4, 0, 0);
            _adminButton.Visible = !SensorSupport.IsInstalled || !IsAdministrator();
            _adminButton.Click += SensorSupportAction;
            emptyLayout.Controls.Add(emptyTitle, 0, 0);
            emptyLayout.Controls.Add(emptyText, 0, 1);
            emptyLayout.Controls.Add(_adminButton, 0, 2);
            _emptyState.Controls.Add(emptyLayout);

            sensorBody.Controls.Add(_sensorList);
            sensorBody.Controls.Add(_emptyState);
            _emptyState.BringToFront();
            sensorsLayout.Controls.Add(sensorBody, 0, 1);

            _updatedLabel = new Label
            {
                Text = "Waiting for the first update…",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 8F)
            };
            sensorsLayout.Controls.Add(_updatedLabel, 0, 2);
            dashboard.Controls.Add(sensorsCard, 0, 1);

            CardPanel settingsCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Card,
                BorderColor = Border,
                Padding = new Padding(18, 10, 18, 10),
                Margin = new Padding(0)
            };
            TableLayoutPanel settingsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            settingsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            settingsLayout.Controls.Add(new Label
            {
                Text = "ALERT SETTINGS",
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            FlowLayoutPanel thresholdRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 3, 0, 0),
                Margin = new Padding(0)
            };
            thresholdRow.Controls.Add(CreateInlineLabel("Orange at"));
            _warningInput = CreateTemperatureInput(_settings.WarningCelsius);
            thresholdRow.Controls.Add(_warningInput);
            thresholdRow.Controls.Add(CreateUnitLabel("°C"));
            thresholdRow.Controls.Add(new Label { Width = 16, Height = 30 });
            thresholdRow.Controls.Add(CreateInlineLabel("Red at"));
            _criticalInput = CreateTemperatureInput(_settings.CriticalCelsius);
            thresholdRow.Controls.Add(_criticalInput);
            thresholdRow.Controls.Add(CreateUnitLabel("°C"));
            Button saveButton = CreateButton("Save changes", AccentColor, 132);
            saveButton.Margin = new Padding(22, 0, 0, 0);
            saveButton.Click += SaveSettings;
            thresholdRow.Controls.Add(saveButton);
            settingsLayout.Controls.Add(thresholdRow, 0, 1);

            TableLayoutPanel startupRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            startupRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            startupRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _startupCheck = new CheckBox
            {
                Text = "Start with Windows",
                Checked = _settings.StartWithWindows,
                AutoSize = true,
                ForeColor = TextPrimary,
                Anchor = AnchorStyles.Left
            };
            Label startupHint = new Label
            {
                Text = "Starts minimized at Windows sign-in (current user)",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };
            startupRow.Controls.Add(_startupCheck, 0, 0);
            startupRow.Controls.Add(startupHint, 1, 0);
            settingsLayout.Controls.Add(startupRow, 0, 2);
            settingsCard.Controls.Add(settingsLayout);
            dashboard.Controls.Add(settingsCard, 0, 2);

            CardPanel processCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Card,
                BorderColor = Border,
                Padding = new Padding(18, 14, 18, 14),
                Margin = new Padding(0)
            };
            TableLayoutPanel processLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            processLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            processLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            processLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            processLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            processCard.Controls.Add(processLayout);

            TableLayoutPanel processHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            processHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            processHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            TableLayoutPanel processTitleBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            processTitleBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            processTitleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            processTitleBlock.Controls.Add(new Label
            {
                Text = "APPLICATION CPU LOAD",
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);
            processTitleBlock.Controls.Add(new Label
            {
                Text = "Use this list to identify heavy processes. Values update every two seconds.",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0)
            }, 0, 1);
            _processCountLabel = new Label
            {
                Text = "SAMPLING",
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = Muted,
                AutoSize = true,
                Anchor = AnchorStyles.Right
            };
            processHeader.Controls.Add(processTitleBlock, 0, 0);
            processHeader.Controls.Add(_processCountLabel, 1, 0);
            processLayout.Controls.Add(processHeader, 0, 0);

            _processUpdatedLabel = new Label
            {
                Text = "Collecting the first CPU sample…",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            processLayout.Controls.Add(_processUpdatedLabel, 0, 1);

            _processList = new BufferedListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Card,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill,
                HideSelection = false,
                Font = new Font("Segoe UI", 9F)
            };
            _processList.Columns.Add("APPLICATION / PROCESS", 190);
            _processList.Columns.Add("PID", 70, HorizontalAlignment.Right);
            _processList.Columns.Add("CPU", 78, HorizontalAlignment.Right);
            _processList.Columns.Add("MEMORY", 100, HorizontalAlignment.Right);
            _processList.Columns.Add("WINDOW", 260);
            _processList.SelectedIndexChanged += delegate { UpdateEndProcessButton(); };
            _processList.Resize += delegate { ResizeProcessColumns(); };
            processLayout.Controls.Add(_processList, 0, 2);

            TableLayoutPanel processActions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 10, 0, 0)
            };
            processActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            processActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            processActions.Controls.Add(new Label
            {
                Text = "Ending a process can lose unsaved work. Select only an application you recognize.",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0)
            }, 0, 0);
            _endProcessButton = CreateButton("End selected task", CriticalColor, 168);
            _endProcessButton.Enabled = false;
            _endProcessButton.Anchor = AnchorStyles.Right;
            _endProcessButton.Click += EndSelectedProcess;
            processActions.Controls.Add(_endProcessButton, 1, 0);
            processLayout.Controls.Add(processActions, 0, 3);
            processCard.Visible = false;
            contentHost.Controls.Add(processCard);
            dashboard.BringToFront();

            temperaturesTabButton.Click += delegate
            {
                processCard.Visible = false;
                dashboard.Visible = true;
                dashboard.BringToFront();
                SetTabSelection(temperaturesTabButton, cpuLoadTabButton);
            };
            cpuLoadTabButton.Click += delegate
            {
                dashboard.Visible = false;
                processCard.Visible = true;
                processCard.BringToFront();
                SetTabSelection(cpuLoadTabButton, temperaturesTabButton);
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open TempGuard", null, delegate { ShowWindow(); });
            _startupMenu = new ToolStripMenuItem("Start with Windows")
            {
                Checked = _settings.StartWithWindows,
                CheckOnClick = true
            };
            _startupMenu.CheckedChanged += delegate
            {
                if (_syncingStartupUi) return;
                _startupCheck.Checked = _startupMenu.Checked;
                PersistSettings(false);
            };
            menu.Items.Add(_startupMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { _allowClose = true; Close(); });

            _tray = new NotifyIcon
            {
                Visible = true,
                Text = "TempGuard - checking sensors",
                ContextMenuStrip = menu
            };
            _tray.DoubleClick += delegate { ShowWindow(); };
            SetTrayIcon(ThermalLevel.Unknown, null);

            _timer = new Timer { Interval = 2000 };
            _timer.Tick += delegate { RefreshDashboard(); };
            _timer.Start();

            FormClosing += OnFormClosing;
            Shown += delegate
            {
                RefreshDashboard();
                if (startMinimized)
                    Hide();
                else
                    BeginInvoke(new Action(PromptForAutomaticStartup));
            };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized) Hide();
            };
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            TableLayoutPanel titleBlock = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };
            titleBlock.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            titleBlock.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            titleBlock.Controls.Add(new Label
            {
                Text = "TempGuard",
                Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);
            titleBlock.Controls.Add(new Label
            {
                Text = "Thermal health for your laptop",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(2, 0, 0, 0)
            }, 0, 1);
            header.Controls.Add(titleBlock, 0, 0);

            Label liveBadge = new Label
            {
                Text = "●  LIVE  •  2s",
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                ForeColor = SafeColor,
                BackColor = CardRaised,
                AutoSize = true,
                Padding = new Padding(11, 7, 11, 7),
                Anchor = AnchorStyles.Right
            };
            header.Controls.Add(liveBadge, 1, 0);
            return header;
        }

        private static Button CreateTabButton(string text, bool selected)
        {
            Button button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = selected ? CardRaised : Background,
                ForeColor = selected ? TextPrimary : Muted,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = selected ? 2 : 1;
            button.FlatAppearance.BorderColor = selected ? AccentColor : Border;
            button.FlatAppearance.MouseOverBackColor = CardRaised;
            button.FlatAppearance.MouseDownBackColor = Mix(CardRaised, Color.Black, 0.10F);
            return button;
        }

        private static void SetTabSelection(Button selected, Button other)
        {
            selected.BackColor = CardRaised;
            selected.ForeColor = TextPrimary;
            selected.FlatAppearance.BorderSize = 2;
            selected.FlatAppearance.BorderColor = AccentColor;
            other.BackColor = Background;
            other.ForeColor = Muted;
            other.FlatAppearance.BorderSize = 1;
            other.FlatAppearance.BorderColor = Border;
        }

        private static Label CreateInlineLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Muted,
                AutoSize = false,
                Size = new Size(text.StartsWith("Orange") ? 100 : 70, 31),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
        }

        private static Label CreateUnitLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Muted,
                AutoSize = false,
                Size = new Size(26, 31),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(5, 0, 0, 0)
            };
        }

        private static NumericUpDown CreateTemperatureInput(int value)
        {
            return new NumericUpDown
            {
                Minimum = 45,
                Maximum = 110,
                Value = value,
                Size = new Size(68, 30),
                BackColor = CardRaised,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                Margin = new Padding(5, 1, 0, 0)
            };
        }

        private static Button CreateButton(string text, Color color, int width)
        {
            Button button = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                Size = new Size(width, 32),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Mix(color, Color.White, 0.10F);
            button.FlatAppearance.MouseDownBackColor = Mix(color, Color.Black, 0.10F);
            return button;
        }

        private void ResizeSensorColumns()
        {
            if (_sensorList.ClientSize.Width < 250) return;
            _sensorList.Columns[1].Width = 82;
            _sensorList.Columns[2].Width = 92;
            _sensorList.Columns[0].Width = Math.Max(180, _sensorList.ClientSize.Width - 174);
        }

        private void ResizeProcessColumns()
        {
            if (_processList.ClientSize.Width < 520) return;
            _processList.Columns[0].Width = 190;
            _processList.Columns[1].Width = 70;
            _processList.Columns[2].Width = 78;
            _processList.Columns[3].Width = 100;
            _processList.Columns[4].Width = Math.Max(140, _processList.ClientSize.Width - 438);
        }

        private void RefreshDashboard()
        {
            RefreshTemperatures();
            RefreshProcesses();
        }

        private void RefreshProcesses()
        {
            int? selectedProcessId = null;
            if (_processList.SelectedItems.Count == 1)
            {
                ProcessLoadReading selected = _processList.SelectedItems[0].Tag as ProcessLoadReading;
                if (selected != null) selectedProcessId = selected.ProcessId;
            }

            bool hadBaseline = _processMonitor.HasBaseline;
            System.Collections.Generic.IList<ProcessLoadReading> readings = _processMonitor.Read();
            _processList.BeginUpdate();
            try
            {
                _processList.Items.Clear();
                foreach (ProcessLoadReading reading in readings)
                {
                    bool protectedProcess = IsProtectedProcess(reading);
                    string displayName = protectedProcess ? reading.Name + "  •  protected" : reading.Name;
                    ListViewItem item = new ListViewItem(displayName);
                    item.Tag = reading;
                    item.SubItems.Add(reading.ProcessId.ToString());
                    item.SubItems.Add(reading.CpuPercent.ToString("0.0") + "%");
                    item.SubItems.Add((reading.WorkingSetBytes / 1048576.0).ToString("0.0") + " MB");
                    item.SubItems.Add(reading.WindowTitle);
                    if (protectedProcess)
                        item.ForeColor = UnknownColor;
                    else if (reading.CpuPercent >= 50)
                        item.BackColor = Mix(Card, CriticalColor, 0.15F);
                    else if (reading.CpuPercent >= 20)
                        item.BackColor = Mix(Card, WarningColor, 0.12F);
                    _processList.Items.Add(item);

                    if (selectedProcessId.HasValue && reading.ProcessId == selectedProcessId.Value)
                        item.Selected = true;
                }
            }
            finally
            {
                _processList.EndUpdate();
            }

            _processCountLabel.Text = readings.Count + (readings.Count == 1 ? " PROCESS" : " PROCESSES");
            ProcessLoadReading busiest = readings.FirstOrDefault(reading => reading.CpuPercent >= 0.1);
            if (!hadBaseline)
                _processUpdatedLabel.Text = "Collecting the first CPU sample… percentages appear on the next update.";
            else if (busiest == null)
                _processUpdatedLabel.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss") + "   •   No process is using significant CPU";
            else
                _processUpdatedLabel.Text = "Updated " + DateTime.Now.ToString("HH:mm:ss") + "   •   Highest: " +
                    busiest.Name + " at " + busiest.CpuPercent.ToString("0.0") + "%";
            UpdateEndProcessButton();
        }

        private void UpdateEndProcessButton()
        {
            if (_processList.SelectedItems.Count != 1)
            {
                _endProcessButton.Enabled = false;
                _endProcessButton.Text = "End selected task";
                return;
            }

            ProcessLoadReading reading = _processList.SelectedItems[0].Tag as ProcessLoadReading;
            bool canEnd = reading != null && !IsProtectedProcess(reading);
            _endProcessButton.Enabled = canEnd;
            _endProcessButton.Text = canEnd ? "End selected task" : "Protected process";
        }

        private static bool IsProtectedProcess(ProcessLoadReading reading)
        {
            if (reading == null || reading.ProcessId <= 4 || reading.ProcessId == CurrentProcessId ||
                reading.SessionId != CurrentSessionId)
                return true;

            string name = reading.Name ?? string.Empty;
            string[] protectedNames =
            {
                "System", "Registry", "Secure System", "Memory Compression", "smss", "csrss",
                "wininit", "winlogon", "services", "lsass", "svchost", "dwm", "explorer", "sihost",
                "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost"
            };
            return protectedNames.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
        }

        private static int GetCurrentProcessId()
        {
            using (Process process = Process.GetCurrentProcess())
                return process.Id;
        }

        private static int GetCurrentSessionId()
        {
            using (Process process = Process.GetCurrentProcess())
                return process.SessionId;
        }

        private void EndSelectedProcess(object sender, EventArgs e)
        {
            if (_processList.SelectedItems.Count != 1) return;
            ProcessLoadReading reading = _processList.SelectedItems[0].Tag as ProcessLoadReading;
            if (reading == null || IsProtectedProcess(reading))
            {
                MessageBox.Show("TempGuard will not end Windows-critical processes or itself.", "Protected process",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "End " + reading.Name + " (PID " + reading.ProcessId + ")?\n\n" +
                "The application will close immediately and unsaved work may be lost. Only continue if you recognize this process.",
                "End selected task?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;

            try
            {
                using (Process process = Process.GetProcessById(reading.ProcessId))
                {
                    if (!string.Equals(process.ProcessName, reading.Name, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The selected process has already exited and its ID was reused.");
                    process.Kill();
                    process.WaitForExit(1000);
                }
                RefreshProcesses();
            }
            catch (ArgumentException)
            {
                MessageBox.Show("That process has already closed.", "TempGuard",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshProcesses();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show("Windows did not allow TempGuard to end this process. It may be protected or running as administrator.\n\n" +
                    ex.Message, "Could not end task", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not end the selected task.\n\n" + ex.Message, "TempGuard",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshTemperatures()
        {
            TemperatureSnapshot snapshot = _monitor.Read();
            TemperatureReading hottest = snapshot.Hottest;
            bool charging = IsCharging();
            _chargingLabel.Text = charging ? "POWER  •  Plugged in / charging" : "POWER  •  On battery / not charging";

            _sensorList.BeginUpdate();
            _sensorList.Items.Clear();
            foreach (TemperatureReading reading in snapshot.Readings.OrderByDescending(r => r.Celsius))
            {
                ListViewItem item = new ListViewItem(reading.DisplayName);
                item.SubItems.Add(reading.HardwareType);
                item.SubItems.Add(Math.Round(reading.Celsius) + "°C");
                _sensorList.Items.Add(item);
            }
            _sensorList.EndUpdate();
            _sensorCountLabel.Text = snapshot.Readings.Count == 1 ? "1 READING" : snapshot.Readings.Count + " READINGS";
            _updatedLabel.Text = "Updated " + snapshot.Time.ToString("HH:mm:ss") + "   •   Hottest sensor controls the tray color";

            ThermalLevel level;
            if (hottest == null)
            {
                level = ThermalLevel.Unknown;
                _temperatureLabel.Text = "—°";
                bool supportInstalled = SensorSupport.IsInstalled;
                _statusLabel.Text = supportInstalled ? "●  SENSOR UNAVAILABLE" : "●  SENSOR DRIVER REQUIRED";
                _adviceLabel.Text = supportInstalled ? "No temperature reading" : "Install temperature sensor support";
                _sourceLabel.Text = supportInstalled
                    ? "Try administrator mode. Some laptop firmware may not expose temperature sensors."
                    : "Your Intel CPU needs the official PawnIO hardware-access driver.";
                _adminButton.Text = supportInstalled ? "Restart as administrator" : "Install sensor support";
                _adminButton.Visible = !supportInstalled || !IsAdministrator();
                _sensorList.Visible = false;
                _emptyState.Visible = true;
                _emptyState.BringToFront();
            }
            else
            {
                int rounded = (int)Math.Round(hottest.Celsius);
                level = Evaluate(hottest.Celsius);
                _temperatureLabel.Text = rounded + "°";
                _sourceLabel.Text = hottest.HardwareType + "  •  " + hottest.DisplayName;
                _sensorList.Visible = true;
                _emptyState.Visible = false;
                if (level == ThermalLevel.Safe)
                {
                    _statusLabel.Text = "●  TEMPERATURE STABLE";
                    _adviceLabel.Text = "Everything looks comfortable";
                }
                else if (level == ThermalLevel.Warning)
                {
                    _statusLabel.Text = "●  NEEDS ATTENTION";
                    _adviceLabel.Text = "Ease the workload and improve airflow";
                }
                else
                {
                    _statusLabel.Text = "●  TOO HOT — ACT NOW";
                    _adviceLabel.Text = charging
                        ? "Stop heavy work and unplug if safe"
                        : "Stop heavy work and let the laptop cool";
                }
            }

            ApplyLevel(level, hottest);
            if (_hasThermalBaseline && level != _lastLevel)
            {
                NotifyThermalTransition(_lastLevel, level, hottest, charging);
            }
            _lastLevel = level;
            _hasThermalBaseline = true;
        }

        private ThermalLevel Evaluate(float temperature)
        {
            // A small hysteresis prevents repeated notifications when the reading
            // fluctuates by one degree directly on a threshold.
            if (_hasThermalBaseline && _lastLevel == ThermalLevel.Critical &&
                temperature >= _settings.CriticalCelsius - 2) return ThermalLevel.Critical;
            if (_hasThermalBaseline && _lastLevel == ThermalLevel.Warning &&
                temperature < _settings.CriticalCelsius &&
                temperature >= _settings.WarningCelsius - 2) return ThermalLevel.Warning;
            if (temperature >= _settings.CriticalCelsius) return ThermalLevel.Critical;
            if (temperature >= _settings.WarningCelsius) return ThermalLevel.Warning;
            return ThermalLevel.Safe;
        }

        private void NotifyThermalTransition(ThermalLevel previous, ThermalLevel current,
            TemperatureReading hottest, bool charging)
        {
            int? temperature = hottest == null ? (int?)null : (int)Math.Round(hottest.Celsius);
            string value = temperature.HasValue ? temperature.Value + "°C. " : "";

            if (current == ThermalLevel.Critical)
            {
                _tray.BalloonTipTitle = "Critical heat — act now";
                _tray.BalloonTipText = value + (charging
                    ? "Save your work, stop heavy processing, and unplug the charger if safe."
                    : "Save your work, stop heavy processing, and let the laptop cool.");
                _tray.BalloonTipIcon = ToolTipIcon.Error;
            }
            else if (current == ThermalLevel.Warning)
            {
                _tray.BalloonTipTitle = previous == ThermalLevel.Critical
                    ? "Cooling down, but still warm"
                    : "Temperature needs attention";
                _tray.BalloonTipText = value + "Keep CPU/GPU load low and make sure the vents have clear airflow.";
                _tray.BalloonTipIcon = ToolTipIcon.Warning;
            }
            else if (current == ThermalLevel.Safe)
            {
                _tray.BalloonTipTitle = previous == ThermalLevel.Unknown
                    ? "Temperature sensor restored"
                    : "Temperature back to stable";
                _tray.BalloonTipText = value + "Monitoring is active. Resume heavier work gradually.";
                _tray.BalloonTipIcon = ToolTipIcon.Info;
            }
            else
            {
                _tray.BalloonTipTitle = "Temperature reading lost";
                _tray.BalloonTipText = "TempGuard cannot verify the current temperature. Open the app and check sensor support.";
                _tray.BalloonTipIcon = ToolTipIcon.Warning;
            }

            _tray.ShowBalloonTip(current == ThermalLevel.Critical ? 8000 : 6000);
        }

        private void ApplyLevel(ThermalLevel level, TemperatureReading hottest)
        {
            Color color = LevelColor(level);
            _temperatureLabel.ForeColor = color;
            _statusLabel.ForeColor = color;
            _statusCard.BackColor = Mix(Card, color, 0.055F);
            _statusCard.BorderColor = Mix(Border, color, 0.30F);
            _statusCard.Invalidate();
            SetTrayIcon(level, hottest == null ? (int?)null : (int)Math.Round(hottest.Celsius));

            string tooltip = hottest == null
                ? "TempGuard - no sensor reading"
                : "TempGuard " + Math.Round(hottest.Celsius) + "°C - " + FriendlyLevel(level) + " (" + hottest.HardwareType + ")";
            _tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        }

        private static string FriendlyLevel(ThermalLevel level)
        {
            if (level == ThermalLevel.Safe) return "Stable";
            if (level == ThermalLevel.Warning) return "Attention";
            if (level == ThermalLevel.Critical) return "Too hot";
            return "Unknown";
        }

        private static Color LevelColor(ThermalLevel level)
        {
            if (level == ThermalLevel.Safe) return SafeColor;
            if (level == ThermalLevel.Warning) return WarningColor;
            if (level == ThermalLevel.Critical) return CriticalColor;
            return UnknownColor;
        }

        private void SetTrayIcon(ThermalLevel level, int? temperature)
        {
            Icon next = CreateTemperatureIcon(LevelColor(level), temperature);
            _tray.Icon = next;
            Icon previous = _currentIcon;
            _currentIcon = next;
            if (previous != null) previous.Dispose();
        }

        private static Icon CreateTemperatureIcon(Color color, int? temperature)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (Brush shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                    graphics.FillEllipse(shadow, 2, 3, 28, 28);
                using (Brush brush = new SolidBrush(color))
                    graphics.FillEllipse(brush, 1, 1, 28, 28);

                string text = temperature.HasValue ? Math.Min(99, Math.Max(0, temperature.Value)).ToString() : "?";
                using (Font font = new Font("Segoe UI", temperature.HasValue ? 10F : 13F, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    SizeF size = graphics.MeasureString(text, font);
                    graphics.DrawString(text, font, textBrush, (30 - size.Width) / 2F, (30 - size.Height) / 2F - 0.5F);
                }

                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private static Color Mix(Color baseColor, Color tint, float amount)
        {
            return Color.FromArgb(
                (int)(baseColor.R + (tint.R - baseColor.R) * amount),
                (int)(baseColor.G + (tint.G - baseColor.G) * amount),
                (int)(baseColor.B + (tint.B - baseColor.B) * amount));
        }

        private static bool IsCharging()
        {
            BatteryChargeStatus status = SystemInformation.PowerStatus.BatteryChargeStatus;
            return (status & BatteryChargeStatus.Charging) == BatteryChargeStatus.Charging ||
                   SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            if (_criticalInput.Value <= _warningInput.Value)
            {
                MessageBox.Show("The red threshold must be higher than the orange threshold.", "TempGuard",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            PersistSettings(true);
        }

        private void PersistSettings(bool showConfirmation)
        {
            _settings.WarningCelsius = (int)_warningInput.Value;
            _settings.CriticalCelsius = (int)_criticalInput.Value;
            bool requestedStartup = _startupCheck.Checked;
            try
            {
                if (requestedStartup != StartupManager.IsEnabled &&
                    !StartupManager.SetEnabled(requestedStartup, Application.ExecutablePath))
                    throw new InvalidOperationException("Windows did not apply the startup setting.");

                _settings.StartWithWindows = StartupManager.IsEnabled;
                SyncStartupControls(_settings.StartWithWindows);
                _settings.Save();
                if (showConfirmation)
                    MessageBox.Show("Alert settings saved.", "TempGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshTemperatures();
            }
            catch (Exception ex)
            {
                _settings.StartWithWindows = StartupManager.IsEnabled;
                SyncStartupControls(_settings.StartWithWindows);
                MessageBox.Show("Could not save settings: " + ex.Message, "TempGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PromptForAutomaticStartup()
        {
            if (_settings.StartWithWindows || _settings.StartupPromptShown) return;

            DialogResult answer = MessageBox.Show(
                "Would you like TempGuard to start automatically when you sign in to Windows?\n\n" +
                "It will start minimized in the system tray for your Windows account. " +
                "If a sensor needs administrator access, TempGuard will tell you and let you restart it explicitly.\n\n" +
                "You can change this later under Alert Settings.",
                "Start TempGuard with Windows?",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            _settings.StartupPromptShown = true;
            if (answer == DialogResult.Yes)
            {
                _startupCheck.Checked = true;
                PersistSettings(false);
                if (!_settings.StartWithWindows)
                    _settings.StartupPromptShown = false;
            }
            else
            {
                _settings.Save();
            }
        }

        private void SyncStartupControls(bool enabled)
        {
            _syncingStartupUi = true;
            try
            {
                _startupCheck.Checked = enabled;
                _startupMenu.Checked = enabled;
            }
            finally { _syncingStartupUi = false; }
        }

        private void SensorSupportAction(object sender, EventArgs e)
        {
            if (SensorSupport.IsInstalled)
                RestartAsAdministrator(sender, e);
            else
                InstallSensorSupport();
        }

        private void InstallSensorSupport()
        {
            DialogResult confirmation = MessageBox.Show(
                "This Intel CPU requires low-level hardware access to read its temperature.\n\n" +
                "TempGuard will download PawnIO 2.1 from its official GitHub release, verify the exact " +
                "SHA-256 hash, and then run the signed installer. This adds a Windows system driver and " +
                "requires administrator permission.\n\n" +
                "Install temperature sensor support now?",
                "Install temperature sensor support",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes) return;

            _adminButton.Enabled = false;
            _adminButton.Text = "Downloading…";
            Application.DoEvents();

            string downloadError;
            if (!SensorSupport.EnsureInstallerDownloaded(out downloadError))
            {
                _adminButton.Enabled = true;
                _adminButton.Text = "Install sensor support";
                MessageBox.Show("TempGuard could not securely download the official sensor driver.\n\n" +
                    downloadError, "TempGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                _adminButton.Text = "Installing…";
                Process installer = SensorSupport.StartInstaller();
                if (installer == null) throw new InvalidOperationException("The installer did not start.");
                installer.WaitForExit();

                if (!SensorSupport.IsInstalled)
                {
                    MessageBox.Show("Sensor support was not installed. The setup may have been cancelled or blocked by Windows.",
                        "TempGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _adminButton.Enabled = true;
                    _adminButton.Text = "Install sensor support";
                    return;
                }

                MessageBox.Show("Sensor support is installed. TempGuard will restart and begin reading the CPU temperature.",
                    "TempGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--wait-for-instance",
                    UseShellExecute = true
                });
                _allowClose = true;
                Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                _adminButton.Enabled = true;
                _adminButton.Text = "Install sensor support";
            }
            catch (Exception ex)
            {
                _adminButton.Enabled = true;
                _adminButton.Text = "Install sensor support";
                MessageBox.Show("Could not install sensor support: " + ex.Message, "TempGuard",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RestartAsAdministrator(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--wait-for-instance",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                _allowClose = true;
                Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The user cancelled the UAC prompt.
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _tray.BalloonTipTitle = "TempGuard is still monitoring";
                _tray.BalloonTipText = "Double-click the tray icon to reopen it.";
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(2500);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) _timer.Dispose();
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                if (_currentIcon != null) _currentIcon.Dispose();
                if (_monitor != null) _monitor.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
