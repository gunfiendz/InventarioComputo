using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages.Mantenimientos
{
    public class MantenimientosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<MantenimientosModel> _logger;

        public List<MantenimientoViewModel> Mantenimientos { get; set; } = new List<MantenimientoViewModel>();
        public List<TipoMantenimiento> TiposMantenimiento { get; set; } = new List<TipoMantenimiento>();
        public List<EquipoViewModel> Equipos { get; set; } = new List<EquipoViewModel>();
        public List<EmpleadoViewModel> Empleados { get; set; } = new List<EmpleadoViewModel>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string NombreUsuario { get; set; }
        public string RolUsuario { get; set; }
        public string SortColumn { get; set; } = "Fecha";
        public string SortDirection { get; set; } = "DESC"; // Por defecto ordenar por fecha reciente
        public string TipoFilter { get; set; }
        public string EquipoFilter { get; set; }
        public string TecnicoFilter { get; set; }
        public string FechaInicioFilter { get; set; }
        public string FechaFinFilter { get; set; }
        public string BusquedaFilter { get; set; }

        public MantenimientosModel(ConexionBDD dbConnection, ILogger<MantenimientosModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync(
            int pagina = 1,
            string sortColumn = "Fecha",
            string sortDirection = "DESC",
            string tipo = null,
            string equipo = null,
            string tecnico = null,
            string fechainicio = null,
            string fechafin = null,
            string busqueda = null)
        {
            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            TipoFilter = tipo;
            EquipoFilter = equipo;
            TecnicoFilter = tecnico;
            FechaInicioFilter = fechainicio;
            FechaFinFilter = fechafin;
            BusquedaFilter = busqueda;

            // Validar columnas de ordenamiento
            var columnasValidas = new Dictionary<string, string>
            {
                {"Fecha", "me.Fecha"},
                {"Tipo", "m.Tipo"},
                {"Equipo", "af.EtiquetaInv"},
                {"Descripcion", "me.Descripcion"},
                {"Tecnico", "e.Nombre"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "Fecha";
            }

            SortDirection = SortDirection.ToUpper() == "ASC" ? "ASC" : "DESC";

            // Obtener usuario
            NombreUsuario = User.Identity.Name;
            RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await CargarDatosFiltros(connection);
                    await CargarMantenimientos(connection, columnasValidas[SortColumn]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar Mantenimientos.Index");
            }
        }

        private async Task CargarDatosFiltros(SqlConnection connection)
        {
            // Tipos de mantenimiento
            var cmdTipos = new SqlCommand("SELECT id_mantenimiento, Tipo FROM Mantenimientos", connection);
            using (var reader = await cmdTipos.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    TiposMantenimiento.Add(new TipoMantenimiento
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }

            // Equipos (Activos fijos)
            var cmdEquipos = new SqlCommand("SELECT id_activofijo, EtiquetaInv, NumeroSerie FROM ActivosFijos", connection);
            using (var reader = await cmdEquipos.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Equipos.Add(new EquipoViewModel
                    {
                        Id = reader.GetInt32(0),
                        EtiquetaInv = reader.GetString(1),
                        NumeroSerie = reader.GetString(2)
                    });
                }
            }

            // Empleados (técnicos)
            var cmdEmpleados = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados", connection);
            using (var reader = await cmdEmpleados.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Empleados.Add(new EmpleadoViewModel
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }

        private async Task CargarMantenimientos(SqlConnection connection, string sortColumn)
        {

            // sortColumn llega con algo tipo "me.Fecha", "m.Tipo", etc.
            var orderBy = $"{sortColumn} {SortDirection}";
            if (string.Equals(sortColumn, "me.Fecha", StringComparison.OrdinalIgnoreCase))
            {
                // Si empatan por fecha, el de mayor id_mantenimientoequipo primero
                orderBy += ", me.id_mantenimientoequipo DESC";
            }

            var query = $@"
                SELECT 
                    me.id_mantenimientoequipo, 
                    me.Fecha, 
                    m.Tipo, 
                    af.EtiquetaInv + ' - ' + af.NumeroSerie AS Equipo,
                    me.Descripcion,
                    e.Nombre AS Tecnico,
                    COUNT(*) OVER() AS TotalRegistros
                FROM MantenimientosEquipos me
                JOIN Mantenimientos m ON me.id_mantenimiento = m.id_mantenimiento
                JOIN ActivosFijos af ON me.id_activofijo = af.id_activofijo
                JOIN Empleados e ON me.id_empleado = e.id_empleado
                WHERE (@Tipo IS NULL OR me.id_mantenimiento = @Tipo)
                  AND (@Equipo IS NULL OR me.id_activofijo = @Equipo)
                  AND (@Tecnico IS NULL OR me.id_empleado = @Tecnico)
                  AND (@FechaInicio IS NULL OR me.Fecha >= @FechaInicio)
                  AND (@FechaFin IS NULL OR me.Fecha <= @FechaFin)
                  AND (@Busqueda = '' OR me.Descripcion LIKE '%' + @Busqueda + '%')
                ORDER BY {orderBy}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Tipo",
                string.IsNullOrEmpty(TipoFilter) ? DBNull.Value : (object)int.Parse(TipoFilter));
            command.Parameters.AddWithValue("@Equipo",
                string.IsNullOrEmpty(EquipoFilter) ? DBNull.Value : (object)int.Parse(EquipoFilter));
            command.Parameters.AddWithValue("@Tecnico",
                string.IsNullOrEmpty(TecnicoFilter) ? DBNull.Value : (object)int.Parse(TecnicoFilter));
            command.Parameters.AddWithValue("@FechaInicio",
                string.IsNullOrEmpty(FechaInicioFilter) ? DBNull.Value : (object)DateTime.Parse(FechaInicioFilter));
            command.Parameters.AddWithValue("@FechaFin",
                string.IsNullOrEmpty(FechaFinFilter) ? DBNull.Value : (object)DateTime.Parse(FechaFinFilter));
            command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
            command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
            command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var mantenimiento = new MantenimientoViewModel
                    {
                        Id = reader.GetInt32(0),
                        Fecha = reader.GetDateTime(1),
                        Tipo = reader.GetString(2),
                        Equipo = reader.GetString(3),
                        Descripcion = reader.GetString(4),
                        Tecnico = reader.GetString(5)
                    };

                    if (!reader.IsDBNull(6))
                    {
                        TotalPaginas = (int)Math.Ceiling((double)reader.GetInt32(6) / RegistrosPorPagina);
                    }

                    Mantenimientos.Add(mantenimiento);
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            string etiqueta = "";
            string serie = "";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // Obtener EtiquetaInv y NumeroSerie del AF asociado al mantenimiento
                    const string qInfo = @"
                        SELECT af.EtiquetaInv, af.NumeroSerie
                        FROM MantenimientosEquipos me
                        JOIN ActivosFijos af ON me.id_activofijo = af.id_activofijo
                        WHERE me.id_mantenimientoequipo = @Id";
                    using (var cmdInfo = new SqlCommand(qInfo, connection))
                    {
                        cmdInfo.Parameters.AddWithValue("@Id", id);
                        using var rd = await cmdInfo.ExecuteReaderAsync();
                        if (await rd.ReadAsync())
                        {
                            etiqueta = rd.GetString(0);
                            serie = rd.GetString(1);
                        }
                    }

                    // Eliminar el mantenimiento
                    string deleteQuery = "DELETE FROM MantenimientosEquipos WHERE id_mantenimientoequipo = @Id";
                    using (var cmd = new SqlCommand(deleteQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }

                // Bitácora (Eliminar) con EtiquetaInv y S/N
                try
                {
                    var detalles = $"Se eliminó el mantenimiento (ID: {id}) del equipo '{etiqueta}' (S/N: {serie}).";
                    await BitacoraHelper.RegistrarAccionAsync(
                        _dbConnection,
                        _logger,
                        User,
                        BitacoraConstantes.Modulos.Mantenimientos,
                        BitacoraConstantes.Acciones.Eliminacion,
                        detalles
                    );
                }
                catch (Exception exBit)
                {
                    _logger.LogError(exBit, "Error al registrar Bitácora de eliminación de mantenimiento Id={Id}", id);
                }

                TempData["Mensaje"] = "¡El mantenimiento ha sido eliminado correctamente!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al eliminar el mantenimiento: {ex.Message}";
                _logger.LogError(ex, "Error al eliminar el mantenimiento Id={Id}", id);
            }
            return RedirectToPage();
        }

        public class MantenimientoViewModel
        {
            public int Id { get; set; }
            public DateTime Fecha { get; set; }
            public string Tipo { get; set; }
            public string Equipo { get; set; }
            public string Descripcion { get; set; }
            public string Tecnico { get; set; }
        }

        public class TipoMantenimiento
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class EquipoViewModel
        {
            public int Id { get; set; }
            public string EtiquetaInv { get; set; }
            public string NumeroSerie { get; set; }
        }

        public class EmpleadoViewModel
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}
