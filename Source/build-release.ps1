param(
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$CopyToRelease,
    [switch]$NoInstall
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$cache = Join-Path $projectRoot '.build-deps'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Find-MSBuild {
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if (-not $root) { continue }
        $vswhere = Join-Path $root 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
                Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            if ($found) { return $found }
        }
        foreach ($edition in 'BuildTools', 'Community', 'Professional', 'Enterprise') {
            $candidate = Join-Path $root "Microsoft Visual Studio\2022\$edition\MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }
}
$msbuild = Find-MSBuild
if (-not $msbuild) {
    if ($NoInstall) { throw 'No se encontro MSBuild. Ejecuta sin -NoInstall para instalar Build Tools.' }
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $bootstrapper = Join-Path $cache 'vs_buildtools.exe'
    Write-Host 'Descargando Visual Studio Build Tools desde Microsoft...'
    Invoke-WebRequest 'https://aka.ms/vs/17/release/vs_BuildTools.exe' -OutFile $bootstrapper -UseBasicParsing
    $signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
        throw 'El instalador no tiene una firma valida de Microsoft.'
    }
    $install = Start-Process -FilePath $bootstrapper -ArgumentList '--passive --wait --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools' -Wait -PassThru -WindowStyle Hidden
    if ($install.ExitCode -notin @(0, 3010)) { throw "Build Tools fallo con codigo $($install.ExitCode)." }
    $msbuild = Find-MSBuild
    if (-not $msbuild) { throw 'MSBuild sigue sin estar disponible. Reinicia si el instalador lo requiere y vuelve a compilar.' }
}
$project = Join-Path $projectRoot 'wumgr\wumgr.csproj'
$targetFx = [regex]::Match([IO.File]::ReadAllText($project), '<TargetFrameworkVersion>(v[0-9.]+)<').Groups[1].Value
if (-not $targetFx) { throw 'No se pudo determinar el framework de destino.' }
$refAsm = @(${env:ProgramFiles(x86)}, $env:ProgramFiles) |
    Where-Object { $_ } | ForEach-Object { Join-Path $_ "Reference Assemblies\Microsoft\Framework\.NETFramework\$targetFx" } |
    Where-Object { Test-Path -LiteralPath (Join-Path $_ 'mscorlib.dll') } | Select-Object -First 1
$buildArguments = @((Join-Path $projectRoot 'wumgr.sln'), '/t:Rebuild', "/p:Configuration=$Configuration", '/m', '/v:minimal')
if (-not $refAsm) {
    # Referencias oficiales en cache local, sin cambiar el framework de destino.
    $packageId = 'microsoft.netframework.referenceassemblies.net' + $targetFx.TrimStart('v').Replace('.', '')
    $packageRoot = Join-Path $cache "$packageId.1.0.3"
    $frameworkRoot = Join-Path $packageRoot 'build\.NETFramework'
    $referenceFile = Join-Path $frameworkRoot "$targetFx\mscorlib.dll"
    if (-not (Test-Path -LiteralPath $referenceFile)) {
        if ($NoInstall) { throw "Faltan las referencias de $targetFx. Ejecuta sin -NoInstall para descargarlas." }
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        $archive = Join-Path $cache "$packageId.1.0.3.zip"
        Write-Host "Descargando referencias oficiales de $targetFx desde NuGet..."
        Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$packageId/1.0.3/$packageId.1.0.3.nupkg" -OutFile $archive -UseBasicParsing
        Expand-Archive -LiteralPath $archive -DestinationPath $packageRoot -Force
        if (-not (Test-Path -LiteralPath $referenceFile)) { throw 'El paquete no contiene las referencias esperadas.' }
    }
    $buildArguments += "/p:TargetFrameworkRootPath=$frameworkRoot\"
    $buildArguments += "/p:FrameworkPathOverride=$(Join-Path $frameworkRoot $targetFx)"
}
foreach ($name in 'Interop.WUApiLib.dll', 'Interop.TaskScheduler.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot "wumgr\lib\$name"))) {
        throw "Falta wumgr\lib\$name. Restaura esta dependencia incluida en el repositorio."
    }
}
Write-Host "MSBuild: $msbuild"
& $msbuild @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'La compilacion no se completo correctamente.' }
$output = Join-Path $projectRoot "wumgr\bin\$Configuration"
$required = @('WinSlimUpdate.exe', 'WinSlimUpdate.exe.config', 'Interop.WUApiLib.dll', 'Interop.TaskScheduler.dll')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $name))) { throw "Falta un archivo de salida: $name" }
}
if ($CopyToRelease) {
    $release = Join-Path (Split-Path -Parent $projectRoot) 'Release'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    foreach ($name in $required) { Copy-Item -LiteralPath (Join-Path $output $name) -Destination $release -Force }
    $symbols = Join-Path $output 'WinSlimUpdate.pdb'
    if (Test-Path -LiteralPath $symbols) { Copy-Item -LiteralPath $symbols -Destination $release -Force }
    Write-Host "Distribucion creada en: $release"
}
Write-Host "Compilacion creada en: $(Join-Path $output 'WinSlimUpdate.exe')"
