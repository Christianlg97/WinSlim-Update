param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$msbuildCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
)
$msbuild = $msbuildCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $msbuild) {
    throw "No se encontró MSBuild. Instala Visual Studio Build Tools con el desarrollo de escritorio de .NET."
}

& $msbuild (Join-Path $projectRoot "wumgr.sln") /t:Rebuild /p:Configuration=$Configuration /m
if ($LASTEXITCODE -ne 0) {
    throw "La compilación no se completó correctamente."
}

$binary = Join-Path $projectRoot "wumgr\bin\$Configuration\WinSlimUpdate.exe"
Write-Host "Compilación creada en: $binary"
