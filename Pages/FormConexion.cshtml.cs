using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace InventarioComputo.Pages
{
    public class FormConexionModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormConexionModel> _logger;

        public FormConexionModel(ConexionBDD dbConnection, ILogger<FormConexionModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public void OnGet() { }

        // GET /FormConexion?handler=Actual
        // Devuelve (si se puede) la info actual; si tu servicio no la expone, retorna valores por defecto.
        public JsonResult OnGetActual()
        {
            // Si tu ConexionBDD expone algo como GetCurrentInfo(), puedes usarlo aqu�.
            // Para no romper nada, devolvemos placeholders.
            return new JsonResult(new
            {
                servidor = "",
                baseDatos = "",
                integratedSecurity = true,
                trustServerCertificate = false
            });
        }

        // POST /FormConexion?handler=Actualizar
        // Recibe los datos del modal, arma la connection string, la prueba y la aplica en runtime.
        public async Task<JsonResult> OnPostActualizarAsync(
            string Servidor,
            string BaseDatos,
            bool IntegratedSecurity,
            string? Usuario,
            string? Contrasena,
            bool TrustServerCertificate)
        {
            if (string.IsNullOrWhiteSpace(Servidor) || string.IsNullOrWhiteSpace(BaseDatos))
            {
                return new JsonResult(new { success = false, message = "Servidor y Base de Datos son obligatorios." });
            }

            try
            {
                var csb = new SqlConnectionStringBuilder
                {
                    DataSource = Servidor,
                    InitialCatalog = BaseDatos,
                    IntegratedSecurity = IntegratedSecurity,
                    TrustServerCertificate = TrustServerCertificate,
                    MultipleActiveResultSets = true
                };

                if (!IntegratedSecurity)
                {
                    csb.UserID = Usuario ?? "";
                    csb.Password = Contrasena ?? "";
                }

                // 1) Verificar conexi�n (abre/cierra)
                using (var testConn = new SqlConnection(csb.ConnectionString))
                {
                    await testConn.OpenAsync();
                    await testConn.CloseAsync();
                }

                // 2) Actualizar en runtime la cadena usada por tu app
                //    -> agrega el m�todo UpdateConnectionStringAsync en tu ConexionBDD (ver m�s abajo).
                await _dbConnection.UpdateConnectionStringAsync(csb.ConnectionString);

                return new JsonResult(new { success = true, message = "Conexi�n actualizada." });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error actualizando conexi�n a BDD");
                return new JsonResult(new { success = false, message = "Ocurri� un error al validar/actualizar la conexi�n." });
            }
        }
    }
}
