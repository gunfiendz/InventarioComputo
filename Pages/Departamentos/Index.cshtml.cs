using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Security.Claims;
using System.Data;

namespace InventarioComputo.Pages.Departamentos
{
    public class DepartamentosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        public List<DepartamentoViewModel> Departamentos { get; set; } = new List<DepartamentoViewModel>();
        public List<Area> Areas { get; set; } = new List<Area>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string NombreUsuario { get; set; }
        public string RolUsuario { get; set; }
        public string SortColumn { get; set; } = "NombreDepartamento";
        public string SortDirection { get; set; } = "ASC";
        public string BusquedaFilter { get; set; }

        public DepartamentosModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync(
            int pagina = 1,
            string sortColumn = "NombreDepartamento",
            string sortDirection = "ASC",
            string busqueda = "")
        {
            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            BusquedaFilter = busqueda;

            var columnasValidas = new Dictionary<string, string>
            {
                {"NombreDepartamento", "de.NombreDepartamento"},
                {"Descripcion", "de.Descripcion"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "NombreDepartamento";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            NombreUsuario = User.Identity.Name;
            RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await CargarAreas(connection);
                    await CargarDepartamentos(connection, columnasValidas[SortColumn]);
                }
            }
            catch (Exception ex)
            {
                // Manejar error
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
                            // Verificar si hay empleados asignados a este departamento
                            string checkEmpleadosQuery = "SELECT COUNT(*) FROM Empleados WHERE id_DE = @Id";
                            using (var cmd = new SqlCommand(checkEmpleadosQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Id", id);
                                int empleadosCount = (int)await cmd.ExecuteScalarAsync();
                                if (empleadosCount > 0)
                                {
                                    throw new InvalidOperationException($"No se puede eliminar el departamento porque tiene {empleadosCount} empleado(s) asociado(s).");
                                }
                            }

                            // 1. Eliminar las áreas asociadas en DepartamentosAreas
                            string deleteAreasQuery = "DELETE FROM DepartamentosAreas WHERE id_DE = @Id";
                            using (var cmd = new SqlCommand(deleteAreasQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Id", id);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            // 2. Eliminar el registro del departamento en DepartamentosEmpresa
                            string deleteDepartamentoQuery = "DELETE FROM DepartamentosEmpresa WHERE id_DE = @Id";
                            using (var cmd = new SqlCommand(deleteDepartamentoQuery, connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@Id", id);
                                await cmd.ExecuteNonQueryAsync();
                            }

                            await transaction.CommitAsync();
                            TempData["Mensaje"] = "¡El departamento ha sido eliminado correctamente!";
                        }
                        catch (InvalidOperationException ex)
                        {
                            await transaction.RollbackAsync();
                            TempData["Error"] = ex.Message;
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            TempData["Error"] = $"Error en la transacción al eliminar: {ex.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error general al eliminar el departamento: {ex.Message}";
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