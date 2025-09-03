using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
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

        public List<BitacoraRegistro> Registros { get; set; } = new List<BitacoraRegistro>();
        public List<Modulo> Modulos { get; set; } = new List<Modulo>();
        public List<Accion> Acciones { get; set; } = new List<Accion>();

        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;

        public string SortColumn { get; set; } = "FechaHora";
        public string SortDirection { get; set; } = "DESC";
        public string RolUsuario { get; set; }

        // Propiedades para los filtros
        public string BusquedaFilter { get; set; }
        public string ModuloFilter { get; set; }
        public string AccionFilter { get; set; }
        public string FechaInicioFilter { get; set; }
        public string FechaFinFilter { get; set; }

        public BitacoraModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
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
            {
                SortColumn = "FechaHora";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

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
                    // Cargar módulos
                    var cmdModulos = new SqlCommand("SELECT id_modulo, Modulo FROM Modulos ORDER BY Modulo", connection);
                    using (var reader = await cmdModulos.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Modulos.Add(new Modulo
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }

                    // Cargar acciones
                    var cmdAcciones = new SqlCommand("SELECT id_accion, Accion FROM Acciones ORDER BY Accion", connection);
                    using (var reader = await cmdAcciones.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Acciones.Add(new Accion
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                ModelState.AddModelError("", $"Error al cargar filtros: {ex.Message}");
            }
        }

        private async Task CargarRegistros(string sortColumn)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var queryBuilder = new System.Text.StringBuilder();
                    queryBuilder.Append(@"
                        SELECT
                            b.id_evento,
                            u.Username,
                            b.FechaHora,
                            m.Modulo,
                            a.Accion,
                            b.Detalles,
                            COUNT(*) OVER() AS TotalRegistros
                        FROM Bitacora b
                        JOIN Usuarios u ON b.id_usuario = u.id_usuario
                        JOIN Modulos m ON b.id_modulo = m.id_modulo
                        JOIN Acciones a ON b.id_accion = a.id_accion
                        WHERE 1=1 ");

                    // Agregar cláusulas WHERE para los filtros
                    if (!string.IsNullOrEmpty(BusquedaFilter))
                    {
                        queryBuilder.Append("AND (b.Detalles LIKE '%' + @Busqueda + '%' OR u.Username LIKE '%' + @Busqueda + '%') ");
                    }
                    if (!string.IsNullOrEmpty(ModuloFilter))
                    {
                        queryBuilder.Append("AND b.id_modulo = @Modulo ");
                    }
                    if (!string.IsNullOrEmpty(AccionFilter))
                    {
                        queryBuilder.Append("AND b.id_accion = @Accion ");
                    }
                    if (!string.IsNullOrEmpty(FechaInicioFilter))
                    {
                        queryBuilder.Append("AND b.FechaHora >= @FechaInicio ");
                    }
                    if (!string.IsNullOrEmpty(FechaFinFilter))
                    {
                        queryBuilder.Append("AND b.FechaHora <= @FechaFin ");
                    }

                    queryBuilder.Append($@"
                        ORDER BY {sortColumn} {SortDirection}
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");

                    var command = new SqlCommand(queryBuilder.ToString(), connection);
                    command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
                    command.Parameters.AddWithValue("@Modulo", string.IsNullOrEmpty(ModuloFilter) ? DBNull.Value : (object)int.Parse(ModuloFilter));
                    command.Parameters.AddWithValue("@Accion", string.IsNullOrEmpty(AccionFilter) ? DBNull.Value : (object)int.Parse(AccionFilter));
                    command.Parameters.AddWithValue("@FechaInicio", string.IsNullOrEmpty(FechaInicioFilter) ? DBNull.Value : (object)DateTime.Parse(FechaInicioFilter));
                    command.Parameters.AddWithValue("@FechaFin", string.IsNullOrEmpty(FechaFinFilter) ? DBNull.Value : (object)DateTime.Parse(FechaFinFilter));
                    command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
                    command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

                    using (var reader = await command.ExecuteReaderAsync())
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
            catch (System.Exception ex)
            {
                // Manejar el error
            }
        }
    }

    public class BitacoraRegistro
    {
        public int IdEvento { get; set; }
        public string Usuario { get; set; }
        public System.DateTime FechaHora { get; set; }
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