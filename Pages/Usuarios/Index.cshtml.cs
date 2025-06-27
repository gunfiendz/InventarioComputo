using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace InventarioComputo.Pages.Usuarios
{
    public class UsuariosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        public List<UsuarioViewModel> Usuarios { get; set; } = new List<UsuarioViewModel>();
        public List<Rol> Roles { get; set; } = new List<Rol>();
        public List<Empleado> Empleados { get; set; } = new List<Empleado>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string SortColumn { get; set; } = "Usuario";
        public string SortDirection { get; set; } = "ASC";
        public string RolFilter { get; set; }
        public string EmpleadoFilter { get; set; }
        public string BusquedaFilter { get; set; }

        public UsuariosModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync(
            int pagina = 1,
            string sortColumn = "Usuario",
            string sortDirection = "ASC",
            string rol = null,
            string empleado = null,
            string busqueda = null)
        {
            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            RolFilter = rol;
            EmpleadoFilter = empleado;
            BusquedaFilter = busqueda;

            // Validar columnas
            var columnasValidas = new Dictionary<string, string>
            {
                {"Usuario", "u.Username"},
                {"Empleado", "e.Nombre"},
                {"Rol", "r.NombreRol"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "Usuario";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await CargarDatosFiltros(connection);
                    await CargarUsuarios(connection, columnasValidas[SortColumn]);
                }
            }
            catch (Exception ex)
            {
                // Manejar error
            }
        }

        private async Task CargarDatosFiltros(SqlConnection connection)
        {
            // Roles
            var cmdRoles = new SqlCommand("SELECT id_rol, NombreRol FROM Roles", connection);
            using (var reader = await cmdRoles.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Roles.Add(new Rol
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }

            // Empleados
            var cmdEmpleados = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados", connection);
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
        }

        private async Task CargarUsuarios(SqlConnection connection, string sortColumn)
        {
            var query = $@"
                SELECT 
                    u.id_usuario,
                    u.Username,
                    e.Nombre AS Empleado,
                    r.NombreRol AS Rol,
                    u.FechaPassword AS UltimoAcceso,
                    COUNT(*) OVER() AS TotalRegistros
                FROM Usuarios u
                JOIN Empleados e ON u.id_empleado = e.id_empleado
                JOIN Roles r ON (
                    SELECT TOP 1 id_rol 
                    FROM EmpleadosEquipos ee 
                    WHERE ee.id_empleado = u.id_empleado 
                    AND ee.ResponsableActual = 1
                ) = r.id_rol
                WHERE (@Rol IS NULL OR r.id_rol = @Rol)
                AND (@Empleado IS NULL OR u.id_empleado = @Empleado)
                AND (@Busqueda = '' OR u.Username LIKE '%' + @Busqueda + '%' OR e.Nombre LIKE '%' + @Busqueda + '%')
                ORDER BY {sortColumn} {SortDirection}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Rol",
                string.IsNullOrEmpty(RolFilter) ? DBNull.Value : (object)int.Parse(RolFilter));
            command.Parameters.AddWithValue("@Empleado",
                string.IsNullOrEmpty(EmpleadoFilter) ? DBNull.Value : (object)int.Parse(EmpleadoFilter));
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
                        Username = reader.GetString(1),
                        Empleado = reader.GetString(2),
                        Rol = reader.GetString(3),
                        UltimoAcceso = reader.IsDBNull(4) ? null : (DateTime?)reader.GetDateTime(4)
                    };

                    if (!reader.IsDBNull(5))
                    {
                        TotalPaginas = (int)Math.Ceiling((double)reader.GetInt32(5) / RegistrosPorPagina);
                    }

                    Usuarios.Add(usuario);
                }
            }
        }

        public class UsuarioViewModel
        {
            public int Id { get; set; }
            public string Username { get; set; }
            public string Empleado { get; set; }
            public string Rol { get; set; }
            public DateTime? UltimoAcceso { get; set; }
        }

        public class Rol
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class Empleado
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}