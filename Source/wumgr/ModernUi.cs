using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace wumgr
{
    public partial class WuMgr
    {
        private static readonly Color UiBackground = Color.FromArgb(24, 24, 24);
        private static readonly Color UiSurface = Color.FromArgb(32, 32, 32);
        private static readonly Color UiSidebar = Color.FromArgb(17, 17, 17);
        private static readonly Color UiSidebarHover = Color.FromArgb(43, 43, 43);
        private static readonly Color UiAccent = Color.FromArgb(224, 226, 230);
        private static readonly Color UiAccentHover = Color.FromArgb(246, 247, 249);
        private static readonly Color UiText = Color.FromArgb(246, 246, 246);
        private static readonly Color UiMuted = Color.FromArgb(163, 163, 163);
        private static readonly Color UiBorder = Color.FromArgb(58, 58, 58);
        private static readonly Color UiSuccess = Color.FromArgb(207, 211, 217);
        private static readonly Color UiDanger = Color.FromArgb(226, 126, 132);
        private static readonly Color UiInput = Color.FromArgb(40, 40, 40);
        private static readonly Color UiHover = Color.FromArgb(49, 49, 49);

        private Label modernPageTitle;
        private Label modernPageSubtitle;
        private Label modernSelectionSummary;
        private Label modernEditionLabel;
        private Label modernActionHint;
        private TableLayoutPanel modernSelectionGrid;
        private Control modernFilterSurface;
        private Button modernLogButton;
        private Panel modernOptionsPage;
        private Panel modernControlPage;
        private Panel modernUpdatePage;
        private Panel modernSettingsPage;
        private CheckBox modernSettingsButton;
        private Button modernMaximizeButton;
        private ModernProgressBar modernProgress;
        private ModernUpdateList modernUpdateList;
        private readonly List<DarkComboBoxRenderer> modernComboRenderers = new List<DarkComboBoxRenderer>();
        private Timer modernListToolTipTimer;
        private string modernListToolTipCell = string.Empty;
        private string modernListToolTipText = string.Empty;
        private Point modernListToolTipPoint;
        private bool modernLogVisible;
        private bool modernSettingsVisible;
        private int modernSortColumn = -1;
        private bool modernSortDescending;

        private enum CaptionButtonIcon
        {
            Minimize,
            Maximize,
            Close
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        private static extern IntPtr SendWindowMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendTextMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private void InitializeModernUi()
        {
            SuspendLayout();

            Text = "WinSlim Update";
            BackColor = UiBackground;
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(1040, 680);
            ClientSize = new Size(1180, 760);
            AutoScaleMode = AutoScaleMode.Dpi;

            tableLayoutPanel1.Controls.Remove(tableLayoutPanel2);
            tableLayoutPanel1.Controls.Remove(panelList);
            tableLayoutPanel2.Controls.Remove(tabs);

            Controls.Clear();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = new Padding(1);
            root.BackColor = UiBorder;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel shell = new TableLayoutPanel();
            shell.Name = "modernShell";
            shell.Dock = DockStyle.Fill;
            shell.Margin = Padding.Empty;
            shell.Padding = Padding.Empty;
            shell.BackColor = UiBackground;
            shell.ColumnCount = 2;
            shell.RowCount = 1;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 234F));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            shell.Controls.Add(BuildSidebar(), 0, 0);
            shell.Controls.Add(BuildMainArea(), 1, 0);
            root.Controls.Add(BuildTitleBar(), 0, 0);
            root.Controls.Add(shell, 0, 1);
            Controls.Add(root);

            Resize += delegate
            {
                UpdateWindowButton();
                UpdateWindowCorners();
            };
            HandleCreated += delegate { UpdateWindowCorners(); };

            ConfigureModernToolTips();
            ApplyExplorerTheme();
            UpdateModernPage();
            UpdateModernSelectionSummary();
            UpdateModernEmptyState();

            if (Program.TestArg("-preview-progress"))
            {
                progTotal.Style = ProgressBarStyle.Marquee;
                lblStatus.Text = "Buscando actualizaciones...";
                btnCancel.Visible = true;
                SyncModernProgress(true);
            }
            if (Program.TestArg("-preview-history"))
                PopulateHistoryPreview();
            else if (Program.TestArg("-preview-list") || Program.TestArg("-preview-scroll"))
                PopulateListPreview();
            if (Program.TestArg("-preview-packages"))
                ShowPackageUpdatesPage();
            if (Program.TestArg("-preview-settings"))
            {
                if (dlSource.Items.Count == 0)
                {
                    dlSource.Items.AddRange(new object[] { "Windows Update", "Microsoft Update", "Catálogo offline" });
                    dlSource.SelectedIndex = 0;
                }
                dlSource.Enabled = true;
                dlAutoCheck.Enabled = true;
                ShowSettingsPage();
            }
            if (Program.TestArg("-preview-tooltip"))
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    string previewText = "Actualización de inteligencia de seguridad para Microsoft Defender Antivirus";
                    toolTip.SetToolTip(modernUpdateList, previewText);
                    toolTip.Show(previewText, modernUpdateList, new Point(180, 105), 6000);
                }));
            }
            if (Program.TestArg("-preview-about"))
            {
                Timer previewAboutTimer = new Timer();
                previewAboutTimer.Interval = 450;
                previewAboutTimer.Tick += delegate
                {
                    previewAboutTimer.Stop();
                    previewAboutTimer.Dispose();
                    menuAbout_Click(null, null);
                };
                previewAboutTimer.Start();
            }

            ResumeLayout(true);
        }

        private Control BuildTitleBar()
        {
            TableLayoutPanel titleBar = new TableLayoutPanel();
            titleBar.Dock = DockStyle.Fill;
            titleBar.Margin = Padding.Empty;
            titleBar.Padding = Padding.Empty;
            titleBar.BackColor = Color.FromArgb(14, 14, 14);
            titleBar.ColumnCount = 3;
            titleBar.RowCount = 1;
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 234F));
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138F));
            titleBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Panel identity = new Panel();
            identity.Dock = DockStyle.Fill;
            identity.Margin = Padding.Empty;
            identity.BackColor = titleBar.BackColor;
            Label title = CreateLabel("◆  WinSlim Update", 9F, FontStyle.Bold, UiText);
            title.Dock = DockStyle.Fill;
            title.Padding = new Padding(4, 0, 0, 0);
            title.TextAlign = ContentAlignment.MiddleLeft;
            Button appMenu = CreateWindowButton("☰");
            appMenu.Dock = DockStyle.Left;
            appMenu.Width = 40;
            appMenu.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
            appMenu.Click += delegate
            {
                if (notifyIcon.ContextMenu != null)
                    notifyIcon.ContextMenu.Show(appMenu, new Point(0, appMenu.Height));
            };
            identity.Controls.Add(title);
            identity.Controls.Add(appMenu);

            Panel dragArea = new Panel();
            dragArea.Dock = DockStyle.Fill;
            dragArea.Margin = Padding.Empty;
            dragArea.BackColor = titleBar.BackColor;

            FlowLayoutPanel controls = new FlowLayoutPanel();
            controls.Dock = DockStyle.Fill;
            controls.Margin = Padding.Empty;
            controls.Padding = Padding.Empty;
            controls.FlowDirection = FlowDirection.LeftToRight;
            controls.WrapContents = false;
            controls.BackColor = titleBar.BackColor;

            Button minimize = CreateCaptionButton(CaptionButtonIcon.Minimize);
            modernMaximizeButton = CreateCaptionButton(CaptionButtonIcon.Maximize);
            Button close = CreateCaptionButton(CaptionButtonIcon.Close);
            close.FlatAppearance.MouseOverBackColor = Color.FromArgb(196, 43, 28);
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            modernMaximizeButton.Click += delegate { ToggleMaximize(); };
            close.Click += delegate { Close(); };
            controls.Controls.Add(minimize);
            controls.Controls.Add(modernMaximizeButton);
            controls.Controls.Add(close);

            titleBar.MouseDown += titleBar_MouseDown;
            identity.MouseDown += titleBar_MouseDown;
            title.MouseDown += titleBar_MouseDown;
            dragArea.MouseDown += titleBar_MouseDown;
            titleBar.DoubleClick += delegate { ToggleMaximize(); };
            identity.DoubleClick += delegate { ToggleMaximize(); };
            title.DoubleClick += delegate { ToggleMaximize(); };
            dragArea.DoubleClick += delegate { ToggleMaximize(); };

            titleBar.Controls.Add(identity, 0, 0);
            titleBar.Controls.Add(dragArea, 1, 0);
            titleBar.Controls.Add(controls, 2, 0);
            return titleBar;
        }

        private Button CreateWindowButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(46, 40);
            button.Margin = Padding.Empty;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiHover;
            button.BackColor = Color.FromArgb(14, 14, 14);
            button.ForeColor = UiText;
            button.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private Button CreateCaptionButton(CaptionButtonIcon icon)
        {
            Button button = CreateWindowButton(string.Empty);
            button.Tag = icon;
            button.Paint += captionButton_Paint;
            return button;
        }

        private void captionButton_Paint(object sender, PaintEventArgs e)
        {
            Button button = sender as Button;
            if (button == null || !(button.Tag is CaptionButtonIcon))
                return;

            CaptionButtonIcon icon = (CaptionButtonIcon)button.Tag;
            float centerX = button.ClientRectangle.Width / 2F;
            float centerY = button.ClientRectangle.Height / 2F;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(UiText, 1.35F))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                if (icon == CaptionButtonIcon.Minimize)
                {
                    e.Graphics.DrawLine(pen, centerX - 5F, centerY + 3F, centerX + 5F, centerY + 3F);
                }
                else if (icon == CaptionButtonIcon.Close)
                {
                    e.Graphics.DrawLine(pen, centerX - 4F, centerY - 4F, centerX + 4F, centerY + 4F);
                    e.Graphics.DrawLine(pen, centerX + 4F, centerY - 4F, centerX - 4F, centerY + 4F);
                }
                else if (WindowState == FormWindowState.Maximized)
                {
                    e.Graphics.DrawRectangle(pen, centerX - 3F, centerY - 5F, 8F, 8F);
                    e.Graphics.DrawRectangle(pen, centerX - 5F, centerY - 3F, 8F, 8F);
                }
                else
                {
                    e.Graphics.DrawRectangle(pen, centerX - 5F, centerY - 5F, 10F, 10F);
                }
            }
        }

        private void titleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            ReleaseCapture();
            SendWindowMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            UpdateWindowButton();
            UpdateWindowCorners();
        }

        private void UpdateWindowButton()
        {
            if (modernMaximizeButton != null)
                modernMaximizeButton.Invalidate();
        }

        private void UpdateWindowCorners()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0)
                return;

            int cornerPreference = WindowState == FormWindowState.Normal ? 2 : 1;
            try
            {
                DwmSetWindowAttribute(Handle, 33, ref cornerPreference, sizeof(int));
            }
            catch
            {
                // Windows anteriores a 11 no reconocen la preferencia DWM.
            }

            Region previousRegion = Region;
            if (WindowState == FormWindowState.Normal)
            {
                using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), 11))
                    Region = new Region(path);
            }
            else
                Region = null;
            if (previousRegion != null)
                previousRegion.Dispose();
        }

        private Control BuildSidebar()
        {
            Panel sidebar = new Panel();
            sidebar.Dock = DockStyle.Fill;
            sidebar.BackColor = UiSidebar;
            sidebar.Padding = new Padding(14, 18, 14, 16);

            Panel brand = new Panel();
            brand.Dock = DockStyle.Top;
            brand.Height = 92;
            brand.BackColor = UiSidebar;

            UpdateLogoMark mark = new UpdateLogoMark();
            mark.Location = new Point(2, 5);
            mark.Size = new Size(36, 36);

            Label product = CreateLabel("WinSlim Update", 11F, FontStyle.Bold, UiText);
            product.Location = new Point(48, 3);
            product.AutoSize = true;

            Label caption = CreateLabel("ACTUALIZACIONES", 7F, FontStyle.Bold, UiMuted);
            caption.Location = new Point(49, 29);
            caption.AutoSize = true;

            modernEditionLabel = CreateLabel("Windows 10 / 11", 8.2F, FontStyle.Regular, UiMuted);
            modernEditionLabel.Location = new Point(2, 58);
            modernEditionLabel.AutoSize = true;

            brand.Controls.Add(mark);
            brand.Controls.Add(product);
            brand.Controls.Add(caption);
            brand.Controls.Add(modernEditionLabel);

            FlowLayoutPanel navigation = new FlowLayoutPanel();
            navigation.Dock = DockStyle.Top;
            navigation.Height = 396;
            navigation.Padding = new Padding(0, 2, 0, 0);
            navigation.Margin = Padding.Empty;
            navigation.BackColor = UiSidebar;
            navigation.FlowDirection = FlowDirection.TopDown;
            navigation.WrapContents = false;
            navigation.AutoScroll = false;

            StyleNavigationButton(btnWinUpd);
            StyleNavigationButton(btnInstalled);
            StyleNavigationButton(btnHidden);
            StyleNavigationButton(btnHistory);
            Label packagesCaption = CreateLabel("APLICACIONES", 8F, FontStyle.Bold, UiMuted);
            packagesCaption.AutoSize = false;
            packagesCaption.Size = new Size(190, 28);
            packagesCaption.Margin = new Padding(8, 5, 8, 0);
            packagesCaption.TextAlign = ContentAlignment.MiddleLeft;
            modernPackageUpdatesButton = new CheckBox();
            modernPackageUpdatesButton.Name = "modernPackageUpdatesButton";
            modernPackageUpdatesButton.Text = "Actualizaciones de\r\npaquetes";
            StyleNavigationButton(modernPackageUpdatesButton);
            modernPackageUpdatesButton.Size = new Size(190, 50);
            modernPackageUpdatesButton.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular);
            modernSettingsButton = new CheckBox();
            modernSettingsButton.Name = "modernSettingsButton";
            modernSettingsButton.Text = "Configuración";
            StyleNavigationButton(modernSettingsButton);
            modernSettingsButton.Click += delegate { ShowSettingsPage(); };
            Label settingsCaption = CreateLabel("CONFIGURACIÓN", 8F, FontStyle.Bold, UiMuted);
            settingsCaption.AutoSize = false;
            settingsCaption.Size = new Size(190, 28);
            settingsCaption.Margin = new Padding(8, 2, 8, 0);
            settingsCaption.TextAlign = ContentAlignment.MiddleLeft;
            navigation.Controls.Add(btnWinUpd);
            navigation.Controls.Add(btnInstalled);
            navigation.Controls.Add(btnHidden);
            navigation.Controls.Add(btnHistory);
            navigation.Controls.Add(packagesCaption);
            navigation.Controls.Add(modernPackageUpdatesButton);
            navigation.Controls.Add(settingsCaption);
            navigation.Controls.Add(modernSettingsButton);
            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 48;
            footer.BackColor = UiSidebar;
            Label footerTitle = CreateLabel("WinSlim Update", 8.2F, FontStyle.Bold, UiMuted);
            footerTitle.Location = new Point(4, 7);
            footerTitle.AutoSize = true;
            Label footerVersion = CreateLabel("Versión " + Program.mVersion, 7.8F, FontStyle.Regular, Color.FromArgb(112, 112, 112));
            footerVersion.Location = new Point(4, 26);
            footerVersion.AutoSize = true;
            footer.Controls.Add(footerTitle);
            footer.Controls.Add(footerVersion);

            sidebar.Controls.Add(footer);
            sidebar.Controls.Add(navigation);
            sidebar.Controls.Add(brand);
            return sidebar;
        }

        private Control BuildMainArea()
        {
            TableLayoutPanel main = new TableLayoutPanel();
            main.Dock = DockStyle.Fill;
            main.Margin = Padding.Empty;
            main.Padding = Padding.Empty;
            main.BackColor = UiBackground;
            main.ColumnCount = 1;
            main.RowCount = 2;
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Panel contentHost = new Panel();
            contentHost.Dock = DockStyle.Fill;
            contentHost.Margin = Padding.Empty;
            contentHost.BackColor = UiBackground;

            modernUpdatePage = BuildUpdatesPage();
            modernSettingsPage = BuildSettingsPage();
            modernPackageUpdatesPage = BuildPackageUpdatesPage();
            contentHost.Controls.Add(modernSettingsPage);
            contentHost.Controls.Add(modernPackageUpdatesPage);
            contentHost.Controls.Add(modernUpdatePage);

            main.Controls.Add(BuildHeader(), 0, 0);
            main.Controls.Add(contentHost, 0, 1);
            return main;
        }

        private Panel BuildUpdatesPage()
        {
            TableLayoutPanel page = new TableLayoutPanel();
            page.Dock = DockStyle.Fill;
            page.Margin = Padding.Empty;
            page.Padding = Padding.Empty;
            page.BackColor = UiBackground;
            page.ColumnCount = 1;
            page.RowCount = 4;
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            page.Controls.Add(BuildActionBar(), 0, 0);
            page.Controls.Add(BuildSelectionBar(), 0, 1);
            page.Controls.Add(BuildUpdateSurface(), 0, 2);
            page.Controls.Add(BuildStatusBar(), 0, 3);
            return page;
        }

        private Control BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.Padding = new Padding(28, 18, 28, 8);
            header.BackColor = UiBackground;

            Panel titles = new Panel();
            titles.BackColor = UiBackground;

            modernPageTitle = CreateLabel("Actualizaciones", 22.5F, FontStyle.Bold, UiText);
            modernPageTitle.Location = new Point(0, 1);
            modernPageTitle.AutoSize = true;

            modernPageSubtitle = CreateLabel("Revisa y administra las actualizaciones de tu equipo.", 10F, FontStyle.Regular, UiMuted);
            modernPageSubtitle.Location = new Point(2, 53);
            modernPageSubtitle.AutoSize = false;
            modernPageSubtitle.AutoEllipsis = true;
            modernPageSubtitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            modernPageSubtitle.Size = new Size(500, 22);

            titles.Controls.Add(modernPageTitle);
            titles.Controls.Add(modernPageSubtitle);

            titles.Dock = DockStyle.Fill;
            header.Controls.Add(titles);
            return header;
        }

        private Control BuildActionBar()
        {
            RoundedPanel actionSurface = new RoundedPanel();
            actionSurface.Dock = DockStyle.Fill;
            actionSurface.Margin = new Padding(28, 0, 28, 10);
            actionSurface.Padding = new Padding(12, 8, 12, 8);
            actionSurface.BackColor = UiSurface;
            actionSurface.BorderColor = UiBorder;
            actionSurface.CornerRadius = 10;

            flowLayoutPanel1.Dock = DockStyle.Fill;
            flowLayoutPanel1.AutoSize = false;
            flowLayoutPanel1.WrapContents = false;
            flowLayoutPanel1.FlowDirection = FlowDirection.LeftToRight;
            flowLayoutPanel1.Padding = Padding.Empty;
            flowLayoutPanel1.Margin = Padding.Empty;
            flowLayoutPanel1.BackColor = UiSurface;

            StyleActionButton(btnSearch, "Buscar", UiAccent, Color.FromArgb(24, 24, 24), 110);
            btnSearch.Height = 36;
            btnSearch.Margin = new Padding(0, 0, 8, 0);
            StyleSecondaryActionButton(btnInstall, "Instalar", UiAccent);
            btnInstall.Width = 132;
            StyleSecondaryActionButton(btnDownload, "Descargar", UiText);
            btnDownload.Width = 148;
            StyleSecondaryActionButton(btnHide, "Ocultar", UiText);
            btnHide.Width = 126;
            StyleSecondaryActionButton(btnUnInstall, "Desinstalar", UiDanger);
            btnUnInstall.Width = 145;
            StyleSecondaryActionButton(btnGetLink, "", UiText);
            btnGetLink.Width = 48;

            flowLayoutPanel1.Controls.SetChildIndex(btnSearch, 0);
            flowLayoutPanel1.Controls.SetChildIndex(btnInstall, 1);
            flowLayoutPanel1.Controls.SetChildIndex(btnDownload, 2);
            flowLayoutPanel1.Controls.SetChildIndex(btnHide, 3);
            flowLayoutPanel1.Controls.SetChildIndex(btnUnInstall, 4);
            flowLayoutPanel1.Controls.SetChildIndex(btnGetLink, 5);

            actionSurface.Controls.Add(flowLayoutPanel1);

            modernActionHint = CreateLabel("Esta sección es sólo de consulta.", 9.5F, FontStyle.Regular, UiMuted);
            modernActionHint.Dock = DockStyle.Fill;
            modernActionHint.TextAlign = ContentAlignment.MiddleLeft;
            modernActionHint.Padding = new Padding(8, 0, 0, 0);
            modernActionHint.Visible = false;
            actionSurface.Controls.Add(modernActionHint);
            return actionSurface;
        }

        private Control BuildSelectionBar()
        {
            RoundedPanel surface = new RoundedPanel();
            surface.Dock = DockStyle.Fill;
            surface.Margin = new Padding(28, 0, 28, 8);
            surface.Padding = new Padding(10, 8, 10, 8);
            surface.BackColor = UiSurface;
            surface.BorderColor = UiBorder;
            surface.CornerRadius = 10;

            TableLayoutPanel selection = new TableLayoutPanel();
            modernSelectionGrid = selection;
            selection.Dock = DockStyle.Fill;
            selection.Margin = Padding.Empty;
            selection.Padding = Padding.Empty;
            selection.BackColor = UiSurface;
            selection.ColumnCount = 4;
            selection.RowCount = 1;
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148F));

            StyleCompactCheckBox(chkAll);
            chkAll.Text = "Seleccionar todo";
            StyleCompactCheckBox(chkGrupe);
            chkGrupe.Text = "Agrupar";

            txtFilter.Dock = DockStyle.Fill;
            txtFilter.Margin = new Padding(12, 6, 4, 6);
            txtFilter.AutoSize = false;
            txtFilter.BorderStyle = BorderStyle.None;
            txtFilter.Font = new Font("Segoe UI", 10F);
            txtFilter.BackColor = UiInput;
            txtFilter.ForeColor = UiText;
            txtFilter.HandleCreated += delegate
            {
                SendTextMessage(txtFilter.Handle, 0x1501, new IntPtr(1), "Filtrar actualizaciones");
            };

            RoundedPanel filterSurface = new RoundedPanel();
            modernFilterSurface = filterSurface;
            filterSurface.Dock = DockStyle.Fill;
            filterSurface.Margin = new Padding(8, 1, 8, 1);
            filterSurface.Padding = new Padding(2);
            filterSurface.BackColor = UiInput;
            filterSurface.BorderColor = UiBorder;
            filterSurface.CornerRadius = 10;

            TableLayoutPanel filterLayout = new TableLayoutPanel();
            filterLayout.Dock = DockStyle.Fill;
            filterLayout.Margin = Padding.Empty;
            filterLayout.Padding = Padding.Empty;
            filterLayout.BackColor = UiInput;
            filterLayout.ColumnCount = 2;
            filterLayout.RowCount = 1;
            filterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            filterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
            filterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label filterIcon = CreateLabel("\uE721", 11F, FontStyle.Regular, UiMuted);
            filterIcon.Font = new Font("Segoe Fluent Icons", 11F, FontStyle.Regular);
            filterIcon.Dock = DockStyle.Fill;
            filterIcon.TextAlign = ContentAlignment.MiddleCenter;
            filterLayout.Controls.Add(txtFilter, 0, 0);
            filterLayout.Controls.Add(filterIcon, 1, 0);
            filterSurface.Controls.Add(filterLayout);

            btnSearchOff.Visible = false;

            modernSelectionSummary = CreateLabel("0 seleccionadas", 9F, FontStyle.Regular, UiMuted);
            modernSelectionSummary.Dock = DockStyle.Fill;
            modernSelectionSummary.TextAlign = ContentAlignment.MiddleRight;
            modernSelectionSummary.Visible = true;

            selection.Controls.Add(chkAll, 0, 0);
            selection.Controls.Add(chkGrupe, 1, 0);
            selection.Controls.Add(filterSurface, 2, 0);
            selection.Controls.Add(modernSelectionSummary, 3, 0);
            surface.Controls.Add(selection);
            return surface;
        }

        private Control BuildUpdateSurface()
        {
            tableLayoutPanel7.Controls.Clear();
            tableLayoutPanel3.Controls.Clear();
            // El listado clásico ya no se crea ni participa en el árbol visual.

            panelList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panelList.Dock = DockStyle.Fill;
            panelList.Margin = Padding.Empty;
            panelList.Padding = Padding.Empty;
            panelList.BackColor = UiSurface;
            panelList.RowStyles[0] = new RowStyle(SizeType.Absolute, 0F);
            panelList.RowStyles[1] = new RowStyle(SizeType.Percent, 100F);
            panelList.RowStyles[2] = new RowStyle(SizeType.Absolute, 0F);
            panelList.RowStyles[3] = new RowStyle(SizeType.Absolute, 0F);

            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logBox.Dock = DockStyle.Fill;
            logBox.Margin = new Padding(0, 8, 0, 0);
            logBox.BorderStyle = BorderStyle.None;
            logBox.BackColor = Color.FromArgb(18, 18, 18);
            logBox.ForeColor = Color.FromArgb(205, 205, 205);
            logBox.Font = new Font("Consolas", 8.8F);

            modernUpdateList = new ModernUpdateList();
            modernUpdateList.Name = "modernUpdateList";
            modernUpdateList.AccessibleName = "Lista de actualizaciones";
            modernUpdateList.AccessibleRole = AccessibleRole.List;
            modernUpdateList.Dock = DockStyle.Fill;
            modernUpdateList.Margin = Padding.Empty;
            modernUpdateList.ItemCheckedChanged += modernUpdateList_ItemCheckedChanged;
            modernUpdateList.SelectedItemChanged += modernUpdateList_SelectedItemChanged;
            modernUpdateList.ColumnClicked += modernUpdateList_ColumnClicked;
            modernUpdateList.MouseMove += modernUpdateList_ToolTipMouseMove;
            modernUpdateList.MouseLeave += modernUpdateList_ToolTipMouseLeave;
            panelList.Controls.Add(modernUpdateList, 0, 1);
            SyncModernUpdateList();

            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(28, 0, 28, 0);
            card.Padding = new Padding(1);
            card.BackColor = UiSurface;
            card.BorderColor = UiBorder;
            card.CornerRadius = 12;

            card.Controls.Add(panelList);
            return card;
        }

        private Control BuildStatusBar()
        {
            RoundedPanel surface = new RoundedPanel();
            surface.Dock = DockStyle.Fill;
            surface.Margin = new Padding(28, 8, 28, 12);
            surface.Padding = new Padding(10, 5, 10, 5);
            surface.BackColor = UiSurface;
            surface.BorderColor = UiBorder;
            surface.CornerRadius = 10;

            TableLayoutPanel status = new TableLayoutPanel();
            status.Dock = DockStyle.Fill;
            status.Margin = Padding.Empty;
            status.Padding = Padding.Empty;
            status.BackColor = UiSurface;
            status.ColumnCount = 5;
            status.RowCount = 1;
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            status.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Margin = new Padding(0, 0, 8, 0);
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.ForeColor = UiMuted;
            lblStatus.Font = new Font("Segoe UI", 9F);
            lblStatus.AutoEllipsis = true;
            lblStatus.Visible = true;
            if (string.IsNullOrWhiteSpace(lblStatus.Text))
                lblStatus.Text = "Listo";

            modernProgress = new ModernProgressBar();
            modernProgress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            modernProgress.Height = 10;
            modernProgress.Margin = new Padding(0, 0, 12, 0);
            modernProgress.Visible = false;

            StyleSecondaryActionButton(btnCancel, "Cancelar", UiDanger);
            btnCancel.Dock = DockStyle.None;
            btnCancel.Anchor = AnchorStyles.None;
            btnCancel.Size = new Size(96, 32);
            btnCancel.MinimumSize = new Size(96, 32);
            btnCancel.MaximumSize = new Size(96, 32);
            btnCancel.Margin = new Padding(2, 0, 8, 0);
            btnCancel.Image = null;
            btnCancel.Padding = Padding.Empty;
            btnCancel.TextImageRelation = TextImageRelation.Overlay;

            modernLogButton = new Button();
            modernLogButton.Text = "Ver actividad";
            modernLogButton.Dock = DockStyle.None;
            modernLogButton.Anchor = AnchorStyles.None;
            modernLogButton.Size = new Size(124, 32);
            modernLogButton.MinimumSize = new Size(124, 32);
            modernLogButton.MaximumSize = new Size(124, 32);
            modernLogButton.Margin = new Padding(2, 0, 0, 0);
            modernLogButton.FlatStyle = FlatStyle.Flat;
            modernLogButton.FlatAppearance.BorderColor = UiBorder;
            modernLogButton.FlatAppearance.MouseOverBackColor = UiHover;
            modernLogButton.BackColor = UiSurface;
            modernLogButton.ForeColor = UiMuted;
            modernLogButton.Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold);
            modernLogButton.Cursor = Cursors.Hand;
            modernLogButton.Click += modernLogButton_Click;

            lblSupport.Dock = DockStyle.Fill;
            lblSupport.Margin = new Padding(8, 0, 6, 0);
            lblSupport.TextAlign = ContentAlignment.MiddleCenter;
            lblSupport.LinkColor = UiAccent;
            lblSupport.ActiveLinkColor = UiAccentHover;

            status.Controls.Add(lblStatus, 0, 0);
            status.Controls.Add(modernProgress, 1, 0);
            status.Controls.Add(btnCancel, 2, 0);
            status.Controls.Add(lblSupport, 3, 0);
            status.Controls.Add(modernLogButton, 4, 0);
            surface.Controls.Add(status);
            return surface;
        }

        private void SyncModernProgress(bool busy)
        {
            if (modernProgress == null)
                return;
            modernProgress.Visible = busy;
            modernProgress.ProgressValue = progTotal.Value;
            modernProgress.IsMarquee = progTotal.Style == ProgressBarStyle.Marquee;
        }

        private void ConfigureSettingsTabs()
        {
            tabOptions.Text = "General";
            tabAU.Text = "Control";
            tabOptions.BackColor = UiSurface;
            tabAU.BackColor = UiSurface;
            tabOptions.AutoScroll = true;
            tabAU.AutoScroll = true;
            tabOptions.ForeColor = UiText;
            tabAU.ForeColor = UiText;

            StyleSettingsControl(dlSource, 14, 14, 222);
            StyleSettingsControl(chkOffline, 16, 50, 220);
            StyleSettingsControl(chkDownload, 16, 78, 220);
            StyleSettingsControl(chkManual, 16, 106, 220);
            StyleSettingsControl(chkOld, 16, 134, 220);
            StyleSettingsControl(chkMsUpd, 16, 162, 220);

            gbStartup.Location = new Point(12, 196);
            gbStartup.Size = new Size(228, 126);
            gbStartup.ForeColor = UiText;
            gbStartup.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
            StyleSettingsControl(chkAutoRun, 10, 24, 208);
            StyleSettingsControl(dlAutoCheck, 10, 51, 208);
            StyleSettingsControl(chkNoUAC, 10, 83, 208);

            StyleSettingsControl(chkBlockMS, 14, 12, 224);
            StyleSettingsControl(radDisable, 14, 42, 224);
            StyleSettingsControl(chkDisableAU, 34, 69, 204);
            StyleSettingsControl(radNotify, 14, 99, 224);
            StyleSettingsControl(radDownload, 14, 127, 224);
            StyleSettingsControl(radSchedule, 14, 155, 224);
            StyleSettingsControl(dlShDay, 34, 183, 118);
            StyleSettingsControl(dlShTime, 158, 183, 72);
            StyleSettingsControl(radDefault, 14, 217, 224);
            label1.Location = new Point(14, 249);
            label1.Size = new Size(216, 1);
            label1.BackColor = UiBorder;
            StyleSettingsControl(chkHideWU, 14, 263, 224);
            StyleSettingsControl(chkStore, 14, 291, 224);
            StyleSettingsControl(chkDrivers, 14, 319, 224);

            chkOffline.Text = "Usar catálogo sin conexión";
            chkDownload.Text = "Actualizar catálogo offline";
            chkManual.Text = "Usar modo manual";
            chkOld.Text = "Incluir reemplazadas";
            chkMsUpd.Text = "Incluir Microsoft Update";
            gbStartup.Text = "Inicio y comprobaciones";
            chkAutoRun.Text = "Ejecutar en segundo plano";
            chkNoUAC.Text = "Ejecutar como administrador";

            chkBlockMS.Text = "Bloquear servidores de Microsoft";
            radDisable.Text = "Desactivar actualización automática";
            chkDisableAU.Text = "Desactivar servicios auxiliares";
            radNotify.Text = "Sólo avisar";
            radDownload.Text = "Descargar, sin instalar";
            radSchedule.Text = "Instalación programada";
            radDefault.Text = "Comportamiento automático de Windows";
            chkHideWU.Text = "Ocultar la página de Windows Update";
            chkStore.Text = "Desactivar actualizaciones de Store";
            chkDrivers.Text = "Incluir controladores";

            if (dlShDay.Items.Count >= 8)
            {
                dlShDay.Items.Clear();
                dlShDay.Items.AddRange(new object[] { "Cada día", "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" });
            }
        }

        private Panel BuildSettingsPage()
        {
            ConfigureSettingsTabs();

            tabs.TabPages.Remove(tabOptions);
            tabs.TabPages.Remove(tabAU);

            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.Margin = Padding.Empty;
            page.BackColor = UiBackground;
            page.Enabled = tabs.Enabled;

            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.Margin = Padding.Empty;
            cards.Padding = new Padding(28, 0, 28, 24);
            cards.BackColor = UiBackground;
            cards.ColumnCount = 2;
            cards.RowCount = 1;
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            RoundedPanel generalCard = BuildSettingsCard("General", "Origen, búsqueda y comportamiento de inicio", tabOptions, false);
            RoundedPanel controlCard = BuildSettingsCard("Control de Windows Update", "Automatización, servicios y directivas", tabAU, true);
            generalCard.Margin = new Padding(0, 0, 9, 0);
            controlCard.Margin = new Padding(9, 0, 0, 0);
            cards.Controls.Add(generalCard, 0, 0);
            cards.Controls.Add(controlCard, 1, 0);
            page.Controls.Add(cards);
            return page;
        }

        private RoundedPanel BuildSettingsCard(string title, string subtitle, TabPage source, bool controlSettings)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.BackColor = UiSurface;
            card.BorderColor = UiBorder;
            card.CornerRadius = 12;
            card.Padding = new Padding(18, 70, 18, 16);

            Label heading = CreateLabel(title, 13.5F, FontStyle.Bold, UiText);
            heading.Location = new Point(20, 16);
            heading.AutoSize = true;
            Label caption = CreateLabel(subtitle, 8.6F, FontStyle.Regular, UiMuted);
            caption.Location = new Point(21, 43);
            caption.AutoSize = true;

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Margin = Padding.Empty;
            content.Padding = Padding.Empty;
            content.BackColor = UiSurface;
            content.AutoScroll = true;
            while (source.Controls.Count > 0)
                content.Controls.Add(source.Controls[0]);

            card.Controls.Add(content);
            card.Controls.Add(heading);
            card.Controls.Add(caption);
            heading.BringToFront();
            caption.BringToFront();

            if (controlSettings)
            {
                modernControlPage = content;
                content.Resize += delegate { LayoutControlSettings(); };
                LayoutControlSettings();
            }
            else
            {
                modernOptionsPage = content;
                content.Resize += delegate { LayoutGeneralSettings(); };
                LayoutGeneralSettings();
            }
            return card;
        }

        private void LayoutGeneralSettings()
        {
            int contentWidth = modernOptionsPage != null && modernOptionsPage.ClientSize.Width > 80
                ? modernOptionsPage.ClientSize.Width
                : 332;
            int rootWidth = Math.Max(190, contentWidth - 16);

            PositionSetting(dlSource, 4, 2, rootWidth);
            PositionSetting(chkOffline, 4, 42, rootWidth);
            PositionSetting(chkDownload, 4, 72, rootWidth);
            PositionSetting(chkManual, 4, 102, rootWidth);
            PositionSetting(chkOld, 4, 132, rootWidth);
            PositionSetting(chkMsUpd, 4, 162, rootWidth);
            gbStartup.Location = new Point(4, 204);
            gbStartup.Size = new Size(rootWidth, 132);
            gbStartup.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            gbStartup.BackColor = UiSurface;
            int startupWidth = Math.Max(160, gbStartup.ClientSize.Width - 20);
            PositionSetting(chkAutoRun, 10, 24, startupWidth);
            PositionSetting(dlAutoCheck, 10, 52, startupWidth);
            PositionSetting(chkNoUAC, 10, 84, startupWidth);
            chkNoUAC.AutoEllipsis = false;
        }

        private void LayoutControlSettings()
        {
            int contentWidth = modernControlPage != null && modernControlPage.ClientSize.Width > 80
                ? modernControlPage.ClientSize.Width
                : 342;
            int rootWidth = Math.Max(200, contentWidth - 16);

            PositionSetting(chkBlockMS, 4, 2, rootWidth);
            PositionSetting(radDisable, 4, 42, rootWidth);
            PositionSetting(chkDisableAU, 24, 72, Math.Max(160, rootWidth - 20));
            PositionSetting(radNotify, 4, 110, rootWidth);
            PositionSetting(radDownload, 4, 142, rootWidth);
            PositionSetting(radSchedule, 4, 174, rootWidth);

            int timeWidth = 88;
            int scheduleRight = 4 + rootWidth;
            int timeLeft = Math.Max(122, scheduleRight - timeWidth);
            int dayWidth = Math.Max(88, timeLeft - 24 - 8);
            PositionSetting(dlShDay, 24, 206, dayWidth);
            PositionSetting(dlShTime, timeLeft, 206, timeWidth);
            PositionSetting(radDefault, 4, 246, rootWidth);
            label1.Location = new Point(4, 286);
            label1.Size = new Size(rootWidth, 1);
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            PositionSetting(chkHideWU, 4, 300, rootWidth);
            PositionSetting(chkStore, 4, 334, rootWidth);
            PositionSetting(chkDrivers, 4, 368, rootWidth);
        }

        private void PositionSetting(Control control, int x, int y, int width)
        {
            control.Location = new Point(x, y);
            control.Width = width;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        }

        private Panel CreateSettingsPage(TabPage source)
        {
            Panel page = new Panel();
            page.Dock = DockStyle.Fill;
            page.Margin = Padding.Empty;
            page.Padding = Padding.Empty;
            page.BackColor = Color.FromArgb(13, 20, 32);
            page.AutoScroll = true;

            while (source.Controls.Count > 0)
                page.Controls.Add(source.Controls[0]);

            return page;
        }

        private Button CreateSettingsSelector(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private void SelectSettingsPage(Button selected, Button other, Control visiblePage, Control hiddenPage)
        {
            selected.BackColor = UiAccent;
            selected.ForeColor = Color.White;
            selected.FlatAppearance.MouseOverBackColor = UiAccentHover;
            other.BackColor = UiSidebar;
            other.ForeColor = UiMuted;
            other.FlatAppearance.MouseOverBackColor = UiSidebarHover;
            visiblePage.Visible = true;
            visiblePage.BringToFront();
            hiddenPage.Visible = false;
        }

        private void StyleNavigationButton(CheckBox button)
        {
            button.Appearance = Appearance.Button;
            button.AutoSize = false;
            button.Dock = DockStyle.None;
            button.Size = new Size(190, 42);
            button.Margin = new Padding(8, 0, 8, 6);
            button.Padding = new Padding(18, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.CheckedBackColor = UiSidebarHover;
            button.FlatAppearance.MouseOverBackColor = UiSidebarHover;
            button.BackColor = UiSidebar;
            button.ForeColor = UiText;
            button.Font = new Font("Segoe UI", 9.4F, FontStyle.Regular);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            button.Paint += navigationButton_Paint;
            button.Click += modernNavigationButton_Click;
            ApplyRoundedRegion(button, 9);
        }

        private void navigationButton_Paint(object sender, PaintEventArgs e)
        {
            CheckBox button = sender as CheckBox;
            if (button == null || !button.Checked)
                return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath indicator = CreateRoundedPath(new Rectangle(4, 11, 4, Math.Max(4, button.Height - 22)), 2))
            using (SolidBrush brush = new SolidBrush(UiAccent))
                e.Graphics.FillPath(brush, indicator);
            e.Graphics.SmoothingMode = SmoothingMode.Default;
        }

        private void modernNavigationButton_Click(object sender, EventArgs e)
        {
            if (sender == modernPackageUpdatesButton)
                ShowPackageUpdatesPage();
            else if (sender != modernSettingsButton)
                ShowUpdatesPage();
        }

        private void StyleActionButton(Button button, string text, Color background, Color foreground, int width)
        {
            button.Text = text;
            button.AutoSize = false;
            button.Width = width;
            button.Height = 42;
            button.Padding = new Padding(8, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = background == UiAccent ? UiAccentHover : background;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold);
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            button.Paint += disabledActionButton_Paint;
            ApplyRoundedRegion(button, 9);
        }

        private void StyleSecondaryActionButton(Button button, string text, Color foreground)
        {
            button.Text = text;
            button.AutoSize = false;
            button.Height = 36;
            button.Width = text.Length > 11 ? 116 : 100;
            button.Margin = new Padding(0, 0, 8, 0);
            button.Padding = new Padding(8, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiHover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(57, 57, 57);
            button.BackColor = UiInput;
            button.ForeColor = foreground;
            button.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
            button.Paint += disabledActionButton_Paint;
            ApplyRoundedRegion(button, 9);
        }

        private void StyleCompactCheckBox(CheckBox checkBox)
        {
            checkBox.Dock = DockStyle.Fill;
            checkBox.AutoSize = false;
            checkBox.Margin = new Padding(2, 0, 2, 0);
            checkBox.ForeColor = UiText;
            checkBox.Font = new Font("Segoe UI", 8.8F);
            checkBox.BackColor = UiSurface;
            checkBox.UseVisualStyleBackColor = false;
        }

        private void ConfigureModernToolTips()
        {
            toolTip.OwnerDraw = true;
            toolTip.BackColor = UiInput;
            toolTip.ForeColor = UiText;
            toolTip.InitialDelay = 500;
            toolTip.ReshowDelay = 100;
            toolTip.AutoPopDelay = 7000;
            toolTip.Popup += modernToolTip_Popup;
            toolTip.Draw += modernToolTip_Draw;

            modernListToolTipTimer = new Timer(components);
            modernListToolTipTimer.Interval = 520;
            modernListToolTipTimer.Tick += modernListToolTipTimer_Tick;
        }

        private void modernToolTip_Popup(object sender, PopupEventArgs e)
        {
            string text = toolTip.GetToolTip(e.AssociatedControl) ?? string.Empty;
            using (Font font = new Font("Segoe UI", 9F, FontStyle.Regular))
            {
                Size measured = TextRenderer.MeasureText(text, font, new Size(560, 0),
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                e.ToolTipSize = new Size(Math.Max(80, measured.Width + 18), Math.Max(30, measured.Height + 12));
            }
        }

        private void modernToolTip_Draw(object sender, DrawToolTipEventArgs e)
        {
            e.Graphics.Clear(UiInput);
            using (Pen border = new Pen(UiBorder))
                e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, e.Bounds.Width - 1), Math.Max(0, e.Bounds.Height - 1));
            Rectangle textBounds = Rectangle.Inflate(e.Bounds, -9, -6);
            using (Font font = new Font("Segoe UI", 9F))
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, textBounds, UiText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak |
                    TextFormatFlags.NoPrefix);
        }

        private void modernUpdateList_ToolTipMouseMove(object sender, MouseEventArgs e)
        {
            if (modernUpdateList == null)
                return;
            string truncatedText = modernUpdateList.GetTruncatedText(e.Location);
            if (string.IsNullOrWhiteSpace(truncatedText))
            {
                HideModernListToolTip();
                return;
            }
            if (string.Equals(modernListToolTipCell, truncatedText, StringComparison.Ordinal))
                return;

            toolTip.Hide(modernUpdateList);
            modernListToolTipTimer.Stop();
            modernListToolTipCell = truncatedText;
            modernListToolTipText = truncatedText;
            modernListToolTipPoint = new Point(Math.Min(modernUpdateList.ClientSize.Width - 24, e.X + 14), e.Y + 20);
            modernListToolTipTimer.Start();
        }

        private void modernUpdateList_ToolTipMouseLeave(object sender, EventArgs e)
        {
            HideModernListToolTip();
        }

        private void modernListToolTipTimer_Tick(object sender, EventArgs e)
        {
            modernListToolTipTimer.Stop();
            if (string.IsNullOrEmpty(modernListToolTipCell) || string.IsNullOrWhiteSpace(modernListToolTipText))
                return;

            toolTip.SetToolTip(modernUpdateList, modernListToolTipText);
            toolTip.Show(modernListToolTipText, modernUpdateList, modernListToolTipPoint, 6500);
        }

        private void HideModernListToolTip()
        {
            if (modernListToolTipTimer != null)
                modernListToolTipTimer.Stop();
            if (!string.IsNullOrEmpty(modernListToolTipCell) && modernUpdateList != null)
                toolTip.Hide(modernUpdateList);
            modernListToolTipCell = string.Empty;
            modernListToolTipText = string.Empty;
            if (modernUpdateList != null)
                toolTip.SetToolTip(modernUpdateList, string.Empty);
        }

        private void StyleSettingsControl(Control control, int x, int y, int width)
        {
            control.Location = new Point(x, y);
            control.Width = width;
            control.ForeColor = UiText;
            control.BackColor = UiSurface;
            control.Font = new Font("Segoe UI", 8.6F, FontStyle.Regular);

            CheckBox checkBox = control as CheckBox;
            if (checkBox != null)
            {
                checkBox.AutoSize = false;
                checkBox.Height = 24;
                checkBox.UseVisualStyleBackColor = false;
            }

            RadioButton radioButton = control as RadioButton;
            if (radioButton != null)
            {
                radioButton.AutoSize = false;
                radioButton.Height = 24;
                radioButton.UseVisualStyleBackColor = false;
            }

            ComboBox comboBox = control as ComboBox;
            if (comboBox != null)
            {
                comboBox.ItemHeight = 22;
                comboBox.DropDownHeight = 154;
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.BackColor = UiInput;
                comboBox.ForeColor = UiText;
                comboBox.DrawMode = DrawMode.OwnerDrawFixed;
                comboBox.DrawItem += darkComboBox_DrawItem;
                comboBox.DropDown += delegate { comboBox.Invalidate(); };
                comboBox.DropDownClosed += delegate { comboBox.Invalidate(); };
                modernComboRenderers.Add(new DarkComboBoxRenderer(comboBox, UiInput, UiBorder, UiMuted));
            }
        }

        private void darkComboBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            ComboBox comboBox = sender as ComboBox;
            if (comboBox == null || e.Index < 0)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color itemBackground = selected ? Color.FromArgb(58, 58, 58) : UiInput;
            using (SolidBrush brush = new SolidBrush(itemBackground))
                e.Graphics.FillRectangle(brush, e.Bounds);

            if (selected && comboBox.DroppedDown)
            {
                using (SolidBrush accent = new SolidBrush(UiAccent))
                    e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top + 3, 3,
                        Math.Max(0, e.Bounds.Height - 6));
            }

            string itemText = comboBox.GetItemText(comboBox.Items[e.Index]);
            Rectangle textBounds = new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, Math.Max(0, e.Bounds.Width - 13), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, itemText, comboBox.Font, textBounds, UiText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void disabledActionButton_Paint(object sender, PaintEventArgs e)
        {
            Button button = sender as Button;
            if (button == null || button.Enabled)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, button.Width - 1), Math.Max(1, button.Height - 1));
            using (GraphicsPath backgroundPath = CreateRoundedPath(bounds, 9))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(37, 37, 37)))
                e.Graphics.FillPath(brush, backgroundPath);

            Rectangle textBounds = new Rectangle(8, 0, Math.Max(0, bounds.Width - 16), bounds.Height);
            if (button.Image != null)
            {
                int imageY = (bounds.Height - button.Image.Height) / 2;
                ControlPaint.DrawImageDisabled(e.Graphics, button.Image, 9, imageY, Color.FromArgb(37, 37, 37));
                textBounds.X += button.Image.Width + 4;
                textBounds.Width -= button.Image.Width + 4;
            }

            TextRenderer.DrawText(e.Graphics, button.Text, button.Font, textBounds, Color.FromArgb(126, 126, 126),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.Graphics.SmoothingMode = SmoothingMode.Default;
        }

        private void PopulateHistoryPreview()
        {
            string[] titles =
            {
                "2026-07 Actualización acumulativa para Windows 10 Version 21H2 para sistemas x64",
                "Intel Corporation - Display - 31.0.101.4502",
                "Intel Corporation - Extension - 31.0.101.4502",
                "Realtek Semiconductor Corp. - Extension - 6.0.9626.1",
                "Realtek Semiconductor Corp. - MEDIA - 6.0.9626.1",
                "Intel - SoftwareComponent - 2303.4.2.0",
                "Intel - SoftwareComponent - 1.42.2023.102",
                "Intel - SoftwareComponent - 2252.71.74.0"
            };

            agent.mUpdateHistory.Clear();
            for (int index = 0; index < titles.Length; index++)
            {
                agent.mUpdateHistory.Add(new MsUpdate
                {
                    Title = titles[index],
                    Category = "",
                    ApplicationID = index == 0 ? "UpdateAgentLCU" : "WinSlim Update",
                    Date = new DateTime(2026, 8, 1).AddMinutes(-index * 8),
                    State = MsUpdate.UpdateState.History,
                    ResultCode = (int)WUApiLib.OperationResultCode.orcSucceeded,
                    HResult = 0
                });
            }

            CurrentList = UpdateLists.UpdateHistory;
            suspendChange = true;
            btnWinUpd.Checked = false;
            btnInstalled.Checked = false;
            btnHidden.Checked = false;
            btnHistory.Checked = true;
            suspendChange = false;
            btnHistory.Text = "Historial (8)";
            modernSettingsVisible = false;
            LoadList();
            UpdateModernPage();
            UpdateModernSelectionSummary();
            UpdateModernEmptyState();
        }

        private void PopulateListPreview()
        {
            updateItems.Clear();
            updateListCheckBoxes = true;
            updateListShowGroups = true;

            mSuspendUpdate = true;
            chkGrupe.Checked = true;
            mSuspendUpdate = false;

            ListViewGroup security = CreateModernGroup("Seguridad", "Seguridad de Windows");
            ListViewGroup quality = CreateModernGroup("Calidad", "Actualizaciones de calidad");
            ListViewGroup framework = CreateModernGroup("Framework", ".NET y componentes del sistema");

            AddListPreviewRow(security,
                "Actualización de inteligencia de seguridad para Microsoft Defender Antivirus",
                "Seguridad", "KB2267602", "30/07/2026", "1,2 MB", "Instalada", new DateTime(2026, 7, 30), 1258291M);
            AddListPreviewRow(security,
                "Actualización de la plataforma de seguridad de Windows",
                "Seguridad", "KB5007651", "29/07/2026", "44,0 MB", "Instalada", new DateTime(2026, 7, 29), 46137344M);
            AddListPreviewRow(quality,
                "Actualización acumulativa para Windows 11, versión 25H2",
                "Calidad", "KB5071123", "28/07/2026", "728 MB", "Instalada", new DateTime(2026, 7, 28), 763363328M);
            AddListPreviewRow(quality,
                "Actualización de la pila de mantenimiento de Windows 11",
                "Calidad", "KB5070890", "28/07/2026", "15,8 MB", "Instalada", new DateTime(2026, 7, 28), 16567501M);
            AddListPreviewRow(framework,
                "Actualización acumulativa de .NET Framework 3.5 y 4.8.1",
                ".NET", "KB5101762", "14/07/2026", "150 MB", "Instalada", new DateTime(2026, 7, 14), 157286400M);
            AddListPreviewRow(framework,
                "Herramienta de eliminación de software malintencionado de Windows",
                "Sistema", "KB890830", "09/07/2026", "90,6 MB", "Instalada", new DateTime(2026, 7, 9), 95000985M);

            if (Program.TestArg("-preview-scroll"))
            {
                ListViewGroup drivers = CreateModernGroup("Controladores", "Controladores");
                for (int index = 0; index < 18; index++)
                {
                    AddListPreviewRow(drivers,
                        "Intel Corporation - SoftwareComponent - 2252.71." + (74 + index) + ".0",
                        "Controladores", "KBUnknown", (8 + index).ToString("00") + "/07/2026",
                        (12 + index) + ",4 MB", "Instalada", new DateTime(2026, 7, 8).AddDays(index),
                        (12 + index) * 1048576M);
                }
            }

            SyncModernUpdateList();

            CurrentList = UpdateLists.InstaledUpdates;
            suspendChange = true;
            btnWinUpd.Checked = false;
            btnInstalled.Checked = true;
            btnHidden.Checked = false;
            btnHistory.Checked = false;
            suspendChange = false;
            btnInstalled.Text = "Instaladas (" + updateItems.Count + ")";
            modernSettingsVisible = false;
            UpdateModernPage();
            UpdateModernSelectionSummary();
            UpdateModernEmptyState();

            if (updateItems.Count > 1 && modernUpdateList != null)
                modernUpdateList.SelectItem(updateItems[1]);
        }

        private static ListViewGroup CreateModernGroup(string name, string title)
        {
            ListViewGroup group = new ListViewGroup(title, HorizontalAlignment.Left);
            group.Name = name;
            return group;
        }

        private void AddListPreviewRow(ListViewGroup group, string title, string category, string kb,
            string dateText, string sizeText, string state, DateTime date, decimal size)
        {
            string[] values = { title, category, kb, dateText, sizeText, state + " · No desinstalable" };
            ListViewItem item = new ListViewItem(values);
            item.Group = group;
            item.SubItems[3].Tag = date;
            item.SubItems[4].Tag = size;
            item.Tag = new MsUpdate
            {
                Title = title,
                Category = category,
                KB = kb,
                Date = date,
                Size = size,
                State = MsUpdate.UpdateState.Installed,
                Attributes = (int)MsUpdate.UpdateAttr.Installed
            };
            updateItems.Add(item);
        }

        private void SyncModernUpdateList()
        {
            if (modernUpdateList == null)
                return;

            modernUpdateList.SetHeaders(
                updateListHeaders[0],
                updateListHeaders[1],
                updateListHeaders[2],
                updateListHeaders[3],
                updateListHeaders[4],
                updateListHeaders[5]);
            modernUpdateList.SetItems(updateItems, updateListShowGroups, updateListCheckBoxes);
        }

        private int GetCheckedItemCount()
        {
            int count = 0;
            foreach (ListViewItem item in updateItems)
            {
                if (item.Checked)
                    count++;
            }
            return count;
        }

        private void modernUpdateList_ItemCheckedChanged(object sender, ModernUpdateList.ItemEventArgs e)
        {
            ignoreChecks = true;
            int checkedCount = GetCheckedItemCount();
            if (checkedCount == 0)
                chkAll.CheckState = CheckState.Unchecked;
            else if (checkedCount == updateItems.Count)
                chkAll.CheckState = CheckState.Checked;
            else
                chkAll.CheckState = CheckState.Indeterminate;
            ignoreChecks = false;
            checkChecks = true;
            UpdateState();
        }

        private void modernUpdateList_SelectedItemChanged(object sender, ModernUpdateList.ItemEventArgs e)
        {
            UpdateSelectedItemDetails(e.Item);
        }

        private void modernUpdateList_ColumnClicked(object sender, ModernUpdateList.ColumnEventArgs e)
        {
            if (modernSortColumn == e.Column)
                modernSortDescending = !modernSortDescending;
            else
            {
                modernSortColumn = e.Column;
                modernSortDescending = false;
            }
            modernUpdateList.SortByColumn(e.Column, modernSortDescending);
        }

        private static Label CreateLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }

        private static void ApplyRoundedRegion(Control control, int radius)
        {
            Action updateRegion = delegate
            {
                if (control.Width <= 0 || control.Height <= 0)
                    return;
                using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius))
                    control.Region = new Region(path);
            };
            control.Resize += delegate { updateRegion(); };
            updateRegion();
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyExplorerTheme()
        {
            try
            {
                int darkMode = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref darkMode, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref darkMode, sizeof(int));
                UpdateWindowCorners();

                SetWindowTheme(txtFilter.Handle, "DarkMode_Explorer", null);
                SetWindowTheme(logBox.Handle, "DarkMode_Explorer", null);
                SetWindowTheme(progTotal.Handle, "DarkMode_Explorer", null);
                if (modernOptionsPage != null)
                    SetWindowTheme(modernOptionsPage.Handle, "DarkMode_Explorer", null);
                if (modernControlPage != null)
                    SetWindowTheme(modernControlPage.Handle, "DarkMode_Explorer", null);

                ApplyDarkNativeTheme(this);
            }
            catch
            {
                // El aspecto nativo sigue siendo funcional si el tema no está disponible.
            }
        }

        private void ApplyDarkNativeTheme(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is ComboBox)
                    SetWindowTheme(child.Handle, "", "");
                else if (child is CheckBox || child is RadioButton || child is ProgressBar)
                    SetWindowTheme(child.Handle, "DarkMode_Explorer", null);
                if (child.HasChildren)
                    ApplyDarkNativeTheme(child);
            }
        }

        private sealed class EmptyStatePanel : Control
        {
            private string titleText = "Todo está al día";
            private string subtitleText = "No se encontraron actualizaciones pendientes.";

            public string TitleText
            {
                get { return titleText; }
                set { titleText = value ?? string.Empty; Invalidate(); }
            }

            public string SubtitleText
            {
                get { return subtitleText; }
                set { subtitleText = value ?? string.Empty; Invalidate(); }
            }

            public EmptyStatePanel()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                BackColor = UiSurface;
                TabStop = false;
                SetStyle(ControlStyles.Selectable, false);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.Clear(UiSurface);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                int centerX = Width / 2;
                int centerY = Math.Max(90, Height / 2 - 30);
                Rectangle iconBounds = new Rectangle(centerX - 30, centerY - 68, 60, 60);
                using (SolidBrush iconFill = new SolidBrush(UiInput))
                    e.Graphics.FillEllipse(iconFill, iconBounds);
                using (Pen iconBorder = new Pen(Color.FromArgb(78, 78, 78), 1.2F))
                    e.Graphics.DrawEllipse(iconBorder, iconBounds);

                Point[] checkPoints =
                {
                    new Point(centerX - 13, centerY - 38),
                    new Point(centerX - 3, centerY - 28),
                    new Point(centerX + 16, centerY - 49)
                };
                using (Pen check = new Pen(UiAccent, 3.2F))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    check.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(check, checkPoints);
                }

                Rectangle titleBounds = new Rectangle(24, centerY + 4, Math.Max(0, Width - 48), 36);
                using (Font titleFont = new Font("Segoe UI Semibold", 16F, FontStyle.Bold))
                    TextRenderer.DrawText(e.Graphics, titleText, titleFont, titleBounds, UiText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                Rectangle subtitleBounds = new Rectangle(24, centerY + 39, Math.Max(0, Width - 48), 28);
                using (Font subtitleFont = new Font("Segoe UI", 9.2F, FontStyle.Regular))
                    TextRenderer.DrawText(e.Graphics, subtitleText, subtitleFont, subtitleBounds, UiMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                e.Graphics.SmoothingMode = SmoothingMode.Default;
            }
        }

        private sealed class ModernListScrollBar : Control
        {
            private const int SbHorz = 0;
            private const int SbVert = 1;
            private const int SifAll = 0x17;
            private const int LvmFirst = 0x1000;
            private const int LvmScroll = LvmFirst + 20;
            private const int WmHScroll = 0x0114;
            private const int WmVScroll = 0x0115;
            private const int SbThumbPosition = 4;

            [StructLayout(LayoutKind.Sequential)]
            private struct ScrollInfo
            {
                public uint cbSize;
                public uint fMask;
                public int nMin;
                public int nMax;
                public uint nPage;
                public int nPos;
                public int nTrackPos;
            }

            [DllImport("user32.dll")]
            private static extern bool GetScrollInfo(IntPtr hWnd, int nBar, ref ScrollInfo info);

            [DllImport("user32.dll")]
            private static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool show);

            [DllImport("user32.dll", EntryPoint = "SendMessage")]
            private static extern IntPtr SendListMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

            private readonly ListView target;
            private readonly bool vertical;
            private readonly Timer syncTimer;
            private int minimum;
            private int maximum;
            private int value;
            private uint pageSize;
            private bool dragging;
            private bool hovered;
            private int dragOffset;
            private int observedItemCount = -1;
            private Rectangle trackBounds;
            private Rectangle thumbBounds;

            public ModernListScrollBar(ListView targetList, bool isVertical)
            {
                target = targetList;
                vertical = isVertical;
                DoubleBuffered = true;
                ResizeRedraw = true;
                Cursor = Cursors.Hand;
                BackColor = UiSurface;
                TabStop = false;
                SetStyle(ControlStyles.Selectable, false);

                syncTimer = new Timer();
                syncTimer.Interval = 60;
                syncTimer.Tick += delegate { RefreshMetrics(); };
                syncTimer.Start();

                target.HandleCreated += target_HandleCreated;
                target.Resize += target_LayoutChanged;
                target.ColumnWidthChanged += target_ColumnWidthChanged;
                target.MouseWheel += target_MouseWheel;
                target.KeyUp += target_KeyUp;
            }

            private void RefreshMetrics()
            {
                if (target == null || target.IsDisposed || !target.IsHandleCreated)
                    return;

                if (vertical)
                {
                    RefreshVerticalMetrics();
                    HideNativeScrollBar();
                    return;
                }

                ScrollInfo info = new ScrollInfo();
                info.cbSize = (uint)Marshal.SizeOf(typeof(ScrollInfo));
                info.fMask = SifAll;
                int scrollBar = vertical ? SbVert : SbHorz;
                if (GetScrollInfo(target.Handle, scrollBar, ref info))
                {
                    int newMaximum = Math.Max(info.nMin, info.nMax - (int)info.nPage + 1);
                    bool changed = minimum != info.nMin || maximum != newMaximum || value != info.nPos || pageSize != info.nPage;
                    minimum = info.nMin;
                    maximum = newMaximum;
                    value = Math.Max(minimum, Math.Min(maximum, info.nPos));
                    pageSize = info.nPage;
                    if (changed)
                        Invalidate();
                }

                HideNativeScrollBar();
            }

            private void RefreshVerticalMetrics()
            {
                int itemCount = target.Items.Count;
                int rowHeight = 34;
                if (itemCount > 0 && target.Items[0].Bounds.Height > 0)
                    rowHeight = target.Items[0].Bounds.Height;

                int groupHeaderCount = 0;
                if (target.ShowGroups)
                {
                    foreach (ListViewGroup group in target.Groups)
                    {
                        if (group.Items.Count > 0)
                            groupHeaderCount++;
                    }
                }

                int availableHeight = Math.Max(rowHeight,
                    target.ClientSize.Height - 28 - (groupHeaderCount * 28));
                int visibleRows = Math.Max(1, availableHeight / Math.Max(1, rowHeight));
                int newMaximum = Math.Max(0, itemCount - visibleRows);
                if (observedItemCount != itemCount)
                {
                    observedItemCount = itemCount;
                    value = 0;
                }
                int newValue = Math.Max(0, Math.Min(newMaximum, value));
                bool changed = minimum != 0 || maximum != newMaximum ||
                    value != newValue || pageSize != (uint)visibleRows;
                minimum = 0;
                maximum = newMaximum;
                value = newValue;
                pageSize = (uint)visibleRows;
                if (changed)
                    Invalidate();
            }

            private void HideNativeScrollBar()
            {
                if (target != null && !target.IsDisposed && target.IsHandleCreated)
                    ShowScrollBar(target.Handle, vertical ? SbVert : SbHorz, false);
            }

            private void target_HandleCreated(object sender, EventArgs e)
            {
                RefreshMetrics();
            }

            private void target_LayoutChanged(object sender, EventArgs e)
            {
                RefreshMetrics();
            }

            private void target_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
            {
                RefreshMetrics();
            }

            private void target_MouseWheel(object sender, MouseEventArgs e)
            {
                if (!vertical)
                    return;

                HandledMouseEventArgs handled = e as HandledMouseEventArgs;
                if (handled != null)
                    handled.Handled = true;

                RefreshVerticalMetrics();
                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines <= 0)
                    lines = 3;
                int notches = e.Delta / Math.Max(1, SystemInformation.MouseWheelScrollDelta);
                ScrollVerticalTo(value - (notches * lines));
            }

            private void target_KeyUp(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageUp ||
                    e.KeyCode == Keys.PageDown || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
                    RefreshAfterNativeScroll();
            }

            private void RefreshAfterNativeScroll()
            {
                HideNativeScrollBar();
                if (target == null || target.IsDisposed || !target.IsHandleCreated)
                    return;
                try
                {
                    target.BeginInvoke(new MethodInvoker(delegate
                    {
                        HideNativeScrollBar();
                        RefreshMetrics();
                    }));
                }
                catch
                {
                    // La ventana puede estar cerrándose mientras llega el último evento de rueda.
                }
            }

            private void UpdateGeometry()
            {
                int trackThickness = hovered || dragging ? 8 : 6;
                trackBounds = vertical
                    ? new Rectangle(Math.Max(0, (Width - trackThickness) / 2), 0, trackThickness, Math.Max(1, Height - 1))
                    : new Rectangle(0, Math.Max(0, (Height - trackThickness) / 2), Math.Max(1, Width - 1), trackThickness);

                if (maximum <= minimum)
                {
                    thumbBounds = trackBounds;
                    return;
                }

                int range = Math.Max(1, maximum - minimum + (int)Math.Max(1U, pageSize));
                int trackLength = vertical ? trackBounds.Height : trackBounds.Width;
                int thumbLength = Math.Max(46, (int)(trackLength * (pageSize / (double)range)));
                thumbLength = Math.Min(trackLength, thumbLength);
                int travel = Math.Max(0, trackLength - thumbLength);
                double ratio = (value - minimum) / (double)Math.Max(1, maximum - minimum);
                int position = (int)Math.Round(travel * ratio);
                thumbBounds = vertical
                    ? new Rectangle(trackBounds.Left, trackBounds.Top + position, trackBounds.Width, thumbLength)
                    : new Rectangle(trackBounds.Left + position, trackBounds.Top, thumbLength, trackBounds.Height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                RefreshMetrics();
                UpdateGeometry();
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (GraphicsPath trackPath = CreateRoundedPath(trackBounds,
                    Math.Max(2, Math.Min(trackBounds.Width, trackBounds.Height) / 2)))
                using (SolidBrush track = new SolidBrush(Color.FromArgb(45, 45, 45)))
                    e.Graphics.FillPath(track, trackPath);

                Color thumbColor = maximum <= minimum
                    ? Color.FromArgb(78, 78, 78)
                    : (hovered || dragging ? Color.FromArgb(196, 198, 202) : Color.FromArgb(137, 139, 143));
                using (GraphicsPath thumbPath = CreateRoundedPath(thumbBounds,
                    Math.Max(2, Math.Min(thumbBounds.Width, thumbBounds.Height) / 2)))
                using (SolidBrush thumb = new SolidBrush(thumbColor))
                    e.Graphics.FillPath(thumb, thumbPath);

                e.Graphics.SmoothingMode = SmoothingMode.Default;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (!dragging)
                {
                    hovered = false;
                    Invalidate();
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left)
                    return;

                RefreshMetrics();
                UpdateGeometry();
                if (maximum <= minimum)
                    return;

                if (thumbBounds.Contains(e.Location))
                    dragOffset = vertical ? e.Y - thumbBounds.Top : e.X - thumbBounds.Left;
                else
                {
                    dragOffset = vertical ? thumbBounds.Height / 2 : thumbBounds.Width / 2;
                    ScrollFromPointer(vertical ? e.Y : e.X);
                }

                dragging = true;
                Capture = true;
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging)
                    ScrollFromPointer(vertical ? e.Y : e.X);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left)
                    return;
                dragging = false;
                Capture = false;
                hovered = ClientRectangle.Contains(e.Location);
                HideNativeScrollBar();
                RefreshAfterNativeScroll();
                Invalidate();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (!vertical || maximum <= minimum)
                    return;

                int lines = SystemInformation.MouseWheelScrollLines;
                if (lines <= 0)
                    lines = 3;
                int notches = e.Delta / Math.Max(1, SystemInformation.MouseWheelScrollDelta);
                ScrollVerticalTo(value - (notches * lines));
            }

            private void ScrollFromPointer(int pointerPosition)
            {
                UpdateGeometry();
                int trackStart = vertical ? trackBounds.Top : trackBounds.Left;
                int trackEnd = vertical ? trackBounds.Bottom : trackBounds.Right;
                int thumbLength = vertical ? thumbBounds.Height : thumbBounds.Width;
                int travel = Math.Max(1, (vertical ? trackBounds.Height : trackBounds.Width) - thumbLength);
                int thumbPosition = Math.Max(trackStart, Math.Min(trackEnd - thumbLength, pointerPosition - dragOffset));
                double ratio = (thumbPosition - trackStart) / (double)travel;
                int targetValue = minimum + (int)Math.Round((maximum - minimum) * ratio);
                int delta = targetValue - value;
                if (delta == 0)
                    return;

                if (vertical)
                {
                    ScrollVerticalTo(targetValue);
                    return;
                }

                SendListMessage(target.Handle, LvmScroll,
                    new IntPtr(delta), IntPtr.Zero);

                ScrollInfo verification = new ScrollInfo();
                verification.cbSize = (uint)Marshal.SizeOf(typeof(ScrollInfo));
                verification.fMask = SifAll;
                int scrollBar = SbHorz;
                if (GetScrollInfo(target.Handle, scrollBar, ref verification) && verification.nPos == value)
                {
                    int packedPosition = ((targetValue & 0xFFFF) << 16) | SbThumbPosition;
                    SendListMessage(target.Handle, WmHScroll, new IntPtr(packedPosition), IntPtr.Zero);
                }
                value = targetValue;
                HideNativeScrollBar();
                RefreshAfterNativeScroll();
                Invalidate();
            }

            private void ScrollVerticalTo(int targetValue)
            {
                if (target.Items.Count == 0)
                    return;

                int clampedValue = Math.Max(minimum, Math.Min(maximum, targetValue));
                int itemIndex = Math.Max(0, Math.Min(target.Items.Count - 1, clampedValue));
                try
                {
                    target.TopItem = target.Items[itemIndex];
                }
                catch
                {
                    int rowHeight = target.Items[0].Bounds.Height > 0 ? target.Items[0].Bounds.Height : 34;
                    int delta = (clampedValue - value) * rowHeight;
                    SendListMessage(target.Handle, LvmScroll, IntPtr.Zero, new IntPtr(delta));
                }

                value = clampedValue;
                HideNativeScrollBar();
                target.Invalidate();
                RefreshAfterNativeScroll();
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    syncTimer.Dispose();
                    if (target != null)
                    {
                        target.HandleCreated -= target_HandleCreated;
                        target.Resize -= target_LayoutChanged;
                        target.ColumnWidthChanged -= target_ColumnWidthChanged;
                        target.MouseWheel -= target_MouseWheel;
                        target.KeyUp -= target_KeyUp;
                    }
                }
                base.Dispose(disposing);
            }
        }

        private sealed class ModernProgressBar : Control
        {
            private readonly Timer animationTimer;
            private int progressValue;
            private int animationOffset;
            private bool isMarquee;

            public int ProgressValue
            {
                get { return progressValue; }
                set
                {
                    progressValue = Math.Max(0, Math.Min(100, value));
                    Invalidate();
                }
            }

            public bool IsMarquee
            {
                get { return isMarquee; }
                set
                {
                    isMarquee = value;
                    animationOffset = 0;
                    UpdateAnimation();
                    Invalidate();
                }
            }

            public ModernProgressBar()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                BackColor = UiInput;
                animationTimer = new Timer();
                animationTimer.Interval = 28;
                animationTimer.Tick += delegate
                {
                    animationOffset += 5;
                    Invalidate();
                };
                VisibleChanged += delegate { UpdateAnimation(); };
            }

            private void UpdateAnimation()
            {
                animationTimer.Enabled = Visible && isMarquee;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width <= 1 || Height <= 1)
                    return;

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = CreateRoundedPath(bounds, Math.Min(6, Height / 2)))
                using (SolidBrush background = new SolidBrush(UiInput))
                using (Pen border = new Pen(UiBorder))
                {
                    e.Graphics.FillPath(background, path);
                    e.Graphics.DrawPath(border, path);
                    Region oldClip = e.Graphics.Clip;
                    e.Graphics.SetClip(path);
                    using (SolidBrush fill = new SolidBrush(UiAccent))
                    {
                        if (isMarquee)
                        {
                            int segment = Math.Max(28, Width / 4);
                            int x = animationOffset % (Width + segment) - segment;
                            Rectangle segmentBounds = new Rectangle(x, 2, segment, Math.Max(1, Height - 4));
                            using (GraphicsPath segmentPath = CreateRoundedPath(segmentBounds,
                                Math.Max(2, segmentBounds.Height / 2)))
                                e.Graphics.FillPath(fill, segmentPath);
                        }
                        else
                        {
                            int fillWidth = (int)((Width - 2) * (progressValue / 100.0));
                            if (fillWidth > 0)
                            {
                                Rectangle fillBounds = new Rectangle(1, 2, fillWidth, Math.Max(1, Height - 4));
                                using (GraphicsPath fillPath = CreateRoundedPath(fillBounds,
                                    Math.Max(2, fillBounds.Height / 2)))
                                    e.Graphics.FillPath(fill, fillPath);
                            }
                        }
                    }
                    e.Graphics.Clip = oldClip;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    animationTimer.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class RoundedPanel : Panel
        {
            public Color BorderColor { get; set; }
            public int CornerRadius { get; set; }

            public RoundedPanel()
            {
                BorderColor = Color.Transparent;
                CornerRadius = 10;
                DoubleBuffered = true;
                ResizeRedraw = true;
            }

            protected override void OnResize(EventArgs eventargs)
            {
                base.OnResize(eventargs);
                if (Width <= 0 || Height <= 0)
                    return;
                using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius))
                    Region = new Region(path);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (BorderColor == Color.Transparent || Width <= 1 || Height <= 1)
                    return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
                using (Pen pen = new Pen(BorderColor))
                    e.Graphics.DrawPath(pen, path);
            }
        }

        private sealed class UpdateLogoMark : Control
        {
            public UpdateLogoMark()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                    ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width < 8 || Height < 8)
                    return;

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle tile = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath tilePath = CreateRoundedPath(tile, 9))
                using (LinearGradientBrush tileFill = new LinearGradientBrush(tile,
                    Color.FromArgb(248, 249, 250), Color.FromArgb(190, 194, 200),
                    LinearGradientMode.Vertical))
                using (Pen tileBorder = new Pen(Color.FromArgb(238, 240, 243)))
                {
                    e.Graphics.FillPath(tileFill, tilePath);
                    e.Graphics.DrawPath(tileBorder, tilePath);
                }

                float scale = Math.Min(Width, Height) / 36F;
                RectangleF ring = new RectangleF(7.5F * scale, 7.5F * scale,
                    20F * scale, 20F * scale);
                using (Pen arrow = new Pen(Color.FromArgb(27, 29, 32), Math.Max(1.7F, 2.05F * scale)))
                {
                    arrow.StartCap = LineCap.Round;
                    arrow.EndCap = LineCap.Round;
                    arrow.LineJoin = LineJoin.Round;
                    DrawArcArrow(e.Graphics, arrow, ring, 205F, 125F, scale);
                    DrawArcArrow(e.Graphics, arrow, ring, 25F, 125F, scale);
                }

                PointF[] checkPoints =
                {
                    new PointF(13F * scale, 18F * scale),
                    new PointF(16.3F * scale, 21.2F * scale),
                    new PointF(22.8F * scale, 13.9F * scale)
                };
                using (Pen check = new Pen(Color.FromArgb(27, 29, 32), Math.Max(1.5F, 1.75F * scale)))
                {
                    check.StartCap = LineCap.Round;
                    check.EndCap = LineCap.Round;
                    check.LineJoin = LineJoin.Round;
                    e.Graphics.DrawLines(check, checkPoints);
                }
            }

            private static void DrawArcArrow(Graphics graphics, Pen pen, RectangleF bounds,
                float startAngle, float sweepAngle, float scale)
            {
                graphics.DrawArc(pen, bounds, startAngle, sweepAngle);
                double radians = (startAngle + sweepAngle) * Math.PI / 180.0;
                float tipX = bounds.Left + bounds.Width / 2F + bounds.Width / 2F * (float)Math.Cos(radians);
                float tipY = bounds.Top + bounds.Height / 2F + bounds.Height / 2F * (float)Math.Sin(radians);
                float directionX = -(float)Math.Sin(radians);
                float directionY = (float)Math.Cos(radians);
                if (sweepAngle < 0F)
                {
                    directionX = -directionX;
                    directionY = -directionY;
                }
                float length = 4.2F * scale;
                float halfWidth = 2.2F * scale;
                float baseX = tipX - directionX * length;
                float baseY = tipY - directionY * length;
                float normalX = -directionY;
                float normalY = directionX;
                graphics.DrawLine(pen, tipX, tipY,
                    baseX + normalX * halfWidth, baseY + normalY * halfWidth);
                graphics.DrawLine(pen, tipX, tipY,
                    baseX - normalX * halfWidth, baseY - normalY * halfWidth);
            }
        }

        private sealed class DarkComboBoxRenderer : NativeWindow
        {
            private const int WM_PAINT = 0x000F;
            private const int WM_NCPAINT = 0x0085;
            private readonly ComboBox comboBox;
            private readonly Color background;
            private readonly Color border;
            private readonly Color arrow;

            public DarkComboBoxRenderer(ComboBox comboBox, Color background, Color border, Color arrow)
            {
                this.comboBox = comboBox;
                this.background = background;
                this.border = border;
                this.arrow = arrow;
                comboBox.HandleCreated += comboBox_HandleCreated;
                comboBox.HandleDestroyed += comboBox_HandleDestroyed;
                comboBox.EnabledChanged += comboBox_StateChanged;
                if (comboBox.IsHandleCreated)
                {
                    AssignHandle(comboBox.Handle);
                    SetWindowTheme(comboBox.Handle, "", "");
                }
            }

            private void comboBox_HandleCreated(object sender, EventArgs e)
            {
                AssignHandle(comboBox.Handle);
                SetWindowTheme(comboBox.Handle, "", "");
            }

            private void comboBox_HandleDestroyed(object sender, EventArgs e)
            {
                ReleaseHandle();
            }

            private void comboBox_StateChanged(object sender, EventArgs e)
            {
                comboBox.Invalidate();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WM_PAINT || m.Msg == WM_NCPAINT)
                    DrawArrowArea();
            }

            private void DrawArrowArea()
            {
                if (Handle == IntPtr.Zero || comboBox.Width <= 0 || comboBox.Height <= 0)
                    return;

                int arrowWidth = Math.Min(22, comboBox.Width);
                Rectangle area = new Rectangle(comboBox.Width - arrowWidth, 1, arrowWidth - 1, comboBox.Height - 2);
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                using (SolidBrush fill = new SolidBrush(background))
                using (Pen borderPen = new Pen(border))
                using (SolidBrush arrowBrush = new SolidBrush(comboBox.Enabled ? arrow : Color.FromArgb(86, 98, 119)))
                {
                    graphics.FillRectangle(fill, area);
                    graphics.DrawRectangle(borderPen, 0, 0,
                        Math.Max(0, comboBox.Width - 1), Math.Max(0, comboBox.Height - 1));
                    graphics.DrawLine(borderPen, area.Left, area.Top, area.Left, area.Bottom);
                    Point center = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2 + 1);
                    Point[] points = new Point[]
                    {
                        new Point(center.X - 4, center.Y - 2),
                        new Point(center.X + 4, center.Y - 2),
                        new Point(center.X, center.Y + 2)
                    };
                    graphics.FillPolygon(arrowBrush, points);
                }
            }
        }

        private void modernLogButton_Click(object sender, EventArgs e)
        {
            modernLogVisible = !modernLogVisible;
            panelList.RowStyles[3].Height = modernLogVisible ? 132F : 0F;
            modernLogButton.Text = modernLogVisible ? "Ocultar actividad" : "Ver actividad";
            UpdateModernEmptyState();
        }

        private void ShowSettingsPage()
        {
            modernSettingsVisible = true;
            packageUpdatesVisible = false;
            suspendChange = true;
            btnWinUpd.Checked = false;
            btnInstalled.Checked = false;
            btnHidden.Checked = false;
            btnHistory.Checked = false;
            suspendChange = false;
            if (modernSettingsButton != null)
                modernSettingsButton.Checked = true;
            if (modernPackageUpdatesButton != null)
                modernPackageUpdatesButton.Checked = false;
            UpdateModernPage();
            UpdateModernEmptyState();
        }

        private void ShowUpdatesPage()
        {
            modernSettingsVisible = false;
            packageUpdatesVisible = false;
            if (modernSettingsButton != null)
                modernSettingsButton.Checked = false;
            if (modernPackageUpdatesButton != null)
                modernPackageUpdatesButton.Checked = false;
            UpdateModernPage();
            UpdateModernEmptyState();
        }

        private void UpdateModernPage()
        {
            if (modernPageTitle == null)
                return;

            if (modernSettingsVisible)
            {
                modernPageTitle.Text = "Configuración";
                modernPageSubtitle.Text = "Ajusta cómo WinSlim busca actualizaciones y controla el comportamiento de Windows Update.";
                if (modernUpdatePage != null)
                    modernUpdatePage.Visible = false;
                if (modernSettingsPage != null)
                {
                    modernSettingsPage.Visible = true;
                    modernSettingsPage.BringToFront();
                }
                if (modernPackageUpdatesPage != null)
                    modernPackageUpdatesPage.Visible = false;
                return;
            }

            if (packageUpdatesVisible)
            {
                modernPageTitle.Text = "Actualizaciones de paquetes";
                modernPageSubtitle.Text = "Detecta y actualiza aplicaciones instaladas con WinGet, de forma independiente a Windows Update.";
                if (modernUpdatePage != null)
                    modernUpdatePage.Visible = false;
                if (modernSettingsPage != null)
                    modernSettingsPage.Visible = false;
                if (modernPackageUpdatesPage != null)
                {
                    modernPackageUpdatesPage.Visible = true;
                    modernPackageUpdatesPage.BringToFront();
                }
                return;
            }

            if (modernSettingsPage != null)
                modernSettingsPage.Visible = false;
            if (modernPackageUpdatesPage != null)
                modernPackageUpdatesPage.Visible = false;
            if (modernUpdatePage != null)
            {
                modernUpdatePage.Visible = true;
                modernUpdatePage.BringToFront();
            }

            bool pending = CurrentList == UpdateLists.PendingUpdates;
            bool installed = CurrentList == UpdateLists.InstaledUpdates;
            bool hidden = CurrentList == UpdateLists.HiddenUpdates;

            if (pending)
            {
                modernPageTitle.Text = "Actualizaciones disponibles";
                modernPageSubtitle.Text = "Elige qué actualizaciones y controladores instalar. En WinSlim no instalamos nada sin tu permiso.";
            }
            else if (installed)
            {
                modernPageTitle.Text = "Actualizaciones instaladas";
                modernPageSubtitle.Text = "Consulta lo que ya está instalado y desinstala una actualización si fuera necesario.";
            }
            else if (hidden)
            {
                modernPageTitle.Text = "Actualizaciones ocultas";
                modernPageSubtitle.Text = "Estas actualizaciones no se instalarán. Puedes volver a mostrarlas cuando quieras.";
            }
            else
            {
                modernPageTitle.Text = "Historial de actualizaciones";
                modernPageSubtitle.Text = "Revisa el resultado de las instalaciones anteriores.";
            }

            btnInstall.Visible = pending;
            btnDownload.Visible = pending;
            btnHide.Visible = pending || hidden;
            btnHide.Text = hidden ? "Volver a mostrar" : "Ocultar";
            btnHide.Width = hidden ? 170 : 126;
            btnUnInstall.Visible = installed;
            btnGetLink.Visible = CurrentList != UpdateLists.UpdateHistory;
            chkAll.Visible = CurrentList != UpdateLists.UpdateHistory;
            modernActionHint.Visible = false;
            flowLayoutPanel1.Visible = true;
            btnSearch.Visible = true;

            if (modernSelectionGrid != null)
            {
                modernSelectionGrid.SetCellPosition(chkGrupe,
                    new TableLayoutPanelCellPosition(CurrentList == UpdateLists.UpdateHistory ? 0 : 1, 0));
                modernSelectionGrid.SetCellPosition(modernFilterSurface,
                    new TableLayoutPanelCellPosition(CurrentList == UpdateLists.UpdateHistory ? 1 : 2, 0));
                modernSelectionGrid.SetColumnSpan(modernFilterSurface, CurrentList == UpdateLists.UpdateHistory ? 2 : 1);
            }
        }

        private void UpdateModernSelectionSummary()
        {
            if (modernSelectionSummary == null)
                return;

            int count = GetCheckedItemCount();
            if (CurrentList == UpdateLists.UpdateHistory)
                modernSelectionSummary.Text = updateItems.Count + " registros";
            else if (count == 1)
                modernSelectionSummary.Text = "1 seleccionada";
            else
                modernSelectionSummary.Text = count + " seleccionadas";

            modernSelectionSummary.ForeColor = count > 0 ? UiSuccess : UiMuted;
        }

        private void UpdateModernEmptyState()
        {
            if (modernUpdateList == null)
                return;

            bool empty = updateItems.Count == 0 && !modernLogVisible
                && !modernSettingsVisible && !packageUpdatesVisible;
            if (!empty)
            {
                modernUpdateList.Invalidate();
                return;
            }

            switch (CurrentList)
            {
                case UpdateLists.PendingUpdates:
                    modernUpdateList.SetEmptyMessage("Todo está al día", "No se encontraron actualizaciones pendientes.");
                    break;
                case UpdateLists.InstaledUpdates:
                    modernUpdateList.SetEmptyMessage("No hay actualizaciones instaladas", "Las actualizaciones instaladas aparecerán aquí.");
                    break;
                case UpdateLists.HiddenUpdates:
                    modernUpdateList.SetEmptyMessage("No hay actualizaciones ocultas", "Las actualizaciones que ocultes aparecerán aquí.");
                    break;
                default:
                    modernUpdateList.SetEmptyMessage("El historial está vacío", "Todavía no hay actividad que mostrar.");
                    break;
            }
        }
    }
}
