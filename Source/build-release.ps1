param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# Hace falta el MSBuild de Visual Studio (Build Tools o IDE, en cualquier ruta): se localiza con vswhere y,
# si no está, en las rutas habituales de 2022. El MSBuild antiguo de C:\Windows\Microsoft.NET no sirve
# porque su compilador de C# 5 no admite el código actual.
$msbuild = $null
$vsRoot = $null
$installerDir = @("${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer", "$env:ProgramFiles\Microsoft Visual Studio\Installer") |
    Where-Object { Test-Path -LiteralPath (Join-Path $_ "vswhere.exe") } | Select-Object -First 1
if ($installerDir) {
    $vswhere = Join-Path $installerDir "vswhere.exe"
    $vsRoot = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1
    $msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
}
if (-not $msbuild) {
    $candidates = foreach ($root in @("${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022", "$env:ProgramFiles\Microsoft Visual Studio\2022")) {
        foreach ($edition in "BuildTools", "Community", "Professional", "Enterprise") { "$root\$edition\MSBuild\Current\Bin\MSBuild.exe" }
    }
    $msbuild = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $msbuild) {
    throw "No se encontró el MSBuild de Visual Studio 2022. Instala Visual Studio Build Tools con las herramientas de compilación de escritorio de .NET."
}

# Sin el targeting pack del .NET Framework del proyecto, MSBuild falla con MSB3644.
$project = Join-Path $projectRoot "wumgr\wumgr.csproj"
$targetFx = [regex]::Match((Get-Content -LiteralPath $project -Raw), '<TargetFrameworkVersion>(v[0-9.]+)<').Groups[1].Value
if ($targetFx) {
    $refAsm = @("${env:ProgramFiles(x86)}\Reference Assemblies", "$env:ProgramFiles\Reference Assemblies") |
        ForEach-Object { Join-Path $_ "Microsoft\Framework\.NETFramework\$targetFx\mscorlib.dll" } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $refAsm) {
        $fxNumber = $targetFx.TrimStart("v")
        $hint = "Abre Visual Studio Installer, pulsa Modificar y marca el componente individual '.NET Framework $fxNumber targeting pack'."
        if ($vsRoot) {
            $hint += " O ejecuta como administrador: `"$installerDir\setup.exe`" modify --installPath `"$vsRoot`" --add Microsoft.Net.Component.$fxNumber.TargetingPack --passive --norestart"
        }
        throw "No está instalado el targeting pack de .NET Framework $fxNumber, necesario para compilar. $hint"
    }
}

Write-Host "MSBuild: $msbuild"
& $msbuild (Join-Path $projectRoot "wumgr.sln") /t:Rebuild /p:Configuration=$Configuration /m
if ($LASTEXITCODE -ne 0) {
    throw "La compilación no se completó correctamente."
}

$binary = Join-Path $projectRoot "wumgr\bin\$Configuration\WinSlimUpdate.exe"
Write-Host "Compilación creada en: $binary"
