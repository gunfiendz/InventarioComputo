using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventarioComputo.Pages
{
    public class FormPermisosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        // Usamos [BindProperty] para que la lista completa se envíe de vuelta al guardar
        [BindProperty]
        public List<UsuarioPermisosViewModel> ListaPermisos { get; set; } = new List<UsuarioPermisosViewModel>();

        public FormPermisosModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync()
        {
            // Carga todos los usuarios y sus permisos.
            // Usamos LEFT JOIN para asegurarnos de que aparezcan incluso los usuarios
            // que aún no tienen un registro en la tabla de permisos.
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT
                        u.id_usuario, u.Username,
                        ISNULL(p.VerInformes, 0) AS VerInformes,
                        ISNULL(p.ModificarActivos, 0) AS ModificarActivos,
                        ISNULL(p.ModificarUsuarios, 0) AS ModificarUsuarios,
                        ISNULL(p.RealizarMantenimientos, 0) AS RealizarMantenimientos,
                        ISNULL(p.AccesoTotal, 0) AS AccesoTotal
                    FROM Usuarios u
                    LEFT JOIN PermisosUsuarios p ON u.id_usuario = p.id_usuario
                    ORDER BY u.Username";

                var command = new SqlCommand(query, connection);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        ListaPermisos.Add(new UsuarioPermisosViewModel
                        {
                            IdUsuario = reader.GetInt32(0),
                            Username = reader.GetString(1),
                            VerInformes = reader.GetBoolean(2),
                            ModificarActivos = reader.GetBoolean(3),
                            ModificarUsuarios = reader.GetBoolean(4),
                            RealizarMantenimientos = reader.GetBoolean(5),
                            AccesoTotal = reader.GetBoolean(6)
                        });
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                await using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        // Iteramos sobre cada usuario que se mostró en la tabla
                        foreach (var usuario in ListaPermisos)
                        {
                            // Usamos un comando MERGE para hacer un "UPSERT":
                            // Si el usuario ya tiene permisos, los ACTUALIZA (UPDATE).
                            // Si no los tiene, INSERTA un nuevo registro (INSERT).
                            var query = @"
                                MERGE PermisosUsuarios AS target
                                USING (SELECT @IdUsuario AS id_usuario) AS source
                                ON (target.id_usuario = source.id_usuario)
                                WHEN MATCHED THEN
                                    UPDATE SET VerInformes = @VerInformes, ModificarActivos = @ModificarActivos,
                                               ModificarUsuarios = @ModificarUsuarios, RealizarMantenimientos = @RealizarMantenimientos,
                                               AccesoTotal = @AccesoTotal
                                WHEN NOT MATCHED THEN
                                    INSERT (id_usuario, VerInformes, ModificarActivos, ModificarUsuarios, RealizarMantenimientos, AccesoTotal)
                                    VALUES (@IdUsuario, @VerInformes, @ModificarActivos, @ModificarUsuarios, @RealizarMantenimientos, @AccesoTotal);";

                            var command = new SqlCommand(query, connection, transaction);
                            command.Parameters.AddWithValue("@IdUsuario", usuario.IdUsuario);
                            command.Parameters.AddWithValue("@VerInformes", usuario.VerInformes);
                            command.Parameters.AddWithValue("@ModificarActivos", usuario.ModificarActivos);
                            command.Parameters.AddWithValue("@ModificarUsuarios", usuario.ModificarUsuarios);
                            command.Parameters.AddWithValue("@RealizarMantenimientos", usuario.RealizarMantenimientos);
                            command.Parameters.AddWithValue("@AccesoTotal", usuario.AccesoTotal);

                            await command.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        ModelState.AddModelError("", $"Error al guardar los permisos: {ex.Message}");
                        return Page();
                    }
                }
            }

            TempData["SuccessMessage"] = "Permisos actualizados correctamente.";
            return RedirectToPage();
        }

        // ViewModel que representa una fila en nuestra tabla de permisos
        public class UsuarioPermisosViewModel
        {
            public int IdUsuario { get; set; }
            public string Username { get; set; }
            public bool VerInformes { get; set; }
            public bool ModificarActivos { get; set; }
            public bool ModificarUsuarios { get; set; }
            public bool RealizarMantenimientos { get; set; }
            public bool AccesoTotal { get; set; }
        }
    }
}