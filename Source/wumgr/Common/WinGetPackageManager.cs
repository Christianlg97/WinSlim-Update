using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace wumgr
{
    /// <summary>
    /// Información de una actualización que WinGet ofrece para un paquete instalado.
    /// </summary>
    internal sealed class PackageUpdateInfo
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string InstalledVersion { get; set; }
        public string AvailableVersion { get; set; }
        public string Source { get; set; }
        public string Status { get; set; }
        public bool Selected { get; set; }

        public PackageUpdateInfo()
        {
            Name = string.Empty;
            Id = string.Empty;
            InstalledVersion = string.Empty;
            AvailableVersion = string.Empty;
            Source = "winget";
            Status = "Disponible";
            Selected = true;
        }
    }

    internal sealed class WinGetQueryResult
    {
        public IList<PackageUpdateInfo> Packages { get; private set; }
        public string ErrorMessage { get; private set; }
        public string DiagnosticOutput { get; private set; }

        public bool Succeeded { get { return string.IsNullOrEmpty(ErrorMessage); } }

        public WinGetQueryResult(IList<PackageUpdateInfo> packages, string errorMessage, string diagnosticOutput)
        {
            Packages = packages ?? new List<PackageUpdateInfo>();
            ErrorMessage = errorMessage ?? string.Empty;
            DiagnosticOutput = diagnosticOutput ?? string.Empty;
        }
    }

    internal sealed class WinGetOperationResult
    {
        public bool Succeeded { get; private set; }
        public int ExitCode { get; private set; }
        public string Message { get; private set; }
        public string DiagnosticOutput { get; private set; }
        public string FailureReason { get; private set; }
        public string ErrorCodeExplanation { get; private set; }
        public string CommandLine { get; private set; }

        public WinGetOperationResult(bool succeeded, int exitCode, string message,
            string diagnosticOutput, string failureReason = null,
            string errorCodeExplanation = null, string commandLine = null)
        {
            Succeeded = succeeded;
            ExitCode = exitCode;
            Message = message ?? string.Empty;
            DiagnosticOutput = diagnosticOutput ?? string.Empty;
            FailureReason = failureReason ?? string.Empty;
            ErrorCodeExplanation = errorCodeExplanation ?? string.Empty;
            CommandLine = commandLine ?? string.Empty;
        }
    }

    /// <summary>
    /// Adaptación para WinSlim Update del flujo CLI del administrador WinGet de UniGetUI.
    /// UniGetUI se distribuye bajo licencia MIT; consulte THIRD_PARTY_NOTICES.md.
    /// </summary>
    internal sealed class WinGetPackageManager
    {
        private static readonly Regex AnsiEscape = new Regex(
            "\\x1B(?:[@-_]|\\[[0-?]*[ -/]*[@-~])",
            RegexOptions.Compiled);

        private readonly string executablePath;

        public WinGetPackageManager()
        {
            executablePath = ResolveExecutablePath();
        }

        public async Task<WinGetQueryResult> FindAvailableUpdatesAsync(CancellationToken cancellationToken)
        {
            ProcessResult result;
            try
            {
                result = await RunAsync(
                    "upgrade --include-unknown --accept-source-agreements --disable-interactivity",
                    cancellationToken);
            }
            catch (Win32Exception exception)
            {
                return new WinGetQueryResult(new List<PackageUpdateInfo>(),
                    "WinGet no está disponible. Instala o actualiza «Instalador de aplicación» desde Microsoft Store.",
                    exception.Message);
            }

            string diagnostic = CombineOutput(result.StandardOutput, result.StandardError);
            if (result.ExitCode != 0)
            {
                return new WinGetQueryResult(new List<PackageUpdateInfo>(),
                    BuildFailureMessage("WinGet no pudo buscar actualizaciones", result), diagnostic);
            }

            IList<PackageUpdateInfo> packages = ParseAvailableUpdates(result.StandardOutput);
            return new WinGetQueryResult(packages, string.Empty, diagnostic);
        }

        public async Task<WinGetOperationResult> UpdatePackageAsync(
            PackageUpdateInfo package, CancellationToken cancellationToken)
        {
            if (package == null)
                throw new ArgumentNullException("package");

            string selector = BuildPackageSelector(package);
            string source = string.IsNullOrWhiteSpace(package.Source)
                ? string.Empty
                : " --source " + QuoteArgument(package.Source);
            string arguments = "upgrade " + selector + source
                + " --include-unknown --accept-source-agreements --accept-package-agreements"
                + " --disable-interactivity --silent --verbose-logs";
            string commandLine = QuoteArgument(executablePath) + " " + arguments;
            DateTime operationStartedUtc = DateTime.UtcNow;

            ProcessResult result;
            try
            {
                result = await RunAsync(arguments, cancellationToken);
            }
            catch (Win32Exception exception)
            {
                string launchDiagnostic = BuildLaunchDiagnostic(package, commandLine, exception);
                return new WinGetOperationResult(false, -1,
                    "WinGet no está disponible.", launchDiagnostic,
                    "No se pudo iniciar el ejecutable de WinGet.",
                    "El sistema no pudo iniciar el proceso de WinGet.", commandLine);
            }

            string wingetLog = ReadLatestWinGetLog(operationStartedUtc);
            string installerLog = ReadRelatedInstallerLogs(
                package, operationStartedUtc, wingetLog);
            string errorCodeExplanation = ExplainWinGetExitCode(result.ExitCode);
            string diagnostic = BuildOperationDiagnostic(
                package, commandLine, result, errorCodeExplanation, wingetLog, installerLog);
            if (result.ExitCode == 0)
            {
                return new WinGetOperationResult(true, result.ExitCode,
                    "Actualizado correctamente", diagnostic, string.Empty,
                    errorCodeExplanation, commandLine);
            }

            string evidence = CombineEvidence(result.StandardOutput,
                result.StandardError, wingetLog, installerLog);
            string failureReason = GetFriendlyFailureReason(package, evidence);
            if (failureReason.Length == 0)
            {
                int installerExitCode;
                string installerExplanation;
                if (TryExplainInstallerExitCode(evidence, out installerExitCode,
                    out installerExplanation) && installerExplanation.Length > 0)
                    failureReason = installerExplanation;
                else
                    failureReason = errorCodeExplanation;
            }
            return new WinGetOperationResult(false, result.ExitCode,
                "No se pudo actualizar " + package.Name, diagnostic, failureReason,
                errorCodeExplanation, commandLine);
        }

        /// <summary>
        /// Analiza la tabla de salida de WinGet sin depender del idioma de sus encabezados.
        /// Los inicios de columna se obtienen de las posiciones de los tokens del encabezado.
        /// </summary>
        internal static IList<PackageUpdateInfo> ParseAvailableUpdates(string output)
        {
            List<PackageUpdateInfo> packages = new List<PackageUpdateInfo>();
            if (string.IsNullOrWhiteSpace(output))
                return packages;

            string cleanOutput = StripTerminalSequences(output);
            string[] lines = cleanOutput.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int[] columns = null;
            bool readingRows = false;
            string previousNonEmpty = string.Empty;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index] ?? string.Empty;
                string trimmed = line.Trim();

                if (!readingRows && IsSeparatorLine(trimmed))
                {
                    columns = GetColumnStarts(previousNonEmpty);
                    readingRows = columns != null;
                    continue;
                }

                if (!readingRows)
                {
                    if (trimmed.Length > 0)
                        previousNonEmpty = line;
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    readingRows = false;
                    previousNonEmpty = string.Empty;
                    continue;
                }

                PackageUpdateInfo package = ParseRow(line, columns);
                if (package != null)
                    packages.Add(package);
            }

            Dictionary<string, PackageUpdateInfo> unique = new Dictionary<string, PackageUpdateInfo>(
                StringComparer.OrdinalIgnoreCase);
            foreach (PackageUpdateInfo package in packages)
            {
                string key = package.Id + "|" + package.Source;
                if (!unique.ContainsKey(key))
                    unique.Add(key, package);
            }
            return unique.Values.ToList();
        }

        private static PackageUpdateInfo ParseRow(string line, int[] columns)
        {
            if (columns == null || columns.Length < 4 || line.Length <= columns[3])
                return null;

            int nameEnd = columns[1];
            int idEnd = columns[2];
            int versionEnd = columns[3];
            int availableEnd = columns.Length > 4 ? columns[4] : line.Length;

            string name = Slice(line, 0, nameEnd);
            string id = Slice(line, columns[1], idEnd);
            string installed = Slice(line, columns[2], versionEnd);
            string available = Slice(line, columns[3], availableEnd);
            string source = columns.Length > 4
                ? Slice(line, columns[4], line.Length)
                : "winget";

            if (name.Length == 0 || id.Length == 0 || installed.Length == 0 || available.Length == 0)
                return null;

            // Las líneas de resumen no alcanzan normalmente la columna de versión; esta
            // comprobación adicional evita tratarlas como paquetes si el formato cambia.
            if (id.IndexOf(' ') >= 0 || available.IndexOf(' ') >= 0)
                return null;

            return new PackageUpdateInfo
            {
                Name = name,
                Id = id,
                InstalledVersion = installed,
                AvailableVersion = available,
                Source = string.IsNullOrWhiteSpace(source) ? "winget" : source,
                Status = "Disponible",
                Selected = true
            };
        }

        private static int[] GetColumnStarts(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return null;

            MatchCollection tokens = Regex.Matches(header, @"\S+");
            if (tokens.Count < 4)
                return null;

            int idToken = -1;
            for (int index = 1; index < tokens.Count; index++)
            {
                if (string.Equals(tokens[index].Value, "id", StringComparison.OrdinalIgnoreCase))
                {
                    idToken = index;
                    break;
                }
            }

            if (idToken < 1 || tokens.Count - idToken < 3)
                idToken = Math.Max(1, tokens.Count - 4);

            List<int> starts = new List<int>();
            starts.Add(0);
            starts.Add(tokens[idToken].Index);
            starts.Add(tokens[idToken + 1].Index);
            starts.Add(tokens[idToken + 2].Index);
            if (tokens.Count > idToken + 3)
                starts.Add(tokens[idToken + 3].Index);
            return starts.ToArray();
        }

        private static bool IsSeparatorLine(string value)
        {
            if (value.Length < 12)
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] != '-' && value[index] != '─')
                    return false;
            }
            return true;
        }

        private static string Slice(string value, int start, int end)
        {
            if (string.IsNullOrEmpty(value) || start >= value.Length || start >= end)
                return string.Empty;
            int safeEnd = Math.Min(value.Length, end);
            return value.Substring(start, safeEnd - start).Trim();
        }

        private Task<ProcessResult> RunAsync(string arguments, CancellationToken cancellationToken)
        {
            return Task.Run(async delegate
            {
                cancellationToken.ThrowIfCancellationRequested();

                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = executablePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };
                    ConfigureElevatedEnvironment(process.StartInfo);

                    process.Start();
                    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                    Task<string> standardError = process.StandardError.ReadToEndAsync();

                    using (cancellationToken.Register(delegate
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill();
                        }
                        catch
                        {
                            // El proceso puede haber terminado entre HasExited y Kill.
                        }
                    }))
                    {
                        await Task.Run(delegate { process.WaitForExit(); });
                        string output = await standardOutput;
                        string error = await standardError;
                        cancellationToken.ThrowIfCancellationRequested();
                        return new ProcessResult(process.ExitCode, output, error);
                    }
                }
            });
        }

        private static void ConfigureElevatedEnvironment(ProcessStartInfo startInfo)
        {
            if (!MiscFunc.IsAdministrator())
                return;

            try
            {
                // UniGetUI evita resultados parciales de WinGet elevado usando un TEMP propio.
                string temp = Path.Combine(Path.GetTempPath(), "WinSlimUpdate", "ElevatedWinGetTemp");
                Directory.CreateDirectory(temp);
                startInfo.EnvironmentVariables["TEMP"] = temp;
                startInfo.EnvironmentVariables["TMP"] = temp;
            }
            catch (Exception exception)
            {
                AppLog.Line("No se pudo preparar la carpeta temporal de WinGet: {0}", exception.Message);
            }
        }

        private static string ResolveExecutablePath()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                string alias = Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe");
                if (File.Exists(alias))
                    return alias;
            }
            return "winget.exe";
        }

        private static string BuildPackageSelector(PackageUpdateInfo package)
        {
            if (!IsTruncated(package.Id))
                return "--id " + QuoteArgument(package.Id) + " --exact";
            if (!IsTruncated(package.Name))
                return "--name " + QuoteArgument(package.Name) + " --exact";
            return "--id " + QuoteArgument(TrimEllipsis(package.Id));
        }

        private static bool IsTruncated(string value)
        {
            return !string.IsNullOrEmpty(value)
                && (value.EndsWith("…", StringComparison.Ordinal)
                    || value.EndsWith("...", StringComparison.Ordinal));
        }

        private static string TrimEllipsis(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.EndsWith("…", StringComparison.Ordinal))
                return value.Substring(0, value.Length - 1);
            if (value.EndsWith("...", StringComparison.Ordinal))
                return value.Substring(0, value.Length - 3);
            return value;
        }

        private static string QuoteArgument(string value)
        {
            value = value ?? string.Empty;
            StringBuilder quoted = new StringBuilder(value.Length + 2);
            quoted.Append('"');
            int backslashes = 0;
            foreach (char character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1);
                    quoted.Append('"');
                }
                else
                {
                    quoted.Append('\\', backslashes);
                    quoted.Append(character);
                }
                backslashes = 0;
            }
            quoted.Append('\\', backslashes * 2);
            quoted.Append('"');
            return quoted.ToString();
        }

        /// <summary>
        /// Traduce los HRESULT públicos de WinGet a una explicación útil para el usuario.
        /// Los códigos se basan en la tabla oficial generada por "winget error --output".
        /// </summary>
        internal static string ExplainWinGetExitCode(int exitCode)
        {
            uint code = unchecked((uint)exitCode);
            switch (code)
            {
                case 0x00000000: return "La operación terminó correctamente.";
                case 0x8A150001: return "WinGet encontró un error interno. Reintenta y, si persiste, actualiza «Instalador de aplicación».";
                case 0x8A150002: return "WinGet rechazó uno o varios argumentos de la línea de comandos.";
                case 0x8A150003: return "WinGet no pudo ejecutar la operación solicitada.";
                case 0x8A150004: return "WinGet no pudo abrir el manifiesto del paquete.";
                case 0x8A150005: return "La operación fue cancelada mediante una señal de control.";
                case 0x8A150006: return "WinGet no pudo completar la instalación mediante ShellExecute: el instalador no pudo iniciarse o terminó con error. Este código es genérico; la causa concreta se obtiene de la salida y de los registros incluidos debajo.";
                case 0x8A150007: return "El manifiesto usa una versión que este cliente WinGet no admite. Actualiza «Instalador de aplicación».";
                case 0x8A150008: return "WinGet no pudo descargar el instalador. Comprueba la conexión, proxy, DNS y acceso al servidor del fabricante.";
                case 0x8A150009: return "WinGet no puede escribir en el índice porque usa una versión de esquema más nueva. Actualiza «Instalador de aplicación».";
                case 0x8A15000A: return "El índice local de WinGet está dañado.";
                case 0x8A15000B: return "La configuración de las fuentes de WinGet está dañada.";
                case 0x8A15000C: return "Ya existe una fuente de WinGet con ese nombre.";
                case 0x8A15000D: return "El tipo de la fuente de WinGet no es válido.";
                case 0x8A15000E: return "El archivo MSIX es un paquete agrupado y no un paquete individual compatible con esta operación.";
                case 0x8A15000F: return "Faltan datos necesarios de la fuente de WinGet.";
                case 0x8A150010: return "No existe un instalador aplicable a esta arquitectura, versión de Windows, ámbito o configuración del equipo.";
                case 0x8A150011: return "El hash del instalador no coincide con el manifiesto. WinGet bloqueó la instalación para proteger la integridad del equipo.";
                case 0x8A150012: return "La fuente solicitada no existe en la configuración de WinGet.";
                case 0x8A150013: return "La ubicación de esa fuente ya está configurada con otro nombre.";
                case 0x8A150014: return "WinGet no encontró un paquete que coincida con el ID o nombre solicitado.";
                case 0x8A150015: return "WinGet no tiene ninguna fuente configurada.";
                case 0x8A150016: return "La búsqueda devolvió varios paquetes y no pudo elegir uno de forma inequívoca.";
                case 0x8A150017: return "No se encontró un manifiesto compatible con los criterios del paquete.";
                case 0x8A150018: return "WinGet no pudo obtener la carpeta pública del paquete de la fuente.";
                case 0x8A150019: return "La operación requiere permisos de administrador.";
                case 0x8A15001A: return "La ubicación de la fuente no cumple los requisitos de seguridad de WinGet.";
                case 0x8A15001B: return "Microsoft Store está bloqueada por una directiva del sistema.";
                case 0x8A15001C: return "La aplicación de Microsoft Store está bloqueada por una directiva.";
                case 0x8A15001D: return "La operación depende de una función experimental desactivada en WinGet.";
                case 0x8A15001E: return "Microsoft Store no pudo instalar o actualizar la aplicación.";
                case 0x8A150028: return "La validación del manifiesto terminó con advertencias.";
                case 0x8A150029: return "El manifiesto del paquete no superó la validación.";
                case 0x8A15002A: return "El manifiesto del paquete no es válido.";
                case 0x8A15002B: return "La actualización ya no es aplicable al paquete instalado.";
                case 0x8A15002C: return "Una actualización múltiple terminó con uno o varios paquetes fallidos.";
                case 0x8A15002D: return "El instalador no superó las comprobaciones de seguridad de WinGet.";
                case 0x8A15002E: return "El tamaño descargado no coincide con el anunciado por el servidor.";
                case 0x8A150034: return "Falló la instalación de uno o varios paquetes importados.";
                case 0x8A150035: return "WinGet no encontró uno o varios de los paquetes solicitados.";
                case 0x8A150036: return "El archivo JSON usado por la operación no es válido.";
                case 0x8A150038: return "Esta versión de WinGet no admite la fuente REST configurada.";
                case 0x8A150039: return "La fuente REST devolvió datos no válidos.";
                case 0x8A15003A: return "La operación está bloqueada por una directiva de grupo.";
                case 0x8A15003B: return "La API de la fuente REST devolvió un error interno.";
                case 0x8A15003C: return "La URL de la fuente REST no es válida.";
                case 0x8A15003D: return "La fuente REST devolvió un tipo de contenido no compatible.";
                case 0x8A15003E: return "La versión del contrato de la fuente REST no es compatible.";
                case 0x8A15003F: return "Los datos de la fuente están dañados o pudieron ser manipulados.";
                case 0x8A150040: return "WinGet no pudo leer el flujo de datos descargado.";
                case 0x8A150041: return "No se aceptaron los acuerdos exigidos por el paquete.";
                case 0x8A150042: return "WinGet no pudo leer una respuesta solicitada en la consola.";
                case 0x8A150043: return "Una o varias fuentes no admiten el tipo de búsqueda solicitado.";
                case 0x8A150044: return "No se encontró el punto de acceso solicitado en la API de la fuente.";
                case 0x8A150045: return "WinGet no pudo abrir la fuente del paquete.";
                case 0x8A150046: return "No se aceptaron los acuerdos exigidos por la fuente.";
                case 0x8A150047: return "Una cabecera personalizada de la fuente supera el límite permitido.";
                case 0x8A150048: return "Falta un archivo de recursos necesario para completar la operación.";
                case 0x8A150049: return "Windows Installer (MSI) terminó con error. El código MSI concreto se interpreta también en el diagnóstico.";
                case 0x8A15004A: return "Los argumentos enviados a Windows Installer (msiexec) no son válidos.";
                case 0x8A15004B: return "WinGet no pudo abrir una o varias fuentes configuradas.";
                case 0x8A15004C: return "No se pudieron validar las dependencias del paquete.";
                case 0x8A15004D: return "Falta uno o varios paquetes necesarios para la operación.";
                case 0x8A15004F: return "La versión ofrecida no es más nueva que la instalada.";
                case 0x8A150050: return "WinGet no puede determinar la versión instalada y no recibió permiso para actualizar una versión desconocida.";
                case 0x8A150052: return "Falló la instalación de un paquete portátil.";
                case 0x8A150053: return "El volumen de destino no admite los puntos de reanálisis necesarios para el paquete portátil.";
                case 0x8A150054: return "Ya existe el mismo paquete portátil instalado desde otra fuente.";
                case 0x8A150055: return "WinGet no puede crear el enlace del paquete portátil porque la ruta ya es una carpeta.";
                case 0x8A150056: return "Este instalador prohíbe ejecutarse desde un proceso con permisos de administrador. Debe instalarse en contexto de usuario.";
                case 0x8A150057: return "Falló la desinstalación previa de un paquete portátil.";
                case 0x8A150058: return "WinGet no pudo validar la versión registrada de la aplicación instalada.";
                case 0x8A150059: return "El paquete o la versión de WinGet no admite uno de los argumentos utilizados.";
                case 0x8A15005B: return "WinGet no encontró el instalador interno esperado dentro del archivo comprimido.";
                case 0x8A15005C: return "WinGet no pudo extraer el archivo comprimido del instalador.";
                case 0x8A15005D: return "La ruta del instalador interno indicada en el manifiesto no es válida.";
                case 0x8A15005E: return "El certificado del servidor no coincide con el certificado esperado.";
                case 0x8A15005F: return "El paquete requiere especificar una ubicación de instalación.";
                case 0x8A150060: return "El análisis antimalware del archivo comprimido falló.";
                case 0x8A150061: return "El paquete ya está instalado y la operación solicitada no admite repetir la instalación.";
                case 0x8A150062: return "Ya existe una fijación de versión para este paquete.";
                case 0x8A150063: return "No existe ninguna fijación de versión para este paquete.";
                case 0x8A150064: return "WinGet no pudo abrir la base de datos de versiones fijadas.";
                case 0x8A150065: return "Una operación con varias aplicaciones terminó con una o más instalaciones fallidas.";
                case 0x8A150067: return "Una o varias consultas no devolvieron exactamente un paquete coincidente.";
                case 0x8A150068: return "El paquete tiene una versión fijada que impide actualizarlo.";
                case 0x8A150069: return "La aplicación instalada es un paquete provisional o de tipo stub.";
                case 0x8A15006A: return "WinGet recibió una orden de cierre mientras realizaba la operación.";
                case 0x8A15006B: return "No se pudieron descargar una o varias dependencias del paquete.";
                case 0x8A15006C: return "La descarga para instalación sin conexión está prohibida para este paquete.";
                case 0x8A15006D: return "Un servicio necesario está ocupado o no disponible. Inténtalo de nuevo más tarde.";
                case 0x8A150073: return "Los datos de autenticación de la fuente no son válidos.";
                case 0x8A150074: return "La fuente no admite el método de autenticación configurado.";
                case 0x8A150075: return "La autenticación con la fuente o Microsoft Store falló.";
                case 0x8A150076: return "La fuente requiere autenticación interactiva y la operación se ejecutó de forma silenciosa.";
                case 0x8A150077: return "El usuario canceló la autenticación.";
                case 0x8A150078: return "La autenticación se realizó con una cuenta distinta a la requerida por la fuente.";
                case 0x8A15007D: return "La operación no está permitida desde un proceso administrador para un paquete instalado sólo para el usuario.";
                case 0x8A15007F: return "WinGet no pudo consultar el catálogo de paquetes de Microsoft Store.";
                case 0x8A150080: return "Microsoft Store no devolvió ningún paquete aplicable a este equipo.";
                case 0x8A150081: return "WinGet no pudo obtener de Microsoft Store la información de descarga del paquete.";
                case 0x8A150082: return "Microsoft Store no devolvió una descarga aplicable para este paquete.";
                case 0x8A150083: return "WinGet no pudo recuperar la licencia del paquete de Microsoft Store.";
                case 0x8A150084: return "Este paquete de Microsoft Store no admite descarga mediante WinGet.";
                case 0x8A150085: return "La cuenta de Microsoft Entra ID no tiene permisos para recuperar la licencia de Store.";
                case 0x8A150086: return "El servidor devolvió un instalador vacío. Comprueba la conexión, proxy o disponibilidad del servidor.";
                case 0x8A15008E: return "La actualización usa una tecnología de instalación distinta a la instalación actual.";
                case 0x8A150101: return "La aplicación está abierta. Ciérrala por completo y vuelve a intentarlo.";
                case 0x8A150102: return "Ya hay otra instalación en curso. Espera a que termine o reinicia Windows.";
                case 0x8A150103: return "Uno o varios archivos que deben actualizarse están en uso.";
                case 0x8A150104: return "Falta una dependencia requerida por el paquete.";
                case 0x8A150105: return "No hay espacio libre suficiente para completar la instalación.";
                case 0x8A150106: return "No hay memoria disponible suficiente. Cierra otras aplicaciones y reintenta.";
                case 0x8A150107: return "El instalador necesita conexión a Internet y no pudo acceder a ella.";
                case 0x8A150108: return "El instalador indicó un error que requiere consultar al soporte del fabricante.";
                case 0x8A150109: return "La instalación terminó, pero es necesario reiniciar Windows para completarla.";
                case 0x8A15010A: return "Windows debe reiniciarse antes de poder instalar este paquete.";
                case 0x8A15010B: return "El instalador inició un reinicio para completar la operación.";
                case 0x8A15010C: return "El usuario canceló la instalación.";
                case 0x8A15010D: return "Ya hay otra versión de esta aplicación instalada.";
                case 0x8A15010E: return "Hay instalada una versión superior; el instalador bloqueó el downgrade.";
                case 0x8A15010F: return "Una directiva de la organización impide instalar el paquete.";
                case 0x8A150110: return "Falló la instalación de una o varias dependencias.";
                case 0x8A150111: return "Otra aplicación está usando el paquete que se intenta actualizar.";
                case 0x8A150112: return "El instalador recibió un parámetro no válido.";
                case 0x8A150113: return "El paquete no es compatible con este sistema operativo o arquitectura.";
                case 0x8A150114: return "El instalador no admite actualizar la instalación existente.";
                case 0x8A150115: return "El instalador devolvió un error específico del fabricante; consulta su salida y registro en el diagnóstico.";
            }

            if ((code & 0xFFFF0000u) == 0x80070000u)
            {
                int win32Code = (int)(code & 0x0000FFFFu);
                return "Windows devolvió el error " + win32Code + ": "
                    + new Win32Exception(win32Code).Message;
            }
            if (exitCode > 0)
                return ExplainInstallerExitCode(exitCode);
            return "WinGet devolvió un código no documentado por esta versión de WinSlim Update. La salida y los registros inferiores contienen la evidencia disponible.";
        }

        private static string CombineEvidence(params string[] values)
        {
            StringBuilder evidence = new StringBuilder();
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    evidence.AppendLine(value);
            }
            return evidence.ToString();
        }

        private static bool TryExplainInstallerExitCode(string evidence,
            out int installerExitCode, out string explanation)
        {
            installerExitCode = 0;
            explanation = string.Empty;
            if (string.IsNullOrWhiteSpace(evidence))
                return false;

            MatchCollection matches = Regex.Matches(evidence,
                @"(?:código de salida|exit code|exited with status|ShellExecute installer failed)\s*:?\s*(-?\d+)",
                RegexOptions.IgnoreCase);
            if (matches.Count == 0
                || !int.TryParse(matches[matches.Count - 1].Groups[1].Value,
                    out installerExitCode))
                return false;
            explanation = ExplainInstallerExitCode(installerExitCode);
            return true;
        }

        private static string ExplainInstallerExitCode(int exitCode)
        {
            switch (exitCode)
            {
                case 0: return "El instalador terminó correctamente.";
                case 1: return "El instalador devolvió un fallo genérico. Este número por sí solo no identifica la causa; WinSlim analiza también su salida y sus registros.";
                case 2: return "El instalador no encontró un archivo necesario.";
                case 5: return "Acceso denegado al ejecutar o escribir archivos de la instalación.";
                case 32: return "Un archivo está abierto o compartido por otro proceso.";
                case 87: return "El instalador recibió un parámetro no válido.";
                case 112: return "No hay espacio suficiente en el disco.";
                case 1223: return "El usuario canceló la operación o el aviso de elevación.";
                case 1601: return "El servicio Windows Installer no está disponible.";
                case 1602: return "El usuario canceló la instalación MSI.";
                case 1603: return "Windows Installer devolvió un error fatal genérico. El registro MSI contiene la causa concreta.";
                case 1605: return "La acción sólo es válida para un producto que ya está instalado.";
                case 1618: return "Hay otra instalación MSI en curso.";
                case 1619: return "Windows Installer no pudo abrir el paquete MSI.";
                case 1620: return "El paquete MSI está dañado o no es válido.";
                case 1638: return "Ya hay instalada otra versión del producto.";
                case 1641: return "El instalador inició un reinicio del sistema.";
                case 3010: return "La instalación terminó correctamente, pero requiere reiniciar Windows.";
                default: return "El instalador devolvió el código " + exitCode
                    + ". No existe una interpretación estándar fiable; consulta las líneas del registro cercanas al fallo.";
            }
        }

        private static string BuildLaunchDiagnostic(
            PackageUpdateInfo package, string commandLine, Exception exception)
        {
            StringBuilder report = new StringBuilder();
            AppendOperationHeader(report, package, commandLine, -1);
            report.AppendLine();
            report.AppendLine("ERROR AL INICIAR WINGET");
            report.AppendLine(exception.ToString());
            return report.ToString();
        }

        private static string BuildOperationDiagnostic(PackageUpdateInfo package,
            string commandLine, ProcessResult result, string errorCodeExplanation,
            string wingetLog, string installerLog)
        {
            StringBuilder report = new StringBuilder();
            AppendOperationHeader(report, package, commandLine, result.ExitCode);
            if (!string.IsNullOrWhiteSpace(errorCodeExplanation))
                report.AppendLine("Interpretación de WinGet: " + errorCodeExplanation);

            int installerExitCode;
            string installerExplanation;
            string evidence = CombineEvidence(result.StandardOutput,
                result.StandardError, wingetLog, installerLog);
            if (TryExplainInstallerExitCode(evidence, out installerExitCode,
                out installerExplanation))
            {
                report.AppendLine("Código del instalador: " + installerExitCode);
                report.AppendLine("Interpretación del instalador: " + installerExplanation);
            }

            string output = StripTerminalSequences(result.StandardOutput).Trim();
            string error = StripTerminalSequences(result.StandardError).Trim();
            report.AppendLine();
            report.AppendLine("SALIDA DE WINGET");
            report.AppendLine(output.Length == 0 ? "(sin salida estándar)" : output);
            if (error.Length > 0)
            {
                report.AppendLine();
                report.AppendLine("SALIDA DE ERROR DE WINGET");
                report.AppendLine(error);
            }

            if (!string.IsNullOrWhiteSpace(wingetLog))
            {
                report.AppendLine();
                report.AppendLine("REGISTRO DE WINGET");
                report.AppendLine(wingetLog.Trim());
            }

            if (!string.IsNullOrWhiteSpace(installerLog))
            {
                report.AppendLine();
                report.AppendLine("REGISTRO DEL INSTALADOR");
                report.AppendLine(installerLog.Trim());
            }
            return report.ToString();
        }

        private static void AppendOperationHeader(StringBuilder report,
            PackageUpdateInfo package, string commandLine, int exitCode)
        {
            report.AppendLine("Actualización de paquete con WinGet");
            report.AppendLine("Paquete: " + package.Name);
            report.AppendLine("ID: " + package.Id);
            report.AppendLine("Versión: " + package.InstalledVersion + " -> " + package.AvailableVersion);
            report.AppendLine("Fuente: " + package.Source);
            report.AppendLine("Administrador: " + (MiscFunc.IsAdministrator() ? "Sí" : "No"));
            report.AppendLine("Comando: " + commandLine);
            if (exitCode != -1)
            {
                report.AppendLine("Código de salida: " + exitCode
                    + " (0x" + unchecked((uint)exitCode).ToString("X8") + ")");
            }
        }

        private static string ReadLatestWinGetLog(DateTime operationStartedUtc)
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe",
                    "LocalState", "DiagOutputDir");
                if (!Directory.Exists(directory))
                    return string.Empty;

                FileInfo latest = new DirectoryInfo(directory)
                    .GetFiles("WinGet-*.log")
                    .Where(file => file.LastWriteTimeUtc >= operationStartedUtc.AddSeconds(-10))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();
                return latest == null ? string.Empty : ReadDiagnosticFile(latest.FullName);
            }
            catch (Exception exception)
            {
                return "No se pudo leer el registro de WinGet: " + exception.Message;
            }
        }

        private static string ReadRelatedInstallerLogs(
            PackageUpdateInfo package, DateTime operationStartedUtc, string wingetLog)
        {
            List<string> candidates = new List<string>();
            if (string.Equals(package.Id, "Docker.DockerDesktop",
                StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "DockerDesktop", "install-log-admin.txt"));
            }

            if (!string.IsNullOrWhiteSpace(wingetLog))
            {
                MatchCollection paths = Regex.Matches(wingetLog,
                    @"(?<path>[A-Za-z]:\\(?:[^<>:""/\\|?*\r\n]+\\)*[^<>:""/\\|?*\r\n]*?\.log)\b",
                    RegexOptions.IgnoreCase);
                foreach (Match path in paths)
                    candidates.Add(path.Groups["path"].Value.Trim());

                MatchCollection textLogs = Regex.Matches(wingetLog,
                    @"(?<path>[A-Za-z]:\\(?:[^<>:""/\\|?*\r\n]+\\)*[^<>:""/\\|?*\r\n]*?log[^<>:""/\\|?*\r\n]*?\.txt)\b",
                    RegexOptions.IgnoreCase);
                foreach (Match path in textLogs)
                    candidates.Add(path.Groups["path"].Value.Trim());
            }

            HashSet<string> readPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            StringBuilder logs = new StringBuilder();
            foreach (string candidate in candidates)
            {
                try
                {
                    string path = Path.GetFullPath(candidate);
                    if (!readPaths.Add(path)
                        || Path.GetFileName(path).StartsWith("WinGet-",
                            StringComparison.OrdinalIgnoreCase)
                        || !File.Exists(path)
                        || File.GetLastWriteTimeUtc(path) < operationStartedUtc.AddSeconds(-10))
                        continue;
                    if (logs.Length > 0)
                        logs.AppendLine();
                    logs.AppendLine(ReadDiagnosticFile(path));
                }
                catch (Exception exception)
                {
                    if (logs.Length > 0)
                        logs.AppendLine();
                    logs.AppendLine("No se pudo leer el registro relacionado "
                        + candidate + ": " + exception.Message);
                }
            }
            return logs.ToString();
        }

        private static string ReadDiagnosticFile(string path)
        {
            const int MaxCharacters = 64000;
            string content = File.ReadAllText(path);
            if (content.Length > MaxCharacters)
                content = content.Substring(content.Length - MaxCharacters);
            return "Archivo: " + path + Environment.NewLine + content;
        }

        private static string GetFriendlyFailureReason(
            PackageUpdateInfo package, string evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence))
                return string.Empty;

            if (string.Equals(package.Id, "Docker.DockerDesktop",
                StringComparison.OrdinalIgnoreCase)
                && evidence.IndexOf("incompatible version of Windows",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Match buildMatch = Regex.Match(evidence, @"(?m)^Build:\s*(\d+)");
                string build = buildMatch.Success
                    ? buildMatch.Groups[1].Value
                    : Environment.OSVersion.Version.Build.ToString();
                return "Docker Desktop " + package.AvailableVersion
                    + " no es compatible con la compilación " + build
                    + " de Windows instalada. El propio instalador exige Windows 10 22H2 "
                    + "(compilación 19045) o Windows 11 23H2 (compilación 22631), o una versión superior.";
            }

            if (ContainsAny(evidence, "incompatible version of Windows",
                "system not supported", "operating system is not supported",
                "no applicable installer"))
                return "El paquete o su instalador no es compatible con esta versión de Windows, arquitectura o configuración del equipo.";
            if (ContainsAny(evidence, "application is currently running",
                "package in use", "file in use", "files are in use",
                "being used by another process", "está siendo usado por otro proceso"))
                return "La aplicación o uno de sus archivos está en uso. Cierra la aplicación, sus procesos en segundo plano y vuelve a intentarlo.";
            if (ContainsAny(evidence, "another installation is already in progress",
                "another install is in progress", "otra instalación está en curso"))
                return "Ya hay otra instalación en curso. Espera a que termine; si quedó bloqueada, reinicia Windows.";
            if (ContainsAny(evidence, "reboot required", "restart required",
                "pending reboot", "reinicio pendiente", "debe reiniciar"))
                return "Windows tiene un reinicio pendiente o el instalador necesita reiniciar antes de continuar.";
            if (ContainsAny(evidence, "not enough disk space", "disk full",
                "no space left", "espacio insuficiente"))
                return "No hay espacio libre suficiente para descargar, extraer o instalar el paquete.";
            if (ContainsAny(evidence, "access is denied", "access denied",
                "permission denied", "requires administrator", "elevation required",
                "acceso denegado"))
                return "El instalador no pudo acceder a un archivo, carpeta o recurso protegido. La operación ya estaba elevada; revisa antivirus, directivas y permisos del destino.";
            if (ContainsAny(evidence, "hash mismatch", "hash does not match",
                "installer hash mismatch", "security check failed"))
                return "El archivo descargado no coincide con el hash o las comprobaciones de seguridad del manifiesto. WinGet bloqueó su ejecución.";
            if (ContainsAny(evidence, "download failed", "network is unreachable",
                "name resolution", "could not resolve", "connection timed out",
                "proxy authentication", "sin conexión"))
                return "No se pudo descargar el instalador. Comprueba conexión, DNS, proxy, firewall y disponibilidad del servidor del fabricante.";
            if (ContainsAny(evidence, "missing dependency", "dependency failed",
                "failed to install dependencies", "falta una dependencia"))
                return "Falta una dependencia o falló la instalación de un componente requerido por el paquete.";
            if (ContainsAny(evidence, "cancelled by user", "canceled by user",
                "operation was canceled", "operación cancelada"))
                return "La instalación fue cancelada por el usuario o por otro proceso.";
            if (ContainsAny(evidence, "newer version is already installed",
                "higher version", "downgrade", "versión superior"))
                return "Ya hay instalada una versión igual o superior y el instalador bloqueó el cambio de versión.";
            if (ContainsAny(evidence, "blocked by policy", "group policy",
                "organization policies", "bloqueada por directiva"))
                return "Una directiva de Windows o de la organización bloqueó la instalación.";

            Match prerequisite = Regex.Match(evidence,
                @"Prerequisite failed:\s*(.+)", RegexOptions.IgnoreCase);
            if (prerequisite.Success)
                return prerequisite.Groups[1].Value.Trim();
            return string.Empty;
        }

        private static bool ContainsAny(string value, params string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                if (value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string BuildFailureMessage(string prefix, ProcessResult result)
        {
            string detail = FirstUsefulLine(result.StandardError);
            if (detail.Length == 0)
                detail = LastUsefulLine(result.StandardOutput);
            if (detail.Length == 0)
                detail = "código 0x" + unchecked((uint)result.ExitCode).ToString("X8");
            return prefix + ": " + detail;
        }

        private static string FirstUsefulLine(string value)
        {
            string[] lines = StripTerminalSequences(value).Replace("\r\n", "\n").Split('\n');
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0)
                    return trimmed;
            }
            return string.Empty;
        }

        private static string LastUsefulLine(string value)
        {
            string[] lines = StripTerminalSequences(value).Replace("\r\n", "\n").Split('\n');
            for (int index = lines.Length - 1; index >= 0; index--)
            {
                string trimmed = lines[index].Trim();
                if (trimmed.Length > 0)
                    return trimmed;
            }
            return string.Empty;
        }

        private static string CombineOutput(string standardOutput, string standardError)
        {
            string output = StripTerminalSequences(standardOutput).Trim();
            string error = StripTerminalSequences(standardError).Trim();
            if (output.Length == 0)
                return error;
            if (error.Length == 0)
                return output;
            return output + Environment.NewLine + error;
        }

        private static string StripTerminalSequences(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            string withoutAnsi = AnsiEscape.Replace(value, string.Empty);
            StringBuilder clean = new StringBuilder(withoutAnsi.Length);
            foreach (char character in withoutAnsi)
            {
                if (character == '\r' || character == '\n' || character == '\t' || !char.IsControl(character))
                    clean.Append(character);
            }
            return clean.ToString();
        }

        private sealed class ProcessResult
        {
            public int ExitCode { get; private set; }
            public string StandardOutput { get; private set; }
            public string StandardError { get; private set; }

            public ProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
            }
        }
    }
}
