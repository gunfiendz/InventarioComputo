using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventarioComputo.Pages.Asignaciones
{
    public class IndexModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        public List<AsignacionHistorialViewModel> Historial { get; set; } = new List<AsignacionHistorialViewModel>();

        [BindProperty(SupportsGet = true)]
        public string? FechaInicio { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? FechaFin { get; set; }
        [BindProperty(SupportsGet = true)]
        public int? EmpleadoId { get; set; }
        [BindProperty(SupportsGet = true)]
        public int? DepartamentoId { get; set; }
        [BindProperty(SupportsGet = true)]
        public string? Busqueda { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; }
        public int RegistrosPorPagina { get; set; } = 15;
        [BindProperty(SupportsGet = true)]
        public string SortColumn { get; set; } = "FechaAsignacion";
        [BindProperty(SupportsGet = true)]
        public string SortDirection { get; set; } = "DESC";

        public List<SelectListItem> Empleados { get; set; }
        public List<SelectListItem> Departamentos { get; set; }

        public IndexModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync()
        {
            await CargarFiltros();

            var queryBuilder = new StringBuilder(@"
                SELECT 
                    ee.id_empleadoequipo,
                    ee.FechaAsignacion, ee.FechaRetiro,
                    af.EtiquetaInv + ' - ' + p.NombrePerfil AS Equipo,
                    af.NumeroSerie, e.Nombre AS Empleado,
                    de.NombreDepartamento AS Departamento,
                    ee.ResponsableActual AS EsActiva,
                    ee.TipoAsignacion,
                    COUNT(*) OVER() as TotalRegistros
                FROM EmpleadosEquipos ee
                JOIN ActivosFijos af ON ee.id_activofijo = af.id_activofijo
                JOIN Perfiles p ON af.id_perfil = p.id_perfil
                JOIN Empleados e ON ee.id_empleado = e.id_empleado
                JOIN DepartamentosEmpresa de ON ee.id_DE = de.id_DE
                WHERE 1=1");

            var parameters = new List<SqlParameter>();

            if (EmpleadoId.HasValue)
            {
                queryBuilder.Append(" AND e.id_empleado = @EmpleadoId");
                parameters.Add(new SqlParameter("@EmpleadoId", EmpleadoId.Value));
            }
            if (DepartamentoId.HasValue)
            {
                queryBuilder.Append(" AND de.id_DE = @DepartamentoId");
                parameters.Add(new SqlParameter("@DepartamentoId", DepartamentoId.Value));
            }
            if (!string.IsNullOrEmpty(FechaInicio))
            {
                queryBuilder.Append(" AND ee.FechaAsignacion >= @FechaInicio");
                parameters.Add(new SqlParameter("@FechaInicio", FechaInicio));
            }
            if (!string.IsNullOrEmpty(FechaFin))
            {
                queryBuilder.Append(" AND ee.FechaAsignacion <= @FechaFin");
                parameters.Add(new SqlParameter("@FechaFin", FechaFin));
            }
            if (!string.IsNullOrEmpty(Busqueda))
            {
                queryBuilder.Append(" AND (af.EtiquetaInv LIKE @Busqueda OR af.NumeroSerie LIKE @Busqueda)");
                parameters.Add(new SqlParameter("@Busqueda", $"%{Busqueda}%"));
            }

            var sortColumns = new Dictionary<string, string>
            {
                { "FechaAsignacion", "ee.FechaAsignacion" },
                { "Equipo", "Equipo" },
                { "Empleado", "Empleado" },
                { "Departamento", "Departamento" },
                { "FechaRetiro", "ee.FechaRetiro" }
            };

            var sortColumn = sortColumns.ContainsKey(SortColumn) ? sortColumns[SortColumn] : "ee.FechaAsignacion";
            var sortDirection = SortDirection.ToUpper() == "ASC" ? "ASC" : "DESC";

            queryBuilder.Append($" ORDER BY {sortColumn} {sortDirection}");
            queryBuilder.Append(" OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");

            parameters.Add(new SqlParameter("@Offset", (PaginaActual - 1) * RegistrosPorPagina));
            parameters.Add(new SqlParameter("@PageSize", RegistrosPorPagina));

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var command = new SqlCommand(queryBuilder.ToString(), connection);
                command.Parameters.AddRange(parameters.ToArray());

                using (var reader = await command.ExecuteReaderAsync())
                {
                    int totalRegistros = 0;
                    while (await reader.ReadAsync())
                    {
                        Historial.Add(new AsignacionHistorialViewModel
                        {
                            Id = reader.GetInt32(0),
                            FechaAsignacion = reader.GetDateTime(1),
                            FechaRetiro = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2),
                            Equipo = reader.GetString(3),
                            NumeroSerie = reader.GetString(4),
                            Empleado = reader.GetString(5),
                            Departamento = reader.GetString(6),
                            EsActiva = reader.GetBoolean(7),
                            TipoAsignacion = reader.IsDBNull(8) ? "" : reader.GetString(8)
                        });
                        totalRegistros = reader.GetInt32(9);
                    }
                    if (totalRegistros > 0)
                        TotalPaginas = (int)Math.Ceiling(totalRegistros / (double)RegistrosPorPagina);
                }
            }
        }

        private async Task CargarFiltros()
        {
            Empleados = new List<SelectListItem>();
            Departamentos = new List<SelectListItem>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmdEmpleados = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre", connection);
                using (var reader = await cmdEmpleados.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        Empleados.Add(new SelectListItem { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) });
                    }
                }

                var cmdDeptos = new SqlCommand("SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa ORDER BY NombreDepartamento", connection);
                using (var reader = await cmdDeptos.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        Departamentos.Add(new SelectListItem { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) });
                    }
                }
            }
        }
    }

    public class AsignacionHistorialViewModel
    {
        public int Id { get; set; }
        public DateTime FechaAsignacion { get; set; }
        public DateTime? FechaRetiro { get; set; }
        public string Equipo { get; set; }
        public string NumeroSerie { get; set; }
        public string Empleado { get; set; }
        public string Departamento { get; set; }
        public bool EsActiva { get; set; }
        public string TipoAsignacion { get; set; }
    }
}