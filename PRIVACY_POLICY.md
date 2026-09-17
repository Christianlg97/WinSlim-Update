# Política de privacidad

WinSlim Update no incluye telemetría propia, cuentas de usuario ni un servicio remoto administrado por el proyecto. La información sobre el equipo y las actualizaciones se procesa localmente.

Para realizar las funciones solicitadas por el usuario, la aplicación puede conectarse a:

- Los servicios de Microsoft utilizados por Windows Update.
- Las fuentes de WinGet y los servidores de los fabricantes de aplicaciones.
- GitHub, únicamente cuando la instalación admite WinSlim OTAs y se consulta o aplica una release: el manifiesto `ota-manifest.json` del repositorio `Christianlg97/WinSlim11_OTAs` en raw.githubusercontent.com, su API de releases como respaldo y las descargas de github.com. La última lista de OTAs consultada se guarda localmente en `ota-cache.json`, junto al programa.

La aplicación no envía deliberadamente datos personales a un servidor propio. Los servicios externos reciben la información técnica habitual de una conexión HTTP, de acuerdo con sus propias políticas.

Los paquetes OTA se almacenan temporalmente bajo `%TEMP%\WinSlimUpdate`, se ejecutan de forma local y se eliminan cuando finaliza la operación.
