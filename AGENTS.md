# Entrada para agentes

WinSlim Update: C# / WinForms / .NET Framework 4.6.1 / Windows. Solucion: `Source/wumgr.sln`.

Antes de explorar, consulta **solo la seccion relevante** de [docs/AI_MAP.md](docs/AI_MAP.md). Contiene rutas, simbolos, flujos, dependencias y comprobaciones. No leas todo el codigo ni toda la documentacion al iniciar un chat. Verifica los simbolos del mapa con `rg` y lee el bloque afectado y sus consumidores; el codigo es la autoridad.

Conserva funcionalidad, estetica, UAC y compatibilidad salvo peticion expresa. `WuMgr.cs`, `ModernUi.cs`, `PackageUpdates.cs`, `OtaUpdates.cs` y el Designer son partes de la misma clase parcial. Los controles heredados siguen teniendo eventos y estado; no los elimines por parecer ocultos. No cambies configuracion real de Windows para validar una correccion.

Compilar: `powershell -NoProfile -ExecutionPolicy Bypass -File Source/build-release.ps1`. Puede instalar Build Tools y descargar referencias oficiales si faltan. `-NoInstall` impide ambas acciones; `-CopyToRelease` prepara la distribucion con sus DLL. Pruebas locales: `powershell -NoProfile -ExecutionPolicy Bypass -File Source/tests/run-regressions.ps1`.

Ignora `bin/`, `obj/`, `.build-deps/` y ejecutables durante la exploracion. No interpretes archivos INI/cache del directorio bin como valores predeterminados. Al cambiar responsabilidades, flujos, dependencias o validaciones, actualiza la seccion correspondiente del mapa. No dupliques este indice en otros documentos.
