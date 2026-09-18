@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul
title Compilador de WinSlim Update

set "ROOT=%~dp0"
set "ASSEMBLY_INFO=%ROOT%Source\wumgr\Properties\AssemblyInfo.cs"
set "PROJECT=%ROOT%Source\wumgr\wumgr.csproj"
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
echo Buscando MSBuild...
set "MSBUILD="
set "VSROOT="
set "VSINSTALLER=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer"
if not exist "%VSINSTALLER%\vswhere.exe" set "VSINSTALLER=%ProgramFiles%\Microsoft Visual Studio\Installer"
if exist "%VSINSTALLER%\vswhere.exe" (
    for /f "usebackq delims=" %%R in (`call "%VSINSTALLER%\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath`) do set "VSROOT=%%R"
    for /f "usebackq delims=" %%M in (`call "%VSINSTALLER%\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%M"
)
if not defined MSBUILD for %%E in (BuildTools Community Professional Enterprise) do (
    if not defined MSBUILD if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\2022\%%E\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles(x86)%\Microsoft Visual Studio\2022\%%E\MSBuild\Current\Bin\MSBuild.exe"
    if not defined MSBUILD if exist "%ProgramFiles%\Microsoft Visual Studio\2022\%%E\MSBuild\Current\Bin\MSBuild.exe" set "MSBUILD=%ProgramFiles%\Microsoft Visual Studio\2022\%%E\MSBuild\Current\Bin\MSBuild.exe"
)

if not defined MSBUILD (
    echo [ERROR] No se encontró el MSBuild de Visual Studio 2022.
    echo Instala Visual Studio Build Tools con la carga de trabajo "Herramientas de compilación de escritorio de .NET".
    echo El MSBuild antiguo de %WINDIR%\Microsoft.NET no sirve: su compilador de C# 5 no admite el código actual.
    goto :error
)
echo MSBuild: %MSBUILD%

rem Sin el targeting pack del .NET Framework del proyecto, MSBuild falla con MSB3644.
set "TARGET_FX="
for /f "usebackq delims=" %%T in (`powershell -NoProfile -Command "$m = Select-String -LiteralPath $env:PROJECT -Pattern '<TargetFrameworkVersion>(v[0-9.]+)<' | Select-Object -First 1; if ($m) { $m.Matches[0].Groups[1].Value }"`) do set "TARGET_FX=%%T"
set "REFASM="
if defined TARGET_FX set "FX_NUMBER=%TARGET_FX:v=%"
if defined TARGET_FX set "REFASM=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\%TARGET_FX%"
if defined TARGET_FX if not exist "%REFASM%\mscorlib.dll" set "REFASM=%ProgramFiles%\Reference Assemblies\Microsoft\Framework\.NETFramework\%TARGET_FX%"
if defined TARGET_FX if not exist "%REFASM%\mscorlib.dll" (
    echo [ERROR] No está instalado el targeting pack de .NET Framework %FX_NUMBER%, necesario para compilar.
    echo Abre Visual Studio Installer, pulsa Modificar y marca el componente individual
    echo ".NET Framework %FX_NUMBER% targeting pack", o ejecuta como administrador:
    if defined VSROOT echo   "%VSINSTALLER%\setup.exe" modify --installPath "%VSROOT%" --add Microsoft.Net.Component.%FX_NUMBER%.TargetingPack --passive --norestart
    goto :error
)

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
if not exist "%RELEASE%" mkdir "%RELEASE%"
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
