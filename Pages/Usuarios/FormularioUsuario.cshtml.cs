using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using InventarioComputo.Data;
using InventarioComputo.Security; // <- para invalidar caché (opcional)

namespace InventarioComputo.Pages.Usuarios
{
    public class FormularioUsuarioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormularioUsuarioModel> _logger;
        private readonly PermisosService _permisosService; // <- opcional pero útil

        [BindProperty]
        public UsuarioViewModel Usuario { get; set; } = new UsuarioViewModel();
        public List<EmpleadoInfo> Empleados { get; set; } = new List<EmpleadoInfo>();
        public List<RolInfo> Roles { get; set; } = new List<RolInfo>();
        public string Modo { get; set; } = "Crear"; // "Crear", "Editar" o "Ver"

        public FormularioUsuarioModel(
            ConexionBDD dbConnection,
            ILogger<FormularioUsuarioModel> logger,
            PermisosService permisosService // <- inyéctalo
        )
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _permisosService = permisosService;
        }

        public async Task<IActionResult> OnGetAsync(string handler, int? id)
        {
            try
            {
                Modo = handler switch
                {
                    "Editar" => "Editar",
                    "Ver" => "Ver",
                    _ => "Crear"
                };

                bool esModoLectura = Modo == "Ver";

                // Cargar datos iniciales
                await CargarDatosIniciales(esModoLectura);

                if (id.HasValue)
                {
                    await CargarUsuarioExistente(id.Value);
                    if (Usuario == null) return NotFound();
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al cargar el formulario de usuario");
                return StatusCode(500, "Error interno al cargar el formulario");
            }
        }

        private async Task CargarUsuarioExistente(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT 
                        id_usuario, 
                        Username, 
                        id_empleado, 
                        FechaPassword,
                        id_rol_sistema
                    FROM Usuarios
                    WHERE id_usuario = @Id";

                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        Usuario = new UsuarioViewModel
                        {
                            Id = reader.GetInt32(0),
                            Username = reader.GetString(1),
                            IdEmpleado = reader.IsDBNull(2) ? null : (int?)reader.GetInt32(2),
                            FechaPassword = reader.GetDateTime(3),
                            IdRolSistema = reader.GetInt32(4)
                        };
                    }
                }
            }
        }

        private async Task CargarDatosIniciales(bool esModoLectura)
        {
            try
            {
                // Cargar empleados (solo los que no tienen usuario en modo Crear)
                if (esModoLectura || Modo == "Editar")
                    Empleados = await ObtenerTodosEmpleados();
                else
                    Empleados = await ObtenerEmpleadosSinUsuario();

                Roles = await ObtenerRoles();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar datos iniciales");
                throw;
            }
        }

        private async Task<List<EmpleadoInfo>> ObtenerEmpleadosSinUsuario()
        {
            var empleados = new List<EmpleadoInfo>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT e.id_empleado, e.Nombre 
                    FROM Empleados e
                    LEFT JOIN Usuarios u ON e.id_empleado = u.id_empleado
                    WHERE u.id_empleado IS NULL
                    ORDER BY e.Nombre";

                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        empleados.Add(new EmpleadoInfo
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1)
                        });
                    }
                }
            }
            return empleados;
        }

        private async Task<List<EmpleadoInfo>> ObtenerTodosEmpleados()
        {
            var empleados = new List<EmpleadoInfo>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre";
                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        empleados.Add(new EmpleadoInfo
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1)
                        });
                    }
                }
            }
            return empleados;
        }

        private async Task<List<RolInfo>> ObtenerRoles()
        {
            var roles = new List<RolInfo>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_rol_sistema, NombreRol FROM RolesSistema ORDER BY NombreRol";
                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        roles.Add(new RolInfo
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1)
                        });
                    }
                }
            }
            return roles;
        }

        public async Task<IActionResult> OnPostRestablecerAsync(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    const string nuevaPassword = "Armeria2025!";

                    var query = @"
                        UPDATE Usuarios SET
                            Password = @Password,
                            FechaPassword = GETDATE()
                        WHERE id_usuario = @Id";

                    var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@Password", nuevaPassword);
                    command.Parameters.AddWithValue("@Id", id);

                    await command.ExecuteNonQueryAsync();

                    // Obtener Username para Bitácora
                    string username = "";
                    using (var cmdUser = new SqlCommand("SELECT Username FROM Usuarios WHERE id_usuario = @Id", connection))
                    {
                        cmdUser.Parameters.AddWithValue("@Id", id);
                        var res = await cmdUser.ExecuteScalarAsync();
                        if (res != null) username = res.ToString();
                    }

                    try
                    {
                        var detalles = $"Se restableció la contraseña del usuario '{username}' (ID: {id}).";
                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Usuarios,
                            BitacoraConstantes.Acciones.ReseteoPassword,
                            detalles
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogError(exBit, "Error al registrar Bitácora de reseteo de contraseña del usuario Id={Id}", id);
                    }

                    TempData["Mensaje"] = "¡Contraseña restablecida con éxito!";

                    return RedirectToPage(new { handler = "Editar", id });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al restablecer contraseña");
                TempData["Error"] = "Error al restablecer: " + ex.Message;
                return RedirectToPage(new { handler = "Editar", id });
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Validación solo para creación
            if (Usuario.Id == 0)
            {
                if (string.IsNullOrEmpty(Usuario.Password))
                    ModelState.AddModelError("Usuario.Password", "La contraseña es requerida");

                if (string.IsNullOrEmpty(Usuario.ConfirmPassword))
                    ModelState.AddModelError("Usuario.ConfirmPassword", "Confirme la contraseña");
                else if (Usuario.Password != Usuario.ConfirmPassword)
                    ModelState.AddModelError("Usuario.ConfirmPassword", "Las contraseñas no coinciden");
            }

            if (!ModelState.IsValid)
            {
                await CargarDatosIniciales(false);
                return Page();
            }

            var esCreacion = (Usuario.Id == 0);

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                await using (var tx = connection.BeginTransaction())
                {
                    string query;

                    if (esCreacion)
                    {
                        query = @"
                            INSERT INTO Usuarios (
                                Username, 
                                Password, 
                                FechaPassword, 
                                id_empleado,
                                id_rol_sistema
                            ) VALUES (
                                @Username, 
                                @Password, 
                                GETDATE(), 
                                @IdEmpleado,
                                @IdRolSistema
                            );
                            SELECT SCOPE_IDENTITY();";
                    }
                    else
                    {
                        query = @"
                            UPDATE Usuarios SET
                                Username = @Username,
                                id_rol_sistema = @IdRolSistema
                            WHERE id_usuario = @Id";
                    }

                    var command = new SqlCommand(query, connection, tx);
                    command.Parameters.AddWithValue("@Username", Usuario.Username);

                    if (esCreacion)
                        command.Parameters.AddWithValue("@Password", Usuario.Password);

                    if (Usuario.IdEmpleado.HasValue)
                        command.Parameters.AddWithValue("@IdEmpleado", Usuario.IdEmpleado.Value);
                    else
                        command.Parameters.AddWithValue("@IdEmpleado", DBNull.Value);

                    command.Parameters.AddWithValue("@IdRolSistema", Usuario.IdRolSistema);

                    if (!esCreacion)
                        command.Parameters.AddWithValue("@Id", Usuario.Id);

                    if (esCreacion)
                    {
                        var newIdObj = await command.ExecuteScalarAsync();
                        Usuario.Id = Convert.ToInt32(newIdObj);

                        // === Asignar permisos según rol (solo en creación) ===
                        string rolNombre = await ObtenerNombreRolAsync(connection, tx, Usuario.IdRolSistema);

                        // Calcula bits según el rol
                        var bits = CalcularPermisosPorRol(rolNombre);

                        // MERGE a PermisosUsuarios
                        var qPerm = @"
MERGE dbo.PermisosUsuarios AS target
USING (SELECT @IdUsuario AS id_usuario) AS source
  ON target.id_usuario = source.id_usuario
WHEN MATCHED THEN
    UPDATE SET
       VerEmpleados            = @VerEmpleados,
        VerUsuarios            = @VerUsuarios,
        VerConexionBDD         = @VerConexionBDD,
        VerReportes            = @VerReportes,
        ModificarActivos       = @ModificarActivos,
        ModificarMantenimientos= @ModificarMantenimientos,
        ModificarEquipos       = @ModificarEquipos,
        ModificarDepartamentos = @ModificarDepartamentos,
        ModificarEmpleados     = @ModificarEmpleados,
        ModificarUsuarios      = @ModificarUsuarios,
        AccesoTotal            = @AccesoTotal
WHEN NOT MATCHED THEN
    INSERT (id_usuario, VerEmpleados, VerUsuarios, VerConexionBDD, VerReportes,
            ModificarActivos, ModificarMantenimientos, ModificarEquipos, ModificarDepartamentos,
            ModificarEmpleados, ModificarUsuarios, AccesoTotal)
    VALUES (@IdUsuario, @VerEmpleados, @VerUsuarios, @VerConexionBDD, @VerReportes,
            @ModificarActivos, @ModificarMantenimientos, @ModificarEquipos, @ModificarDepartamentos,
            @ModificarEmpleados, @ModificarUsuarios, @AccesoTotal);";

                        var cmdPerm = new SqlCommand(qPerm, connection, tx);
                        cmdPerm.Parameters.AddWithValue("@IdUsuario", Usuario.Id);

                        // Si AccesoTotal = true, forzamos todos los demás a true
                        bool VerEmpleados = bits.AccesoTotal || bits.VerEmpleados;
                        bool VerUsuarios = bits.AccesoTotal || bits.VerUsuarios;
                        bool VerConexionBDD = bits.AccesoTotal || bits.VerConexionBDD;
                        bool VerReportes = bits.AccesoTotal || bits.VerReportes;
                        bool ModificarActivos = bits.AccesoTotal || bits.ModificarActivos;
                        bool ModificarMantenimientos = bits.AccesoTotal || bits.ModificarMantenimientos;
                        bool ModificarEquipos = bits.AccesoTotal || bits.ModificarEquipos;
                        bool ModificarDepartamentos = bits.AccesoTotal || bits.ModificarDepartamentos;
                        bool ModificarEmpleados = bits.AccesoTotal || bits.ModificarEmpleados;
                        bool ModificarUsuarios = bits.AccesoTotal || bits.ModificarUsuarios;

                        cmdPerm.Parameters.AddWithValue("@VerEmpleados", VerEmpleados);
                        cmdPerm.Parameters.AddWithValue("@VerUsuarios", VerUsuarios);
                        cmdPerm.Parameters.AddWithValue("@VerConexionBDD", VerConexionBDD);
                        cmdPerm.Parameters.AddWithValue("@VerReportes", VerReportes);
                        cmdPerm.Parameters.AddWithValue("@ModificarActivos", ModificarActivos);
                        cmdPerm.Parameters.AddWithValue("@ModificarMantenimientos", ModificarMantenimientos);
                        cmdPerm.Parameters.AddWithValue("@ModificarEquipos", ModificarEquipos);
                        cmdPerm.Parameters.AddWithValue("@ModificarDepartamentos", ModificarDepartamentos);
                        cmdPerm.Parameters.AddWithValue("@ModificarEmpleados", ModificarEmpleados);
                        cmdPerm.Parameters.AddWithValue("@ModificarUsuarios", ModificarUsuarios);
                        cmdPerm.Parameters.AddWithValue("@AccesoTotal", bits.AccesoTotal);

                        await cmdPerm.ExecuteNonQueryAsync();

                        // invalidar caché por si acaso
                        _permisosService.Invalidate(Usuario.Id);
                    }
                    else
                    {
                        // TODO (a futuro): si decides que al cambiar el rol también se reasignen permisos,
                        // aquí podríamos repetir el bloque de cálculo y MERGE.
                        await command.ExecuteNonQueryAsync();
                    }

                    await tx.CommitAsync();

                    // Bitácora crear/modificar
                    try
                    {
                        var detalles = esCreacion
                            ? $"Se creó el usuario '{Usuario.Username}' (ID: {Usuario.Id}) con permisos por rol."
                            : $"Se modificó el usuario '{Usuario.Username}' (ID: {Usuario.Id}).";

                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Usuarios,
                            esCreacion ? BitacoraConstantes.Acciones.Creacion : BitacoraConstantes.Acciones.Modificacion,
                            detalles
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogError(exBit, "Error al registrar Bitácora de {Op} de usuario Id={Id}",
                            esCreacion ? "creación" : "modificación", Usuario.Id);
                    }

                    return RedirectToPage("./Index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar el usuario");
                ModelState.AddModelError("", "Error al guardar: " + ex.Message);
                await CargarDatosIniciales(false);
                return Page();
            }
        }

        private async Task<string> ObtenerNombreRolAsync(SqlConnection connection, SqlTransaction tx, int idRol)
        {
            using var cmd = new SqlCommand("SELECT NombreRol FROM RolesSistema WHERE id_rol_sistema = @Id", connection, tx);
            cmd.Parameters.AddWithValue("@Id", idRol);
            var res = await cmd.ExecuteScalarAsync();
            return res?.ToString()?.Trim() ?? string.Empty;
        }

        private (bool VerEmpleados, bool VerUsuarios, bool VerConexionBDD, bool VerReportes,
                 bool ModificarActivos, bool ModificarMantenimientos, bool ModificarEquipos, bool ModificarDepartamentos,
                 bool ModificarEmpleados, bool ModificarUsuarios, bool AccesoTotal)
            CalcularPermisosPorRol(string nombreRol)
        {
            // Normalizamos el nombre para comparar
            var rol = (nombreRol ?? string.Empty).Trim().ToLowerInvariant();

            // Defaults
            bool VerEmpleados = false, VerUsuarios = false, VerConexionBDD = false, VerReportes = false;
            bool ModificarActivos = false, ModificarMantenimientos = false, ModificarEquipos = false, ModificarDepartamentos = false;
            bool ModificarEmpleados = false, ModificarUsuarios = false, AccesoTotal = false;

            switch (rol)
            {
                case "administrador":
                    AccesoTotal = true; // forzará todo a true al guardar
                    break;

                case "técnico":
                case "tecnico": // por si no hay tilde en la BD
                    VerEmpleados = true;
                    VerUsuarios = true;
                    VerReportes = true;
                    ModificarActivos = true;
                    ModificarMantenimientos = true;
                    ModificarEquipos = true;
                    ModificarDepartamentos = true;
                    break;

                case "usuario":
                    VerReportes = true;
                    ModificarActivos = true;
                    ModificarMantenimientos = true;
                    break;

                case "auditor":
                    VerReportes = true;
                    break;

                default:
                    // Rol desconocido: sin permisos por defecto
                    break;
            }

            return (VerEmpleados, VerUsuarios, VerConexionBDD, VerReportes,
                    ModificarActivos, ModificarMantenimientos, ModificarEquipos, ModificarDepartamentos,
                    ModificarEmpleados, ModificarUsuarios, AccesoTotal);
        }

        public class UsuarioViewModel
        {
            public int Id { get; set; }

            [Required(ErrorMessage = "El nombre de usuario es requerido")]
            [StringLength(50, ErrorMessage = "Máximo 50 caracteres")]
            public string Username { get; set; }

            [StringLength(100, ErrorMessage = "Máximo 100 caracteres", MinimumLength = 6)]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [DataType(DataType.Password)]
            public string ConfirmPassword { get; set; }

            [Display(Name = "Empleado")]
            public int? IdEmpleado { get; set; }

            [Required(ErrorMessage = "Debe seleccionar un rol")]
            [Display(Name = "Rol")]
            public int IdRolSistema { get; set; }

            [Display(Name = "Último cambio de contraseña")]
            public DateTime FechaPassword { get; set; }
        }

        public class EmpleadoInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class RolInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}
