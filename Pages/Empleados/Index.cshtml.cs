using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages.Empleados
{
    public class EmpleadosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<EmpleadosModel> _logger;

        public List<EmpleadoViewModel> Empleados { get; set; } = new List<EmpleadoViewModel>();
        public List<Departamento> Departamentos { get; set; } = new List<Departamento>();
        public List<Puesto> Puestos { get; set; } = new List<Puesto>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string SortColumn { get; set; } = "Nombre";
        public string SortDirection { get; set; } = "ASC";
        public string DepartamentoFilter { get; set; }
        public string PuestoFilter { get; set; }
        public string BusquedaFilter { get; set; }

        public EmpleadosModel(ConexionBDD dbConnection, ILogger<EmpleadosModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync(
            int pagina = 1,
            string sortColumn = "Nombre",
            string sortDirection = "ASC",
            string departamento = null,
            string puesto = null,
            string busqueda = null)
        {
            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            DepartamentoFilter = departamento;
            PuestoFilter = puesto;
            BusquedaFilter = busqueda;

            var columnasValidas = new Dictionary<string, string>
            {
                {"Nombre", "e.Nombre"},
                {"Puesto", "p.NombrePuesto"},
                {"Departamento", "d.NombreDepartamento"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "Nombre";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await CargarDatosFiltros(connection);
                    await CargarEmpleados(connection, columnasValidas[SortColumn]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar Empleados.Index");
            }
        }

        private async Task CargarDatosFiltros(SqlConnection connection)
        {
            var cmdDepartamentos = new SqlCommand("SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa", connection);
            using (var reader = await cmdDepartamentos.ExecuteReaderAsync())
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

            var cmdPuestos = new SqlCommand("SELECT id_puesto, NombrePuesto FROM Puestos", connection);
            using (var reader = await cmdPuestos.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Puestos.Add(new Puesto
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }

        private async Task CargarEmpleados(SqlConnection connection, string sortColumn)
        {
            var query = $@"
                SELECT 
                    e.id_empleado,
                    e.Nombre,
                    p.NombrePuesto,
                    d.NombreDepartamento,
                    e.Email,
                    e.Telefono,
                    COUNT(*) OVER() AS TotalRegistros
                FROM Empleados e
                JOIN Puestos p ON e.id_puesto = p.id_puesto
                JOIN DepartamentosEmpresa d ON e.id_DE = d.id_DE
                WHERE (@Departamento IS NULL OR e.id_DE = @Departamento)
                AND (@Puesto IS NULL OR e.id_puesto = @Puesto)
                AND (@Busqueda = '' OR e.Nombre LIKE '%' + @Busqueda + '%' OR e.Email LIKE '%' + @Busqueda + '%')
                ORDER BY {sortColumn} {SortDirection}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Departamento",
                string.IsNullOrEmpty(DepartamentoFilter) ? DBNull.Value : (object)int.Parse(DepartamentoFilter));
            command.Parameters.AddWithValue("@Puesto",
                string.IsNullOrEmpty(PuestoFilter) ? DBNull.Value : (object)int.Parse(PuestoFilter));
            command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
            command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
            command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var empleado = new EmpleadoViewModel
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1),
                        Puesto = reader.GetString(2),
                        Departamento = reader.GetString(3),
                        Email = reader.IsDBNull(4) ? null : reader.GetString(4),
                        Telefono = reader.IsDBNull(5) ? null : reader.GetString(5)
                    };

                    if (!reader.IsDBNull(6))
                    {
                        TotalPaginas = (int)Math.Ceiling((double)reader.GetInt32(6) / RegistrosPorPagina);
                    }

                    Empleados.Add(empleado);
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string deleteQuery = "DELETE FROM Empleados WHERE id_empleado = @Id";
                    using (var cmd = new SqlCommand(deleteQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                try
                {
                    var detalles = $"Se eliminó el empleado Id={id}.";
                    await BitacoraHelper.RegistrarAccionAsync(
                        _dbConnection,
                        _logger,
                        User,
                        BitacoraConstantes.Modulos.Empleados,
                        BitacoraConstantes.Acciones.Eliminacion,
                        detalles
                    );
                }
                catch (Exception exBit)
                {
                    _logger.LogError(exBit, "Error al registrar Bitácora de eliminación de empleado Id={Id}", id);
                }

                TempData["Mensaje"] = "¡El empleado ha sido eliminado correctamente!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al eliminar el empleado: {ex.Message}";
                _logger.LogError(ex, "Error al eliminar el empleado Id={Id}", id);
            }

            return RedirectToPage();
        }

        public class EmpleadoViewModel
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public string Puesto { get; set; }
            public string Departamento { get; set; }
            public string Email { get; set; }
            public string Telefono { get; set; }
        }

        public class Departamento
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class Puesto
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}
