$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
# Run the build script once without -NoInstall to provision missing dependencies.
& (Join-Path $source 'build-release.ps1') -NoInstall
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$compiler = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (-not $compiler) { throw 'No se encontro el compilador Roslyn.' }
$output = Join-Path $source 'wumgr\bin\Regression'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$binary = Join-Path $output 'RegressionTests.exe'
$windowsBase = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\WPF\WindowsBase.dll'
& $compiler /nologo /target:exe "/out:$binary" "/r:$windowsBase" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    (Join-Path $PSScriptRoot 'RegressionTests.cs') (Join-Path $source 'wumgr\Common\HttpTask.cs') (Join-Path $source 'wumgr\Common\SegmentedHttpDownload.cs') `
    (Join-Path $source 'wumgr\Common\MultiValueDictionary.cs') (Join-Path $source 'wumgr\UpdateDownloader.cs') (Join-Path $source 'wumgr\UpdateInstaller.cs')
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron compilar las pruebas.' }
$test = New-Object System.Diagnostics.Process
try {
    $test.StartInfo.FileName = $binary
    $test.StartInfo.UseShellExecute = $false
    $test.StartInfo.CreateNoWindow = $true
    $test.StartInfo.RedirectStandardOutput = $true
    $test.StartInfo.RedirectStandardError = $true
    if (-not $test.Start()) { throw 'No se pudieron iniciar las pruebas.' }
    $stdout = $test.StandardOutput.ReadToEndAsync()
    $stderr = $test.StandardError.ReadToEndAsync()
    if (-not $test.WaitForExit(60000)) { $test.Kill(); throw 'Las pruebas excedieron el tiempo limite.' }
    Write-Host $stdout.GetAwaiter().GetResult()
    if ($stderr.GetAwaiter().GetResult()) { Write-Host $stderr.GetAwaiter().GetResult() }
    if ($test.ExitCode -ne 0) { throw "Pruebas fallidas: $($test.ExitCode)" }
} finally { $test.Dispose() }
