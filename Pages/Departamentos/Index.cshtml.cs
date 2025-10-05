using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;
using System.Text;

namespace InventarioComputo.Pages.Departamentos
{
    public class DepartamentosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<DepartamentosModel> _logger;

        public List<DepartamentoViewModel> Departamentos { get; set; } = new List<DepartamentoViewModel>();
        public List<Area> Areas { get; set; } = new List<Area>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string NombreUsuario { get; set; }
        public string RolUsuario { get; set; }
        public string SortColumn { get; set; } = "NombreDepartamento";
        public string SortDirection { get; set; } = "ASC";

        [BindProperty(SupportsGet = true)]
        public string BusquedaFilter { get; set; }

        public DepartamentosModel(ConexionBDD dbConnection, ILogger<DepartamentosModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            // Mapea columnas permitidas para ORDER BY (idéntico patrón a tus otros index)
            var sortColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "NombreDepartamento", "de.NombreDepartamento" },
                { "Descripcion",        "de.Descripcion" }
            };
            var sortColExpr = sortColumns.ContainsKey(SortColumn) ? sortColumns[SortColumn] : "de.NombreDepartamento";
            var sortDir = (SortDirection?.ToUpper() == "DESC") ? "DESC" : "ASC";

            var queryBuilder = new StringBuilder(@"
                SELECT 
                    de.id_DE,
                    de.NombreDepartamento,
                    de.Descripcion,
                    COUNT(*) OVER() AS TotalRegistros
                FROM DepartamentosEmpresa de
                WHERE 1=1");

            var parameters = new List<SqlParameter>();

            if (!string.IsNullOrWhiteSpace(BusquedaFilter))
            {
                queryBuilder.Append(" AND (de.NombreDepartamento LIKE @Busqueda OR de.Descripcion LIKE @Busqueda)");
                parameters.Add(new SqlParameter("@Busqueda", $"%{BusquedaFilter.Trim()}%"));
            }

            queryBuilder.Append($" ORDER BY {sortColExpr} {sortDir}, de.id_DE DESC");
            queryBuilder.Append(" OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");

            parameters.Add(new SqlParameter("@Offset", (PaginaActual - 1) * RegistrosPorPagina));
            parameters.Add(new SqlParameter("@PageSize", RegistrosPorPagina));

            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                using var command = new SqlCommand(queryBuilder.ToString(), connection);
                command.Parameters.AddRange(parameters.ToArray());

                using var reader = await command.ExecuteReaderAsync();
                int totalRegistros = 0;

                while (await reader.ReadAsync())
                {
                    Departamentos.Add(new DepartamentoViewModel
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1),
                        Descripcion = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        // AreasAsociadas queda vacía si no la llenas en otro query (la vista compila igual)
                    });

                    if (totalRegistros == 0)
                        totalRegistros = reader.GetInt32(3);
                }

                if (totalRegistros > 0)
                    TotalPaginas = (int)Math.Ceiling(totalRegistros / (double)RegistrosPorPagina);
            }
            catch (Exception ex)
            {
                // Solo error en catch, como pediste
                _logger.LogError(ex, "Error al cargar Departamentos.Index");
                // la vista puede renderizar con lista vacía
            }
        }

        private async Task CargarAreas(SqlConnection connection)
        {
            var cmd = new SqlCommand("SELECT id_area, NombreArea FROM Areas ORDER BY NombreArea", connection);
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Areas.Add(new Area
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }

        private async Task CargarDepartamentos(SqlConnection connection, string sortColumn)
        {
            var countQuery = @"
                SELECT COUNT(DISTINCT de.id_DE) 
                FROM DepartamentosEmpresa de
                LEFT JOIN DepartamentosAreas da ON de.id_DE = da.id_DE
                LEFT JOIN Areas a ON da.id_area = a.id_area
                WHERE (@Busqueda = '' OR de.NombreDepartamento LIKE '%' + @Busqueda + '%' OR de.Descripcion LIKE '%' + @Busqueda + '%')";

            var countCommand = new SqlCommand(countQuery, connection);
            countCommand.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
            var totalRegistros = (int)await countCommand.ExecuteScalarAsync();
            TotalPaginas = (int)Math.Ceiling((double)totalRegistros / RegistrosPorPagina);

            var query = $@"
                SELECT 
                    de.id_DE,
                    de.NombreDepartamento,
                    de.Descripcion,
                    a.NombreArea
                FROM DepartamentosEmpresa de
                LEFT JOIN DepartamentosAreas da ON de.id_DE = da.id_DE
                LEFT JOIN Areas a ON da.id_area = a.id_area
                WHERE (@Busqueda = '' OR de.NombreDepartamento LIKE '%' + @Busqueda + '%' OR de.Descripcion LIKE '%' + @Busqueda + '%')
                ORDER BY {sortColumn} {SortDirection}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
            command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
            command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

            Departamentos = new List<DepartamentoViewModel>();
            var departamentosDict = new Dictionary<int, DepartamentoViewModel>();

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    int id = reader.GetInt32(0);

                    if (!departamentosDict.TryGetValue(id, out var departamento))
                    {
                        departamento = new DepartamentoViewModel
                        {
                            Id = id,
                            Nombre = reader.GetString(1),
                            Descripcion = reader.IsDBNull(2) ? null : reader.GetString(2)
                        };
                        departamentosDict[id] = departamento;
                        Departamentos.Add(departamento);
                    }

                    if (!reader.IsDBNull(3))
                    {
                        departamento.AreasAsociadas.Add(reader.GetString(3));
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Validar empleados asociados
                            string checkEmpleadosQuery = "SELECT COUNT(*) FROM Empleados WHERE id_DE = @Id";
                            using (var cmd = new SqlCommand(checkEmpleadosQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Id", id);
                                int empleadosCount = (int)await cmd.ExecuteScalarAsync();
                                if (empleadosCount > 0)
                                {
                                    TempData["Error"] = $"No se puede eliminar el departamento porque tiene {empleadosCount} empleado(s) asociado(s).";
                                    await transaction.RollbackAsync();
                                    return RedirectToPage();
                                }
                            }

                            string deleteAreasQuery = "DELETE FROM DepartamentosAreas WHERE id_DE = @Id";
                            using (var cmd = new SqlCommand(deleteAreasQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Id", id);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            string deleteDepartamentoQuery = "DELETE FROM DepartamentosEmpresa WHERE id_DE = @Id";
                            using (var cmd = new SqlCommand(deleteDepartamentoQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Id", id);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            await transaction.CommitAsync();

                            try
                            {
                                var detalles = $"Se eliminó el departamento Id={id}.";
                                await BitacoraHelper.RegistrarAccionAsync(
                                    _dbConnection,
                                    _logger,
                                    User,
                                    BitacoraConstantes.Modulos.Departamentos,
                                    BitacoraConstantes.Acciones.Eliminacion,
                                    detalles
                                );
                            }
                            catch (Exception exBit)
                            {
                                _logger.LogError(exBit, "Error al registrar Bitácora de eliminación de departamento Id={Id}", id);
                            }

                            TempData["Success"] = "Departamento eliminado correctamente.";
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            TempData["Error"] = $"Error en la transacción al eliminar: {ex.Message}";
                            _logger.LogError(ex, "Error en la transacción al eliminar departamento Id={Id}", id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error general al eliminar el departamento: {ex.Message}";
                _logger.LogError(ex, "Error general al eliminar el departamento Id={Id}", id);
            }

            return RedirectToPage();
        }

        public class DepartamentoViewModel
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public string Descripcion { get; set; }
            public List<string> AreasAsociadas { get; set; } = new List<string>();
        }

        public class Area
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}
