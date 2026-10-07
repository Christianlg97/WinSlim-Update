using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Forms;
using System.Collections;
using System.ServiceProcess;
using WUApiLib;

public sealed class FakeSearchJob : ISearchJob
{
    public object AsyncState { get { return null; } }
    public bool IsCompleted { get; private set; }
    public readonly ManualResetEvent CallbackReturned = new ManualResetEvent(false);
    public ISearchCompletedCallback Callback;
    public bool SuppressCallback;
    public readonly ManualResetEvent CleanupFinished = new ManualResetEvent(false);
    public void CleanUp()
    {
        if (!CallbackReturned.WaitOne(2000)) throw new Exception("Cleanup inside active callback");
        CleanupFinished.Set();
    }
    public void RequestAbort()
    {
        Thread.Sleep(200);
        IsCompleted = true;
        if (!SuppressCallback) Callback.Invoke(this, null);
        CallbackReturned.Set();
    }
}

public sealed class FakeSearcher : IUpdateSearcher
{
    public bool CanAutomaticallyUpgradeService { get; set; }
    public string ClientApplicationID { get; set; }
    public bool IncludePotentiallySupersededUpdates { get; set; }
    public ServerSelection ServerSelection { get; set; }
    public bool Online { get; set; }
    public string ServiceID { get; set; }
    public bool ThrowOnEnd;
    public ISearchResult Result;
    public ISearchResult EndSearch(ISearchJob job)
    {
        if (!job.IsCompleted)
            throw new InvalidOperationException("Incomplete native search");
        if (ThrowOnEnd) throw new InvalidOperationException("Aborted search");
        return Result;
    }
    public ISearchJob BeginSearch(string criteria, object callback, object state) { throw new NotSupportedException(); }
    public string EscapeString(string value) { return value; }
    public IUpdateHistoryEntryCollection QueryHistory(int start, int count) { throw new NotSupportedException(); }
    public ISearchResult Search(string criteria) { throw new NotSupportedException(); }
    public int GetTotalHistoryCount() { return 0; }
}

public sealed class FailedSearchResult : ISearchResult
{
    public OperationResultCode ResultCode { get { return OperationResultCode.orcFailed; } }
    public ICategoryCollection RootCategories { get { return null; } }
    public IUpdateExceptionCollection Warnings { get { return null; } }
    public UpdateCollection Updates { get { throw new Exception("Failed results must not be enumerated"); } }
}

public static class SearchRegression
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string lastResult;
    public static void Capture<T>(object sender, T args)
    {
        lastResult = args.GetType().GetField("Ret").GetValue(args).ToString();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Type type = Assembly.LoadFrom(args[0]).GetType("wumgr.WuAgent");
            foreach (ServiceControllerStatus status in new[] { ServiceControllerStatus.Running, ServiceControllerStatus.Stopped, ServiceControllerStatus.StartPending, ServiceControllerStatus.StopPending })
            {
                FakeUpdateService service = new FakeUpdateService { Status = status };
                wumgr.WindowsUpdateServiceRecovery.Restart(service, delegate { });
                if (service.Status != ServiceControllerStatus.Running || service.Starts != 1 ||
                    service.Stops != (status == ServiceControllerStatus.Running || status == ServiceControllerStatus.StartPending ? 1 : 0))
                    throw new Exception("Invalid service restart sequence: " + status);
            }
            FakeUpdateService timeout = new FakeUpdateService { Status = ServiceControllerStatus.Running, FailWait = true };
            try { wumgr.WindowsUpdateServiceRecovery.Restart(timeout, delegate { }); throw new Exception("Timeout was ignored"); }
            catch (System.ServiceProcess.TimeoutException) { }
            Console.WriteLine("PASS: restart service states and bounded timeout (simulated service only)");
            MethodInfo configure = type.GetMethod("ConfigureOnlineSearcher", BindingFlags.NonPublic | BindingFlags.Static);
            FakeSearcher configured = new FakeSearcher { Online = false, ServerSelection = ServerSelection.ssOthers, ServiceID = "offline" };
            configure.Invoke(null, new object[] { configured, null, false });
            if (!configured.Online || configured.ServerSelection != ServerSelection.ssDefault) throw new Exception("Online default retained offline selection");
            configure.Invoke(null, new object[] { configured, "selected-service", false });
            if (configured.ServerSelection != ServerSelection.ssOthers || configured.ServiceID != "selected-service") throw new Exception("Selected service ignored");
            configure.Invoke(null, new object[] { configured, "managed", true });
            if (configured.ServerSelection != ServerSelection.ssManagedServer) throw new Exception("Managed service selection changed");
            Console.WriteLine("PASS: online/default, explicit service and managed service selection");
            foreach (int scenario in new[] { 0, 1, 2, 3 })
            {
                bool throwing = scenario == 1;
                object agent = FormatterServices.GetUninitializedObject(type);
                type.GetField("mDispatcher", Fields).SetValue(agent, Dispatcher.CurrentDispatcher);
                type.GetMethod("SetSearchUiContext").Invoke(agent, new object[] { new WindowsFormsSynchronizationContext() });
                Type operation = type.GetNestedType("AgentOperation", BindingFlags.Public);
                type.GetField("mCurOperation", Fields).SetValue(agent, Enum.Parse(operation, "CheckingUpdates"));
                FakeSearchJob job = new FakeSearchJob();
                job.SuppressCallback = scenario == 2;
                type.GetField("mSearchJob", Fields).SetValue(agent, job);
                type.GetField("mUpdateSearcher", Fields).SetValue(agent, new FakeSearcher { ThrowOnEnd = throwing });
                Type callbackType = type.GetNestedType("UpdateCallback", BindingFlags.NonPublic);
                object callback = Activator.CreateInstance(callbackType, new[] { agent });
                job.Callback = (ISearchCompletedCallback)callback;
                type.GetField("mCallback", Fields).SetValue(agent, callback);
                lastResult = null;
                EventInfo finished = type.GetEvent("Finished");
                MethodInfo handler = typeof(SearchRegression).GetMethod("Capture").MakeGenericMethod(finished.EventHandlerType.GetGenericArguments()[0]);
                finished.AddEventHandler(agent, Delegate.CreateDelegate(finished.EventHandlerType, handler));
                Stopwatch time = Stopwatch.StartNew();
                type.GetMethod("CancelOperations").Invoke(agent, null);
                if (time.ElapsedMilliseconds > 150) throw new Exception("Cancel blocked the UI thread");
                if ((bool)type.GetMethod("IsBusy").Invoke(agent, null)) throw new Exception("Cancel waited for WUA instead of discarding the search");
                FakeSearchJob replacement = null;
                if (scenario == 3)
                {
                    replacement = new FakeSearchJob();
                    type.GetField("mSearchJob", Fields).SetValue(agent, replacement);
                    type.GetField("mCurOperation", Fields).SetValue(agent, Enum.Parse(operation, "CheckingUpdates"));
                }
                while (!job.CleanupFinished.WaitOne(0) && time.ElapsedMilliseconds < 4000)
                {
                    Application.DoEvents();
                    type.GetMethod("PollSearchCompletion").Invoke(agent, null);
                    Thread.Sleep(10);
                }
                if (scenario != 3 && (bool)type.GetMethod("IsBusy").Invoke(agent, null)) throw new Exception("Search remained busy after cancellation");
                if (!job.CallbackReturned.WaitOne(1000)) throw new Exception("Callback did not return");
                if (!job.CleanupFinished.WaitOne(2000)) throw new Exception("Native job not cleaned up");
                if (scenario == 3)
                {
                    if (type.GetField("mSearchJob", Fields).GetValue(agent) != replacement ||
                        !(bool)type.GetMethod("IsBusy").Invoke(agent, null)) throw new Exception("Late cancelled result overwrote a new search");
                    replacement.CallbackReturned.Dispose();
                    replacement.CleanupFinished.Dispose();
                }
                else if (type.GetField("mSearchJob", Fields).GetValue(agent) != null) throw new Exception("Search job was retained");
                if (lastResult != "Abborted") throw new Exception("Cancellation was reported as " + lastResult);
                job.CallbackReturned.Dispose();
                job.CleanupFinished.Dispose();
                Console.WriteLine("PASS: WinForms cancellation, EndSearch error=" + throwing + ", missing callback=" + job.SuppressCallback);
            }
            object failingAgent = FormatterServices.GetUninitializedObject(type);
            FakeSearchJob failingJob = new FakeSearchJob();
            failingJob.CallbackReturned.Set();
            type.GetField("mSearchJob", Fields).SetValue(failingAgent, failingJob);
            type.GetField("mUpdateSearcher", Fields).SetValue(failingAgent, new FakeSearcher());
            type.GetField("mCurOperation", Fields).SetValue(failingAgent,
                Enum.Parse(type.GetNestedType("AgentOperation", BindingFlags.Public), "CheckingUpdates"));
            type.GetMethod("OnUpdatesFound", Fields).Invoke(failingAgent, new object[] { failingJob });
            if ((bool)type.GetMethod("IsBusy").Invoke(failingAgent, null)) throw new Exception("Invalid result left search busy");
            failingJob.CallbackReturned.Dispose();
            Console.WriteLine("PASS: invalid search results leave the agent idle");
            object failed = FormatterServices.GetUninitializedObject(type);
            FakeSearchJob failedJob = new FakeSearchJob();
            // Mark completed without delivering a callback for direct finalization.
            failedJob.SuppressCallback = true;
            failedJob.RequestAbort();
            type.GetField("mSearchJob", Fields).SetValue(failed, failedJob);
            type.GetField("mUpdateSearcher", Fields).SetValue(failed, new FakeSearcher { Result = new FailedSearchResult() });
            type.GetField("mDispatcher", Fields).SetValue(failed, Dispatcher.CurrentDispatcher);
            FieldInfo pending = type.GetField("mPendingUpdates");
            IList previous = (IList)Activator.CreateInstance(pending.FieldType);
            previous.Add(Activator.CreateInstance(pending.FieldType.GetGenericArguments()[0]));
            pending.SetValue(failed, previous);
            type.GetField("mCurOperation", Fields).SetValue(failed,
                Enum.Parse(type.GetNestedType("AgentOperation", BindingFlags.Public), "CheckingUpdates"));
            type.GetMethod("OnUpdatesFound", Fields).Invoke(failed, new object[] { failedJob });
            if (previous.Count != 1 || (bool)type.GetMethod("IsBusy").Invoke(failed, null)) throw new Exception("Failed search erased previous results or remained busy");
            if (!failedJob.CleanupFinished.WaitOne(2000)) throw new Exception("Failed job not cleaned up");
            Console.WriteLine("PASS: failed result preserves previous updates and releases job");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

internal sealed class FakeUpdateService : wumgr.IUpdateServiceControl
{
    public ServiceControllerStatus Status { get; set; }
    public int Starts, Stops;
    public bool FailWait;
    public void Start() { Starts++; Status = ServiceControllerStatus.StartPending; }
    public void Stop() { Stops++; Status = ServiceControllerStatus.StopPending; }
    public void Wait(ServiceControllerStatus status, TimeSpan timeout)
    {
        if (timeout.TotalSeconds != 30) throw new Exception("Missing bounded service wait");
        if (FailWait) throw new System.ServiceProcess.TimeoutException();
        Status = status;
    }
}
