using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages
{
    public class FormPermisosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormPermisosModel> _logger;

        [BindProperty]
        public List<UsuarioPermisosViewModel> ListaPermisos { get; set; } = new List<UsuarioPermisosViewModel>();

        public FormPermisosModel(ConexionBDD dbConnection, ILogger<FormPermisosModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
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
            await using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var actuales = new Dictionary<int, (bool VerInformes, bool ModificarActivos, bool ModificarUsuarios, bool RealizarMantenimientos, bool AccesoTotal)>();
                    var qActuales = @"
                        SELECT id_usuario, VerInformes, ModificarActivos, ModificarUsuarios, RealizarMantenimientos, AccesoTotal
                        FROM PermisosUsuarios";
                    using (var cmdAct = new SqlCommand(qActuales, connection, transaction))
                    using (var rd = await cmdAct.ExecuteReaderAsync())
                    {
                        while (await rd.ReadAsync())
                        {
                            actuales[rd.GetInt32(0)] = (
                                rd.GetBoolean(1),
                                rd.GetBoolean(2),
                                rd.GetBoolean(3),
                                rd.GetBoolean(4),
                                rd.GetBoolean(5)
                            );
                        }
                    }

                    int cambios = 0;

                    foreach (var usuario in ListaPermisos)
                    {
                        bool huboCambio;
                        if (actuales.TryGetValue(usuario.IdUsuario, out var prev))
                        {
                            huboCambio =
                                prev.VerInformes != usuario.VerInformes ||
                                prev.ModificarActivos != usuario.ModificarActivos ||
                                prev.ModificarUsuarios != usuario.ModificarUsuarios ||
                                prev.RealizarMantenimientos != usuario.RealizarMantenimientos ||
                                prev.AccesoTotal != usuario.AccesoTotal;
                        }
                        else
                        {
                            huboCambio = true;
                        }

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

                        if (huboCambio) cambios++;
                    }

                    await transaction.CommitAsync();

                    try
                    {
                        var detalles = $"Se actualizaron los permisos para {cambios} usuarios.";
                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Permisos,
                            BitacoraConstantes.Acciones.CambioPermisos,
                            detalles
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogError(exBit, "Error al registrar Bitácora de cambio de permisos");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error al guardar permisos de usuarios");
                    ModelState.AddModelError("", $"Error al guardar los permisos: {ex.Message}");
                    return Page();
                }
            }

            TempData["SuccessMessage"] = "Permisos actualizados correctamente.";
            return RedirectToPage();
        }

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
