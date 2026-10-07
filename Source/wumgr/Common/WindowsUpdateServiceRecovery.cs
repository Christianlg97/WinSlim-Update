using System;
using System.ServiceProcess;

namespace wumgr
{
    internal interface IUpdateServiceControl
    {
        ServiceControllerStatus Status { get; }
        void Stop();
        void Start();
        void Wait(ServiceControllerStatus status, TimeSpan timeout);
    }

    internal static class WindowsUpdateServiceRecovery
    {
        private sealed class ServiceControl : IUpdateServiceControl, IDisposable
        {
            private readonly ServiceController service = new ServiceController("wuauserv");
            public ServiceControllerStatus Status { get { service.Refresh(); return service.Status; } }
            public void Stop()
            {
                ServiceController[] dependencies = service.DependentServices;
                try
                {
                    foreach (ServiceController dependency in dependencies)
                        if (dependency.Status != ServiceControllerStatus.Stopped)
                            throw new InvalidOperationException("Hay un servicio dependiente activo; no se forzará su parada.");
                    service.Stop();
                }
                finally { foreach (ServiceController dependency in dependencies) dependency.Dispose(); }
            }
            public void Start() { service.Start(); }
            public void Wait(ServiceControllerStatus status, TimeSpan timeout) { service.WaitForStatus(status, timeout); }
            public void Dispose() { service.Dispose(); }
        }

        internal static void Restart(Action<string> log)
        {
            using (ServiceControl service = new ServiceControl())
                Restart(service, log);
        }

        internal static void Restart(IUpdateServiceControl service, Action<string> log)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(30);
            log("Preparando el servicio de Windows Update para la búsqueda.");
            if (service.Status == ServiceControllerStatus.StartPending)
                service.Wait(ServiceControllerStatus.Running, timeout);
            if (service.Status == ServiceControllerStatus.StopPending)
                service.Wait(ServiceControllerStatus.Stopped, timeout);
            if (service.Status != ServiceControllerStatus.Stopped)
            {
                log("Deteniendo el servicio de Windows Update...");
                service.Stop();
                service.Wait(ServiceControllerStatus.Stopped, timeout);
            }
            log("Iniciando el servicio de Windows Update...");
            service.Start();
            service.Wait(ServiceControllerStatus.Running, timeout);
            log("Servicio de Windows Update preparado.");
        }
    }
}
