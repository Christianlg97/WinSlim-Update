$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$compiler = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1
if (!$compiler) { throw 'No se encontro el compilador Roslyn.' }
$output = Join-Path $source 'wumgr/bin/Regression'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$interop = Join-Path $source 'wumgr/lib/Interop.WUApiLib.dll'
Copy-Item -LiteralPath $interop -Destination $output -Force
$windowsBase = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/WPF/WindowsBase.dll'
$binary = Join-Path $output 'SearchRegression.exe'
& $compiler /nologo /target:exe "/out:$binary" "/r:$interop" "/r:$windowsBase" /r:System.Windows.Forms.dll /r:System.ServiceProcess.dll (Join-Path $PSScriptRoot 'SearchRegression.cs') (Join-Path $source 'wumgr/Common/WindowsUpdateServiceRecovery.cs')
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron compilar las pruebas de busqueda.' }
& $binary (Join-Path $source 'wumgr/bin/Release/WinSlimUpdate.exe')
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de busqueda.' }
