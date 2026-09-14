@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
title Compilador de WinSlim Update

set "ROOT=%~dp0"
set "ASSEMBLY_INFO=%ROOT%Source\wumgr\Properties\AssemblyInfo.cs"
set "SOLUTION=%ROOT%Source\wumgr.sln"
set "OUTPUT=%ROOT%Source\wumgr\bin\Release"
set "RELEASE=%ROOT%Release"

echo ============================================================
echo              Compilador de WinSlim Update
echo ============================================================
echo.

if not exist "%ASSEMBLY_INFO%" (
    echo [ERROR] No se encontró:
    echo %ASSEMBLY_INFO%
    goto :error
)

for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "$line = Select-String -LiteralPath $env:ASSEMBLY_INFO -Pattern '^^\[assembly: AssemblyFileVersion'; $line.Line -split [char]34 | Select-Object -Index 1"`) do set "CURRENT_VERSION=%%V"

if not defined CURRENT_VERSION (
    echo [ERROR] No se pudo leer la versión actual del ensamblado.
    goto :error
)

echo Versión actual: %CURRENT_VERSION%
echo.
choice /C SN /N /M "¿Quieres cambiar la versión antes de compilar? [S/N]: "
if errorlevel 2 goto :build

:ask_version
echo.
set "NEW_VERSION="
set /p "NEW_VERSION=Introduce la nueva versión (por ejemplo 3.0.4): "
if not defined NEW_VERSION (
    echo [ERROR] Debes introducir una versión.
    goto :ask_version
)

powershell -NoProfile -Command "if ($env:NEW_VERSION -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { exit 1 }; $parts = $env:NEW_VERSION.Split('.'); foreach ($part in $parts) { $number = 0; if (-not [uint16]::TryParse($part, [ref]$number)) { exit 1 } }; exit 0"
if errorlevel 1 (
    echo [ERROR] Formato no válido. Usa X.Y.Z o X.Y.Z.W, con valores entre 0 y 65535.
    goto :ask_version
)

for /f "tokens=1-4 delims=." %%A in ("%NEW_VERSION%") do (
    if "%%D"=="" (
        set "ASSEMBLY_VERSION=%%A.%%B.%%C.0"
    ) else (
        set "ASSEMBLY_VERSION=%%A.%%B.%%C.%%D"
    )
)

powershell -NoProfile -Command "$path = $env:ASSEMBLY_INFO; $text = Get-Content -LiteralPath $path -Raw; $version = $env:ASSEMBLY_VERSION; $text = [regex]::Replace($text, '(?m)^\[assembly: AssemblyVersion\(\"[^\"]+\"\)\]\r?$', '[assembly: AssemblyVersion(\"' + $version + '\")]'); $text = [regex]::Replace($text, '(?m)^\[assembly: AssemblyFileVersion\(\"[^\"]+\"\)\]\r?$', '[assembly: AssemblyFileVersion(\"' + $version + '\")]'); [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))"
if errorlevel 1 (
    echo [ERROR] No se pudo actualizar AssemblyInfo.cs.
    goto :error
)

set "CURRENT_VERSION=%ASSEMBLY_VERSION%"
echo Versión actualizada a: %CURRENT_VERSION%

:build
echo.
echo Buscando MSBuild...
set "MSBUILD="
if exist "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if not defined MSBUILD if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" set "MSBUILD=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"

if not defined MSBUILD (
    echo [ERROR] No se encontró MSBuild.
    echo Instala Visual Studio Build Tools con las herramientas de escritorio de .NET.
    goto :error
)

echo MSBuild: %MSBUILD%
echo Compilando WinSlim Update %CURRENT_VERSION% en modo Release...
echo.
"%MSBUILD%" "%SOLUTION%" /t:Rebuild /p:Configuration=Release /v:minimal
if errorlevel 1 (
    echo.
    echo [ERROR] La compilación no se completó correctamente.
    goto :error
)

if not exist "%OUTPUT%\WinSlimUpdate.exe" (
    echo [ERROR] La compilación terminó, pero no se encontró WinSlimUpdate.exe.
    goto :error
)

echo.
echo Copiando el resultado a la carpeta Release...
copy /Y "%OUTPUT%\WinSlimUpdate.exe" "%RELEASE%\WinSlimUpdate.exe" >nul
if errorlevel 1 (
    echo [ERROR] No se pudo reemplazar Release\WinSlimUpdate.exe.
    echo Cierra WinSlim Update si está abierto y vuelve a ejecutar este compilador.
    goto :error
)
if exist "%OUTPUT%\WinSlimUpdate.pdb" copy /Y "%OUTPUT%\WinSlimUpdate.pdb" "%RELEASE%\WinSlimUpdate.pdb" >nul
if exist "%OUTPUT%\WinSlimUpdate.exe.config" copy /Y "%OUTPUT%\WinSlimUpdate.exe.config" "%RELEASE%\WinSlimUpdate.exe.config" >nul

echo.
echo ============================================================
echo Compilación completada correctamente.
echo Versión: %CURRENT_VERSION%
echo Archivo: %RELEASE%\WinSlimUpdate.exe
echo ============================================================
echo.
pause
exit /b 0

:error
echo.
pause
exit /b 1
