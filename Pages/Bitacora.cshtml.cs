using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Data;

namespace InventarioComputo.Pages
{
    public class BitacoraModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<BitacoraModel> _logger;

        public List<BitacoraRegistro> Registros { get; set; } = new List<BitacoraRegistro>();
        public List<Modulo> Modulos { get; set; } = new List<Modulo>();
        public List<Accion> Acciones { get; set; } = new List<Accion>();

        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;

        public string SortColumn { get; set; } = "FechaHora";
        public string SortDirection { get; set; } = "DESC";
        public string RolUsuario { get; set; }

        // Filtros
        public string BusquedaFilter { get; set; }
        public string ModuloFilter { get; set; }
        public string AccionFilter { get; set; }
        public string FechaInicioFilter { get; set; }
        public string FechaFinFilter { get; set; }

        public BitacoraModel(ConexionBDD dbConnection, ILogger<BitacoraModel> logger)
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

        public async Task<IActionResult> OnGetAsync(
            int pagina = 1,
            string sortColumn = "FechaHora",
            string sortDirection = "DESC",
            string busqueda = "",
            string modulo = "",
            string accion = "",
            string fechainicio = "",
            string fechafin = "")
        {
            RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

            if (RolUsuario != "Administrador")
            {
                _logger.LogWarning("Bitácora: acceso denegado para usuario {User} con rol {Rol}",
                    User?.Identity?.Name ?? "(anon)", RolUsuario ?? "(null)");
                return Forbid();
            }

            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            BusquedaFilter = busqueda;
            ModuloFilter = modulo;
            AccionFilter = accion;
            FechaInicioFilter = fechainicio;
            FechaFinFilter = fechafin;

            var columnasValidas = new Dictionary<string, string>
            {
                {"FechaHora", "b.FechaHora"},
                {"Usuario", "u.Username"},
                {"Modulo", "m.Modulo"},
                {"Accion", "a.Accion"},
                {"Detalles", "b.Detalles"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
                SortColumn = "FechaHora";

            SortDirection = SortDirection?.ToUpper() == "DESC" ? "DESC" : "ASC";

            await CargarDatosFiltros();
            await CargarRegistros(columnasValidas[SortColumn]);

            return Page();
        }

        private async Task CargarDatosFiltros()
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // Módulos
                    var cmdModulos = new SqlCommand("SELECT id_modulo, Modulo FROM Modulos ORDER BY Modulo", connection);
                    using (var reader = await cmdModulos.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            Modulos.Add(new Modulo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }

                    // Acciones
                    var cmdAcciones = new SqlCommand("SELECT id_accion, Accion FROM Acciones ORDER BY Accion", connection);
                    using (var reader = await cmdAcciones.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                            Acciones.Add(new Accion { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Error al cargar filtros de bitácora.");
                _logger.LogError(ex, "Bitácora: error al cargar los filtros (Módulos/Acciones).");
            }
        }

        private async Task CargarRegistros(string sortColumn)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var where = new System.Text.StringBuilder(@"
                        FROM Bitacora b
                        JOIN Usuarios u ON b.id_usuario = u.id_usuario
                        JOIN Modulos m ON b.id_modulo = m.id_modulo
                        JOIN Acciones a ON b.id_accion = a.id_accion
                        WHERE 1=1 ");

                    var parameters = new List<SqlParameter>();

                    // Filtros
                    if (!string.IsNullOrWhiteSpace(BusquedaFilter))
                    {
                        where.Append(" AND (b.Detalles LIKE @Busqueda OR u.Username LIKE @Busqueda) ");
                        parameters.Add(new SqlParameter("@Busqueda", $"%{BusquedaFilter}%"));
                    }

                    if (!string.IsNullOrWhiteSpace(ModuloFilter) && int.TryParse(ModuloFilter, out var moduloId))
                    {
                        where.Append(" AND b.id_modulo = @Modulo ");
                        parameters.Add(new SqlParameter("@Modulo", moduloId));
                    }

                    if (!string.IsNullOrWhiteSpace(AccionFilter) && int.TryParse(AccionFilter, out var accionId))
                    {
                        where.Append(" AND b.id_accion = @Accion ");
                        parameters.Add(new SqlParameter("@Accion", accionId));
                    }

                    if (!string.IsNullOrWhiteSpace(FechaInicioFilter) && DateTime.TryParse(FechaInicioFilter, out var fIni))
                    {
                        where.Append(" AND b.FechaHora >= @FechaInicio ");
                        parameters.Add(new SqlParameter("@FechaInicio", fIni));
                    }

                    if (!string.IsNullOrWhiteSpace(FechaFinFilter) && DateTime.TryParse(FechaFinFilter, out var fFin))
                    {
                        where.Append(" AND b.FechaHora <= @FechaFin ");
                        parameters.Add(new SqlParameter("@FechaFin", fFin));
                    }

                    // COUNT total registros
                    var countSql = "SELECT COUNT(*) " + where.ToString();
                    int totalRegistros = 0;
                    using (var countCmd = new SqlCommand(countSql, connection))
                    {
                        AgregarCopiaParametros(countCmd, parameters); // usar copias
                        var countObj = await countCmd.ExecuteScalarAsync();
                        if (countObj != null && countObj != DBNull.Value)
                            totalRegistros = Convert.ToInt32(countObj);
                    }

                    RegistrosPorPagina = 15;
                    TotalPaginas = Math.Max(1, (int)Math.Ceiling(totalRegistros / (double)RegistrosPorPagina));

                    var selectSql = $@"
                        SELECT
                            b.id_evento,
                            u.Username,
                            b.FechaHora,
                            m.Modulo,
                            a.Accion,
                            b.Detalles
                        {where}
                        ORDER BY {sortColumn} {SortDirection}
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

                    using (var cmd = new SqlCommand(selectSql, connection))
                    {
                        AgregarCopiaParametros(cmd, parameters); // usar copias
                        cmd.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
                        cmd.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

                        Registros.Clear();

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                Registros.Add(new BitacoraRegistro
                                {
                                    IdEvento = reader.GetInt32(0),
                                    Usuario = reader.GetString(1),
                                    FechaHora = reader.GetDateTime(2),
                                    Modulo = reader.GetString(3),
                                    Accion = reader.GetString(4),
                                    Detalles = reader.GetString(5)
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Bitácora: error al cargar registros. pagina={PaginaActual}, sort={SortColumn} {SortDirection}, filtros: busqueda='{BusquedaFilter}', modulo='{ModuloFilter}', accion='{AccionFilter}', fechainicio='{FechaInicioFilter}', fechafin='{FechaFinFilter}'",
                    PaginaActual, SortColumn, SortDirection, BusquedaFilter, ModuloFilter, AccionFilter, FechaInicioFilter, FechaFinFilter);

                TempData["Error"] = "Ocurrió un error al cargar la bitácora.";
            }
        }
    }

    public class BitacoraRegistro
    {
        public int IdEvento { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaHora { get; set; }
        public string Modulo { get; set; }
        public string Accion { get; set; }
        public string Detalles { get; set; }
    }

    public class Modulo
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
    }

    public class Accion
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
    }
}
