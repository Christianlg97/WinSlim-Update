using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace wumgr
{
    public partial class WuMgr
    {
        private const string OtaReleasesApi = "https://api.github.com/repos/Christianlg97/WinSlim11_OTAs/releases?per_page=100";
        private const string OtaRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\OEMInformation";
        private const string OtaRegistryValue = "OTAManifestVersion";

        private readonly List<OtaReleaseInfo> otaUpdates = new List<OtaReleaseInfo>();
        private readonly List<ListViewItem> otaUpdateItems = new List<ListViewItem>();
        private CheckBox modernOtaUpdatesButton;
        private Panel modernOtaUpdatesPage;
        private ModernUpdateList modernOtaUpdateList;
        private Button otaRefreshButton;
        private Button otaApplyUpdateButton;
        private Button otaOpenReleaseButton;
        private Label otaInstalledVersionLabel;
        private Label otaStatusLabel;
        private ModernProgressBar otaProgress;
        private CancellationTokenSource otaCancellation;
        private bool otaFeatureAvailable;
        private bool otaUpdatesVisible;
        private bool otaUpdatesLoaded;
        private bool otaOperationBusy;
        private int otaSortColumn = -1;
        private bool otaSortDescending;

        private Panel BuildOtaUpdatesPage()
        {
            TableLayoutPanel page = new TableLayoutPanel();
            page.Dock = DockStyle.Fill;
            page.Margin = Padding.Empty;
            page.Padding = Padding.Empty;
            page.BackColor = UiBackground;
            page.ColumnCount = 1;
            page.RowCount = 3;
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            page.Controls.Add(BuildOtaActionBar(), 0, 0);
            page.Controls.Add(BuildOtaListSurface(), 0, 1);
            page.Controls.Add(BuildOtaStatusBar(), 0, 2);
            page.Visible = false;
            return page;
        }

        private Control BuildOtaActionBar()
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
            actions.WrapContents = false;
            actions.BackColor = UiSurface;

            otaRefreshButton = new Button();
            StyleActionButton(otaRefreshButton, "Buscar OTAs", UiAccent, Color.FromArgb(24, 24, 24), 140);
            otaRefreshButton.Height = 36;
            otaRefreshButton.Margin = new Padding(0, 0, 8, 0);
            otaRefreshButton.Click += async delegate { await RefreshOtaUpdatesAsync(); };

            otaApplyUpdateButton = new Button();
            StyleActionButton(otaApplyUpdateButton, "Aplicar actualización", UiAccent, Color.FromArgb(24, 24, 24), 180);
            otaApplyUpdateButton.Height = 36;
            otaApplyUpdateButton.Margin = new Padding(0, 0, 8, 0);
            otaApplyUpdateButton.Visible = false;
            otaApplyUpdateButton.Enabled = false;
            otaApplyUpdateButton.Click += async delegate { await ApplySelectedOtaAsync(); };

            otaOpenReleaseButton = new Button();
            StyleSecondaryActionButton(otaOpenReleaseButton, "Ver en GitHub", UiAccent);
            otaOpenReleaseButton.Width = 150;
            otaOpenReleaseButton.Enabled = false;
            otaOpenReleaseButton.Click += delegate { OpenSelectedOtaRelease(); };

            otaInstalledVersionLabel = CreateLabel("Versión instalada: comprobando...", 9F, FontStyle.Regular, UiMuted);
            otaInstalledVersionLabel.AutoSize = false;
            otaInstalledVersionLabel.AutoEllipsis = true;
            otaInstalledVersionLabel.Size = new Size(315, 36);
            otaInstalledVersionLabel.TextAlign = ContentAlignment.MiddleLeft;
            otaInstalledVersionLabel.Margin = new Padding(14, 0, 0, 0);

            actions.Controls.Add(otaRefreshButton);
            actions.Controls.Add(otaApplyUpdateButton);
            actions.Controls.Add(otaOpenReleaseButton);
            actions.Controls.Add(otaInstalledVersionLabel);
            surface.Controls.Add(actions);
            return surface;
        }

        private Control BuildOtaListSurface()
        {
            modernOtaUpdateList = new ModernUpdateList();
            modernOtaUpdateList.Name = "modernOtaUpdateList";
            modernOtaUpdateList.AccessibleName = "Lista de actualizaciones OTA de WinSlim";
            modernOtaUpdateList.AccessibleRole = AccessibleRole.List;
            modernOtaUpdateList.Dock = DockStyle.Fill;
            modernOtaUpdateList.SetHeaders("Actualización", "Versión", "Fecha", "Archivo", "Tamaño", "Estado");
            modernOtaUpdateList.SetColumnWidths(280, 100, 110, 260, 95, 155);
            modernOtaUpdateList.SetEmptyMessage("WinSlim está al día", "No hay ninguna OTA posterior a la versión instalada.");
            modernOtaUpdateList.SelectedItemChanged += otaUpdateList_SelectedItemChanged;
            modernOtaUpdateList.ColumnClicked += otaUpdateList_ColumnClicked;

            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(28, 0, 28, 0);
            card.Padding = new Padding(1);
            card.BackColor = UiSurface;
            card.BorderColor = UiBorder;
            card.CornerRadius = 12;
            card.Controls.Add(modernOtaUpdateList);
            return card;
        }

        private Control BuildOtaStatusBar()
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
            status.ColumnCount = 2;
            status.RowCount = 1;
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));

            otaStatusLabel = CreateLabel("Las OTAs se comprobarán automáticamente al abrir esta sección.", 9F, FontStyle.Regular, UiMuted);
            otaStatusLabel.Dock = DockStyle.Fill;
            otaStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
            otaStatusLabel.AutoEllipsis = true;

            otaProgress = new ModernProgressBar();
            otaProgress.Dock = DockStyle.Fill;
            otaProgress.Margin = new Padding(16, 17, 4, 17);
            otaProgress.Visible = false;
            status.Controls.Add(otaStatusLabel, 0, 0);
            status.Controls.Add(otaProgress, 1, 0);
            surface.Controls.Add(status);
            return surface;
        }

        private void ShowOtaUpdatesPage()
        {
            if (!otaFeatureAvailable || !HasOtaManifestRegistration())
            {
                otaUpdatesVisible = false;
                return;
            }
            otaUpdatesVisible = true;
            packageUpdatesVisible = false;
            modernSettingsVisible = false;
            suspendChange = true;
            btnWinUpd.Checked = btnInstalled.Checked = btnHidden.Checked = btnHistory.Checked = false;
            suspendChange = false;
            if (modernSettingsButton != null) modernSettingsButton.Checked = false;
            if (modernPackageUpdatesButton != null) modernPackageUpdatesButton.Checked = false;
            if (modernOtaUpdatesButton != null) modernOtaUpdatesButton.Checked = true;
            UpdateModernPage();

            if (!otaUpdatesLoaded && !otaOperationBusy)
                BeginInvoke(new MethodInvoker(async delegate { await RefreshOtaUpdatesAsync(); }));
        }

        private async Task RefreshOtaUpdatesAsync()
        {
            if (otaOperationBusy) return;
            otaCancellation = new CancellationTokenSource();
            SetOtaBusy(true, "Buscando actualizaciones OTA de WinSlim...");
            try
            {
                string installedTag = ReadInstalledOtaTag();
                OtaVersion installedVersion;
                bool hasInstalledVersion = OtaVersion.TryParse(installedTag, out installedVersion);
                otaInstalledVersionLabel.Text = hasInstalledVersion
                    ? "Versión instalada: " + installedTag
                    : "Versión instalada: no detectada";

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (HttpClient client = new HttpClient())
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, OtaReleasesApi))
                {
                    request.Headers.UserAgent.ParseAdd("WinSlim-Update/" + Program.mVersion);
                    request.Headers.Accept.ParseAdd("application/vnd.github+json");
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, otaCancellation.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("GitHub respondió " + (int)response.StatusCode + " (" + response.ReasonPhrase + ").");
                        using (System.IO.Stream stream = await response.Content.ReadAsStreamAsync())
                        {
                            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<GitHubRelease>));
                            List<GitHubRelease> releases = (List<GitHubRelease>)serializer.ReadObject(stream);
                            List<OtaReleaseInfo> valid = (releases ?? new List<GitHubRelease>())
                                .Where(item => !item.Draft && !item.Prerelease)
                                .Select(OtaReleaseInfo.FromGitHubRelease)
                                .Where(item => item != null && (!hasInstalledVersion || item.Version.CompareTo(installedVersion) > 0))
                                .OrderByDescending(item => item.Version)
                                .ToList();

                            otaUpdates.Clear();
                            otaUpdates.AddRange(valid);
                        }
                    }
                }

                otaUpdatesLoaded = true;
                RebuildOtaUpdateList();
                otaStatusLabel.Text = otaUpdates.Count == 0
                    ? "No hay OTAs posteriores a la versión instalada."
                    : otaUpdates.Count == 1 ? "Hay 1 OTA disponible." : "Hay " + otaUpdates.Count + " OTAs disponibles.";
                AppLog.Line("GitHub encontró {0} OTAs posteriores a {1}.", otaUpdates.Count,
                    string.IsNullOrWhiteSpace(installedTag) ? "una versión no detectada" : installedTag);
            }
            catch (OperationCanceledException)
            {
                otaStatusLabel.Text = "Búsqueda de OTAs cancelada.";
            }
            catch (Exception exception)
            {
                otaUpdatesLoaded = false;
                otaStatusLabel.Text = "No se pudieron consultar las OTAs: " + exception.Message;
                modernOtaUpdateList.SetEmptyMessage("No se pudo consultar GitHub", exception.Message);
                AppLog.Line("Error al consultar las OTAs: {0}", exception.ToString());
            }
            finally
            {
                if (otaCancellation != null) { otaCancellation.Dispose(); otaCancellation = null; }
                SetOtaBusy(false, otaStatusLabel.Text);
            }
        }

        private void RebuildOtaUpdateList()
        {
            otaUpdateItems.Clear();
            OtaVersion latest = otaUpdates.Count == 0 ? default(OtaVersion) : otaUpdates.Max(item => item.Version);
            foreach (OtaReleaseInfo ota in otaUpdates)
            {
                bool isLatest = ota.Version.CompareTo(latest) == 0;
                string[] columns = {
                    ota.Name, ota.Version.ToString(),
                    ota.PublishedAt.ToLocalTime().ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern),
                    ota.AssetName, FileOps.FormatSize(ota.AssetSize), isLatest ? "Última disponible" : "Disponible"
                };
                ListViewItem item = new ListViewItem(columns);
                item.Tag = ota;
                item.SubItems[2].Tag = ota.PublishedAt;
                item.SubItems[4].Tag = (decimal)ota.AssetSize;
                otaUpdateItems.Add(item);
            }
            modernOtaUpdateList.SetEmptyMessage("WinSlim está al día", "No hay ninguna OTA posterior a la versión instalada.");
            modernOtaUpdateList.SetItems(otaUpdateItems, false, false);
            if (otaSortColumn >= 0) modernOtaUpdateList.SortByColumn(otaSortColumn, otaSortDescending);
            bool hasUpdates = otaUpdateItems.Count > 0;
            otaApplyUpdateButton.Visible = hasUpdates;
            otaApplyUpdateButton.Enabled = false;
            otaOpenReleaseButton.Enabled = false;
            if (hasUpdates)
                modernOtaUpdateList.SelectItem(otaUpdateItems[0]);
        }

        private void otaUpdateList_SelectedItemChanged(object sender, ModernUpdateList.ItemEventArgs e)
        {
            bool validSelection = e.Item != null && e.Item.Tag is OtaReleaseInfo;
            otaOpenReleaseButton.Enabled = !otaOperationBusy && validSelection;
            otaApplyUpdateButton.Enabled = !otaOperationBusy && validSelection;
        }

        private void otaUpdateList_ColumnClicked(object sender, ModernUpdateList.ColumnEventArgs e)
        {
            if (otaSortColumn == e.Column) otaSortDescending = !otaSortDescending;
            else { otaSortColumn = e.Column; otaSortDescending = false; }
            modernOtaUpdateList.SortByColumn(otaSortColumn, otaSortDescending);
        }

        private void OpenSelectedOtaRelease()
        {
            ListViewItem selected = modernOtaUpdateList.SelectedItem;
            OtaReleaseInfo ota = selected == null ? null : selected.Tag as OtaReleaseInfo;
            if (ota == null || string.IsNullOrWhiteSpace(ota.HtmlUrl)) return;
            try { Process.Start(new ProcessStartInfo(ota.HtmlUrl) { UseShellExecute = true }); }
            catch (Exception exception) { otaStatusLabel.Text = "No se pudo abrir GitHub: " + exception.Message; }
        }

        private async Task ApplySelectedOtaAsync()
        {
            if (otaOperationBusy)
                return;

            ListViewItem selected = modernOtaUpdateList.SelectedItem;
            OtaReleaseInfo ota = selected == null ? null : selected.Tag as OtaReleaseInfo;
            if (ota == null)
            {
                otaStatusLabel.Text = "Selecciona una actualización OTA para aplicarla.";
                return;
            }
            if (string.IsNullOrWhiteSpace(ota.AssetDownloadUrl))
            {
                otaStatusLabel.Text = "La release seleccionada no contiene un ZIP descargable.";
                return;
            }

            string operationDirectory = Path.Combine(Path.GetTempPath(), "WinSlimUpdate", "OTA_" + Guid.NewGuid().ToString("N"));
            string zipName = Path.GetFileName(ota.AssetName);
            if (string.IsNullOrWhiteSpace(zipName) || !zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                zipName = "WinSlim_OTA.zip";
            string zipPath = Path.Combine(operationDirectory, zipName);
            string extractDirectory = Path.Combine(operationDirectory, "Package");
            bool installerCompleted = false;

            otaCancellation = new CancellationTokenSource();
            SetOtaBusy(true, "Descargando " + ota.AssetName + "...");
            try
            {
                Directory.CreateDirectory(operationDirectory);
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (HttpClient client = new HttpClient())
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, ota.AssetDownloadUrl))
                {
                    request.Headers.UserAgent.ParseAdd("WinSlim-Update/" + Program.mVersion);
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, otaCancellation.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException("GitHub respondió " + (int)response.StatusCode + " (" + response.ReasonPhrase + ").");
                        using (Stream source = await response.Content.ReadAsStreamAsync())
                        using (FileStream destination = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            await source.CopyToAsync(destination, 81920, otaCancellation.Token);
                    }
                }

                otaCancellation.Token.ThrowIfCancellationRequested();
                otaStatusLabel.Text = "Extrayendo el paquete de actualización...";
                await Task.Run(delegate { ExtractOtaPackageSafely(zipPath, extractDirectory, otaCancellation.Token); });

                string[] installers = Directory.GetFiles(extractDirectory, "Install_Update.exe", SearchOption.AllDirectories);
                if (installers.Length == 0)
                    throw new FileNotFoundException("El paquete no contiene Install_Update.exe.");

                string installerPath = installers[0];
                otaStatusLabel.Text = "Aplicando WinSlim OTA " + ota.Version + "...";
                ProcessStartInfo startInfo = new ProcessStartInfo(installerPath);
                startInfo.WorkingDirectory = Path.GetDirectoryName(installerPath);
                startInfo.UseShellExecute = true;
                using (Process installer = Process.Start(startInfo))
                {
                    if (installer == null)
                        throw new InvalidOperationException("No se pudo iniciar Install_Update.exe.");
                    await Task.Run(delegate { installer.WaitForExit(); });
                    if (installer.ExitCode != 0)
                        throw new InvalidOperationException("Install_Update.exe terminó con el código " + installer.ExitCode + ".");
                }

                installerCompleted = true;
                otaStatusLabel.Text = "La actualización OTA terminó correctamente.";
                AppLog.Line("Se aplicó la OTA {0} mediante {1}.", ota.Tag, ota.AssetName);
            }
            catch (OperationCanceledException)
            {
                otaStatusLabel.Text = "Aplicación de la OTA cancelada.";
            }
            catch (Exception exception)
            {
                otaStatusLabel.Text = "No se pudo aplicar la OTA: " + exception.Message;
                AppLog.Line("Error al aplicar la OTA {0}: {1}", ota.Tag, exception.ToString());
            }
            finally
            {
                if (otaCancellation != null) { otaCancellation.Dispose(); otaCancellation = null; }
                TryDeleteOtaOperationDirectory(operationDirectory);
                SetOtaBusy(false, otaStatusLabel.Text);
            }

            if (installerCompleted)
            {
                otaUpdatesLoaded = false;
                await RefreshOtaUpdatesAsync();
            }
        }

        private static void ExtractOtaPackageSafely(string zipPath, string destination, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destination);
            string destinationRoot = Path.GetFullPath(destination + Path.DirectorySeparatorChar);
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string outputPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    if (!outputPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("El ZIP contiene una ruta no segura: " + entry.FullName);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(outputPath);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(outputPath);
                    if (!Directory.Exists(parent))
                        Directory.CreateDirectory(parent);
                    using (Stream source = entry.Open())
                    using (FileStream destinationFile = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        source.CopyTo(destinationFile);
                }
            }
        }

        private static void TryDeleteOtaOperationDirectory(string operationDirectory)
        {
            if (string.IsNullOrWhiteSpace(operationDirectory) || !Directory.Exists(operationDirectory))
                return;
            try
            {
                Directory.Delete(operationDirectory, true);
            }
            catch (Exception exception)
            {
                AppLog.Line("No se pudo limpiar la carpeta OTA temporal {0}: {1}", operationDirectory, exception.Message);
            }
        }

        private void SetOtaBusy(bool busy, string status)
        {
            otaOperationBusy = busy;
            otaRefreshButton.Enabled = !busy;
            otaOpenReleaseButton.Enabled = !busy && modernOtaUpdateList.SelectedItem != null;
            otaApplyUpdateButton.Enabled = !busy && modernOtaUpdateList.SelectedItem != null;
            otaProgress.Visible = busy;
            otaProgress.IsMarquee = busy;
            if (!string.IsNullOrWhiteSpace(status)) otaStatusLabel.Text = status;
        }

        private static string ReadInstalledOtaTag()
        {
            string tag;
            return TryReadRegisteredOtaTag(out tag) ? tag : string.Empty;
        }

        private static bool HasOtaManifestRegistration()
        {
            string ignored;
            return TryReadRegisteredOtaTag(out ignored);
        }

        private static bool TryReadRegisteredOtaTag(out string tag)
        {
            tag = string.Empty;
            RegistryView[] views = Environment.Is64BitOperatingSystem
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Registry32 };
            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = baseKey.OpenSubKey(OtaRegistryPath, false))
                    {
                        if (key == null || !key.GetValueNames().Any(name =>
                            string.Equals(name, OtaRegistryValue, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if (key.GetValueKind(OtaRegistryValue) != RegistryValueKind.String)
                            continue;
                        object value = key.GetValue(OtaRegistryValue, string.Empty,
                            RegistryValueOptions.DoNotExpandEnvironmentNames);
                        tag = value == null ? string.Empty : value.ToString().Trim();
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    AppLog.Line("No se pudo comprobar el registro de WinSlim OTA ({0}): {1}", view, exception.Message);
                }
            }
            return false;
        }
    }

    internal sealed class OtaReleaseInfo
    {
        public string Name, Tag, HtmlUrl, AssetName, AssetDownloadUrl;
        public long AssetSize;
        public DateTime PublishedAt;
        public OtaVersion Version;

        public static OtaReleaseInfo FromGitHubRelease(GitHubRelease release)
        {
            OtaVersion version;
            if (release == null || !OtaVersion.TryParse(release.TagName, out version)) return null;
            GitHubAsset asset = release.Assets == null ? null : release.Assets.FirstOrDefault(item =>
                item != null && !string.IsNullOrWhiteSpace(item.Name) && item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            DateTime published;
            if (!DateTime.TryParse(release.PublishedAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out published)) published = DateTime.MinValue;
            return new OtaReleaseInfo {
                Name = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                Tag = release.TagName, HtmlUrl = release.HtmlUrl, Version = version, PublishedAt = published,
                AssetName = asset == null ? "Sin archivo ZIP" : asset.Name,
                AssetDownloadUrl = asset == null ? string.Empty : asset.BrowserDownloadUrl,
                AssetSize = asset == null ? 0 : asset.Size
            };
        }
    }

    internal struct OtaVersion : IComparable<OtaVersion>
    {
        private static readonly Regex Pattern = new Regex(@"^WS11OTA_(\d+)\.(\d+)\.(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        public int Major, Minor, Patch;
        public int CompareTo(OtaVersion other) { int value = Major.CompareTo(other.Major); if (value == 0) value = Minor.CompareTo(other.Minor); return value == 0 ? Patch.CompareTo(other.Patch) : value; }
        public override string ToString() { return Major + "." + Minor + "." + Patch; }
        public static bool TryParse(string tag, out OtaVersion version)
        {
            version = default(OtaVersion);
            Match match = Pattern.Match((tag ?? string.Empty).Trim());
            int major, minor, patch;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out major) || !int.TryParse(match.Groups[2].Value, out minor) || !int.TryParse(match.Groups[3].Value, out patch)) return false;
            version = new OtaVersion { Major = major, Minor = minor, Patch = patch };
            return true;
        }
    }

    [DataContract]
    internal sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")] public string TagName { get; set; }
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
        [DataMember(Name = "published_at")] public string PublishedAt { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; }
    }

    [DataContract]
    internal sealed class GitHubAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "browser_download_url")] public string BrowserDownloadUrl { get; set; }
    }
}
