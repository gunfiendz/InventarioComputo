using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Security.Claims;

namespace InventarioComputo.Pages.Sucursales
{
    public class SucursalesModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        public List<SucursalViewModel> Sucursales { get; set; } = new List<SucursalViewModel>();
        public List<Departamento> Departamentos { get; set; } = new List<Departamento>();
        public List<Ciudad> Ciudades { get; set; } = new List<Ciudad>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string NombreUsuario { get; set; }
        public string RolUsuario { get; set; }

        // Propiedades para ordenamiento (idénticas a Inventario)
        public string SortColumn { get; set; } = "Nombre";
        public string SortDirection { get; set; } = "ASC";

        // Propiedades para filtros
        public string DepartamentoFilter { get; set; }
        public string CiudadFilter { get; set; }
        public string BusquedaFilter { get; set; }

        public SucursalesModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync(
            int pagina = 1,
            string sortColumn = "Nombre",
            string sortDirection = "ASC",
            string departamento = null,
            string ciudad = null,
            string busqueda = "")
        {
            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            DepartamentoFilter = departamento;
            CiudadFilter = ciudad;
            BusquedaFilter = busqueda;

            // Validar columnas para evitar SQL injection (como en Inventario)
            var columnasValidas = new Dictionary<string, string>
            {
                {"Nombre", "s.Nombre"},
                {"Direccion", "s.Direccion"},
                {"Ciudad", "c.Ciudad"},
                {"Departamento", "d.Departamento"},
                {"Telefono", "s.Telefono"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "Nombre";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            // Obtener usuario
            NombreUsuario = User.Identity.Name;
            RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await CargarDepartamentos(connection);
                    await CargarCiudades(connection);
                    await CargarSucursales(connection, columnasValidas[SortColumn]);
                }
            }
            catch (Exception ex)
            {
                // Manejar error
            }
        }

        private async Task CargarDepartamentos(SqlConnection connection)
        {
            var cmd = new SqlCommand("SELECT id_departamento, Departamento FROM Departamentos ORDER BY Departamento", connection);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Departamentos.Add(new Departamento
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }

        private async Task CargarCiudades(SqlConnection connection)
        {
            var query = "SELECT id_ciudad, Ciudad FROM Ciudades";
            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrEmpty(DepartamentoFilter))
            {
                query += " WHERE id_departamento = @DepartamentoId";
                parameters.Add(new SqlParameter("@DepartamentoId", int.Parse(DepartamentoFilter)));
            }

            query += " ORDER BY Ciudad";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddRange(parameters.ToArray());

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Ciudades.Add(new Ciudad
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }

        private async Task CargarSucursales(SqlConnection connection, string sortColumn)
        {
            var query = $@"
                SELECT 
                    s.id_sucursal, s.Nombre, s.Direccion, s.Telefono,
                    c.Ciudad, d.Departamento,
                    COUNT(*) OVER() AS TotalRegistros
                FROM Sucursales s
                JOIN Ciudades c ON s.id_ciudad = c.id_ciudad
                JOIN Departamentos d ON c.id_departamento = d.id_departamento
                WHERE (@Departamento IS NULL OR c.id_departamento = @Departamento)
                AND (@Ciudad IS NULL OR s.id_ciudad = @Ciudad)
                AND (@Busqueda = '' OR s.Nombre LIKE '%' + @Busqueda + '%' OR s.Direccion LIKE '%' + @Busqueda + '%')
                ORDER BY {sortColumn} {SortDirection}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Departamento",
                string.IsNullOrEmpty(DepartamentoFilter) ? DBNull.Value : (object)int.Parse(DepartamentoFilter));
            command.Parameters.AddWithValue("@Ciudad",
                string.IsNullOrEmpty(CiudadFilter) ? DBNull.Value : (object)int.Parse(CiudadFilter));
            command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
            command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
            command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var sucursal = new SucursalViewModel
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1),
                        Direccion = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Telefono = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CiudadNombre = reader.GetString(4),
                        DepartamentoNombre = reader.GetString(5)
                    };

                    if (!reader.IsDBNull(6))
                    {
                        TotalPaginas = (int)Math.Ceiling((double)reader.GetInt32(6) / RegistrosPorPagina);
                    }

                    Sucursales.Add(sucursal);
                }
            }
        }

        public async Task<JsonResult> OnGetCiudadesPorDepartamentoAsync(int departamentoId)
        {
            var ciudades = new List<Ciudad>();

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var cmd = new SqlCommand(
                        "SELECT id_ciudad, Ciudad FROM Ciudades WHERE id_departamento = @DepartamentoId ORDER BY Ciudad",
                        connection);
                    cmd.Parameters.AddWithValue("@DepartamentoId", departamentoId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            ciudades.Add(new Ciudad
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            catch
            {
                // Log error
            }

            return new JsonResult(ciudades);
        }

        public class SucursalViewModel
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public string Direccion { get; set; }
            public string Telefono { get; set; }
            public string CiudadNombre { get; set; }
            public string DepartamentoNombre { get; set; }
        }

        public class Departamento
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class Ciudad
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}