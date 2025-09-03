using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;

namespace InventarioComputo.Pages
{
    public class FormContraModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        public FormContraModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public void OnGet() { }

        public async Task<JsonResult> OnPostChangePasswordAsync(string currentPassword, string newPassword, string confirmNewPassword)
        {
            if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
            {
                return new JsonResult(new { success = false, message = "Todos los campos son obligatorios." });
            }

            if (newPassword != confirmNewPassword)
            {
                return new JsonResult(new { success = false, message = "Las nuevas contraseñas no coinciden." });
            }

            var userIdString = User.Claims.FirstOrDefault(c => c.Type == "id_usuario")?.Value;
            if (!int.TryParse(userIdString, out int userId))
            {
                return new JsonResult(new { success = false, message = "Error de autenticación. Sesión inválida." });
            }

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var cmdCheck = new SqlCommand("SELECT Password FROM Usuarios WHERE id_usuario = @Id", connection);
                    cmdCheck.Parameters.AddWithValue("@Id", userId);
                    var storedPassword = (await cmdCheck.ExecuteScalarAsync()) as string;

                    if (storedPassword == null)
                    {
                        return new JsonResult(new { success = false, message = "El usuario no existe." });
                    }

                    if (storedPassword != currentPassword)
                    {
                        return new JsonResult(new { success = false, message = "La contraseña actual es incorrecta." });
                    }

                    var cmdUpdate = new SqlCommand(
                        "UPDATE Usuarios SET Password = @NewPassword, FechaPassword = GETDATE() WHERE id_usuario = @Id",
                        connection);
                    cmdUpdate.Parameters.AddWithValue("@NewPassword", newPassword);
                    cmdUpdate.Parameters.AddWithValue("@Id", userId);

                    await cmdUpdate.ExecuteNonQueryAsync();

                    return new JsonResult(new { success = true, message = "¡Contraseña actualizada con éxito!" });
                }
            }
            catch (System.Exception ex)
            {
                return new JsonResult(new { success = false, message = "Ocurrió un error en el servidor." });
            }
        }
    }
}