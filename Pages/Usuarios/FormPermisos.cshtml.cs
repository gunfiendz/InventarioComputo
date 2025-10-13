using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;
using InventarioComputo.Security; // <- para PermisosService

namespace InventarioComputo.Pages
{
    public class FormPermisosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormPermisosModel> _logger;
        private readonly PermisosService _permisosService; // <- cache de permisos

        [BindProperty]
        public List<UsuarioPermisosViewModel> ListaPermisos { get; set; } = new List<UsuarioPermisosViewModel>();

        public FormPermisosModel(
            ConexionBDD dbConnection,
            ILogger<FormPermisosModel> logger,
            PermisosService permisosService) // <- inyéctalo
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _permisosService = permisosService;
        }

        public async Task OnGetAsync()
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT
                        u.id_usuario,
                        u.Username,
                        ISNULL(p.VerEmpleados, 0)            AS VerEmpleados,
                        ISNULL(p.VerUsuarios, 0)             AS VerUsuarios,
                        ISNULL(p.VerConexionBDD, 0)          AS VerConexionBDD,
                        ISNULL(p.VerReportes, 0)             AS VerReportes,
                        ISNULL(p.VerBitacora, 0)             AS VerBitacora,
                        ISNULL(p.ModificarActivos, 0)        AS ModificarActivos,
                        ISNULL(p.ModificarMantenimientos, 0) AS ModificarMantenimientos,
                        ISNULL(p.ModificarEquipos, 0)        AS ModificarEquipos,
                        ISNULL(p.ModificarDepartamentos, 0)  AS ModificarDepartamentos,
                        ISNULL(p.ModificarEmpleados, 0)      AS ModificarEmpleados,
                        ISNULL(p.ModificarUsuarios, 0)       AS ModificarUsuarios,
                        ISNULL(p.AccesoTotal, 0)             AS AccesoTotal
                    FROM dbo.Usuarios u
                    LEFT JOIN dbo.PermisosUsuarios p
                           ON u.id_usuario = p.id_usuario
                    ORDER BY u.Username";

                var command = new SqlCommand(query, connection);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        ListaPermisos.Add(new UsuarioPermisosViewModel
                        {
                            IdUsuario = reader.GetInt32(reader.GetOrdinal("id_usuario")),
                            Username = reader.GetString(reader.GetOrdinal("Username")),
                            VerEmpleados = reader.GetBoolean(reader.GetOrdinal("VerEmpleados")),
                            VerUsuarios = reader.GetBoolean(reader.GetOrdinal("VerUsuarios")),
                            VerConexionBDD = reader.GetBoolean(reader.GetOrdinal("VerConexionBDD")),
                            VerReportes = reader.GetBoolean(reader.GetOrdinal("VerReportes")),
                            VerBitacora = reader.GetBoolean(reader.GetOrdinal("VerBitacora")),
                            ModificarActivos = reader.GetBoolean(reader.GetOrdinal("ModificarActivos")),
                            ModificarMantenimientos = reader.GetBoolean(reader.GetOrdinal("ModificarMantenimientos")),
                            ModificarEquipos = reader.GetBoolean(reader.GetOrdinal("ModificarEquipos")),
                            ModificarDepartamentos = reader.GetBoolean(reader.GetOrdinal("ModificarDepartamentos")),
                            ModificarEmpleados = reader.GetBoolean(reader.GetOrdinal("ModificarEmpleados")),
                            ModificarUsuarios = reader.GetBoolean(reader.GetOrdinal("ModificarUsuarios")),
                            AccesoTotal = reader.GetBoolean(reader.GetOrdinal("AccesoTotal"))
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
                    // Cargar estado actual para detectar cambios mínimos
                    var actuales = new Dictionary<int, (bool VerEmpleados, bool VerUsuarios, bool VerConexionBDD, bool VerReportes, bool VerBitacora,
                                                        bool ModificarActivos, bool ModificarMantenimientos, bool ModificarEquipos,
                                                        bool ModificarDepartamentos, bool ModificarEmpleados, bool ModificarUsuarios,
                                                        bool AccesoTotal)>();

                    var qActuales = @"
    SELECT id_usuario,
           ISNULL(VerEmpleados,0)            AS VerEmpleados,
           ISNULL(VerUsuarios,0)             AS VerUsuarios,
           ISNULL(VerConexionBDD,0)          AS VerConexionBDD,
           ISNULL(VerReportes,0)             AS VerReportes,
           ISNULL(VerBitacora,0)             AS VerBitacora,
           ISNULL(ModificarActivos,0)        AS ModificarActivos,
           ISNULL(ModificarMantenimientos,0) AS ModificarMantenimientos,
           ISNULL(ModificarEquipos,0)        AS ModificarEquipos,
           ISNULL(ModificarDepartamentos,0)  AS ModificarDepartamentos,
           ISNULL(ModificarEmpleados,0)      AS ModificarEmpleados,
           ISNULL(ModificarUsuarios,0)       AS ModificarUsuarios,
           ISNULL(AccesoTotal,0)             AS AccesoTotal
    FROM dbo.PermisosUsuarios";

                    using (var cmdAct = new SqlCommand(qActuales, connection, transaction))
                    using (var rd = await cmdAct.ExecuteReaderAsync())
                    {
                        // helper local
                        bool B(string name)
                        {
                            var i = rd.GetOrdinal(name);
                            return !rd.IsDBNull(i) && rd.GetBoolean(i);
                        }

                        while (await rd.ReadAsync())
                        {
                            actuales[rd.GetInt32(rd.GetOrdinal("id_usuario"))] = (
                                B("VerEmpleados"),
                                B("VerUsuarios"),
                                B("VerConexionBDD"),
                                B("VerReportes"),
                                B("VerBitacora"),
                                B("ModificarActivos"),
                                B("ModificarMantenimientos"),
                                B("ModificarEquipos"),
                                B("ModificarDepartamentos"),
                                B("ModificarEmpleados"),
                                B("ModificarUsuarios"),
                                B("AccesoTotal")
                            );
                        }
                    }

                    int cambios = 0;

                    foreach (var usuario in ListaPermisos)
                    {
                        // Regla server-side: AccesoTotal fuerza todo lo demás a true
                        var verEmpleados = usuario.AccesoTotal || usuario.VerEmpleados;
                        var verUsuarios = usuario.AccesoTotal || usuario.VerUsuarios;
                        var verConexionBDD = usuario.AccesoTotal || usuario.VerConexionBDD;
                        var verReportes = usuario.AccesoTotal || usuario.VerReportes;
                        var verBitacora = usuario.AccesoTotal || usuario.VerBitacora;
                        var modificarActivos = usuario.AccesoTotal || usuario.ModificarActivos;
                        var modificarMantenimientos = usuario.AccesoTotal || usuario.ModificarMantenimientos;
                        var modificarEquipos = usuario.AccesoTotal || usuario.ModificarEquipos;
                        var modificarDepartamentos = usuario.AccesoTotal || usuario.ModificarDepartamentos;
                        var modificarEmpleados = usuario.AccesoTotal || usuario.ModificarEmpleados;
                        var modificarUsuarios = usuario.AccesoTotal || usuario.ModificarUsuarios;
                        var accesoTotal = usuario.AccesoTotal;

                        bool huboCambio;
                        if (actuales.TryGetValue(usuario.IdUsuario, out var prev))
                        {
                            huboCambio =
                                prev.VerEmpleados != verEmpleados ||
                                prev.VerUsuarios != verUsuarios ||
                                prev.VerConexionBDD != verConexionBDD ||
                                prev.VerReportes != verReportes ||
                                prev.VerBitacora != verBitacora ||
                                prev.ModificarActivos != modificarActivos ||
                                prev.ModificarMantenimientos != modificarMantenimientos ||
                                prev.ModificarEquipos != modificarEquipos ||
                                prev.ModificarDepartamentos != modificarDepartamentos ||
                                prev.ModificarEmpleados != modificarEmpleados ||
                                prev.ModificarUsuarios != modificarUsuarios ||
                                prev.AccesoTotal != accesoTotal;
                        }
                        else
                        {
                            huboCambio = true;
                        }

                        var query = @"
                            MERGE dbo.PermisosUsuarios AS target
                            USING (SELECT @IdUsuario AS id_usuario) AS source
                              ON (target.id_usuario = source.id_usuario)
                            WHEN MATCHED THEN
                                UPDATE SET
                                   VerEmpleados            = @VerEmpleados,
                                   VerUsuarios             = @VerUsuarios,
                                   VerConexionBDD          = @VerConexionBDD,
                                   VerReportes             = @VerReportes,
                                   VerBitacora             = @VerBitacora,
                                   ModificarActivos        = @ModificarActivos,
                                   ModificarMantenimientos = @ModificarMantenimientos,
                                   ModificarEquipos        = @ModificarEquipos,
                                   ModificarDepartamentos  = @ModificarDepartamentos,
                                   ModificarEmpleados      = @ModificarEmpleados,
                                   ModificarUsuarios       = @ModificarUsuarios,
                                   AccesoTotal             = @AccesoTotal
                            WHEN NOT MATCHED THEN
                                INSERT (id_usuario, VerEmpleados, VerUsuarios, VerConexionBDD, VerReportes,VerBitacora,
                                        ModificarActivos, ModificarMantenimientos, ModificarEquipos, ModificarDepartamentos,
                                        ModificarEmpleados, ModificarUsuarios, AccesoTotal)
                                VALUES (@IdUsuario, @VerEmpleados, @VerUsuarios, @VerConexionBDD, @VerReportes, @VerBitacora,
                                        @ModificarActivos, @ModificarMantenimientos, @ModificarEquipos, @ModificarDepartamentos,
                                        @ModificarEmpleados, @ModificarUsuarios, @AccesoTotal);";

                        var command = new SqlCommand(query, connection, transaction);
                        command.Parameters.AddWithValue("@IdUsuario", usuario.IdUsuario);
                        command.Parameters.AddWithValue("@VerEmpleados", verEmpleados);
                        command.Parameters.AddWithValue("@VerUsuarios", verUsuarios);
                        command.Parameters.AddWithValue("@VerConexionBDD", verConexionBDD);
                        command.Parameters.AddWithValue("@VerReportes", verReportes);
                        command.Parameters.AddWithValue("@VerBitacora", verBitacora);
                        command.Parameters.AddWithValue("@ModificarActivos", modificarActivos);
                        command.Parameters.AddWithValue("@ModificarMantenimientos", modificarMantenimientos);
                        command.Parameters.AddWithValue("@ModificarEquipos", modificarEquipos);
                        command.Parameters.AddWithValue("@ModificarDepartamentos", modificarDepartamentos);
                        command.Parameters.AddWithValue("@ModificarEmpleados", modificarEmpleados);
                        command.Parameters.AddWithValue("@ModificarUsuarios", modificarUsuarios);
                        command.Parameters.AddWithValue("@AccesoTotal", accesoTotal);

                        await command.ExecuteNonQueryAsync();

                        if (huboCambio)
                        {
                            cambios++;
                            _permisosService.Invalidate(usuario.IdUsuario); // limpiar caché
                        }
                    }

                    await transaction.CommitAsync();

                    try
                    {
                        var detalles = $"Se actualizaron los permisos para {cambios} usuario(s).";
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
            public string Username { get; set; } = string.Empty;

            // Ver / Lectura
            public bool VerEmpleados { get; set; }
            public bool VerUsuarios { get; set; }
            public bool VerConexionBDD { get; set; }
            public bool VerReportes { get; set; }
            public bool VerBitacora { get; set; }

            // Modificar / Escritura
            public bool ModificarActivos { get; set; }
            public bool ModificarMantenimientos { get; set; }
            public bool ModificarEquipos { get; set; }
            public bool ModificarDepartamentos { get; set; }
            public bool ModificarEmpleados { get; set; }
            public bool ModificarUsuarios { get; set; }

            // Raíz
            public bool AccesoTotal { get; set; }
        }
    }
}
