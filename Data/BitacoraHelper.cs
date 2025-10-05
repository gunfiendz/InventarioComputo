using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System; // Agregado para usar StringComparison
using System.Linq; // Agregado para usar FirstOrDefault
using Microsoft.Extensions.Logging;
using System.Data;

// Asegúrate de que el namespace coincida con la ubicación del archivo
namespace InventarioComputo.Data
{
    /// <summary>
    /// Clase de ayuda estática para registrar acciones de usuario en la bitácora.
    /// </summary>
    public static class BitacoraHelper
    {
        /// <summary>
        /// Registra una acción en la tabla Bitacora de forma asíncrona.
        /// </summary>
        /// <param name="dbConnection">La instancia de la conexión a la base de datos.</param>
        /// <param name="user">El objeto ClaimsPrincipal del usuario que realiza la acción (lo obtienes con 'User' en el PageModel).</param>
        /// <param name="idModulo">El ID del módulo desde BitacoraConstantes.</param>
        /// <param name="idAccion">El ID de la acción desde BitacoraConstantes.</param>
        /// <param name="detalles">Una descripción de la acción (ej: "Se creó el usuario 'nuevoadmin'").</param>
        public static async Task RegistrarAccionAsync(
            ConexionBDD dbConnection,
            ILogger logger,
            ClaimsPrincipal user,
            int idModulo,
            int idAccion,
            string detalles)
        {
            // Busca el Claim "id_usuario" que guardamos durante el login
            var userIdString = user.Claims.FirstOrDefault(c => c.Type.Equals("id_usuario", StringComparison.OrdinalIgnoreCase))?.Value;

            // Si no podemos obtener el ID del usuario, no podemos registrar. Salimos silenciosamente.
            if (!int.TryParse(userIdString, out int idUsuario))
            {
                // En un entorno real, podrías registrar este fallo en un log de sistema.
                return;
            }

            try
            {
                using (var connection = await dbConnection.GetConnectionAsync())
                {
                    var query = "INSERT INTO Bitacora (id_usuario, id_modulo, id_accion, Detalles) VALUES (@IdUsuario, @IdModulo, @IdAccion, @Detalles)";
                    var command = new SqlCommand(query, connection);

                    command.Parameters.AddWithValue("@IdUsuario", idUsuario);
                    command.Parameters.AddWithValue("@IdModulo", idModulo);
                    command.Parameters.AddWithValue("@IdAccion", idAccion);
                    command.Parameters.AddWithValue("@Detalles", detalles);

                    await command.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error al registrar en bitácora: {ex.Message}");
            }
        }
    }
}