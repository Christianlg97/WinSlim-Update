$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$compiler = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'No se encontro Roslyn.' }
$output = Join-Path $source 'wumgr/bin/Regression'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$binary = Join-Path $output 'SegmentedDownloadTests.exe'
$windowsBase = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/WPF/WindowsBase.dll'
& $compiler /nologo /target:exe "/out:$binary" "/r:$windowsBase" (Join-Path $PSScriptRoot 'SegmentedDownloadTests.cs') (Join-Path $source 'wumgr/Common/HttpTask.cs') (Join-Path $source 'wumgr/Common/SegmentedHttpDownload.cs')
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron compilar las pruebas.' }
& $binary
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas segmentadas.' }
