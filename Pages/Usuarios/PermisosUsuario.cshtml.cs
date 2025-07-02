using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace InventarioComputo.Pages.Usuarios
{
    public class PermisosUsuarioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        [BindProperty(SupportsGet = true)]
        public int Id { get; set; }

        [BindProperty]
        public PermisosViewModel Permisos { get; set; } = new PermisosViewModel();

        public string Username { get; set; }

        public PermisosUsuarioModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (Id <= 0) return NotFound();

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // Obtener nombre de usuario
                    var cmdUsuario = new SqlCommand(
                        "SELECT Username FROM Usuarios WHERE id_usuario = @Id",
                        connection);
                    cmdUsuario.Parameters.AddWithValue("@Id", Id);
                    Username = (await cmdUsuario.ExecuteScalarAsync())?.ToString() ?? "Usuario Desconocido";

                    // Cargar permisos existentes
                    var cmdPermisos = new SqlCommand(
                        "SELECT * FROM PermisosUsuarios WHERE id_usuario = @Id",
                        connection);
                    cmdPermisos.Parameters.AddWithValue("@Id", Id);

                    using (var reader = await cmdPermisos.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            Permisos = new PermisosViewModel
                            {
                                IdPermiso = reader.GetInt32(0),
                                IdUsuario = reader.GetInt32(1),
                                VerInformes = reader.GetBoolean(2),
                                ModificarActivos = reader.GetBoolean(3),
                                ModificarUsuarios = reader.GetBoolean(4),
                                RealizarMantenimientos = reader.GetBoolean(5),
                                AccesoTotal = reader.GetBoolean(6)
                            };
                        }
                        else
                        {
                            // Si no existe registro, crear uno nuevo
                            Permisos.IdUsuario = Id;
                        }
                    }
                }
                return Page();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar permisos: " + ex.Message);
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string query;
                    if (Permisos.IdPermiso == 0)
                    {
                        query = @"
                            INSERT INTO PermisosUsuarios (
                                id_usuario, 
                                VerInformes, 
                                ModificarActivos, 
                                ModificarUsuarios, 
                                RealizarMantenimientos, 
                                AccesoTotal
                            ) VALUES (
                                @IdUsuario, 
                                @VerInformes, 
                                @ModificarActivos, 
                                @ModificarUsuarios, 
                                @RealizarMantenimientos, 
                                @AccesoTotal
                            )";
                    }
                    else
                    {
                        query = @"
                            UPDATE PermisosUsuarios SET
                                VerInformes = @VerInformes,
                                ModificarActivos = @ModificarActivos,
                                ModificarUsuarios = @ModificarUsuarios,
                                RealizarMantenimientos = @RealizarMantenimientos,
                                AccesoTotal = @AccesoTotal
                            WHERE id_permiso_usuario = @IdPermiso";
                    }

                    var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@IdUsuario", Permisos.IdUsuario);
                    command.Parameters.AddWithValue("@VerInformes", Permisos.VerInformes);
                    command.Parameters.AddWithValue("@ModificarActivos", Permisos.ModificarActivos);
                    command.Parameters.AddWithValue("@ModificarUsuarios", Permisos.ModificarUsuarios);
                    command.Parameters.AddWithValue("@RealizarMantenimientos", Permisos.RealizarMantenimientos);
                    command.Parameters.AddWithValue("@AccesoTotal", Permisos.AccesoTotal);

                    if (Permisos.IdPermiso > 0)
                    {
                        command.Parameters.AddWithValue("@IdPermiso", Permisos.IdPermiso);
                    }

                    await command.ExecuteNonQueryAsync();
                }

                TempData["Mensaje"] = "Permisos actualizados correctamente";
                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al guardar permisos: " + ex.Message);
                return Page();
            }
        }

        public class PermisosViewModel
        {
            public int IdPermiso { get; set; }
            public int IdUsuario { get; set; }

            [Display(Name = "Ver Informes")]
            public bool VerInformes { get; set; }

            [Display(Name = "Modificar Activos")]
            public bool ModificarActivos { get; set; }

            [Display(Name = "Modificar Usuarios")]
            public bool ModificarUsuarios { get; set; }

            [Display(Name = "Realizar Mantenimientos")]
            public bool RealizarMantenimientos { get; set; }

            [Display(Name = "Acceso Total")]
            public bool AccesoTotal { get; set; }
        }
    }
}