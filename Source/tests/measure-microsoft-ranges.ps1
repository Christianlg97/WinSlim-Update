# Explicit opt-in network benchmark. Transfers 24.25 MiB from Microsoft's CDN.
$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$compiler = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'No se encontro Roslyn.' }
$output = Join-Path $PSScriptRoot '../wumgr/bin/Regression'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$binary = Join-Path $output 'MicrosoftRangeBenchmark.exe'
& $compiler /nologo /target:exe "/out:$binary" (Join-Path $PSScriptRoot 'MicrosoftRangeBenchmark.cs')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el benchmark.' }
& $binary
if ($LASTEXITCODE -ne 0) { throw 'No se pudo completar la medicion acotada.' }
