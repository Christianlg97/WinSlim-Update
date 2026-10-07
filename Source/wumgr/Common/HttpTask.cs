using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;


class HttpTask
{
    //const int DefaultTimeout = 2 * 60 * 1000; // 2 minutes timeout
    const int BUFFER_SIZE = 81920;
    private byte[] BufferRead;
    private volatile HttpWebRequest request;
    private HttpWebResponse response;
    private Stream streamResponse;
    private Stream streamWriter;
    private Dispatcher mDispatcher;
    private string mUrl;
    private string mDlPath;
    private string mDlName;
    private long mLength = -1;
    private long mOffset = -1;
    private volatile bool Canceled = false;
    private DateTime lastTime;
    private bool allowSegments;
    private volatile CancellationTokenSource segmentedCancellation;
    private readonly object progressLock = new object();

    public string DlPath { get { return mDlPath; } }
    public string DlName { get { return mDlName; } }

    public HttpTask(string Url, string DlPath, string DlName = null, bool Update = false)
    {
        mUrl = Url;
        mDlPath = DlPath;
        mDlName = DlName;
        allowSegments = Update;

        BufferRead = null;
        request = null;
        response = null;
        streamResponse = null;
        streamWriter = null;
        mDispatcher = Dispatcher.CurrentDispatcher;
    }

    // Abort the request if the timer fires.
    /*private static void TimeoutCallback(object state, bool timedOut)
    {
        if (timedOut)
        {
            HttpWebRequest request = state as HttpWebRequest;
            if (request != null)
                request.Abort();
        }
    }*/

    public bool Start()
    {
        Canceled = false;
        return StartRequest();
    }

    private bool StartRequest()
    {
        if (Canceled) return false;
        try
        {
            TimeSpan delay = SegmentedHttpDownload.RetryDelay(new Uri(mUrl));
            if (delay > TimeSpan.FromSeconds(60))
            {
                AppLog.Line("El servidor limita las descargas; quedan {0:F0} s antes de poder reintentar.", delay.TotalSeconds);
                return false;
            }
            if (delay > TimeSpan.Zero)
            {
                CancellationTokenSource waiting = new CancellationTokenSource();
                segmentedCancellation = waiting;
                if (Canceled) waiting.Cancel();
                AppLog.Line("Respetando el tiempo de espera del servidor ({0:F0} s).", delay.TotalSeconds);
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delay, waiting.Token).ConfigureAwait(false);
                        if (segmentedCancellation == waiting) segmentedCancellation = null;
                        if (!StartRequest()) mDispatcher.Invoke(new Action(() => Finish(0, Canceled ? -1 : -2)));
                    }
                    catch (Exception error) { mDispatcher.Invoke(new Action(() => Finish(0, Canceled ? -1 : -3, error))); }
                    finally
                    {
                        if (segmentedCancellation == waiting) segmentedCancellation = null;
                        waiting.Dispose();
                    }
                });
                return true;
            }
            // Create a HttpWebrequest object to the desired URL.
            request = (HttpWebRequest)WebRequest.Create(mUrl);
            if (Canceled)
            {
                request.Abort();
                return false;
            }
            //myHttpWebRequest.AllowAutoRedirect = false;

            /**
                * If you are behind a firewall and you do not have your browser proxy setup
                * you need to use the following proxy creation code.

                // Create a proxy object.
                WebProxy myProxy = new WebProxy();

                // Associate a new Uri object to the _wProxy object, using the proxy address
                // selected by the user.
                myProxy.Address = new Uri("http://myproxy");


                // Finally, initialize the Web request object proxy property with the _wProxy
                // object.
                myHttpWebRequest.Proxy=myProxy;
                ***/

            BufferRead = new byte[BUFFER_SIZE];
            mOffset = 0;
            mLength = -1;
            mOldPercent = -1;

            // Start the asynchronous request.
            IAsyncResult result = (IAsyncResult)request.BeginGetResponse(new AsyncCallback(RespCallback), this);

            // this line implements the timeout, if there is a timeout, the callback fires and the request becomes aborted
            //ThreadPool.RegisterWaitForSingleObject(result.AsyncWaitHandle, new WaitOrTimerCallback(TimeoutCallback), request, DefaultTimeout, true);          
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine("\nMain Exception raised!");
            Console.WriteLine("\nMessage:{0}", e.Message);
        }
        return false;
    }

    public void Cancel()
    {
        Canceled = true;
        CancellationTokenSource segmented = segmentedCancellation;
        if (segmented != null)
            try { segmented.Cancel(); } catch (ObjectDisposedException) { }
        HttpWebRequest active = request;
        if (active != null)
            active.Abort();
    }

    private void Finish(int Success, int ErrCode, Exception Error = null)
    {
        if (Canceled)
            Success = 0;
        // Release the HttpWebResponse resource.
        if (streamWriter != null)
            streamWriter.Close();
        if (streamResponse != null)
            streamResponse.Close();
        if (response != null)
            response.Close();
        response = null;
        request = null;
        streamResponse = null;
        streamWriter = null;
        BufferRead = null;

        if (Success == 1)
        {
            try
            {
                if (File.Exists(mDlPath + @"\" + mDlName))
                    File.Delete(mDlPath + @"\" + mDlName);
                File.Move(mDlPath + @"\" + mDlName + ".tmp", mDlPath + @"\" + mDlName);
            }
            catch
            {
                AppLog.Line("No se pudo preparar la descarga {0}", mDlPath + @"\" + mDlName + ".tmp");
                mDlName += ".tmp";
            }

            try { File.SetLastWriteTime(mDlPath + @"\" + mDlName, lastTime); } catch { } // set last mod time
        }
        else if (Success == 2)
        {
            AppLog.Line("El archivo ya está descargado: {0}", mDlPath + @"\" + mDlName);
        }
        else
        {
            try { File.Delete(mDlPath + @"\" + mDlName + ".tmp"); } catch { } // delete partial file
            AppLog.Line("No se pudo descargar el archivo: {0}", mDlPath + @"\" + mDlName);
        }

        if (Finished != null)
            Finished(this, new FinishedEventArgs(Success > 0 ? 0 : Canceled ? -1 : ErrCode, Error));
    }

    static public string GetNextTempFile(string path, string baseName)
    {
        for (int i = 0; i < 10000; i++)
        {
            if (!File.Exists(path + @"\" + baseName + "_" + i + ".tmp"))
                return baseName + "_" + i;
        }
        return baseName;
    }

    private static void RespCallback(IAsyncResult asynchronousResult)
    {
        int Success = 0;
        int ErrCode = 0;
        Exception Error = null;
        HttpTask task = (HttpTask)asynchronousResult.AsyncState;
        try
        {
            // State of request is asynchronous.
            task.response = (HttpWebResponse)task.request.EndGetResponse(asynchronousResult);

            ErrCode = (int)task.response.StatusCode;

            Console.WriteLine("The server at {0} returned {1}", task.response.ResponseUri, task.response.StatusCode);

            string fileName = Path.GetFileName(task.response.ResponseUri.LocalPath);
            task.lastTime = DateTime.Now;

            Console.WriteLine("With headers:");
            foreach (string key in task.response.Headers.AllKeys)
            {
                Console.WriteLine("\t{0}:{1}", key, task.response.Headers[key]);

                if (key.Equals("Content-Length", StringComparison.CurrentCultureIgnoreCase))
                {
                    task.mLength = task.response.ContentLength;
                }
                else if (key.Equals("Content-Disposition", StringComparison.CurrentCultureIgnoreCase))
                {
                    string cd = task.response.Headers[key];
                    int nameStart = cd.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                    if (nameStart >= 0)
                        fileName = Path.GetFileName(cd.Substring(nameStart + 9).Split(';')[0].Trim().Trim('"'));
                }
                else if (key.Equals("Last-Modified", StringComparison.CurrentCultureIgnoreCase))
                {
                    task.lastTime = task.response.LastModified;
                }
            }

            //Console.WriteLine(task.lastTime);

            if (task.mDlName == null)
                task.mDlName = fileName;

            FileInfo testInfo = new FileInfo(task.mDlPath + @"\" + task.mDlName);
            if (testInfo.Exists && testInfo.LastWriteTime == task.lastTime && testInfo.Length == task.mLength)
            {
                task.request.Abort();
                Success = 2;
            }
            else
            {
                // prepare download filename
                if (!Directory.Exists(task.mDlPath))
                    Directory.CreateDirectory(task.mDlPath);
                if (task.mDlName.Length == 0 || task.mDlName[0] == '?')
                    task.mDlName = GetNextTempFile(task.mDlPath, "Download");

                FileInfo info = new FileInfo(task.mDlPath + @"\" + task.mDlName + ".tmp");
                if (info.Exists)
                    info.Delete();

                if (task.TryStartSegmented(info.FullName))
                    return;

                // Read the response into a Stream object.
                task.streamResponse = task.response.GetResponseStream();

                task.streamWriter = info.OpenWrite();

                // Begin the Reading of the contents of the HTML page and print it to the console.
                task.streamResponse.BeginRead(task.BufferRead, 0, BUFFER_SIZE, new AsyncCallback(ReadCallBack), task);
                return;
            }
        }
        catch (WebException e)
        {
            if (e.Response != null)
            {
                HttpWebResponse limited = e.Response as HttpWebResponse;
                if (limited != null && ((int)limited.StatusCode == 429 || limited.StatusCode == HttpStatusCode.ServiceUnavailable) &&
                    SegmentedHttpDownload.IsMicrosoftHost(limited.ResponseUri))
                {
                    TimeSpan delay = SegmentedHttpDownload.ParseRetryAfter(limited.Headers[HttpResponseHeader.RetryAfter]);
                    SegmentedHttpDownload.RegisterCooldown(limited.ResponseUri, delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(5));
                }
                string fileName = Path.GetFileName(e.Response.ResponseUri.AbsolutePath.ToString());

                if (task.mDlName == null)
                    task.mDlName = fileName;

                // An HTTP error does not prove that an existing file matches
                // the requested version. Keep it, but report the failed transfer.
                e.Response.Close();
            }

            if(Success == 0)
            {
                ErrCode = -2;
                Error = e;
                Console.WriteLine("\nRespCallback Exception raised!");
                Console.WriteLine("\nMessage:{0}", e.Message);
                Console.WriteLine("\nStatus:{0}", e.Status);
            }
        }
        catch (Exception e)
        {
            ErrCode = -2;
            Error = e;
            Console.WriteLine("\nRespCallback Exception raised!");
            Console.WriteLine("\nMessage:{0}", e.Message);
        }
        task.mDispatcher.Invoke(new Action(() => {
            task.Finish(Success, ErrCode, Error);
        }));
    }

    private int mOldPercent = -1;

    private bool TryStartSegmented(string temporaryPath)
    {
        string etag = response.Headers[HttpResponseHeader.ETag];
        Uri uri = response.ResponseUri;
        if (!allowSegments || mLength < SegmentedHttpDownload.MinimumLength ||
            !SegmentedHttpDownload.IsMicrosoftHost(uri) || !SegmentedHttpDownload.HasStrongETag(etag) ||
            !string.IsNullOrEmpty(response.ContentEncoding))
            return false;
        allowSegments = false;
        response.Close();
        request.Abort();
        response = null;
        request = null;
        CancellationTokenSource cancellation = new CancellationTokenSource();
        segmentedCancellation = cancellation;
        if (Canceled) cancellation.Cancel();
        Task.Run(async () =>
        {
            try
            {
                SegmentedDownloadResult result = await SegmentedHttpDownload.DownloadAsync(uri, temporaryPath,
                    mLength, etag, cancellation.Token, bytes =>
                    {
                        lock (progressLock)
                        {
                            int percent = (int)Math.Min(100, bytes * 100.0 / mLength);
                            if (percent <= mOldPercent) return;
                            mOldPercent = percent;
                            mDispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!Canceled && segmentedCancellation == cancellation && Progress != null)
                                    Progress(this, new ProgressEventArgs(percent));
                            }));
                        }
                    }).ConfigureAwait(false);
                if (result.Downloaded)
                {
                    AppLog.Line("Descarga HTTP adaptativa: {0} conexión(es), rangos y longitud verificados.", result.Connections);
                    mDispatcher.Invoke(new Action(() => Finish(1, 0)));
                }
                else
                {
                    AppLog.Line(result.FallbackReason ?? "Se usará la descarga HTTP simple.");
                    if (result.RetryAfter > TimeSpan.FromSeconds(60))
                        throw new IOException("El servidor limita las descargas; respeta Retry-After antes de reintentar.");
                    if (result.RetryAfter > TimeSpan.Zero)
                        await Task.Delay(result.RetryAfter, cancellation.Token).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    segmentedCancellation = null;
                    if (!StartRequest())
                        mDispatcher.Invoke(new Action(() => Finish(0, Canceled ? -1 : -2)));
                }
            }
            catch (Exception error)
            {
                mDispatcher.Invoke(new Action(() => Finish(0, Canceled ? -1 : -3, error)));
            }
            finally
            {
                if (segmentedCancellation == cancellation) segmentedCancellation = null;
                cancellation.Dispose();
            }
        });
        return true;
    }

    private static void ReadCallBack(IAsyncResult asyncResult)
    {
        int Success = 0;
        int ErrCode = 0;
        Exception Error = null;
        HttpTask task = (HttpTask)asyncResult.AsyncState;
        try
        {
            int read = task.streamResponse.EndRead(asyncResult);
            // Read the HTML page and then print it to the console.
            if (read > 0)
            {
                task.streamWriter.Write(task.BufferRead, 0, read);
                task.mOffset += read;

                int Percent = task.mLength > 0 ? (int)((Int64)100 * task.mOffset / task.mLength) : -1;
                if (Percent != task.mOldPercent)
                {
                    task.mOldPercent = Percent;
                    task.mDispatcher.Invoke(new Action(() => {
                        if (task.Progress != null)
                            task.Progress(task, new ProgressEventArgs(Percent));
                    }));
                }

                // setup next read
                task.streamResponse.BeginRead(task.BufferRead, 0, BUFFER_SIZE, new AsyncCallback(ReadCallBack), task);
                return;
            }
            else
            {
                // this is done on finisch
                //task.streamWriter.Close();
                //task.streamResponse.Close();
                if (task.Canceled)
                    throw new OperationCanceledException();
                if (task.mLength >= 0 && task.mOffset != task.mLength)
                    throw new EndOfStreamException("La descarga terminó antes de recibir el archivo completo.");
                Success = 1;
            }

        }
        catch (Exception e)
        {
            ErrCode = -3;
            Error = e;
            Console.WriteLine("\nReadCallBack Exception raised!");
            Console.WriteLine("\nMessage:{0}", e.Message);
        }
        task.mDispatcher.Invoke(new Action(() => {
            task.Finish(Success, ErrCode, Error);
        }));
    }

    public class FinishedEventArgs : EventArgs
    {
        public FinishedEventArgs(int ErrCode = 0, Exception Error = null)
        {
            this.ErrCode = ErrCode;
            this.Error = Error;
        }
        public string GetError()
        {
            if (Error != null)
                return Error.ToString();
            switch(ErrCode)
            {
                case 0: return "Ok";
                case -1: return "Canceled";
                default: return ErrCode.ToString();
            }
        }
        public bool Success { get { return ErrCode == 0; } }
        public bool Cancelled { get { return ErrCode == -1; } }

        public int ErrCode = 0;
        public Exception Error = null;
    }
    public event EventHandler<FinishedEventArgs> Finished;

    public class ProgressEventArgs : EventArgs
    {
        public ProgressEventArgs(int Percent)
        {
            this.Percent = Percent;
        }
        public int Percent = 0;
    }
    public event EventHandler<ProgressEventArgs> Progress;
}
