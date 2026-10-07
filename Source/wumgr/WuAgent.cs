using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WUApiLib;//this is required to use the Interfaces given by microsoft. 
using System.Threading;
using System.Windows.Threading;
using System.IO;
using System.Net;
using System.ComponentModel;
using System.Windows.Forms;
using System.ServiceProcess;
using System.Collections.Specialized;
using System.Globalization;
using System.Diagnostics;

namespace wumgr
{
    class WuAgent
    {
        UpdateSession mUpdateSession = null;
        UpdateServiceManager mUpdateServiceManager = null;
        IUpdateService mOfflineService = null;
        IUpdateSearcher mUpdateSearcher = null;
        ISearchJob mSearchJob = null;
        private Stopwatch searchWatch;
        private TimeSpan nextSearchNotice;
        private CancellationTokenSource searchPreparation;
        WUApiLib.UpdateDownloader mDownloader = null;
        IDownloadJob mDownloadJob = null;
        IUpdateInstaller mInstaller = null;
        IInstallationJob mInstalationJob = null;

        public List<MsUpdate> mUpdateHistory = new List<MsUpdate>();
        public List<MsUpdate> mPendingUpdates = new List<MsUpdate>();
        public List<MsUpdate> mInstalledUpdates = new List<MsUpdate>();
        public List<MsUpdate> mHiddenUpdates = new List<MsUpdate>();

        protected Dispatcher mDispatcher = null;
        private SynchronizationContext searchUiContext;
        private ISearchJob searchPollJob;
        private DateTime nextSearchPoll;

        public void PollSearchCompletion()
        {
            ISearchJob job = mSearchJob;
            if (job == null)
                return;
            if (searchWatch != null && searchWatch.Elapsed >= nextSearchNotice)
            {
                AppLog.Line("Windows Update sigue procesando la consulta ({0:F0} s); aún no ha devuelto el resultado.", searchWatch.Elapsed.TotalSeconds);
                nextSearchNotice = searchWatch.Elapsed.Add(TimeSpan.FromSeconds(30));
            }
            if (DateTime.UtcNow < nextSearchPoll ||
                Interlocked.CompareExchange(ref searchPollJob, job, null) != null)
                return;
            nextSearchPoll = DateTime.UtcNow.AddSeconds(1);
            Task.Run(() =>
            {
                try
                {
                    if (job.IsCompleted)
                        PostSearchToUi(() => OnUpdatesFound(job));
                }
                catch (Exception error)
                {
                    PostSearchToUi(() =>
                    {
                        if (job == mSearchJob)
                            AppLog.Line("No se pudo comprobar el estado de búsqueda: " + error.Message);
                    });
                }
                finally { Interlocked.CompareExchange(ref searchPollJob, null, job); }
            });
        }

        public void SetSearchUiContext(SynchronizationContext context)
        {
            searchUiContext = context;
        }

        private void PostSearchToUi(Action action)
        {
            if (searchUiContext != null)
                searchUiContext.Post(delegate { action(); }, null);
            else
                mDispatcher.BeginInvoke(action);
        }

        private void CompleteSearchOnUi(ISearchJob job)
        {
            // Preserve the native callback lifetime while consuming its result.
            // RequestAbort runs off the UI thread, so this handoff cannot block
            // a UI-thread abort waiting for the same callback.
            if (searchUiContext != null)
                searchUiContext.Send(delegate { OnUpdatesFound(job); }, null);
            else
                mDispatcher.Invoke(new Action(() => OnUpdatesFound(job)));
        }

        public string dlPath = null;

        public int LastInstallationErrorHResult { get; private set; }
        public string LastInstallationErrorDetails { get; private set; }
        public string LastInstallationErrorUpdate { get; private set; }

        public System.Collections.Specialized.StringCollection mServiceList = new System.Collections.Specialized.StringCollection();

        private static WuAgent mInstance = null;
        public static WuAgent GetInstance() { return mInstance; }

        UpdateDownloader mUpdateDownloader = null;
        UpdateInstaller mUpdateInstaller = null;

        public WuAgent()
        {
            mInstance = this;
            mDispatcher = Dispatcher.CurrentDispatcher;

            mUpdateDownloader = new UpdateDownloader();
            mUpdateDownloader.Finished += DownloadsFinished;
            mUpdateDownloader.Progress += DownloadProgress;


            mUpdateInstaller = new UpdateInstaller();
            mUpdateInstaller.Finished += InstallFinished;
            mUpdateInstaller.ItemFinished += InstallItemFinished;
            mUpdateInstaller.Progress += InstallProgress;

            dlPath = Program.wrkPath + @"\Updates";

            WindowsUpdateAgentInfo info = new WindowsUpdateAgentInfo();
            var currentVersion = info.GetInfo("ApiMajorVersion").ToString().Trim() + "." + info.GetInfo("ApiMinorVersion").ToString().Trim() + " (" + info.GetInfo("ProductVersionString").ToString().Trim() + ")";
            AppLog.Line("Versión del agente de Windows Update: {0}", currentVersion);

            mUpdateSession = new UpdateSession();
            mUpdateSession.ClientApplicationID = Program.mName;
            //mUpdateSession.UserLocale = 1033; // alwys show strings in englisch

            mUpdateServiceManager = new UpdateServiceManager();

            if(MiscFunc.parseInt(Program.IniReadValue("Options", "LoadLists", "0")) != 0)
                LoadUpdates();
        }

        public bool Init()
        {
            if (!LoadServices(true))
                return false;
            
            mUpdateSearcher = mUpdateSession.CreateUpdateSearcher();

            UpdateHistory();
            return true;
        }

        public void UnInit()
        {
            ClearOffline();

            mUpdateSearcher = null;
        }

        public bool IsActive()
        {
            return mUpdateSearcher != null;
        }

        public bool IsBusy()
        {
            return mCurOperation != AgentOperation.None;
        }

        public bool LoadServices(bool cleanUp = false)
        {
            try
            {
                Console.WriteLine("Update Services:");
                mServiceList.Clear();
                foreach (IUpdateService service in mUpdateServiceManager.Services)
                {
                    if (service.Name == mMyOfflineSvc)
                    {
                        if (cleanUp)
                        {
                            try
                            {
                                mUpdateServiceManager.RemoveService(service.ServiceID);
                            }
                            catch { }
                        }
                        continue;
                    }

                    Console.WriteLine(service.Name + ": " + service.ServiceID);
                    //AppLog.Line(service.Name + ": " + service.ServiceID);
                    mServiceList.Add(service.Name);
                }

                return true;
            }
            catch (Exception err)
            {
                if((uint)err.HResult != 0x80070422)
                    LogError(err);
                return false;
            }
        }

        private void LogError(Exception error)
        {
            uint errCode = (uint)error.HResult;
            AppLog.Line("Error 0x{0}: {1}", errCode.ToString("X").PadLeft(8,'0'), UpdateErrors.GetErrorStr(errCode));
        }

        private void ClearLastInstallationError()
        {
            LastInstallationErrorHResult = 0;
            LastInstallationErrorDetails = "";
            LastInstallationErrorUpdate = "";
        }

        private void SetLastInstallationError(int hResult, string updateTitle)
        {
            if (LastInstallationErrorHResult != 0)
                return;

            LastInstallationErrorHResult = hResult;
            LastInstallationErrorUpdate = updateTitle ?? "";
            LastInstallationErrorDetails = DescribeInstallationError(unchecked((uint)hResult));
        }

        private static string DescribeInstallationError(uint code)
        {
            switch (code)
            {
                case 0x80073701:
                    return "Faltan uno o varios componentes del almacén de Windows.";
                case 0x80073712:
                    return "El almacén de componentes de Windows está dañado.";
                case 0x800F081F:
                    return "Windows no encuentra los archivos necesarios para reparar o instalar la actualización.";
                case 0x800F0831:
                    return "Falta el manifiesto de un paquete necesario de Windows.";
                case 0x80070002:
                    return "Windows no encuentra uno de los archivos necesarios.";
                case 0x80070005:
                    return "Windows denegó el acceso durante la instalación.";
                case 0x80240016:
                    return "Hay otra instalación en curso o Windows necesita reiniciarse antes de continuar.";
                case 0x80240017:
                    return "La actualización ya no es aplicable a este equipo.";
                case 0x80240022:
                    return "Windows no pudo instalar ninguna de las actualizaciones seleccionadas.";
                default:
                    return "Windows devolvió un error durante la instalación.";
            }
        }

        public static string MsUpdGUID = "7971f918-a847-4430-9279-4a52d1efe18d"; // Microsoft Update
        public static string WinUpdUID = "9482f4b4-e343-43b6-b170-9a65bc822c77"; // Windows Update
        public static string WsUsUID = "3da21691-e39d-4da6-8a4b-b43877bcb1b7"; // Windows Server Update Service

        public static string DCatGUID = "8b24b027-1dee-babb-9a95-3517dfb9c552"; // DCat Flighting Prod - Windows Insider Program
        public static string WinStorGUID = "117cab2d-82b1-4b5a-a08c-4d62dbee7782 "; // Windows Store
        public static string WinStorDCat2GUID = "855e8a7c-ecb4-4ca3-b045-1dfa50104289"; // Windows Store (DCat Prod) - Insider Updates for Store Apps

        public void EnableService(string GUID, bool enable = true)
        {
            if (enable)
                AddService(GUID);
            else
                RemoveService(GUID);
            LoadServices();
        }

        private void AddService(string ID)
        {
            mUpdateServiceManager.AddService2(ID, (int)(tagAddServiceFlag.asfAllowOnlineRegistration | tagAddServiceFlag.asfAllowPendingRegistration | tagAddServiceFlag.asfRegisterServiceWithAU), "");
        }

        private void RemoveService(string ID)
        {
            mUpdateServiceManager.RemoveService(ID);
        }

        public bool TestService(string ID)
        {
            foreach (IUpdateService service in mUpdateServiceManager.Services)
            {
                if (service.ServiceID.Equals(ID))
                    return true;
            }
            return false;
        }

        public string GetServiceName(string ID, bool bAdd = false)
        {
            foreach (IUpdateService service in mUpdateServiceManager.Services)
            {
                if (service.ServiceID.Equals(ID))
                    return service.Name;
            }
            if (bAdd == false)
                return null;
            AddService(ID);
            LoadServices();
            return GetServiceName(ID, false);
        }

        public void UpdateHistory()
        {
            mUpdateHistory.Clear();
            int count = mUpdateSearcher.GetTotalHistoryCount();
            if (count == 0) // sanity check
                return;
            foreach (IUpdateHistoryEntry2 update in mUpdateSearcher.QueryHistory(0, count))
            {
                if (update.Title == null) // sanity check
                    continue;
                mUpdateHistory.Add(new MsUpdate(update));
            }
        }

        public enum RetCodes
        {
            InProgress = 2,
            Success = 1,

            Undefined = 0,

            AccessError = -1,
            Busy = -2,
            DownloadFailed = -3,
            InstallFailed = -4,
            NoUpdated = -5,
            InternalError = -6,
            FileNotFound = -7,

            Abborted = -99
        }

        public string mMyOfflineSvc = "Offline Sync Service";

        private RetCodes SetupOffline()
        {
            try
            {
                if (mOfflineService == null)
                {
                    AppLog.Line("Preparando el catálogo de actualizaciones sin conexión.");

                    // http://go.microsoft.com/fwlink/p/?LinkID=74689
                    mOfflineService = mUpdateServiceManager.AddScanPackageService(mMyOfflineSvc, dlPath + @"\wsusscn2.cab");
                }

                mUpdateSearcher.ServerSelection = ServerSelection.ssOthers;
                mUpdateSearcher.ServiceID = mOfflineService.ServiceID;
                mUpdateSearcher.Online = false;
            }
            catch (Exception err)
            {
                AppLog.Line(err.Message);
                RetCodes ret = RetCodes.InternalError;
                if (err.GetType() == typeof(System.IO.FileNotFoundException))
                    ret = RetCodes.FileNotFound;
                if (err.GetType() == typeof(System.UnauthorizedAccessException))
                    ret = RetCodes.AccessError;
                return ret;
            }
            return RetCodes.Success;
        }

        private bool mIsValid = false;
        public bool IsValid() { return mIsValid; }

        private RetCodes ClearOffline()
        {
            if (mOfflineService != null)
            {
                // note: if we keep references to updates reffering to an removed service we may got a crash
                foreach (MsUpdate Update in mUpdateHistory)
                    Update.Invalidate();
                foreach (MsUpdate Update in mPendingUpdates)
                    Update.Invalidate();
                foreach (MsUpdate Update in mInstalledUpdates)
                    Update.Invalidate();
                foreach (MsUpdate Update in mHiddenUpdates)
                    Update.Invalidate();
                mIsValid = false;

                OnUpdatesChanged();

                try
                {
                    mUpdateServiceManager.RemoveService(mOfflineService.ServiceID);
                    mOfflineService = null;
                }
                catch (Exception err)
                {
                    AppLog.Line(err.Message);
                    return RetCodes.InternalError;
                }
            }
            return RetCodes.Success;
        }

        private void SetOnline(string ServiceName)
        {
            ConfigureOnlineSearcher(mUpdateSearcher, null, false);
            foreach (IUpdateService service in mUpdateServiceManager.Services)
            {
                if (service.Name.Equals(ServiceName, StringComparison.CurrentCultureIgnoreCase))
                {
                    ConfigureOnlineSearcher(mUpdateSearcher, service.ServiceID, service.IsManaged);
                    AppLog.Line("Origen de búsqueda: " + service.Name);
                    return;
                }
            }
        }

        UpdateCallback mCallback = null;

        public enum AgentOperation
        {
            None = 0,
            CheckingUpdates,
            PreparingCheck,
            DownloadingUpdates,
            InstallingUpdates,
            PreparingUpdates,
            RemoveingUpdates,
            CancelingOperation
        };

        private AgentOperation mCurOperation = AgentOperation.None;

        public AgentOperation CurOperation() { return mCurOperation; }

        public RetCodes SearchForUpdates(String Source = "", bool IncludePotentiallySupersededUpdates = false)
        {
            if (IsBusy() || mCallback != null)
                return RetCodes.Busy;

            // A previous cancelled job may still be winding down in WUA.
            // Keep the next operation's searcher separate from that job.
            mUpdateSearcher = mUpdateSession.CreateUpdateSearcher();
            mUpdateSearcher.IncludePotentiallySupersededUpdates = IncludePotentiallySupersededUpdates;

            SetOnline(Source);

            return StartWindowsSearch(true);
        }

        public RetCodes SearchForUpdates(bool Download, bool IncludePotentiallySupersededUpdates = false)
        {
            if (IsBusy() || mCallback != null)
                return RetCodes.Busy;

            mUpdateSearcher = mUpdateSession.CreateUpdateSearcher();
            mUpdateSearcher.IncludePotentiallySupersededUpdates = IncludePotentiallySupersededUpdates;

            if (Download)
            {
                mCurOperation = AgentOperation.PreparingCheck;
                OnProgress(-1, 0, 0, 0);

                AppLog.Line("Descargando wsusscn2.cab...");

                List<UpdateDownloader.Task> downloads = new List<UpdateDownloader.Task>();
                UpdateDownloader.Task download = new UpdateDownloader.Task();
                download.Url = Program.IniReadValue("Options", "OfflineCab", "http://go.microsoft.com/fwlink/p/?LinkID=74689");
                download.Path = dlPath;
                download.FileName = "wsusscn2.cab";
                downloads.Add(download); 
                if(!mUpdateDownloader.Download(downloads))
                    OnFinished(RetCodes.DownloadFailed);
                return RetCodes.InProgress;
            }

            RetCodes ret = SetupOffline();
            if (ret < 0)
                return ret;

            return StartWindowsSearch(false);
        }

        private RetCodes OnWuError(Exception err)
        {
            bool access = err.GetType() == typeof(System.UnauthorizedAccessException);
            RetCodes ret = access ? RetCodes.AccessError : RetCodes.InternalError;

            if (mCurOperation == AgentOperation.InstallingUpdates ||
                mCurOperation == AgentOperation.PreparingUpdates)
                SetLastInstallationError(err.HResult, "");

            mCallback = null;
            AppLog.Line(err.Message);
            OnFinished(ret);
            return ret;
        }

        private RetCodes StartWindowsSearch(bool restartService)
        {
            mCurOperation = AgentOperation.CheckingUpdates;
            OnProgress(-1, 0, 0, 0);

            if (restartService)
            {
                ServerSelection selection = mUpdateSearcher.ServerSelection;
                string serviceId = selection == ServerSelection.ssOthers ? mUpdateSearcher.ServiceID : null;
                bool online = mUpdateSearcher.Online;
                bool superseded = mUpdateSearcher.IncludePotentiallySupersededUpdates;
                CancellationTokenSource preparation = new CancellationTokenSource();
                searchPreparation = preparation;
                Task.Run(() =>
                {
                    Exception failure = null;
                    try { WindowsUpdateServiceRecovery.Restart(message => AppLog.Line(message)); }
                    catch (Exception error) { failure = error; }
                    PostSearchToUi(() =>
                    {
                        bool cancelled = preparation.IsCancellationRequested;
                        searchPreparation = null;
                        preparation.Dispose();
                        if (cancelled)
                        {
                            OnFinished(RetCodes.Abborted);
                            return;
                        }
                        if (failure != null)
                        {
                            AppLog.Line("No se pudo preparar Windows Update: " + failure.Message);
                            OnFinished(RetCodes.InternalError);
                            return;
                        }
                        try
                        {
                            // Restarting the service can invalidate the previous COM searcher.
                            mUpdateSearcher = mUpdateSession.CreateUpdateSearcher();
                            mUpdateSearcher.ServerSelection = selection;
                            if (selection == ServerSelection.ssOthers) mUpdateSearcher.ServiceID = serviceId;
                            mUpdateSearcher.Online = online;
                            mUpdateSearcher.IncludePotentiallySupersededUpdates = superseded;
                            BeginWindowsSearch();
                        }
                        catch (Exception error) { OnWuError(error); }
                    });
                });
                return RetCodes.InProgress;
            }
            return BeginWindowsSearch();
        }

        private RetCodes BeginWindowsSearch()
        {

            mCallback = new UpdateCallback(this);

            AppLog.Line("Buscando actualizaciones...");
            //for the above search criteria refer to 
            // http://msdn.microsoft.com/en-us/library/windows/desktop/aa386526(v=VS.85).aspx
            try
            {
                //string query = "(IsInstalled = 0 and IsHidden = 0) or (IsInstalled = 1 and IsHidden = 0) or (IsHidden = 1)";
                //string query = "(IsInstalled = 0 and IsHidden = 0) or (IsInstalled = 1 and IsHidden = 0) or (IsHidden = 1) or (IsInstalled = 0 and IsHidden = 0 and DeploymentAction='OptionalInstallation') or (IsInstalled = 1 and IsHidden = 0 and DeploymentAction='OptionalInstallation') or (IsHidden = 1 and DeploymentAction='OptionalInstallation')";
                string query;
                if (MiscFunc.IsWindows7OrLower)
                    query = "(IsInstalled = 0 and IsHidden = 0) or (IsInstalled = 1 and IsHidden = 0) or (IsHidden = 1)";
                else
                    query = "(IsInstalled = 0 and IsHidden = 0 and DeploymentAction=*) or (IsInstalled = 1 and IsHidden = 0 and DeploymentAction=*) or (IsHidden = 1 and DeploymentAction=*)";
                AppLog.Line("Enviando consulta a Windows Update (origen: {0}, online: {1}).", mUpdateSearcher.ServerSelection, mUpdateSearcher.Online);
                mSearchJob = mUpdateSearcher.BeginSearch(query, mCallback, null);
                Interlocked.Exchange(ref searchPollJob, null);
                nextSearchPoll = DateTime.MinValue;
                nextSearchNotice = TimeSpan.FromSeconds(30);
                searchWatch = Stopwatch.StartNew();
                AppLog.Line("Consulta aceptada por Windows Update; esperando respuesta.");
            }
            catch (Exception err)
            {
                return OnWuError(err);
            }
            return RetCodes.InProgress;
        }

        public IUpdate FindUpdate(string UUID)
        {
            if (mUpdateSearcher == null)
                return null;
            try
            {
                // Note: this is sloooow!
                ISearchResult result = mUpdateSearcher.Search("UpdateID = '" + UUID + "'");
                if (result.Updates.Count > 0)
                    return result.Updates[0];
            }
            catch (Exception err)
            {
                AppLog.Line(err.Message);
            }
            return null;
        }

        public void CancelOperations()
        {
            if (mCurOperation == AgentOperation.CancelingOperation)
                return;
            if(IsBusy())
                mCurOperation = AgentOperation.CancelingOperation;

            if (searchPreparation != null)
            {
                searchPreparation.Cancel();
                // Finish restoring the service before abandoning this search.
                OnProgress(-1, 0, 0, 0);
                return;
            }

            // Note: at any given time only one (or none) of the 3 conditions can be true
            if (mCallback != null)
            {
                if (mSearchJob != null)
                {
                    ISearchJob searchJob = mSearchJob;
                    IUpdateSearcher searcher = mUpdateSearcher;
                    mSearchJob = null;
                    Interlocked.Exchange(ref searchPollJob, null);
                    mCallback = null;
                    searchWatch = null;
                    AppLog.Line("Búsqueda cancelada; liberando la operación de Windows Update en segundo plano.");
                    OnFinished(RetCodes.Abborted);
                    Task.Run(() =>
                    {
                        try { searchJob.RequestAbort(); }
                        catch (Exception error)
                        {
                            PostSearchToUi(() => AppLog.Line("El servicio no aceptó el aborto de la búsqueda descartada: " + error.Message));
                        }
                        try
                        {
                            searchJob.CleanUp();
                            searcher.EndSearch(searchJob);
                        }
                        catch (Exception error)
                        {
                            PostSearchToUi(() => AppLog.Line("Finalización de la búsqueda descartada: " + error.Message));
                        }
                    });
                    return;
                }

                if (mDownloadJob != null)
                    mDownloadJob.RequestAbort();

                if (mInstalationJob != null)
                    mInstalationJob.RequestAbort();
            }
            else if (mUpdateDownloader.IsBusy())
            {
                mUpdateDownloader.CancelOperations();
            }
            else if (mUpdateInstaller.IsBusy())
            {
                mUpdateInstaller.CancelOperations();
            }
        }

        public RetCodes DownloadUpdatesManually(List<MsUpdate> Updates, bool Install = false)
        {
            if (mUpdateDownloader.IsBusy())
                return RetCodes.Busy;

            if (Install)
                ClearLastInstallationError();

            mCurOperation = Install ? AgentOperation.PreparingUpdates : AgentOperation.DownloadingUpdates;
            OnProgress(-1, 0, 0, 0);

            List<UpdateDownloader.Task> downloads = new List<UpdateDownloader.Task>();
            foreach (MsUpdate Update in Updates)
            {
                if (Update.Downloads.Count == 0)
                {
                    AppLog.Line("No se encontraron enlaces de descarga para: {0}", Update.Title);
                    continue;
                }

                foreach (string url in Update.Downloads)
                {
                    UpdateDownloader.Task download = new UpdateDownloader.Task();
                    download.Url = url;
                    download.Path = dlPath + @"\" + Update.KB;
                    download.KB = Update.KB;
                    downloads.Add(download);
                }
            }

            if (!mUpdateDownloader.Download(downloads, Updates))
                OnFinished(RetCodes.DownloadFailed);
            
            return RetCodes.InProgress;
        }

        private RetCodes InstallUpdatesManually(List<MsUpdate> Updates, MultiValueDictionary<string, string> AllFiles)
        {
            if (mUpdateInstaller.IsBusy())
                return RetCodes.Busy;

            mCurOperation = AgentOperation.InstallingUpdates;
            OnProgress(-1, 0, 0, 0);

            if (!mUpdateInstaller.Install(Updates, AllFiles))
            {
                OnFinished(RetCodes.InstallFailed);
                return RetCodes.InstallFailed;
            }
            
            return RetCodes.InProgress;
        }


        public RetCodes UnInstallUpdatesManually(List<MsUpdate> Updates)
        {
            if (mUpdateInstaller.IsBusy())
                return RetCodes.Busy;

            List<MsUpdate> FilteredUpdates = new List<MsUpdate>();
            foreach (MsUpdate Update in Updates)
            {
                if (((int)Update.Attributes & (int)MsUpdate.UpdateAttr.Uninstallable) == 0)
                {
                    AppLog.Line("Esta actualización no se puede desinstalar: {0}", Update.Title);
                    continue;
                }
                FilteredUpdates.Add(Update);
            }
            if (FilteredUpdates.Count == 0)
            {
                AppLog.Line("No hay actualizaciones seleccionadas que se puedan desinstalar.");
                return RetCodes.NoUpdated;
            }

            mCurOperation = AgentOperation.RemoveingUpdates;
            OnProgress(-1, 0, 0, 0);

            if (!mUpdateInstaller.UnInstall(FilteredUpdates))
                OnFinished(RetCodes.InstallFailed);

            return RetCodes.InProgress;
        }

        void DownloadsFinished(object sender, UpdateDownloader.FinishedEventArgs args) // "manuall" mode
        {
            if (mCurOperation == AgentOperation.CancelingOperation)
            {
                OnFinished(RetCodes.Abborted);
                return;
            }

            if (mCurOperation == AgentOperation.PreparingCheck)
            {
                if (!args.Success)
                {
                    OnFinished(RetCodes.DownloadFailed);
                    return;
                }
                AppLog.Line("Se ha descargado wsusscn2.cab.");

                RetCodes ret = ClearOffline();
                if (ret == RetCodes.Success)
                    ret = SetupOffline();
                if (ret == RetCodes.Success)
                    ret = StartWindowsSearch(false);
                if (ret <= 0)
                    OnFinished(ret);
            }
            else
            {
                MultiValueDictionary<string, string> AllFiles = new MultiValueDictionary<string, string>();
                foreach (UpdateDownloader.Task task in args.Downloads)
                {
                    if (task.Failed || string.IsNullOrEmpty(task.FileName) || !File.Exists(Path.Combine(task.Path, task.FileName)))
                        continue;
                    AllFiles.Add(task.KB, task.Path + @"\" + task.FileName);
                }

                // TODO
                /*string INIPath = dlPath + @"\updates.ini";
                foreach (string KB in AllFiles.Keys)
                {
                    string Files = "";
                    foreach (string FileName in AllFiles.GetValues(KB))
                    {
                        if (Files.Length > 0)
                            Files += "|";
                        Files += FileName;
                    }
                    Program.IniWriteValue(KB, "Files", Files, INIPath);
                }*/

                AppLog.Line("Se descargaron {0} de {1} archivos en {2}.", AllFiles.GetCount(), args.Downloads.Count, dlPath);

                if (mCurOperation == AgentOperation.PreparingUpdates)
                {
                    RetCodes ret = InstallUpdatesManually(args.Updates, AllFiles);
                    if (ret <= 0)
                        OnFinished(ret);
                }
                else
                {
                    RetCodes ret = AllFiles.GetCount() == args.Downloads.Count ? RetCodes.Success : RetCodes.DownloadFailed;
                    if (mCurOperation == AgentOperation.CancelingOperation)
                        ret = RetCodes.Abborted;
                    OnFinished(ret);
                }
            }
        }

        void DownloadProgress(object sender, ProgressArgs args)
        {
            OnProgress(args.TotalCount, args.TotalPercent, args.CurrentIndex, args.CurrentPercent, args.Info);
        }

        void InstallFinished(object sender, UpdateInstaller.FinishedEventArgs args) // "manuall" mode
        {
            if (args.Success)
                AppLog.Line("La operación con las actualizaciones se completó correctamente.");
            else
                AppLog.Line("Algunas actualizaciones no se pudieron procesar.");

            if (args.Reboot)
                AppLog.Line("Es necesario reiniciar para completar una o varias actualizaciones.");

            OnUpdatesChanged();

            RetCodes ret = args.Success ? RetCodes.Success : RetCodes.InstallFailed;
            if (mCurOperation == AgentOperation.CancelingOperation)
                ret = RetCodes.Abborted;
            OnFinished(ret, args.Reboot);
        }

        private static void ConfigureOnlineSearcher(IUpdateSearcher searcher, string serviceId, bool managed)
        {
            searcher.ServerSelection = string.IsNullOrEmpty(serviceId) ? ServerSelection.ssDefault :
                managed ? ServerSelection.ssManagedServer : ServerSelection.ssOthers;
            if (!string.IsNullOrEmpty(serviceId) && !managed)
                searcher.ServiceID = serviceId;
            searcher.Online = true;
        }

        void InstallItemFinished(object sender, UpdateInstaller.ItemFinishedEventArgs args)
        {
            if (!args.Success || args.Update == null)
                return;

            if (mCurOperation == AgentOperation.InstallingUpdates)
            {
                if (RemoveFrom(mPendingUpdates, args.Update))
                {
                    args.Update.Attributes |= (int)MsUpdate.UpdateAttr.Installed;
                    mInstalledUpdates.Add(args.Update);
                }
            }
            else if (mCurOperation == AgentOperation.RemoveingUpdates)
            {
                if (RemoveFrom(mInstalledUpdates, args.Update))
                {
                    args.Update.Attributes &= ~(int)MsUpdate.UpdateAttr.Installed;
                    mPendingUpdates.Add(args.Update);
                }
            }

            OnUpdatesChanged();
        }

        void InstallProgress(object sender, ProgressArgs args)
        {
            OnProgress(args.TotalCount, args.TotalPercent, args.CurrentIndex, args.CurrentPercent, args.Info);
        }

        public RetCodes DownloadUpdates(List<MsUpdate> Updates, bool Install = false)
        {
            if (mCallback != null)
                return RetCodes.Busy;

            if (Install)
                ClearLastInstallationError();

            if (mDownloader == null)
                mDownloader = mUpdateSession.CreateUpdateDownloader();

            mDownloader.Updates = new UpdateCollection();
            foreach (MsUpdate Update in Updates)
            {
                IUpdate update = Update.GetUpdate();
                if (update == null)
                    continue;

                if (update.EulaAccepted == false)
                {
                    update.AcceptEula();
                }
                mDownloader.Updates.Add(update);
            }

            if (mDownloader.Updates.Count == 0)
            {
                AppLog.Line("No hay actualizaciones seleccionadas para descargar.");
                return RetCodes.NoUpdated;
            }

            mCurOperation = Install ? AgentOperation.PreparingUpdates : AgentOperation.DownloadingUpdates;
            OnProgress(-1, 0, 0, 0);

            mCallback = new UpdateCallback(this);

            AppLog.Line("Descargando actualizaciones. Esto puede tardar varios minutos...");
            try
            {
                mDownloadJob = mDownloader.BeginDownload(mCallback, mCallback, Updates);
            }
            catch (Exception err)
            {
                return OnWuError(err);
            }
            return RetCodes.InProgress;
        }

        private RetCodes InstallUpdates(List<MsUpdate> Updates)
        {
            if (mCallback != null)
                return RetCodes.Busy;

            if (mInstaller == null)
                mInstaller = mUpdateSession.CreateUpdateInstaller() as IUpdateInstaller;

            mInstaller.Updates = new UpdateCollection();
            foreach (MsUpdate Update in Updates)
            {
                IUpdate update = Update.GetUpdate();
                if (update == null)
                    continue;

                mInstaller.Updates.Add(update);
            }

            if (mInstaller.Updates.Count == 0)
            {
                AppLog.Line("No hay actualizaciones seleccionadas para instalar.");
                return RetCodes.NoUpdated;
            }

            mCurOperation = AgentOperation.InstallingUpdates;
            OnProgress(-1, 0, 0, 0);

            mCallback = new UpdateCallback(this);

            AppLog.Line("Instalando actualizaciones. Esto puede tardar varios minutos...");
            try
            {
                mInstalationJob = mInstaller.BeginInstall(mCallback, mCallback, Updates);
            }
            catch (Exception err)
            {
                return OnWuError(err);
            }
            return RetCodes.InProgress;
        }

        // Note: this works _only_ for updates installed from WSUS
        /*public RetCodes UnInstallUpdates(List<MsUpdate> Updates)
        {
            if (mCallback != null)
                return RetCodes.Busy;

            if (mInstaller == null)
                mInstaller = mUpdateSession.CreateUpdateInstaller() as IUpdateInstaller;

            mInstaller.Updates = new UpdateCollection();
            foreach (MsUpdate Update in Updates)
            {
                IUpdate update = Update.GetUpdate();
                if (update == null)
                    continue;

                if (!update.IsUninstallable)
                {
                    AppLog.Line("Esta actualización no se puede desinstalar: {0}", update.Title);
                    continue;
                }
                mInstaller.Updates.Add(update);
            }
            if (mInstaller.Updates.Count == 0)
            {
                AppLog.Line("No hay actualizaciones seleccionadas que se puedan desinstalar.");
                return RetCodes.NoUpdated;
            }

            mCurOperation = AgentOperation.RemoveingUpdates;
            OnProgress(-1, 0, 0, 0);

            mCallback = new UpdateCallback(this);

            AppLog.Line("Desinstalando actualizaciones. Esto puede tardar varios minutos...");
            try
            {
                mInstalationJob = mInstaller.BeginUninstall(mCallback, mCallback, Updates);
            }
            catch (Exception err)
            {
                return OnWuError(err);
            }
            return RetCodes.InProgress;
        }*/

        public bool RemoveFrom(List<MsUpdate> Updates, MsUpdate Update)
        {
            for (int i = 0; i < Updates.Count; i++)
            {
                if (Updates[i] == Update)
                {
                    Updates.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public void HideUpdates(List<MsUpdate> Updates, bool Hide)
        {
            foreach (MsUpdate Update in Updates)
            {
                try
                {
                    IUpdate update = Update.GetUpdate();
                    if (update == null)
                        continue;
                    update.IsHidden = Hide;

                    if (Hide)
                    {
                        Update.Attributes |= (int)MsUpdate.UpdateAttr.Hidden;
                        mHiddenUpdates.Add(Update);
                        RemoveFrom(mPendingUpdates, Update);
                    }
                    else
                    {
                        Update.Attributes &= ~(int)MsUpdate.UpdateAttr.Hidden;
                        mPendingUpdates.Add(Update);
                        RemoveFrom(mHiddenUpdates, Update);
                    }

                    OnUpdatesChanged();
                }
                catch { } // Hide update may throw an exception, if the user has hidden the update manually while the search was in progress.
            }
        }

        protected void OnUpdatesFound(ISearchJob searchJob)
        {
            if (searchJob != mSearchJob)
                return;
            mSearchJob = null;
            mCallback = null;
            bool cancelled = mCurOperation == AgentOperation.CancelingOperation;

            ISearchResult SearchResults = null;
            try
            {
                SearchResults = mUpdateSearcher.EndSearch(searchJob);
            }
            catch (Exception err)
            {
                searchWatch = null;
                if (!cancelled)
                {
                    AppLog.Line("La búsqueda de actualizaciones ha fallado.");
                    LogError(err);
                }
                OnFinished(cancelled ? RetCodes.Abborted : RetCodes.InternalError);
                return;
            }

            finally
            {
                // CleanUp waits for callbacks. Run it outside the callback/UI
                // thread, including when EndSearch throws or search is aborted.
                Task.Run(() =>
                {
                    try { searchJob.CleanUp(); }
                    catch (Exception error) { PostSearchToUi(() => AppLog.Line("No se pudo liberar la búsqueda: " + error.Message)); }
                });
            }
            if (searchWatch != null)
            {
                AppLog.Line("Consulta de Windows Update: {0:F1} s.", searchWatch.Elapsed.TotalSeconds);
                searchWatch = null;
            }
            if (cancelled)
            {
                OnFinished(RetCodes.Abborted);
                return;
            }
            RetCodes ret = RetCodes.Undefined;
            try
            {
                if (SearchResults.ResultCode == OperationResultCode.orcAborted)
                {
                    OnFinished(RetCodes.Abborted);
                    return;
                }
                if (SearchResults.ResultCode != OperationResultCode.orcSucceeded &&
                    SearchResults.ResultCode != OperationResultCode.orcSucceededWithErrors)
                {
                    AppLog.Line("La búsqueda no terminó correctamente: " + SearchResults.ResultCode);
                    OnFinished(RetCodes.InternalError);
                    return;
                }
                Stopwatch processing = Stopwatch.StartNew();
                List<MsUpdate> pending = new List<MsUpdate>();
                List<MsUpdate> installed = new List<MsUpdate>();
                List<MsUpdate> hidden = new List<MsUpdate>();

                foreach (IUpdate update in SearchResults.Updates)
                {
                    MsUpdate.UpdateState state = update.IsHidden ? MsUpdate.UpdateState.Hidden :
                        update.IsInstalled ? MsUpdate.UpdateState.Installed : MsUpdate.UpdateState.Pending;
                    MsUpdate model = new MsUpdate(update, state);
                    if (state == MsUpdate.UpdateState.Hidden) hidden.Add(model);
                    else if (state == MsUpdate.UpdateState.Installed) installed.Add(model);
                    else pending.Add(model);
                    Console.WriteLine(model.Title);
                }
                mPendingUpdates.Clear(); mPendingUpdates.AddRange(pending);
                mInstalledUpdates.Clear(); mInstalledUpdates.AddRange(installed);
                mHiddenUpdates.Clear(); mHiddenUpdates.AddRange(hidden);
                mIsValid = true;

                AppLog.Line("Se encontraron {0} actualizaciones pendientes.", mPendingUpdates.Count);

                OnUpdatesChanged(true);
                AppLog.Line("Preparación de resultados e interfaz: {0:F1} s.", processing.Elapsed.TotalSeconds);

                if (SearchResults.ResultCode == OperationResultCode.orcSucceeded || SearchResults.ResultCode == OperationResultCode.orcSucceededWithErrors)
                    ret = RetCodes.Success;
                else if (SearchResults.ResultCode == OperationResultCode.orcAborted)
                    ret = RetCodes.Abborted;
                else if (SearchResults.ResultCode == OperationResultCode.orcFailed)
                    ret = RetCodes.InternalError;
            }
            catch (Exception error)
            {
                mIsValid = false;
                LogError(error);
                ret = RetCodes.InternalError;
            }
            OnFinished(ret);
        }

        protected void OnUpdatesDownloaded(IDownloadJob downloadJob, List<MsUpdate> Updates)
        {
            if (downloadJob != mDownloadJob)
                return;
            mDownloadJob = null;
            mCallback = null;

            IDownloadResult DownloadResults = null;
            try
            {
                DownloadResults = mDownloader.EndDownload(downloadJob);
            }
            catch (Exception err)
            {
                AppLog.Line("La descarga de actualizaciones ha fallado.");
                LogError(err);
                OnFinished(RetCodes.InternalError);
                return;
            }

            OnUpdatesChanged();

            if (mCurOperation == AgentOperation.PreparingUpdates)
            {
                RetCodes ret = InstallUpdates(Updates);
                if (ret <= 0)
                    OnFinished(ret);
            }
            else
            {
                AppLog.Line("Actualizaciones descargadas en %windir%\\SoftwareDistribution\\Download.");

                RetCodes ret = RetCodes.Undefined;
                if (DownloadResults.ResultCode == OperationResultCode.orcSucceeded || DownloadResults.ResultCode == OperationResultCode.orcSucceededWithErrors)
                    ret = RetCodes.Success;
                else if (DownloadResults.ResultCode == OperationResultCode.orcAborted)
                    ret = RetCodes.Abborted;
                else if (DownloadResults.ResultCode == OperationResultCode.orcFailed)
                    ret = RetCodes.InternalError;
                OnFinished(ret);
            }
        }

        protected void OnInstalationCompleted(IInstallationJob installationJob, List<MsUpdate> Updates)
        {
            if (installationJob != mInstalationJob)
                return;
            mInstalationJob = null;
            mCallback = null;

            IInstallationResult InstallationResults = null;
            try
            {
                if (mCurOperation == AgentOperation.InstallingUpdates)
                    InstallationResults = mInstaller.EndInstall(installationJob);
                else if (mCurOperation == AgentOperation.RemoveingUpdates)
                    InstallationResults = mInstaller.EndUninstall(installationJob);
                
            }
            catch (Exception err)
            {
                AppLog.Line("No se pudieron procesar las actualizaciones.");
                LogError(err);
                SetLastInstallationError(err.HResult, Updates.Count > 0 ? Updates[0].Title : "");
                OnFinished(RetCodes.InternalError);
                return;
            }

            bool anyFailure = false;
            bool anySuccess = false;
            for (int index = 0; index < Updates.Count; index++)
            {
                MsUpdate Update = Updates[index];
                IUpdateInstallationResult itemResult = InstallationResults.GetUpdateResult(index);
                bool succeeded = itemResult.ResultCode == OperationResultCode.orcSucceeded ||
                    itemResult.ResultCode == OperationResultCode.orcSucceededWithErrors;

                if (!succeeded)
                {
                    anyFailure = true;
                    SetLastInstallationError(itemResult.HResult, Update.Title);
                    uint errorCode = unchecked((uint)itemResult.HResult);
                    AppLog.Line("No se pudo instalar {0}. Error 0x{1}: {2}", Update.Title,
                        errorCode.ToString("X8"), DescribeInstallationError(errorCode));
                    continue;
                }

                anySuccess = true;
                if (mCurOperation == AgentOperation.InstallingUpdates)
                {
                    if (RemoveFrom(mPendingUpdates, Update))
                    {
                        Update.Attributes |= (int)MsUpdate.UpdateAttr.Installed;
                        mInstalledUpdates.Add(Update);
                    }
                }
                else if (mCurOperation == AgentOperation.RemoveingUpdates)
                {
                    if (RemoveFrom(mInstalledUpdates, Update))
                    {
                        Update.Attributes &= ~(int)MsUpdate.UpdateAttr.Installed;
                        mPendingUpdates.Add(Update);
                    }
                }
            }

            if (anyFailure)
                AppLog.Line(anySuccess
                    ? "Algunas actualizaciones se instalaron, pero otras produjeron errores."
                    : "No se pudo instalar ninguna de las actualizaciones seleccionadas.");
            else
                AppLog.Line("La operación con las actualizaciones se completó correctamente.");

            if (InstallationResults.RebootRequired)
                AppLog.Line("Es necesario reiniciar para completar una o varias actualizaciones.");

            OnUpdatesChanged();

            RetCodes ret = anyFailure ? RetCodes.InstallFailed : RetCodes.Success;
            if (InstallationResults.ResultCode == OperationResultCode.orcAborted)
                ret = RetCodes.Abborted;
            OnFinished(ret, InstallationResults.RebootRequired);
        }


        public void EnableWuAuServ()
        {
            ServiceController svc = new ServiceController("wuauserv"); // Windows Update Service
            try
            {
                ServiceHelper.ChangeStartMode(svc, ServiceStartMode.Manual);
                svc.Refresh();

                if (svc.Status == ServiceControllerStatus.StopPending)
                    svc.WaitForStatus(ServiceControllerStatus.Stopped,
                        TimeSpan.FromSeconds(15));

                svc.Refresh();
                if (svc.Status == ServiceControllerStatus.Stopped)
                    svc.Start();
                else if (svc.Status == ServiceControllerStatus.Paused)
                    svc.Continue();

                svc.Refresh();
                if (svc.Status != ServiceControllerStatus.Running)
                {
                    svc.WaitForStatus(ServiceControllerStatus.Running,
                        TimeSpan.FromSeconds(15));
                }
                AppLog.Line("Servicio de Windows Update activo.");
            }
            catch (Exception err)
            {
                AppLog.Line("No se pudo activar el servicio de Windows Update: "
                    + err.Message);
            }
            svc.Close();
        }

        public class ProgressArgs : EventArgs
        {
            public ProgressArgs(int TotalCount, int TotalPercent, int CurrentIndex, int CurrentPercent, String Info)
            {
                this.TotalCount = TotalCount;
                this.TotalPercent = TotalPercent;
                this.CurrentIndex = CurrentIndex;
                this.CurrentPercent = CurrentPercent;
                this.Info = Info;
            }

            public int TotalCount = 0;
            public int TotalPercent = 0;
            public int CurrentIndex = 0;
            public int CurrentPercent = 0;
            public String Info = "";
        }

        public event EventHandler<ProgressArgs> Progress;

        protected void OnProgress(int TotalUpdates, int TotalPercent, int CurrentIndex, int UpdatePercent, String Info = "")
        {
            if (Progress != null)
                Progress(this, new ProgressArgs(TotalUpdates, TotalPercent, CurrentIndex, UpdatePercent, Info));
        }

        public class FinishedArgs : EventArgs
        {
            public FinishedArgs(AgentOperation op, RetCodes ret, bool needReboot = false)
            {
                Op = op;
                Ret = ret;
                RebootNeeded = needReboot;
            }

            public AgentOperation Op = AgentOperation.None;
            public RetCodes Ret = RetCodes.Undefined;
            public bool RebootNeeded = false;
        }
        public event EventHandler<FinishedArgs> Finished;

        protected void OnFinished(RetCodes ret, bool needReboot = false)
        {
            FinishedArgs args = new FinishedArgs(mCurOperation, ret, needReboot);

            mCurOperation = AgentOperation.None;

            if (Finished != null)
                Finished(this, args);
        }

        public class UpdatesArgs : EventArgs
        {
            public UpdatesArgs(bool found)
            {
                Found = found;
            }
            public bool Found = false;
        }
        public event EventHandler<UpdatesArgs> UpdatesChaged;

        protected void OnUpdatesChanged(bool found = false)
        {
            string INIPath = dlPath + @"\updates.ini";
            FileOps.DeleteFile(INIPath);

            StoreUpdates(mUpdateHistory);
            StoreUpdates(mPendingUpdates);
            StoreUpdates(mInstalledUpdates);
            StoreUpdates(mHiddenUpdates);
            
            if (UpdatesChaged != null)
                UpdatesChaged(this, new UpdatesArgs(found));
        }

        private void StoreUpdates(List<MsUpdate> Updates)
        {
            string INIPath = dlPath + @"\updates.ini";
            foreach (MsUpdate Update in Updates)
            {
                if (Update.KB.Length == 0) // sanity check
                    continue;

                Program.IniWriteValue(Update.KB, "UUID", Update.UUID, INIPath);

                Program.IniWriteValue(Update.KB, "Title", Update.Title, INIPath);
                Program.IniWriteValue(Update.KB, "Info", Update.Description, INIPath);
                Program.IniWriteValue(Update.KB, "Category", Update.Category, INIPath);

                Program.IniWriteValue(Update.KB, "Date", Update.Date.ToString(CultureInfo.CurrentUICulture.DateTimeFormat.ShortDatePattern), INIPath);
                Program.IniWriteValue(Update.KB, "Size", Update.Size.ToString(), INIPath);

                Program.IniWriteValue(Update.KB, "SupportUrl", Update.SupportUrl, INIPath);

                Program.IniWriteValue(Update.KB, "Downloads", string.Join("|",Update.Downloads.Cast<string>().ToArray<string>()), INIPath);

                Program.IniWriteValue(Update.KB, "State", ((int)Update.State).ToString(), INIPath);
                Program.IniWriteValue(Update.KB, "Attributes", Update.Attributes.ToString(), INIPath);
                Program.IniWriteValue(Update.KB, "ResultCode", Update.ResultCode.ToString(), INIPath);
                Program.IniWriteValue(Update.KB, "HResult", Update.HResult.ToString(), INIPath);
            }
        }

        private void LoadUpdates()
        {
            string INIPath = dlPath + @"\updates.ini";
            foreach (string KB in Program.IniEnumSections(INIPath))
            {
                if (KB.Length == 0)
                    continue;

                MsUpdate Update = new MsUpdate();
                Update.KB = KB;
                Update.UUID = Program.IniReadValue(Update.KB, "UUID", "", INIPath);

                Update.Title = Program.IniReadValue(Update.KB, "Title", "", INIPath);
                Update.Description = Program.IniReadValue(Update.KB, "Info", "", INIPath);
                Update.Category = Program.IniReadValue(Update.KB, "Category", "", INIPath);

                try { Update.Date = DateTime.Parse(Program.IniReadValue(Update.KB, "Date", "", INIPath)); } catch { }
                Update.Size = (decimal)MiscFunc.parseInt(Program.IniReadValue(Update.KB, "Size", "0", INIPath));

                Update.SupportUrl = Program.IniReadValue(Update.KB, "SupportUrl", "", INIPath);
                Update.Downloads.AddRange(Program.IniReadValue(Update.KB, "Downloads", "", INIPath).Split('|'));

                Update.State = (MsUpdate.UpdateState)MiscFunc.parseInt(Program.IniReadValue(Update.KB, "State", "0", INIPath));
                Update.Attributes = MiscFunc.parseInt(Program.IniReadValue(Update.KB, "Attributes", "0", INIPath));
                Update.ResultCode = MiscFunc.parseInt(Program.IniReadValue(Update.KB, "ResultCode", "0", INIPath));
                Update.HResult = MiscFunc.parseInt(Program.IniReadValue(Update.KB, "HResult", "0", INIPath));

                switch (Update.State)
                {
                    case MsUpdate.UpdateState.Pending: mPendingUpdates.Add(Update); break;
                    case MsUpdate.UpdateState.Installed: mInstalledUpdates.Add(Update); break;
                    case MsUpdate.UpdateState.Hidden: mHiddenUpdates.Add(Update); break;
                    case MsUpdate.UpdateState.History: mUpdateHistory.Add(Update); break;
                }
            }
        }

        class UpdateCallback : ISearchCompletedCallback, IDownloadProgressChangedCallback, IDownloadCompletedCallback, IInstallationProgressChangedCallback, IInstallationCompletedCallback
        {
            private WuAgent agent;
            
            public UpdateCallback(WuAgent agent)
            {
                this.agent = agent;
            }

            // Implementation of ISearchCompletedCallback interface...
            public void Invoke(ISearchJob searchJob, ISearchCompletedCallbackArgs e)
            {
                // !!! warning this function is invoced from a different thread !!!            
                agent.CompleteSearchOnUi(searchJob);
            }

            // Implementation of IDownloadProgressChangedCallback interface...
            public void Invoke(IDownloadJob downloadJob, IDownloadProgressChangedCallbackArgs callbackArgs)
            {
                // !!! warning this function is invoced from a different thread !!!            
                agent.mDispatcher.Invoke(new Action(() => {
                    agent.OnProgress(downloadJob.Updates.Count, callbackArgs.Progress.PercentComplete, callbackArgs.Progress.CurrentUpdateIndex + 1,
                        callbackArgs.Progress.CurrentUpdatePercentComplete, downloadJob.Updates[callbackArgs.Progress.CurrentUpdateIndex].Title);
                }));
            }

            // Implementation of IDownloadCompletedCallback interface...
            public void Invoke(IDownloadJob downloadJob, IDownloadCompletedCallbackArgs callbackArgs)
            {
                // !!! warning this function is invoced from a different thread !!!            
                agent.mDispatcher.Invoke(new Action(() => {
                    agent.OnUpdatesDownloaded(downloadJob, (List<MsUpdate>)downloadJob.AsyncState);
                }));
            }

            // Implementation of IInstallationProgressChangedCallback interface...
            public void Invoke(IInstallationJob installationJob, IInstallationProgressChangedCallbackArgs callbackArgs)
            {
                // !!! warning this function is invoced from a different thread !!!            
                agent.mDispatcher.Invoke(new Action(() => {
                    agent.OnProgress(installationJob.Updates.Count, callbackArgs.Progress.PercentComplete, callbackArgs.Progress.CurrentUpdateIndex + 1,
                        callbackArgs.Progress.CurrentUpdatePercentComplete, installationJob.Updates[callbackArgs.Progress.CurrentUpdateIndex].Title);
                }));
            }

            // Implementation of IInstallationCompletedCallback interface...
            public void Invoke(IInstallationJob installationJob, IInstallationCompletedCallbackArgs callbackArgs)
            {
                // !!! warning this function is invoced from a different thread !!!            
                agent.mDispatcher.Invoke(new Action(() => {
                    agent.OnInstalationCompleted(installationJob, (List<MsUpdate>)installationJob.AsyncState);
                }));
            }
        }
    }
}
