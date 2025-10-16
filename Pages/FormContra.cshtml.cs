using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using System.Linq; // <- FirstOrDefault
using InventarioComputo.Security; // <- PasswordHasher
using InventarioComputo.Data;     // <- ConexionBDD

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
            // Validaciones básicas
            if (string.IsNullOrWhiteSpace(currentPassword) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmNewPassword))
            {
                return new JsonResult(new { success = false, message = "Todos los campos son obligatorios." });
            }

            if (newPassword != confirmNewPassword)
            {
                return new JsonResult(new { success = false, message = "Las nuevas contraseñas no coinciden." });
            }

            // (Opcional) Política mínima
            if (newPassword.Length < 6)
            {
                return new JsonResult(new { success = false, message = "La nueva contraseña debe tener al menos 6 caracteres." });
            }

            // Id de usuario desde los claims
            var userIdString = User.Claims.FirstOrDefault(c => c.Type == "id_usuario")?.Value
                               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out int userId))
            {
                return new JsonResult(new { success = false, message = "Error de autenticación. Sesión inválida." });
            }

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // 1) Traer hash/valor almacenado
                    string storedPassword = null!;
                    using (var cmdCheck = new SqlCommand("SELECT Password FROM Usuarios WHERE id_usuario = @Id", connection))
                    {
                        cmdCheck.Parameters.AddWithValue("@Id", userId);
                        storedPassword = (await cmdCheck.ExecuteScalarAsync()) as string;
                    }

                    if (string.IsNullOrEmpty(storedPassword))
                    {
                        return new JsonResult(new { success = false, message = "El usuario no existe." });
                    }

                    // 2) Verificar contraseña actual (hash o texto plano legado)
                    bool formatoHash = storedPassword.Split('.', 3).Length == 3; // iter.salt.hash
                    bool okActual = formatoHash
                        ? PasswordHasher.Verify(currentPassword, storedPassword)
                        : string.Equals(currentPassword, storedPassword, StringComparison.Ordinal);

                    if (!okActual)
                    {
                        return new JsonResult(new { success = false, message = "La contraseña actual es incorrecta." });
                    }

                    // 3) Generar hash de la nueva contraseña y actualizar
                    var newHash = PasswordHasher.Hash(newPassword);

                    using (var cmdUpdate = new SqlCommand(
                        "UPDATE Usuarios SET Password = @NewHash, FechaPassword = GETDATE() WHERE id_usuario = @Id",
                        connection))
                    {
                        cmdUpdate.Parameters.AddWithValue("@NewHash", newHash);
                        cmdUpdate.Parameters.AddWithValue("@Id", userId);
                        await cmdUpdate.ExecuteNonQueryAsync();
                    }

                    return new JsonResult(new { success = true, message = "¡Contraseña actualizada con éxito!" });
                }
            }
            catch
            {
                // Log opcional si tienes ILogger inyectado
                return new JsonResult(new { success = false, message = "Ocurrió un error en el servidor." });
            }
        }
    }
}
