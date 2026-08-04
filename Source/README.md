# WinSlim Update

WinSlim Update es un gestor de actualizaciones para WinSlim 10 y 11. Mantiene el motor de WuMgr y su acceso directo a la API de Windows Update, pero ofrece una interfaz moderna, clara y completamente en español.

La interfaz utiliza un tema oscuro monocromático inspirado en Sparkle y UniGetUI, con superficies negras y grafito, tipografía blanca y acciones en tonos plateados.

Desde la versión 2.4, el listado es un control propio de WinSlim Update: no utiliza ni superpone el listado clásico de Windows, por lo que casillas, filas, grupos y desplazamiento comparten el mismo sistema de dibujo e interacción.

## Funciones

- Buscar actualizaciones de Windows y otros productos de Microsoft.
- Elegir exactamente qué actualizaciones descargar o instalar.
- Consultar las actualizaciones instaladas y el historial de resultados.
- Ocultar actualizaciones y volver a mostrarlas.
- Desinstalar únicamente las actualizaciones que Windows marca como desinstalables, con confirmación previa.
- Incluir o excluir controladores.
- Usar el catálogo sin conexión `wsusscn2.cab`.
- Controlar el comportamiento automático de Windows Update mediante directivas.
- Ejecutar búsquedas periódicas en segundo plano.
- Copiar enlaces directos de descarga y consultar la página de soporte de cada KB.

## Uso

1. Ejecuta `WinSlimUpdate.exe`.
2. Acepta la solicitud de administrador; es necesaria para instalar, ocultar o desinstalar actualizaciones y para cambiar directivas.
3. Pulsa **Buscar**.
4. Marca las actualizaciones que quieras y elige **Instalar**, **Descargar** u **Ocultar**.

La aplicación no instala nada sin una acción explícita del usuario.

## Compilación

El proyecto usa Windows Forms y .NET Framework 4.6.1. Abre `wumgr.sln` en Visual Studio con las herramientas de desarrollo de escritorio de .NET, o ejecuta `build-release.ps1` desde PowerShell.

Las bibliotecas de interoperabilidad incluidas se generan a partir de las bibliotecas de tipos de Windows Update Agent y del Programador de tareas presentes en Windows. No se necesita ningún paquete externo.

## Origen y licencia

WinSlim Update está basado en [WuMgr de David Xanatos](https://github.com/DavidXanatos/wumgr), distribuido bajo GNU General Public License v3. Se conserva la licencia original en `LICENSE`; cualquier redistribución o modificación debe cumplir sus condiciones.

Los iconos originales proceden de Icons8, tal como se acredita en `wumgr/res/icons8.txt`.
