using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace wumgr
{
    public partial class WuMgr
    {
        private readonly WinGetPackageManager packageManager = new WinGetPackageManager();
        private readonly List<PackageUpdateInfo> packageUpdates = new List<PackageUpdateInfo>();
        private readonly List<ListViewItem> packageUpdateItems = new List<ListViewItem>();

        private CheckBox modernPackageUpdatesButton;
        private Panel modernPackageUpdatesPage;
        private ModernUpdateList modernPackageUpdateList;
        private Button packageRefreshButton;
        private Button packageUpdateSelectedButton;
        private Button packageCancelButton;
        private Button packageSelectAllButton;
        private Button packageGroupBySourceButton;
        private TextBox packageFilter;
        private Label packageSelectionSummary;
        private Label packageStatusLabel;
        private ModernProgressBar packageProgress;
        private CancellationTokenSource packageOperationCancellation;
        private bool packageUpdatesVisible;
        private bool packageUpdatesLoaded;
        private bool packageGroupBySourceEnabled;
        private bool packageOperationBusy;
        private int packageSortColumn = -1;
        private bool packageSortDescending;

        private Panel BuildPackageUpdatesPage()
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
            page.Controls.Add(BuildPackageActionBar(), 0, 0);
            page.Controls.Add(BuildPackageSelectionBar(), 0, 1);
            page.Controls.Add(BuildPackageListSurface(), 0, 2);
            page.Controls.Add(BuildPackageStatusBar(), 0, 3);
            page.Visible = false;
            return page;
        }

        private Control BuildPackageActionBar()
        {
            RoundedPanel surface = new RoundedPanel();
            surface.Dock = DockStyle.Fill;
            surface.Margin = new Padding(28, 0, 28, 10);
            surface.Padding = new Padding(12, 8, 12, 8);
            surface.BackColor = UiSurface;
            surface.BorderColor = UiBorder;
            surface.CornerRadius = 10;

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Margin = Padding.Empty;
            actions.Padding = Padding.Empty;
            actions.WrapContents = false;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.BackColor = UiSurface;

            packageRefreshButton = new Button();
            StyleActionButton(packageRefreshButton, "Buscar paquetes", UiAccent, Color.FromArgb(24, 24, 24), 154);
            packageRefreshButton.Height = 36;
            packageRefreshButton.Margin = new Padding(0, 0, 8, 0);
            packageRefreshButton.Click += async delegate { await RefreshPackageUpdatesAsync(); };

            packageUpdateSelectedButton = new Button();
            StyleSecondaryActionButton(packageUpdateSelectedButton, "Actualizar selección", UiAccent);
            packageUpdateSelectedButton.Width = 184;
            packageUpdateSelectedButton.Click += async delegate { await UpdateSelectedPackagesAsync(); };

            Label separationHint = CreateLabel(
                "Aplicaciones · independiente de Windows Update",
                9F, FontStyle.Regular, UiMuted);
            separationHint.AutoSize = false;
            separationHint.AutoEllipsis = true;
            separationHint.Size = new Size(286, 36);
            separationHint.TextAlign = ContentAlignment.MiddleLeft;
            separationHint.Margin = new Padding(14, 0, 0, 0);

            actions.Controls.Add(packageRefreshButton);
            actions.Controls.Add(packageUpdateSelectedButton);
            actions.Controls.Add(separationHint);
            surface.Controls.Add(actions);
            return surface;
        }

        private Control BuildPackageSelectionBar()
        {
            RoundedPanel surface = new RoundedPanel();
            surface.Dock = DockStyle.Fill;
            surface.Margin = new Padding(28, 0, 28, 8);
            surface.Padding = new Padding(10, 8, 10, 8);
            surface.BackColor = UiSurface;
            surface.BorderColor = UiBorder;
            surface.CornerRadius = 10;

            TableLayoutPanel selection = new TableLayoutPanel();
            selection.Dock = DockStyle.Fill;
            selection.Margin = Padding.Empty;
            selection.Padding = Padding.Empty;
            selection.BackColor = UiSurface;
            selection.ColumnCount = 4;
            selection.RowCount = 1;
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115F));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135F));
            selection.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            packageSelectAllButton = new Button();
            packageSelectAllButton.Text = "Todos";
            StylePackageSelectionToggle(packageSelectAllButton);
            toolTip.SetToolTip(packageSelectAllButton, "Seleccionar o desmarcar todos los paquetes visibles");
            packageSelectAllButton.Click += packageSelectAllButton_Click;

            packageGroupBySourceButton = new Button();
            packageGroupBySourceButton.Text = "Por fuente";
            StylePackageSelectionToggle(packageGroupBySourceButton);
            toolTip.SetToolTip(packageGroupBySourceButton, "Agrupar los paquetes por su fuente de WinGet");
            packageGroupBySourceButton.Click += packageGroupBySourceButton_Click;

            packageFilter = new TextBox();
            packageFilter.Dock = DockStyle.Fill;
            packageFilter.Margin = new Padding(12, 6, 4, 6);
            packageFilter.AutoSize = false;
            packageFilter.BorderStyle = BorderStyle.None;
            packageFilter.Font = new Font("Segoe UI", 10F);
            packageFilter.BackColor = UiInput;
            packageFilter.ForeColor = UiText;
            packageFilter.HandleCreated += delegate
            {
                SendTextMessage(packageFilter.Handle, 0x1501, new IntPtr(1), "Filtrar por nombre, ID o versión");
            };
            packageFilter.TextChanged += delegate { RebuildPackageUpdateList(); };

            RoundedPanel filterSurface = new RoundedPanel();
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
            Label filterIcon = CreateLabel("\uE721", 11F, FontStyle.Regular, UiMuted);
            filterIcon.Font = new Font("Segoe Fluent Icons", 11F, FontStyle.Regular);
            filterIcon.Dock = DockStyle.Fill;
            filterIcon.TextAlign = ContentAlignment.MiddleCenter;
            filterLayout.Controls.Add(packageFilter, 0, 0);
            filterLayout.Controls.Add(filterIcon, 1, 0);
            filterSurface.Controls.Add(filterLayout);

            packageSelectionSummary = CreateLabel("0 seleccionados", 9F, FontStyle.Regular, UiMuted);
            packageSelectionSummary.Dock = DockStyle.Fill;
            packageSelectionSummary.TextAlign = ContentAlignment.MiddleRight;

            selection.Controls.Add(packageSelectAllButton, 0, 0);
            selection.Controls.Add(packageGroupBySourceButton, 1, 0);
            selection.Controls.Add(filterSurface, 2, 0);
            selection.Controls.Add(packageSelectionSummary, 3, 0);
            surface.Controls.Add(selection);
            return surface;
        }

        private void StylePackageSelectionToggle(Button button)
        {
            button.Dock = DockStyle.Fill;
            button.AutoSize = false;
            button.Margin = new Padding(2, 0, 2, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiHover;
            button.BackColor = UiSurface;
            button.ForeColor = UiText;
            button.Font = new Font("Segoe UI", 8.8F, FontStyle.Regular);
            button.Padding = new Padding(9, 0, 4, 0);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        private Control BuildPackageListSurface()
        {
            modernPackageUpdateList = new ModernUpdateList();
            modernPackageUpdateList.Name = "modernPackageUpdateList";
            modernPackageUpdateList.AccessibleName = "Lista de actualizaciones de paquetes";
            modernPackageUpdateList.AccessibleRole = AccessibleRole.List;
            modernPackageUpdateList.Dock = DockStyle.Fill;
            modernPackageUpdateList.Margin = Padding.Empty;
            modernPackageUpdateList.SetHeaders(
                "Nombre del paquete", "ID del paquete", "Versión", "Nueva versión", "Fuente", "Estado");
            modernPackageUpdateList.SetColumnWidths(240, 190, 105, 110, 82, 145);
            modernPackageUpdateList.SetEmptyMessage(
                "Todo está al día", "No se encontraron actualizaciones de paquetes.");
            modernPackageUpdateList.ItemCheckedChanged += packageUpdateList_ItemCheckedChanged;
            modernPackageUpdateList.ColumnClicked += packageUpdateList_ColumnClicked;

            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(28, 0, 28, 0);
            card.Padding = new Padding(1);
            card.BackColor = UiSurface;
            card.BorderColor = UiBorder;
            card.CornerRadius = 12;
            card.Controls.Add(modernPackageUpdateList);
            return card;
        }

        private Control BuildPackageStatusBar()
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
            status.ColumnCount = 4;
            status.RowCount = 1;
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));

            packageStatusLabel = CreateLabel("Pulsa «Buscar paquetes» para comprobar WinGet.", 9F, FontStyle.Regular, UiMuted);
            packageStatusLabel.Dock = DockStyle.Fill;
            packageStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            packageStatusLabel.AutoEllipsis = true;

            packageProgress = new ModernProgressBar();
            packageProgress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            packageProgress.Height = 10;
            packageProgress.Margin = new Padding(0, 0, 12, 0);
            packageProgress.Visible = false;

            packageCancelButton = new Button();
            StyleSecondaryActionButton(packageCancelButton, "Cancelar", UiDanger);
            packageCancelButton.Anchor = AnchorStyles.None;
            packageCancelButton.Size = new Size(96, 32);
            packageCancelButton.MinimumSize = new Size(96, 32);
            packageCancelButton.MaximumSize = new Size(96, 32);
            packageCancelButton.Margin = new Padding(2, 0, 8, 0);
            packageCancelButton.Visible = false;
            packageCancelButton.Click += delegate { CancelPackageOperation(); };

            Label engine = CreateLabel("Motor: WinGet", 8.8F, FontStyle.Bold, UiMuted);
            engine.Dock = DockStyle.Fill;
            engine.TextAlign = ContentAlignment.MiddleCenter;

            status.Controls.Add(packageStatusLabel, 0, 0);
            status.Controls.Add(packageProgress, 1, 0);
            status.Controls.Add(packageCancelButton, 2, 0);
            status.Controls.Add(engine, 3, 0);
            surface.Controls.Add(status);
            return surface;
        }

        private void ShowPackageUpdatesPage()
        {
            packageUpdatesVisible = true;
            modernSettingsVisible = false;
            suspendChange = true;
            btnWinUpd.Checked = false;
            btnInstalled.Checked = false;
            btnHidden.Checked = false;
            btnHistory.Checked = false;
            suspendChange = false;
            if (modernSettingsButton != null)
                modernSettingsButton.Checked = false;
            if (modernPackageUpdatesButton != null)
                modernPackageUpdatesButton.Checked = true;
            UpdateModernPage();

            if (!packageUpdatesLoaded && !packageOperationBusy)
                BeginInvoke(new MethodInvoker(async delegate { await RefreshPackageUpdatesAsync(); }));
        }

        private async Task RefreshPackageUpdatesAsync()
        {
            if (packageOperationBusy)
                return;

            packageOperationCancellation = new CancellationTokenSource();
            SetPackageOperationBusy(true, "Buscando actualizaciones de paquetes...");
            try
            {
                WinGetQueryResult result = await packageManager.FindAvailableUpdatesAsync(
                    packageOperationCancellation.Token);
                if (!result.Succeeded)
                {
                    packageStatusLabel.Text = result.ErrorMessage;
                    modernPackageUpdateList.SetEmptyMessage(
                        "No se pudo consultar WinGet", result.ErrorMessage);
                    AppLog.Line("WinGet: {0}\r\n{1}", result.ErrorMessage, result.DiagnosticOutput);
                    packageUpdatesLoaded = false;
                    return;
                }

                packageUpdates.Clear();
                packageUpdates.AddRange(result.Packages);
                packageUpdatesLoaded = true;
                modernPackageUpdateList.SetEmptyMessage(
                    "Todo está al día", "No se encontraron actualizaciones de paquetes.");
                RebuildPackageUpdateList();
                packageStatusLabel.Text = packageUpdates.Count == 0
                    ? "Todos los paquetes están actualizados."
                    : packageUpdates.Count == 1
                        ? "Se encontró 1 actualización de paquete."
                        : "Se encontraron " + packageUpdates.Count + " actualizaciones de paquetes.";
                AppLog.Line("WinGet encontró {0} actualizaciones de paquetes.", packageUpdates.Count);
            }
            catch (OperationCanceledException)
            {
                packageStatusLabel.Text = "Búsqueda de paquetes cancelada.";
            }
            catch (Exception exception)
            {
                packageStatusLabel.Text = "No se pudo consultar WinGet: " + exception.Message;
                modernPackageUpdateList.SetEmptyMessage(
                    "No se pudo consultar WinGet", exception.Message);
                AppLog.Line("Error al consultar WinGet: {0}", exception.ToString());
                packageUpdatesLoaded = false;
            }
            finally
            {
                DisposePackageCancellation();
                SetPackageOperationBusy(false, packageStatusLabel.Text);
                UpdatePackageNavigationText();
            }
        }

        private async Task UpdateSelectedPackagesAsync()
        {
            if (packageOperationBusy)
                return;

            if (!MiscFunc.IsAdministrator())
            {
                MessageBox.Show(
                    "WinSlim Update debe ejecutarse como administrador para instalar actualizaciones de paquetes.",
                    Program.mName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                packageStatusLabel.Text = "Se necesitan permisos de administrador para actualizar paquetes.";
                return;
            }

            List<PackageUpdateInfo> selected = packageUpdates.Where(item => item.Selected).ToList();
            if (selected.Count == 0)
            {
                packageStatusLabel.Text = "Selecciona al menos un paquete para actualizar.";
                return;
            }

            packageOperationCancellation = new CancellationTokenSource();
            SetPackageOperationBusy(true, "Preparando actualizaciones de paquetes...");
            int succeeded = 0;
            int failed = 0;
            int completed = 0;
            try
            {
                foreach (PackageUpdateInfo package in selected)
                {
                    bool packageFinished = false;
                    while (!packageFinished)
                    {
                        packageOperationCancellation.Token.ThrowIfCancellationRequested();
                        package.Status = "Actualizando...";
                        packageStatusLabel.Text = "Actualizando " + package.Name + " ("
                            + (completed + 1) + " de " + selected.Count + ")...";
                        RebuildPackageUpdateList();

                        WinGetOperationResult result = await packageManager.UpdatePackageAsync(
                            package, packageOperationCancellation.Token);
                        if (result.Succeeded)
                        {
                            succeeded++;
                            package.Status = "Actualizado";
                            package.Selected = false;
                            packageFinished = true;
                            AppLog.Line("WinGet actualizó {0} ({1}) a {2}.",
                                package.Name, package.Id, package.AvailableVersion);
                        }
                        else
                        {
                            package.Status = "Error";
                            packageStatusLabel.Text = "No se pudo actualizar " + package.Name + ".";
                            RebuildPackageUpdateList();
                            AppLog.Line("WinGet no pudo actualizar {0} ({1}): {2}\r\n{3}",
                                package.Name, package.Id, result.FailureReason, result.DiagnosticOutput);

                            DialogResult choice;
                            using (PackageUpdateErrorDialog dialog =
                                new PackageUpdateErrorDialog(package, result))
                            {
                                choice = dialog.ShowDialog(this);
                            }
                            if (choice == DialogResult.Retry)
                            {
                                package.Status = "Reintentando...";
                                packageStatusLabel.Text = "Reintentando " + package.Name + "...";
                                RebuildPackageUpdateList();
                                continue;
                            }

                            failed++;
                            packageFinished = true;
                        }
                    }
                    completed++;
                    RebuildPackageUpdateList();
                }

                packageUpdates.RemoveAll(item => item.Status == "Actualizado");
                RebuildPackageUpdateList();
                packageStatusLabel.Text = BuildPackageOperationSummary(succeeded, failed);
            }
            catch (OperationCanceledException)
            {
                foreach (PackageUpdateInfo package in selected.Where(item => item.Status == "Actualizando..."))
                    package.Status = "Cancelado";
                packageStatusLabel.Text = "Actualización de paquetes cancelada. "
                    + BuildPackageOperationSummary(succeeded, failed);
                RebuildPackageUpdateList();
            }
            catch (Exception exception)
            {
                failed++;
                foreach (PackageUpdateInfo package in selected.Where(item => item.Status == "Actualizando..."))
                    package.Status = "Error";
                packageStatusLabel.Text = "Error al actualizar paquetes: " + exception.Message;
                AppLog.Line("Error al actualizar paquetes con WinGet: {0}", exception.ToString());
                RebuildPackageUpdateList();
            }
            finally
            {
                DisposePackageCancellation();
                SetPackageOperationBusy(false, packageStatusLabel.Text);
                UpdatePackageNavigationText();
            }
        }

        private void RebuildPackageUpdateList()
        {
            if (modernPackageUpdateList == null)
                return;

            List<PackageUpdateInfo> visible = GetFilteredPackageUpdates();
            packageUpdateItems.Clear();
            Dictionary<string, ListViewGroup> groups = new Dictionary<string, ListViewGroup>(
                StringComparer.CurrentCultureIgnoreCase);

            foreach (PackageUpdateInfo package in visible)
            {
                string[] columns =
                {
                    package.Name,
                    package.Id,
                    package.InstalledVersion,
                    package.AvailableVersion,
                    package.Source,
                    package.Status
                };
                ListViewItem item = new ListViewItem(columns);
                item.Checked = package.Selected;
                item.Tag = package;

                if (packageGroupBySourceEnabled)
                {
                    string source = string.IsNullOrWhiteSpace(package.Source) ? "WinGet" : package.Source;
                    ListViewGroup group;
                    if (!groups.TryGetValue(source, out group))
                    {
                        group = new ListViewGroup(source, HorizontalAlignment.Left);
                        group.Name = source;
                        groups.Add(source, group);
                    }
                    item.Group = group;
                }
                packageUpdateItems.Add(item);
            }

            modernPackageUpdateList.SetItems(
                packageUpdateItems,
                packageGroupBySourceEnabled,
                true);
            if (packageSortColumn >= 0)
                modernPackageUpdateList.SortByColumn(packageSortColumn, packageSortDescending);
            UpdatePackageSelectionState(visible);
        }

        private List<PackageUpdateInfo> GetFilteredPackageUpdates()
        {
            string filter = packageFilter == null ? string.Empty : packageFilter.Text.Trim();
            if (filter.Length == 0)
                return new List<PackageUpdateInfo>(packageUpdates);
            return packageUpdates.Where(item =>
                ContainsText(item.Name, filter)
                || ContainsText(item.Id, filter)
                || ContainsText(item.InstalledVersion, filter)
                || ContainsText(item.AvailableVersion, filter)
                || ContainsText(item.Source, filter)).ToList();
        }

        private static bool ContainsText(string value, string search)
        {
            return (value ?? string.Empty).IndexOf(
                search, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void packageSelectAllButton_Click(object sender, EventArgs e)
        {
            List<PackageUpdateInfo> visible = GetFilteredPackageUpdates();
            bool select = visible.Count > 0 && !visible.All(item => item.Selected);
            foreach (PackageUpdateInfo package in visible)
                package.Selected = select;
            RebuildPackageUpdateList();
        }

        private void packageGroupBySourceButton_Click(object sender, EventArgs e)
        {
            packageGroupBySourceEnabled = !packageGroupBySourceEnabled;
            RebuildPackageUpdateList();
        }

        private void packageUpdateList_ItemCheckedChanged(object sender, ModernUpdateList.ItemEventArgs e)
        {
            PackageUpdateInfo package = e.Item == null ? null : e.Item.Tag as PackageUpdateInfo;
            if (package != null)
                package.Selected = e.Item.Checked;
            UpdatePackageSelectionState(GetFilteredPackageUpdates());
        }

        private void packageUpdateList_ColumnClicked(object sender, ModernUpdateList.ColumnEventArgs e)
        {
            if (packageSortColumn == e.Column)
                packageSortDescending = !packageSortDescending;
            else
            {
                packageSortColumn = e.Column;
                packageSortDescending = false;
            }
            modernPackageUpdateList.SortByColumn(packageSortColumn, packageSortDescending);
        }

        private void UpdatePackageSelectionState(IList<PackageUpdateInfo> visible)
        {
            int selected = packageUpdates.Count(item => item.Selected);
            bool allVisibleSelected = visible.Count > 0 && visible.All(item => item.Selected);
            packageSelectAllButton.Text = allVisibleSelected
                ? "✓  Todos"
                : "Todos";
            packageSelectAllButton.BackColor = allVisibleSelected ? UiHover : UiSurface;
            packageGroupBySourceButton.Text = packageGroupBySourceEnabled
                ? "✓  Por fuente"
                : "Por fuente";
            packageGroupBySourceButton.BackColor = packageGroupBySourceEnabled ? UiHover : UiSurface;

            packageSelectionSummary.Text = selected == 1
                ? "1 seleccionado"
                : selected + " seleccionados";
            packageSelectionSummary.ForeColor = selected > 0 ? UiSuccess : UiMuted;
            if (!packageOperationBusy)
                packageUpdateSelectedButton.Enabled = selected > 0;
        }

        private void SetPackageOperationBusy(bool busy, string status)
        {
            packageOperationBusy = busy;
            if (packageRefreshButton != null)
                packageRefreshButton.Enabled = !busy;
            if (packageUpdateSelectedButton != null)
                packageUpdateSelectedButton.Enabled = !busy && packageUpdates.Any(item => item.Selected);
            if (packageFilter != null)
                packageFilter.Enabled = !busy;
            if (packageSelectAllButton != null)
                packageSelectAllButton.Enabled = !busy;
            if (packageGroupBySourceButton != null)
                packageGroupBySourceButton.Enabled = !busy;
            if (packageCancelButton != null)
                packageCancelButton.Visible = busy;
            if (packageProgress != null)
            {
                packageProgress.Visible = busy;
                packageProgress.IsMarquee = busy;
            }
            if (packageStatusLabel != null && !string.IsNullOrWhiteSpace(status))
                packageStatusLabel.Text = status;
        }

        private void CancelPackageOperation()
        {
            if (packageOperationCancellation != null && !packageOperationCancellation.IsCancellationRequested)
            {
                packageStatusLabel.Text = "Cancelando la operación de WinGet...";
                packageOperationCancellation.Cancel();
            }
        }

        private void DisposePackageCancellation()
        {
            CancellationTokenSource cancellation = packageOperationCancellation;
            packageOperationCancellation = null;
            if (cancellation != null)
                cancellation.Dispose();
        }

        private void UpdatePackageNavigationText()
        {
            if (modernPackageUpdatesButton == null)
                return;
            modernPackageUpdatesButton.Text = "Actualizaciones de\r\npaquetes";
        }

        private static string BuildPackageOperationSummary(int succeeded, int failed)
        {
            string summary = succeeded == 1
                ? "1 paquete actualizado"
                : succeeded + " paquetes actualizados";
            if (failed > 0)
                summary += failed == 1 ? "; 1 error." : "; " + failed + " errores.";
            else
                summary += ".";
            return summary;
        }
    }
}
