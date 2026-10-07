@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
title Compilador de WinSlim Update

set "ROOT=%~dp0"
set "ASSEMBLY_INFO=%ROOT%Source\wumgr\Properties\AssemblyInfo.cs"
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

powershell -NoProfile -Command "if ($env:NEW_VERSION -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { exit 1 }; $parts = $env:NEW_VERSION.Split('.'); foreach ($part in $parts) { $number = 0; if (-not [uint16]::TryParse($part, [ref]$number) -or $number -ge 65535) { exit 1 } }; exit 0"
if errorlevel 1 (
    echo [ERROR] Formato no válido. Usa X.Y.Z o X.Y.Z.W, con valores entre 0 y 65534.
    goto :ask_version
)

for /f "tokens=1-4 delims=." %%A in ("%NEW_VERSION%") do (
    if "%%D"=="" (
        set "ASSEMBLY_VERSION=%%A.%%B.%%C.0"
    ) else (
        set "ASSEMBLY_VERSION=%%A.%%B.%%C.%%D"
    )
)

rem AssemblyInfo.cs se lee y se escribe como UTF-8 sin BOM. Las comillas del regex van como \x22 y [char]34
rem para no meter comillas escapadas en la orden: cmd las interpretaba y se comía el acento circunflejo del regex.
powershell -NoProfile -Command "$path = $env:ASSEMBLY_INFO; $version = $env:ASSEMBLY_VERSION; $q = [char]34; $text = [IO.File]::ReadAllText($path); $text = [regex]::Replace($text, '(?m)^\[assembly: AssemblyVersion\(\x22[^\x22]+\x22\)\]', '[assembly: AssemblyVersion(' + $q + $version + $q + ')]'); $text = [regex]::Replace($text, '(?m)^\[assembly: AssemblyFileVersion\(\x22[^\x22]+\x22\)\]', '[assembly: AssemblyFileVersion(' + $q + $version + $q + ')]'); [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))"
if errorlevel 1 (
    echo [ERROR] No se pudo actualizar AssemblyInfo.cs.
    goto :error
)

set "CURRENT_VERSION=%ASSEMBLY_VERSION%"
echo Versión actualizada a: %CURRENT_VERSION%

:build
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%Source\build-release.ps1" -Configuration Release -CopyToRelease
if errorlevel 1 goto :error

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
