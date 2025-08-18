using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Security.Claims;
using System.Linq;
using System.Data;

namespace InventarioComputo.Pages.Usuarios
{
    public class UsuariosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        public List<UsuarioViewModel> Usuarios { get; set; } = new List<UsuarioViewModel>();
        public List<Empleado> Empleados { get; set; } = new List<Empleado>();
        public List<RolInfo> Roles { get; set; } = new List<RolInfo>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string SortColumn { get; set; } = "Username";
        public string SortDirection { get; set; } = "ASC";
        public string EmpleadoFilter { get; set; }
        public string RolFilter { get; set; }
        public string BusquedaFilter { get; set; }

        public UsuariosModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync(
            int pagina = 1,
            string sortColumn = "Username",
            string sortDirection = "ASC",
            string empleado = null,
            string rol = null,
            string busqueda = null)
        {
            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            EmpleadoFilter = empleado;
            RolFilter = rol;
            BusquedaFilter = busqueda;

            var columnasValidas = new Dictionary<string, string>
            {
                {"Username", "u.Username"},
                {"Empleado", "e.Nombre"},
                {"Rol", "rs.NombreRol"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "Username";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await CargarFiltros(connection);
                    await CargarUsuarios(connection, columnasValidas[SortColumn]);
                }
            }
            catch (Exception ex)
            {
                // Manejar error
            }
        }

        private async Task CargarFiltros(SqlConnection connection)
        {
            var cmdEmpleados = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre", connection);
            using (var reader = await cmdEmpleados.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Empleados.Add(new Empleado
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }

            var cmdRoles = new SqlCommand("SELECT id_rol_sistema, NombreRol FROM RolesSistema ORDER BY NombreRol", connection);
            using (var reader = await cmdRoles.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Roles.Add(new RolInfo
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }

        private async Task CargarUsuarios(SqlConnection connection, string sortColumn)
        {
            var countQuery = @"
                SELECT COUNT(*)
                FROM Usuarios u
                LEFT JOIN Empleados e ON u.id_empleado = e.id_empleado
                JOIN RolesSistema rs ON u.id_rol_sistema = rs.id_rol_sistema
                WHERE (@Empleado IS NULL OR u.id_empleado = @Empleado)
                AND (@Rol IS NULL OR u.id_rol_sistema = @Rol)
                AND (@Busqueda = '' OR u.Username LIKE '%' + @Busqueda + '%' OR e.Nombre LIKE '%' + @Busqueda + '%')";

            var countCommand = new SqlCommand(countQuery, connection);
            countCommand.Parameters.AddWithValue("@Empleado",
                string.IsNullOrEmpty(EmpleadoFilter) ? DBNull.Value : (object)int.Parse(EmpleadoFilter));
            countCommand.Parameters.AddWithValue("@Rol", string.IsNullOrEmpty(RolFilter) ? DBNull.Value : (object)int.Parse(RolFilter));
            countCommand.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");

            var totalRegistros = (int)await countCommand.ExecuteScalarAsync();
            TotalPaginas = (int)Math.Ceiling((double)totalRegistros / RegistrosPorPagina);

            var query = $@"
                SELECT 
                    u.id_usuario, 
                    u.Username, 
                    e.Nombre, 
                    rs.NombreRol,
                    COUNT(*) OVER() AS TotalRegistros
                FROM Usuarios u
                LEFT JOIN Empleados e ON u.id_empleado = e.id_empleado
                JOIN RolesSistema rs ON u.id_rol_sistema = rs.id_rol_sistema
                WHERE (@Empleado IS NULL OR u.id_empleado = @Empleado)
                AND (@Rol IS NULL OR u.id_rol_sistema = @Rol)
                AND (@Busqueda = '' OR u.Username LIKE '%' + @Busqueda + '%' OR e.Nombre LIKE '%' + @Busqueda + '%')
                ORDER BY {sortColumn} {SortDirection}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Empleado",
                string.IsNullOrEmpty(EmpleadoFilter) ? DBNull.Value : (object)int.Parse(EmpleadoFilter));
            command.Parameters.AddWithValue("@Rol", string.IsNullOrEmpty(RolFilter) ? DBNull.Value : (object)int.Parse(RolFilter));
            command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
            command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
            command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var usuario = new UsuarioViewModel
                    {
                        Id = reader.GetInt32(0),
                        NombreUsuario = reader.GetString(1),
                        NombreEmpleado = reader.IsDBNull(2) ? "No asignado" : reader.GetString(2),
                        Rol = reader.GetString(3)
                    };

                    if (!reader.IsDBNull(4))
                    {
                        TotalPaginas = (int)Math.Ceiling((double)reader.GetInt32(4) / RegistrosPorPagina);
                    }

                    Usuarios.Add(usuario);
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // Eliminar permisos asociados
                    var deletePermisosQuery = "DELETE FROM PermisosUsuarios WHERE id_usuario = @Id";
                    using (var cmd = new SqlCommand(deletePermisosQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    var deleteQuery = "DELETE FROM Usuarios WHERE id_usuario = @Id";
                    using (var cmd = new SqlCommand(deleteQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                TempData["Mensaje"] = "¡El usuario ha sido eliminado correctamente!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al eliminar el usuario: {ex.Message}";
                Console.WriteLine($"Error al eliminar el usuario: {ex.Message}");
            }
            return RedirectToPage();
        }

        public class UsuarioViewModel
        {
            public int Id { get; set; }
            public string NombreUsuario { get; set; }
            public string NombreEmpleado { get; set; }
            public string Rol { get; set; }
        }

        public class Empleado
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