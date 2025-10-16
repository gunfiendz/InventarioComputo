using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages.Inventario
{
    public class InventarioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<InventarioModel> _logger;

        public List<Equipo> Equipos { get; set; } = new List<Equipo>();
        public List<DepartamentoEmpresa> DepartamentosEmpresa { get; set; } = new List<DepartamentoEmpresa>();
        public List<Estado> Estados { get; set; } = new List<Estado>();
        public List<TipoEquipo> TiposEquipos { get; set; } = new List<TipoEquipo>();
        public List<Marca> Marcas { get; set; } = new List<Marca>();
        public List<Empleado> Empleados { get; set; } = new List<Empleado>();

        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;

        public string NombreUsuario { get; set; }
        public string RolUsuario { get; set; }

        public string SortColumn { get; set; } = "EtiquetaInv";
        public string SortDirection { get; set; } = "ASC";

        [BindProperty(SupportsGet = true)] public string TipoEquipoFilter { get; set; }
        [BindProperty(SupportsGet = true)] public string MarcaFilter { get; set; }
        [BindProperty(SupportsGet = true)] public string DepartamentoEmpresaFilter { get; set; }
        [BindProperty(SupportsGet = true)] public string EstadoFilter { get; set; }
        [BindProperty(SupportsGet = true)] public string BusquedaFilter { get; set; }
        [BindProperty(SupportsGet = true)] public string VidaUtilFilter { get; set; }

        [BindProperty(SupportsGet = true)] public DateTime? FechaDesdeFilter { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? FechaHastaFilter { get; set; }

        public InventarioModel(ConexionBDD dbConnection, ILogger<InventarioModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        private static void AgregarCopiaParametros(SqlCommand cmd, IEnumerable<SqlParameter> parameters)
        {
            foreach (var p in parameters)
            {
                var copy = new SqlParameter(p.ParameterName, p.Value ?? DBNull.Value);
                cmd.Parameters.Add(copy);
            }
        }


        public async Task OnGetAsync(int pagina = 1, string sortColumn = null, string sortDirection = null)
        {
            if (!string.IsNullOrWhiteSpace(sortColumn)) SortColumn = sortColumn;
            if (!string.IsNullOrWhiteSpace(sortDirection)) SortDirection = sortDirection;

            PaginaActual = pagina;
            NombreUsuario = User.Identity?.Name;
            RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

            var columnasValidas = new Dictionary<string, string>
            {
                {"EtiquetaInv", "af.EtiquetaInv"},
                {"NumeroSerie", "af.NumeroSerie"},
                {"TipoEquipo", "te.TipoEquipo"},
                {"Marca", "m.Marca"},
                {"DepartamentoEmpresa", "ISNULL(de.NombreDepartamento, 'No asignado')"},
                {"Estado", "e.Estado"},
                {"AsignadoA", "ISNULL(emp.Nombre, 'No asignado')"},
                {"VidaUtil", "VidaUtilAnios"}
            };

            if (!columnasValidas.ContainsKey(SortColumn)) SortColumn = "EtiquetaInv";
            SortDirection = SortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    using (var rd = await new SqlCommand("SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa ORDER BY NombreDepartamento", connection).ExecuteReaderAsync())
                        while (await rd.ReadAsync()) DepartamentosEmpresa.Add(new DepartamentoEmpresa { id_DE = rd.GetInt32(0), NombreDepartamento = rd.GetString(1) });

                    using (var rd = await new SqlCommand("SELECT id_estado, Estado FROM Estados ORDER BY Estado", connection).ExecuteReaderAsync())
                        while (await rd.ReadAsync()) Estados.Add(new Estado { id_estado = rd.GetInt32(0), EstadoActual = rd.GetString(1) });

                    using (var rd = await new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo", connection).ExecuteReaderAsync())
                        while (await rd.ReadAsync()) TiposEquipos.Add(new TipoEquipo { id_tipoequipo = rd.GetInt32(0), NombreTipoEquipo = rd.GetString(1) });

                    using (var rd = await new SqlCommand("SELECT id_marca, Marca FROM Marcas ORDER BY Marca", connection).ExecuteReaderAsync())
                        while (await rd.ReadAsync()) Marcas.Add(new Marca { id_marca = rd.GetInt32(0), NombreMarca = rd.GetString(1) });

                    var sbWhere = new System.Text.StringBuilder(@"
FROM ActivosFijos af
JOIN Perfiles p       ON af.id_perfil = p.id_perfil
JOIN Modelos mo       ON p.id_modelo = mo.id_modelo
JOIN Marcas m         ON mo.id_marca = m.id_marca
JOIN TiposEquipos te  ON mo.id_tipoequipo = te.id_tipoequipo
JOIN Estados e        ON af.id_estado = e.id_estado
LEFT JOIN EmpleadosEquipos ee ON af.id_activofijo = ee.id_activofijo AND ee.ResponsableActual = 1
LEFT JOIN Empleados emp       ON ee.id_empleado = emp.id_empleado
LEFT JOIN DepartamentosEmpresa de ON ee.id_DE = de.id_DE
WHERE 1=1 ");

                    var parameters = new List<SqlParameter>();

                    if (!string.IsNullOrEmpty(TipoEquipoFilter))
                    {
                        sbWhere.Append(" AND te.id_tipoequipo = @TipoEquipo ");
                        parameters.Add(new SqlParameter("@TipoEquipo", TipoEquipoFilter));
                    }
                    if (!string.IsNullOrEmpty(MarcaFilter))
                    {
                        sbWhere.Append(" AND m.id_marca = @Marca ");
                        parameters.Add(new SqlParameter("@Marca", MarcaFilter));
                    }
                    if (!string.IsNullOrEmpty(DepartamentoEmpresaFilter))
                    {
                        sbWhere.Append(" AND de.id_DE = @DepartamentoEmpresa ");
                        parameters.Add(new SqlParameter("@DepartamentoEmpresa", DepartamentoEmpresaFilter));
                    }
                    if (!string.IsNullOrEmpty(EstadoFilter))
                    {
                        sbWhere.Append(" AND e.id_estado = @Estado ");
                        parameters.Add(new SqlParameter("@Estado", EstadoFilter));
                    }

                    if (!string.IsNullOrEmpty(BusquedaFilter))
                    {
                        sbWhere.Append(" AND (af.EtiquetaInv LIKE @Busqueda OR af.NumeroSerie LIKE @Busqueda) ");
                        parameters.Add(new SqlParameter("@Busqueda", $"%{BusquedaFilter}%"));
                    }

                    if (!string.IsNullOrEmpty(VidaUtilFilter))
                    {
                        switch (VidaUtilFilter)
                        {
                            case "sin-dato":
                                sbWhere.Append(" AND af.FechaFinVidaUtil IS NULL ");
                                break;
                            case "vencida":
                                sbWhere.Append(" AND af.FechaFinVidaUtil IS NOT NULL AND DATEDIFF(YEAR, GETDATE(), af.FechaFinVidaUtil) <= 0 ");
                                break;
                            case "0-1":
                                sbWhere.Append(" AND af.FechaFinVidaUtil IS NOT NULL AND DATEDIFF(YEAR, GETDATE(), af.FechaFinVidaUtil) > 0 AND DATEDIFF(YEAR, GETDATE(), af.FechaFinVidaUtil) <= 1 ");
                                break;
                            case "1-3":
                                sbWhere.Append(" AND af.FechaFinVidaUtil IS NOT NULL AND DATEDIFF(YEAR, GETDATE(), af.FechaFinVidaUtil) > 1 AND DATEDIFF(YEAR, GETDATE(), af.FechaFinVidaUtil) <= 3 ");
                                break;
                            case "3plus":
                                sbWhere.Append(" AND af.FechaFinVidaUtil IS NOT NULL AND DATEDIFF(YEAR, GETDATE(), af.FechaFinVidaUtil) > 3 ");
                                break;
                        }
                    }

                    if (FechaDesdeFilter.HasValue)
                    {
                        sbWhere.Append(" AND af.FechaCompra >= @FechaDesde ");
                        parameters.Add(new SqlParameter("@FechaDesde", FechaDesdeFilter.Value.Date));
                    }
                    if (FechaHastaFilter.HasValue)
                    {
                        sbWhere.Append(" AND af.FechaCompra < @FechaHastaNext ");
                        parameters.Add(new SqlParameter("@FechaHastaNext", FechaHastaFilter.Value.Date.AddDays(1)));
                    }

                    var countSql = $"SELECT COUNT(*) {sbWhere}";
                    var countCmd = new SqlCommand(countSql, connection);
                    AgregarCopiaParametros(countCmd, parameters);
                    var totalRegistros = (int)await countCmd.ExecuteScalarAsync();
                    TotalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)RegistrosPorPagina));

                    var selectSql = $@"
SELECT 
    af.id_activofijo,
    af.EtiquetaInv,
    af.NumeroSerie,
    te.TipoEquipo,
    p.NombrePerfil,
    m.Marca,
    mo.Modelo,
    ISNULL(de.NombreDepartamento, 'No asignado') AS Departamento,
    e.Estado,
    ISNULL(emp.Nombre, 'No asignado') AS AsignadoA,
    CASE WHEN af.FechaFinVidaUtil IS NULL THEN NULL 
         ELSE DATEDIFF(YEAR, GETDATE(), af.FechaFinVidaUtil) 
    END AS VidaUtilAnios
{sbWhere}
ORDER BY {columnasValidas[SortColumn]} {SortDirection}
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                    var cmd = new SqlCommand(selectSql, connection);
                    cmd.Parameters.AddRange(parameters.ToArray());
                    cmd.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
                    cmd.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

                    using (var rd = await cmd.ExecuteReaderAsync())
                    {
                        int oId = rd.GetOrdinal("id_activofijo");
                        int oEtiq = rd.GetOrdinal("EtiquetaInv");
                        int oSerie = rd.GetOrdinal("NumeroSerie");
                        int oTipo = rd.GetOrdinal("TipoEquipo");
                        int oPerfil = rd.GetOrdinal("NombrePerfil");
                        int oMarca = rd.GetOrdinal("Marca");
                        int oModelo = rd.GetOrdinal("Modelo");
                        int oDep = rd.GetOrdinal("Departamento");
                        int oEstado = rd.GetOrdinal("Estado");
                        int oAsig = rd.GetOrdinal("AsignadoA");
                        int oVU = rd.GetOrdinal("VidaUtilAnios");

                        while (await rd.ReadAsync())
                        {
                            Equipos.Add(new Equipo
                            {
                                id_activofijo = rd.GetInt32(oId),
                                EtiquetaInv = rd.GetString(oEtiq),
                                NumeroSerie = rd.GetString(oSerie),
                                TipoEquipo = rd.GetString(oTipo),
                                NombrePerfil = rd.GetString(oPerfil),
                                Marca = rd.GetString(oMarca),
                                Modelo = rd.GetString(oModelo),
                                DepartamentoEmpresa = rd.GetString(oDep),
                                Estado = rd.GetString(oEstado),
                                AsignadoA = rd.GetString(oAsig),
                                VidaUtilAnios = rd.IsDBNull(oVU) ? (int?)null : rd.GetInt32(oVU)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar el listado de inventario.");
                TempData["ErrorMessage"] = "Ocurrió un error al cargar el inventario.";
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                using (var tx = connection.BeginTransaction())
                {
                    string etiquetaInv = "desconocida";
                    var cmdInfo = new SqlCommand("SELECT EtiquetaInv FROM ActivosFijos WHERE id_activofijo = @Id", connection, tx);
                    cmdInfo.Parameters.AddWithValue("@Id", id);
                    var result = await cmdInfo.ExecuteScalarAsync();
                    if (result != null) etiquetaInv = result.ToString();

                    using (var cmd1 = new SqlCommand("DELETE FROM EmpleadosEquipos WHERE id_activofijo = @Id", connection, tx))
                    { cmd1.Parameters.AddWithValue("@Id", id); await cmd1.ExecuteNonQueryAsync(); }

                    using (var cmd2 = new SqlCommand("DELETE FROM MantenimientosEquipos WHERE id_activofijo = @Id", connection, tx))
                    { cmd2.Parameters.AddWithValue("@Id", id); await cmd2.ExecuteNonQueryAsync(); }

                    using (var cmd3 = new SqlCommand("DELETE FROM ActivosFijos WHERE id_activofijo = @Id", connection, tx))
                    { cmd3.Parameters.AddWithValue("@Id", id); await cmd3.ExecuteNonQueryAsync(); }

                    await tx.CommitAsync();

                    string detalles = $"Se eliminó el activo fijo con etiqueta '{etiquetaInv}' (ID: {id}).";
                    await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                        BitacoraConstantes.Modulos.Inventario,
                        BitacoraConstantes.Acciones.Eliminacion,
                        detalles);
                }

                TempData["SuccessMessage"] = "¡El activo fijo ha sido eliminado correctamente!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Error al eliminar el activo fijo. Puede que tenga dependencias.";
                _logger.LogError(ex, "Error al eliminar activo fijo con ID {ActivoId}. Usuario: {Username}",
                    id, User?.Identity?.Name ?? "anon");
            }
            return RedirectToPage();
        }

        public class Equipo
        {
            public int id_activofijo { get; set; }
            public string EtiquetaInv { get; set; }
            public string NumeroSerie { get; set; }
            public string TipoEquipo { get; set; }
            public string NombrePerfil { get; set; }
            public string Marca { get; set; }
            public string Modelo { get; set; }
            public string DepartamentoEmpresa { get; set; }
            public string Estado { get; set; }
            public string AsignadoA { get; set; }
            public int? VidaUtilAnios { get; set; }
        }

        public class DepartamentoEmpresa { public int id_DE { get; set; } public string NombreDepartamento { get; set; } }
        public class Estado { public int id_estado { get; set; } public string EstadoActual { get; set; } }
        public class TipoEquipo { public int id_tipoequipo { get; set; } public string NombreTipoEquipo { get; set; } }
        public class Marca { public int id_marca { get; set; } public string NombreMarca { get; set; } }
        public class Empleado { public int id_empleado { get; set; } public string NombreEmpleado { get; set; } }
    }
}
