# Changelog

## WinSlim Update 3.0.3

- Eliminado por completo el submenú visual «Herramientas»; el menú de la aplicación muestra únicamente «Acerca de» y «Salir».
- El servicio de Windows Update se configura en inicio manual y se inicia automáticamente antes de inicializar el agente de actualizaciones.
- Retiradas las rutas de interfaz y configuración heredada que permitían detener y deshabilitar el servicio desde WinSlim Update.
- Añadida `DOCUMENTACION.md`, una guía integral de uso, arquitectura, configuración, seguridad, diagnóstico, compilación y mantenimiento del proyecto.

## WinSlim Update 3.0.2

- Retirada la opción heredada «Actualizar herramientas»: no descargaba ni actualizaba componentes, sino que solo reconstruía el menú desde la carpeta local `Tools`, por lo que no producía ningún cambio visible durante el uso normal.
- El separador de herramientas solo aparece cuando existe al menos una herramienta local que mostrar.

## WinSlim Update 3.0.1

- WinSlim Update solicita elevación UAC desde el manifiesto y ejecuta WinGet con el token de administrador heredado.
- Los fallos al actualizar paquetes abren un modal oscuro con la causa detectada, código de salida, comando, salida de WinGet y registros del instalador.
- Se interpretan los códigos oficiales de WinGet, HRESULT/Win32 y los códigos habituales de instaladores MSI o EXE; además se analizan los registros para reconocer bloqueos, reinicios pendientes, falta de espacio, permisos, red, dependencias, requisitos y otras causas comunes.
- El modal permite reintentar la actualización, copiar el diagnóstico completo o cerrar y continuar con el siguiente paquete.
- Se reconoce el rechazo de Docker Desktop por versión incompatible de Windows y se muestra el requisito de compilación de forma legible.

## WinSlim Update 3.0.0

- Nueva sección independiente **Actualizaciones de paquetes**, claramente separada de las actualizaciones del sistema.
- Detección de aplicaciones instaladas con versiones nuevas mediante WinGet, incluida la fuente, versión instalada y versión disponible.
- Selección individual o global, filtrado, agrupación por fuente y ordenación por columnas dentro del listado moderno de WinSlim Update.
- Actualización silenciosa y secuencial de los paquetes seleccionados, con cancelación, progreso y resultado por paquete.
- Flujo de WinGet adaptado del administrador de paquetes de UniGetUI y acreditado conforme a su licencia MIT.

## WinSlim Update 2.4.7

- Aumentada la separación entre el título de cada apartado y su subtítulo para evitar contactos tipográficos con escalado DPI.
- «Desinstalar» se habilita al seleccionar cualquier actualización instalada; si Windows no permite retirarla, la aplicación explica el motivo al pulsarlo.
- La columna Estado distingue entre actualizaciones que «Se puede desinstalar» y las que figuran como «No desinstalable», según la información nativa de Windows Update.
- El nuevo símbolo de WinSlim Update sustituye también al icono heredado de WuMgr en el ejecutable, la ventana, la barra de tareas, Alt+Tab y el área de notificación.

## WinSlim Update 2.4.6

- El subtítulo de actualizaciones disponibles se reformula como «Elige qué actualizaciones y controladores instalar. En WinSlim no instalamos nada sin tu permiso.».

## WinSlim Update 2.4.5

- Sustituido el monograma «W» por un logotipo vectorial de actualización en blanco, gris plateado y negro, manteniendo sus dimensiones de 36 × 36 píxeles.
- El subtítulo de actualizaciones disponibles pasa a ser «WinSlim, no instala actualizaciones sin tu permiso».

## WinSlim Update 2.4.4

- Reorganizados los créditos de «Acerca de» para presentar WinSlim Update bajo la dirección de Christian Luis González y acreditar claramente a WuMgr 1.1b de David Xanatos como base original.

## WinSlim Update 2.4.3

- Añadido Christian Luis González — WinSlim Team Project Leader a los créditos de «Acerca de», manteniendo la autoría original de David Xanatos.
- Traducidos al español los rótulos informativos del cuadro «Acerca de».
- Las columnas de las listas se pueden redimensionar arrastrando los separadores de sus encabezados, con cursor y resaltado visual específicos.
- Los anchos personalizados se conservan al navegar entre disponibles, instaladas, ocultas e historial.

## WinSlim Update 2.4.2

- Las líneas de las categorías se sustituyen por chevrones modernos situados junto al nombre.
- El chevrón apunta hacia abajo al mostrar las actualizaciones y hacia la derecha al plegarlas.
- Eliminada la flecha duplicada del extremo derecho y añadido un estado hover sobre toda la cabecera desplegable.
- Botones de minimizar, maximizar, restaurar y cerrar unificados con símbolos vectoriales al estilo de Windows 11, sin depender de fuentes externas.
- Ventana principal con esquinas redondeadas reales en modo normal y bordes completos al maximizar.
- Retirada la reparación alternativa con DISM/SFC: los fallos de Windows Update conservan su código y comportamiento nativos.

## WinSlim Update 2.4.1

- El visto del estado «Todo está al día» ahora se dibuja como vector y no puede quedar recortado por la fuente o el escalado.
- El estado de búsqueda muestra «Buscando actualizaciones...» desde que comienza la operación y durante todo su progreso.

## WinSlim Update 2.4.0

- Frontal del listado reconstruido como un control moderno propio, sin el `ListView` clásico de Windows detrás.
- Eliminada del proyecto la clase visual anterior y cualquier superposición de barras, filas o estados nativos.
- Renderizado de doble búfer para evitar destellos y restos gráficos al pasar el ratón, seleccionar o desplazarse.
- Casillas dibujadas y pulsadas desde la misma geometría, corrigiendo el desfase al marcar y desmarcar.
- Grupos, cabeceras, selección, estados, tooltips y barras de desplazamiento integrados en una única superficie.
- Rueda, arrastre vertical, desplazamiento horizontal y navegación por teclado gestionados de forma nativa por el nuevo frontal.

## WinSlim Update 2.3.4

- La barra vertical se sincroniza inmediatamente con la rueda del ratón y responde al arrastre directo.
- La barra nativa de Windows permanece oculta durante el desplazamiento para evitar superposiciones.
- Desplegables de Configuración completamente oscuros, incluida la selección y el estado abierto.
- Anchuras adaptables con márgenes seguros para que ningún desplegable sobresalga o quede recortado.
- Tooltips largos del listado rediseñados en grafito, blanco y plata.

## WinSlim Update 2.3.3

- Cabeceras de categorías simplificadas: fondo plano, texto destacado y chevrón blanco.
- Eliminados los artefactos del estilo nativo al pasar el ratón o seleccionar filas.
- Repintado estable de las categorías después de interacciones con el listado.
- Categorías normalizadas a nombres precisos y coherentes en español.

## WinSlim Update 2.3.2

- Eliminada la cabecera de grupo vacía que podía aparecer en el historial.
- El historial sin categorías se presenta automáticamente como una lista plana.
- Los grupos se reconstruyen al cambiar de sección para evitar cabeceras residuales.

## WinSlim Update 2.3.1

- Campos de filtrado más altos, redondeados y con mayor espacio interior.
- Cabeceras de categorías destacadas mediante cápsulas oscuras y fondo completamente plano.
- Barra vertical sustituida por el mismo diseño fino y permanente de la barra horizontal.
- Selector lateral ajustado para que su borde derecho nunca quede recortado.
- Los fallos de instalación muestran ahora el código exacto de Windows y la actualización afectada.
- Reparación guiada con DISM y SFC para errores del almacén de componentes, siempre con confirmación previa.
- Las instalaciones parciales conservan correctamente las actualizaciones que sí se completaron.

## WinSlim Update 2.3.0

- Interfaz acercada al lenguaje visual de UniGetUI y Windows 11.
- Acciones superiores convertidas en botones independientes, oscuros y redondeados.
- Navegación lateral rediseñada con tarjetas suaves, separación y selección plateada.
- Sustituida la barra horizontal nativa por un control oscuro, fino, permanente y sin flechas.
- Nuevo estado vacío centrado para indicar claramente cuando el equipo está al día.

## WinSlim Update 2.2.2

- Corregido el recorte de los botones y del estado de progreso en el borde inferior.
- Botones "Cancelar" y "Ver actividad" centrados, con tamaño estable ante cambios de escala.
- Indicador de progreso más fino y redondeado para evitar el aspecto de botón deformado.

## WinSlim Update 2.2.1

- Corregido el recorte del texto "Ejecutar como administrador" en Configuración.
- Los controles de inicio ahora adaptan su anchura al tamaño disponible y al escalado de pantalla.

## WinSlim Update 2.2.0

- Listado rediseñado con filas más amplias, alternancia tonal suave y separadores discretos.
- Eliminada la selección azul nativa en favor de un resaltado grafito con indicador plateado.
- Nuevas casillas oscuras, estados con indicador visual y grupos integrados en la paleta monocromática.
- Los colores personalizados de las actualizaciones ahora se mezclan suavemente con el tema oscuro.

## WinSlim Update 2.1.1

- Sustituida la barra de progreso nativa blanca y verde por una barra oscura propia con progreso plateado.
- Botón Cancelar simplificado, sin icono y con tamaño estable en cualquier escala de pantalla.
- Ajustado el estado de búsqueda para mantener la coherencia visual del tema.

## WinSlim Update 2.1.0

- Rediseño visual inspirado en Sparkle y UniGetUI.
- Nueva paleta monocromática basada en negro, grafito, blanco y plata.
- Barra de ventana propia, navegación simplificada y superficies con esquinas suaves.
- Configuración trasladada a una página independiente con dos tarjetas amplias.
- Filtro, acciones, listado y estados reorganizados para reducir el ruido visual.

## WinSlim Update 2.0.1

- Nuevo tema oscuro integral optimizado para Windows 10 y 11.
- Barra de título, pestañas, filtros, listado, controles y registro adaptados al modo oscuro.
- Contraste y estados interactivos revisados para mantener una lectura cómoda.

## WinSlim Update 2.0.0

- Nueva interfaz moderna y adaptable con navegación lateral y acciones con texto.
- Experiencia completamente en español y marca WinSlim Update.
- Flujo inicial centrado en las actualizaciones disponibles.
- Registro de actividad plegable, filtro siempre visible y soporte para DPI alto.
- Confirmación antes de desinstalar y filtrado de actualizaciones no desinstalables.
- Corrección del tratamiento de los códigos de reinicio 1641 y 3010.
- Bibliotecas de interoperabilidad incluidas para una compilación reproducible sin generar COM durante el build.
All notable changes to this project will be documented in this file.
This project adheres to [Semantic Versioning](http://semver.org/).

## [1.1] - 2019-12-11
### Added
- DpiAwareness
- Application ID column
- support for DeploymentAction='OptionalInstallation'


## [1.0] - 2019-10-19
### Added
- Added italian translation thx @irondave
- Added Brazilian Portuguese translation thx @Possessed777
- Added ini option to select language

### Fixed
- fixed minor issues with progress display

### Changed
- date format should now be proeprly localized
- improved auto check for update feature


## [0.9a+] - 2018-12-07
### Added
- Added Russian translation thx @zetcamp

## [0.9a] - 2018-12-06
### Added
- Added Japanese translation thx @Rukoto 
- Added Polish translation thx @vitos
- added select all checkbox

### Fixed
- Fixed auto update crash issue
- date formating in last searche for rupdate log
- fixed date and size sorting issue in columns

### Changed
- now ctrl+f sets cursot to the searhc box
- improved sorting, now sort order can be reversed by clicking agina on the column


## [0.8g beta] - 2018-11-1
### Added
- Added french translation thanks to Leo

### Changed
- now the WU setting is always available, and it not ato set when chagrin AU blocking options.


## [0.8] - 2018-10-23
### Fixed
- issue when uninstalling updates

## [0.8c beta] - 2018-10-21
### Added
- messge box promping for a reboot when changing update facilitator settings
- tooltips to list view for long texts

### Changed
- some buttons are now disabled when no updates are checked

### Fixed
- issue with supprot url's nor manualy generated mased on the kb number


## [0.8b beta] - 2018-10-20
### Added
- command line parameter for scripted operation, disabling configuration options -provisioned
- added search filter ctrl+f
- addec ctrl+c to copy infos about selected updates
- added option to blacklist updates by KB using the updates ini, also collor them or pre select them

Example:
[KB4023307]
BlackList=1
Remove=0
Color=#ffcccc

[KB4343909]
Select=1
Color=#ccffcc

### Changed
- updates are now cached in updates.ini inside teh downloads directory, updates.ini in the working directorty is used for persistent update informations

### Fixed
- fixed typos in transaltion thx to Carlos Detweiller and PointZero


## [0.8a beta] - 2018-10-19
### Added
- translation support

### Fixed
- crash bug in uninstall routile
- size and date columns ware out of order
- fixed some GPO related crash issues


## [0.7] - 2018-10-05
### Added
- option to disable update facilitation services
- ability to "manually" install updates

### Changed
- automatic update GPO handling, now much more user friendly
- reworked error handling to allow limited non admin operation
- reworked status codes for better ui expirience
- when download fails but the file was already downloaded in the previuse session the old file is used
- reworked UAC bypass handling

### Fixed
- windows 10 version detection
- issue when started rom a read only directory, fallback to ...\{UserProfile}\Downloads\WuMgr\
- crash bug when firewall blocks downloads
- issue client not properl abborting operations on cancesss


## [0.6b] - 2018-09-30
### Fixed
- issues only one instance restriction
- issues with list view separation


## [0.6] - 2018-09-30
### Added
- checkbox to hide the WU settings page instead of automatic operation
- when access elevation fails the tool now asks for admin rights
- added tool entry to setup/remove windows defender update task

### Changed
- ObjectListView.dll is not longer required instead a simple self contained control is used
- replaced the app icon with a nicer one

### Fixed
- issue when UAC bypass failed due to restriction to only one instance
- then starting a tool from the menu it sets working directory to the tool's directory
- fixed issues with -onclose now no console window is shown and better "" parsing is implemented


## [0.5] - 2018-09-16
### Added
- wumgr.ini is restricted to be writeble only by administrators
- added better download system, now server date is checked and files get downloaded only if there are newer ont he server
- automatic check for updates
- added support url label
- added customizable tools menu tin better integrate 3rd aprty tools, accessible from tray and the window system menu.
- added update cache such to remembet the last state between application restarts

### Changed
- UAC bypass implementation to prevent possible privilege escalation
- Cleanuped the code base
- auto start option is now called run in background, when closing the window ehen in that mode the application sontinues to run in tray

### Fixed
- fixed size display for kb sized patches
- auto update issue introduced in 0.4
- update list not updating when updates were installed/uninstalled/etc
- two instances cant longer be started at once

## [0.4] - 2018-09-08
### Added
- option to register and unregister microsoft update
- added commandline option -offline [download|no_download|download_new]
- added commandline option -online [serviceID]
- added GPO to block connections to M$ update servers on pro/home SKU's based on the "Windows Restricted Traffic Limited Functionality Baseline"
- added check to switch between "manual" download/instalation (that is done by WuMgr without using windows update facilities) and the usage of wuauserv
- added propepr icons
- automatically hiding the windows update page when update is disabled or access to M$ servers restricted
- added about dialog
- added configuration ini file

### Changed
- improved applog
- improved agent events
- fixed category and state display for history
- unifyed catalog cab and update download
- improved custom update downloader

## [0.3] - 2018-09-02
### Added
- System tray Icon
- Auto Start
- UAC bypass for administrator users
- added warning if running without window supdate service enabled
- added -console command line option to show a debug console 
- added /? command line option to show all available command line options
- added direct update download, i.e. not using the update service
- added propepr slitter for the log
- added settings saving to registry

### Fixed
- multiple errors with offlien update search
- issue with slow history loading

## [0.2] - 2018-08-26
### Added
- Add command line options compatible with wumt (wumgr -update -onclose [command])
- Add option to auto download the *.cab file for semi-offline update
- Finish the GPO groupe

## [0.1] - 2018-08-16
### Added
- Initial release
