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
        public JsonResult OnGetActual()
        {
            var info = _dbConnection.GetCurrentInfo();
            if (info == null)
            {
                return new JsonResult(new
                {
                    servidor = "",
                    baseDatos = "",
                    integratedSecurity = true,
                    trustServerCertificate = false
                });
            }

            return new JsonResult(new
            {
                servidor = info.Value.Servidor,
                baseDatos = info.Value.BaseDatos,
                integratedSecurity = info.Value.IntegratedSecurity,
                trustServerCertificate = info.Value.TrustServerCertificate
            });
        }

        // POST /FormConexion?handler=Actualizar
        public async Task<JsonResult> OnPostActualizarAsync(
            string Servidor,
            string BaseDatos,
            bool IntegratedSecurity,
            string? Usuario,
            string? Contrasena,
            bool TrustServerCertificate)
        {
            if (string.IsNullOrWhiteSpace(Servidor) || string.IsNullOrWhiteSpace(BaseDatos))
                return new JsonResult(new { success = false, message = "Servidor y Base de Datos son obligatorios." });

            if (!IntegratedSecurity)
            {
                if (string.IsNullOrWhiteSpace(Usuario) || string.IsNullOrWhiteSpace(Contrasena))
                    return new JsonResult(new { success = false, message = "Debes ingresar Usuario y Contraseña cuando desmarcas Seguridad Integrada." });

                Usuario = Usuario!.Trim();
                Contrasena = Contrasena!.Trim();
            }

            try
            {
                var csb = new SqlConnectionStringBuilder
                {
                    DataSource = Servidor,
                    InitialCatalog = BaseDatos,
                    IntegratedSecurity = IntegratedSecurity,
                    MultipleActiveResultSets = true
                };

                if (!IntegratedSecurity)
                {
                    if (string.IsNullOrWhiteSpace(Usuario) || string.IsNullOrWhiteSpace(Contrasena))
                        return new JsonResult(new { success = false, message = "Debes ingresar Usuario y Contraseña cuando desmarcas Seguridad Integrada." });
                }
                else
                {
                    Usuario = null;
                    Contrasena = null;
                }


                // Cifrado / certificado
                if (TrustServerCertificate)
                {
                    csb.Encrypt = true;
                    csb.TrustServerCertificate = true;
                }
                else
                {
                    csb.Encrypt = false;
                    csb.TrustServerCertificate = false;
                }

                // Log seguro (no expone password)
                var safe = new SqlConnectionStringBuilder(csb.ConnectionString) { Password = "****" }.ConnectionString;
                _logger.LogInformation("Probando conexión con: {Conn}", safe);

                // Probar conexión
                using (var testConn = new SqlConnection(csb.ConnectionString))
                {
                    await testConn.OpenAsync();
                    await testConn.CloseAsync();
                }

                // 1) Activar en runtime
                await _dbConnection.UpdateConnectionStringAsync(csb.ConnectionString);

                // 2) Persistir para próximos arranques (App_Data/conn.json)
                await _dbConnection.PersistConnectionStringAsync(csb.ConnectionString);

                return new JsonResult(new { success = true, message = "Conexión actualizada y guardada." });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error actualizando conexión a BDD");
                var detail = ex.InnerException?.Message ?? ex.Message;
                return new JsonResult(new { success = false, message = detail });
            }
        }
    }
}
