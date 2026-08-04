using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wumgr
{
    static public class Translate
    {
        static SortedDictionary<string, string> mStrings = new SortedDictionary<string, string>();

        static public void Load(string lang = "")
        {
            if (lang == "")
                lang = "es";
            

            mStrings.Add("msg_running", "WinSlim Update ya se está ejecutando.");
            mStrings.Add("msg_admin_req", "{0} necesita permisos de administrador para instalar actualizaciones.");
            mStrings.Add("msg_ro_wrk_dir", "No se puede escribir en la carpeta de trabajo: {0}");
            mStrings.Add("cap_chk_upd", "Conviene buscar actualizaciones");
            mStrings.Add("msg_chk_upd", "{0} lleva {1} días sin poder buscar actualizaciones. Abre la aplicación para revisar el problema.");
            mStrings.Add("cap_new_upd", "Hay actualizaciones disponibles");
            mStrings.Add("msg_new_upd", "{0} ha encontrado {1} actualizaciones. Revísalas y elige cuáles quieres instalar.");
            mStrings.Add("lbl_fnd_upd", "Disponibles ({0})");
            mStrings.Add("lbl_inst_upd", "Instaladas ({0})");
            mStrings.Add("lbl_block_upd", "Ocultas ({0})");
            mStrings.Add("lbl_old_upd", "Historial ({0})");
            mStrings.Add("msg_tool_err", "No se pudo iniciar la herramienta.");
            mStrings.Add("msg_admin_dl", "La descarga mediante Windows Update necesita permisos de administrador. También puedes usar la descarga manual.");
            mStrings.Add("msg_admin_inst", "Necesitas permisos de administrador para instalar actualizaciones.");
            mStrings.Add("msg_admin_rem", "Necesitas permisos de administrador para desinstalar actualizaciones.");
            mStrings.Add("msg_dl_done", "Las actualizaciones se han descargado en {0} y están listas para instalar.");
            mStrings.Add("msg_dl_err", "Las actualizaciones se guardaron en {0}, pero algunas descargas no se completaron.");
            mStrings.Add("msg_inst_done", "Las actualizaciones se instalaron correctamente. Es necesario reiniciar el equipo.");
            mStrings.Add("msg_inst_err", "Algunas actualizaciones no se pudieron instalar. También es necesario reiniciar el equipo.");
            mStrings.Add("err_admin", "no están disponibles los permisos necesarios");
            mStrings.Add("err_busy", "ya hay otra operación en curso");
            mStrings.Add("err_dl", "falló la descarga");
            mStrings.Add("err_inst", "falló la instalación");
            mStrings.Add("err_no_sel", "no hay actualizaciones seleccionadas o compatibles con esta acción");
            mStrings.Add("err_int", "se produjo un error interno");
            mStrings.Add("err_file", "no se encontraron los archivos necesarios");
            mStrings.Add("msg_err", "No se pudo completar «{0}»: {1}.");
            mStrings.Add("msg_wuau", "El servicio de Windows Update no está disponible. ¿Quieres iniciarlo ahora?");
            mStrings.Add("menu_tools", "&Herramientas");
            mStrings.Add("menu_about", "&Acerca de");
            mStrings.Add("menu_exit", "&Salir");
            mStrings.Add("stat_not_start", "No iniciada");
            mStrings.Add("stat_in_prog", "En curso");
            mStrings.Add("stat_success", "Correcta");
            mStrings.Add("stat_success_2", "Correcta con errores");
            mStrings.Add("stat_failed", "Fallida");
            mStrings.Add("stat_abbort", "Cancelada");
            mStrings.Add("stat_beta", "Beta");
            mStrings.Add("stat_install", "Instalada");
            mStrings.Add("stat_rem", "Se puede desinstalar");
            mStrings.Add("stat_no_rem", "No desinstalable");
            mStrings.Add("stat_block", "Oculta");
            mStrings.Add("stat_dl", "Descargada");
            mStrings.Add("stat_pending", "Pendiente");
            mStrings.Add("stat_sel", "Recomendada");
            mStrings.Add("stat_mand", "Obligatoria");
            mStrings.Add("stat_excl", "Exclusiva");
            mStrings.Add("stat_reboot", "Requiere reinicio");
            mStrings.Add("menu_wuau", "Servicio de Windows Update");
            mStrings.Add("menu_refresh", "&Actualizar herramientas");
            mStrings.Add("op_check", "Buscando actualizaciones");
            mStrings.Add("op_prep", "Preparando la búsqueda");
            mStrings.Add("op_dl", "Descargar actualizaciones");
            mStrings.Add("op_inst", "Instalar actualizaciones");
            mStrings.Add("op_rem", "Desinstalar actualizaciones");
            mStrings.Add("op_cancel", "Cancelar la operación");
            mStrings.Add("op_unk", "Operación desconocida");
            mStrings.Add("msg_gpo", "Esta edición de Windows no respeta por completo las directivas estándar. Para mantener bloqueadas las actualizaciones automáticas hay que desactivar también los servicios facilitadores.");
            mStrings.Add("col_title", "Actualización");
            mStrings.Add("col_cat", "Categoría");
            mStrings.Add("col_kb", "Artículo KB");
            mStrings.Add("col_app_id", "Id. de aplicación");
            mStrings.Add("col_date", "Fecha");
            mStrings.Add("col_site", "Tamaño");
            mStrings.Add("col_stat", "Estado");
            mStrings.Add("lbl_support", "Más información");
            mStrings.Add("lbl_search", "Filtrar:");
            mStrings.Add("tip_search", "Buscar actualizaciones");
            mStrings.Add("tip_inst", "Instalar las seleccionadas");
            mStrings.Add("tip_dl", "Descargar las seleccionadas");
            mStrings.Add("tip_hide", "Ocultar o volver a mostrar");
            mStrings.Add("tip_lnk", "Copiar enlaces de descarga");
            mStrings.Add("tip_rem", "Desinstalar las seleccionadas");
            mStrings.Add("tip_cancel", "Cancelar la operación");
            mStrings.Add("lbl_opt", "General");
            mStrings.Add("lbl_au", "Control");
            mStrings.Add("lbl_off", "Usar catálogo sin conexión");
            mStrings.Add("lbl_dl", "Actualizar catálogo offline");
            mStrings.Add("lbl_man", "Descarga e instalación manual");
            mStrings.Add("lbl_old", "Incluir actualizaciones reemplazadas");
            mStrings.Add("lbl_ms", "Incluir Microsoft Update");
            mStrings.Add("lbl_start", "Inicio y comprobaciones");
            mStrings.Add("lbl_auto", "Ejecutar en segundo plano");
            mStrings.Add("lbl_ac_no", "No buscar automáticamente");
            mStrings.Add("lbl_ac_day", "Buscar una vez al día");
            mStrings.Add("lbl_ac_week", "Buscar una vez a la semana");
            mStrings.Add("lbl_ac_month", "Buscar una vez al mes");
            mStrings.Add("lbl_uac", "Iniciar siempre como administrador");
            mStrings.Add("lbl_block_ms", "Bloquear servidores de Windows Update");
            mStrings.Add("lbl_au_off", "Desactivar actualizaciones automáticas");
            mStrings.Add("lbl_au_dissable", "Desactivar servicios facilitadores");
            mStrings.Add("lbl_au_notify", "Sólo avisar");
            mStrings.Add("lbl_au_dl", "Descargar, sin instalar");
            mStrings.Add("lbl_au_time", "Instalación programada");
            mStrings.Add("lbl_au_def", "Comportamiento automático de Windows");
            mStrings.Add("lbl_hide", "Ocultar la página de Windows Update");
            mStrings.Add("lbl_store", "Desactivar actualizaciones de Microsoft Store");
            mStrings.Add("lbl_drv", "Incluir controladores");
            mStrings.Add("msg_disable_au", "Es necesario reiniciar para aplicar por completo la nueva configuración.");
            mStrings.Add("lbl_all", "Seleccionar todo");
            mStrings.Add("lbl_group", "Agrupar");
            mStrings.Add("lbl_patreon", "Apoyar al autor original de WuMgr");
            mStrings.Add("lbl_github", "Ver el código original de WuMgr");

            string langINI = Program.appPath + @"\Translation.ini";

            if (!File.Exists(langINI))
            {
                foreach (string key in mStrings.Keys)
                    Program.IniWriteValue("es", key, mStrings[key], langINI);
                return;
            }

            if (lang != "es")
            {
                foreach (string key in mStrings.Keys.ToList())
                {
                    string str = Program.IniReadValue(lang, key, "", langINI);
                    if (str.Length == 0)
                        continue;

                    mStrings.Remove(key);
                    mStrings.Add(key, str);
                }
            }
        }

        static public string fmt(string id, params object[] args)
        {
            try
            {
                string str = id;
                mStrings.TryGetValue(id, out str);
                return string.Format(str, args);
            }
            catch
            {
                return "err on " + id;
            }
        }
    }
}
