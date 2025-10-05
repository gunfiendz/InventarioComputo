using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging; // <-- Necesario para el Logger
using InventarioComputo.Data;      // <-- Necesario para la Bitácora

namespace InventarioComputo.Pages.Inventario
{
    public class InventarioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<InventarioModel> _logger; // Inyectar el Logger

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

        [BindProperty(SupportsGet = true)]
        public string TipoEquipoFilter { get; set; }
        [BindProperty(SupportsGet = true)]
        public string MarcaFilter { get; set; }
        [BindProperty(SupportsGet = true)]
        public string DepartamentoEmpresaFilter { get; set; }
        [BindProperty(SupportsGet = true)]
        public string EstadoFilter { get; set; }
        [BindProperty(SupportsGet = true)]
        public string EmpleadoFilter { get; set; }
        [BindProperty(SupportsGet = true)]
        public string BusquedaFilter { get; set; }

        public InventarioModel(ConexionBDD dbConnection, ILogger<InventarioModel> logger) // Constructor actualizado
        {
            _dbConnection = dbConnection;
            _logger = logger; // Asignar Logger
        }

        public async Task OnGetAsync(int pagina = 1)
        {
            PaginaActual = pagina;
            NombreUsuario = User.Identity.Name;
            RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

            var columnasValidas = new Dictionary<string, string>
            {
                {"EtiquetaInv", "af.EtiquetaInv"},
                {"NumeroSerie", "af.NumeroSerie"},
                {"TipoEquipo", "te.TipoEquipo"},
                {"Marca", "m.Marca"},
                {"DepartamentoEmpresa", "ISNULL(de.NombreDepartamento, 'No asignado')"},
                {"Estado", "e.Estado"},
                {"AsignadoA", "ISNULL(emp.Nombre, 'No asignado')"}
            };

            if (!columnasValidas.ContainsKey(SortColumn)) { SortColumn = "EtiquetaInv"; }
            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // Lógica para cargar los dropdowns de filtros
                    var cmdDepartamentosEmpresa = new SqlCommand("SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa ORDER BY NombreDepartamento", connection);
                    using (var reader = await cmdDepartamentosEmpresa.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) { DepartamentosEmpresa.Add(new DepartamentoEmpresa { id_DE = reader.GetInt32(0), NombreDepartamento = reader.GetString(1) }); }
                    }

                    var cmdEstados = new SqlCommand("SELECT id_estado, Estado FROM Estados ORDER BY Estado", connection);
                    using (var reader = await cmdEstados.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) { Estados.Add(new Estado { id_estado = reader.GetInt32(0), EstadoActual = reader.GetString(1) }); }
                    }

                    var cmdTiposEquipos = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo", connection);
                    using (var reader = await cmdTiposEquipos.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) { TiposEquipos.Add(new TipoEquipo { id_tipoequipo = reader.GetInt32(0), NombreTipoEquipo = reader.GetString(1) }); }
                    }

                    var cmdMarcas = new SqlCommand("SELECT id_marca, Marca FROM Marcas ORDER BY Marca", connection);
                    using (var reader = await cmdMarcas.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) { Marcas.Add(new Marca { id_marca = reader.GetInt32(0), NombreMarca = reader.GetString(1) }); }
                    }

                    var cmdEmpleados = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre", connection);
                    using (var reader = await cmdEmpleados.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) { Empleados.Add(new Empleado { id_empleado = reader.GetInt32(0), NombreEmpleado = reader.GetString(1) }); }
                    }

                    var queryBuilder = new System.Text.StringBuilder(@"
                        FROM ActivosFijos af
                        JOIN Perfiles p ON af.id_perfil = p.id_perfil
                        JOIN Modelos mo ON p.id_modelo = mo.id_modelo
                        JOIN Marcas m ON mo.id_marca = m.id_marca
                        JOIN TiposEquipos te ON mo.id_tipoequipo = te.id_tipoequipo
                        JOIN Estados e ON af.id_estado = e.id_estado
                        LEFT JOIN EmpleadosEquipos ee ON af.id_activofijo = ee.id_activofijo AND ee.ResponsableActual = 1
                        LEFT JOIN Empleados emp ON ee.id_empleado = emp.id_empleado
                        LEFT JOIN DepartamentosEmpresa de ON ee.id_DE = de.id_DE
                        WHERE 1=1 ");

                    var parameters = new List<SqlParameter>();

                    if (!string.IsNullOrEmpty(TipoEquipoFilter)) { queryBuilder.Append("AND te.id_tipoequipo = @TipoEquipo "); parameters.Add(new SqlParameter("@TipoEquipo", TipoEquipoFilter)); }
                    if (!string.IsNullOrEmpty(MarcaFilter)) { queryBuilder.Append("AND m.id_marca = @Marca "); parameters.Add(new SqlParameter("@Marca", MarcaFilter)); }
                    if (!string.IsNullOrEmpty(DepartamentoEmpresaFilter)) { queryBuilder.Append("AND de.id_DE = @DepartamentoEmpresa "); parameters.Add(new SqlParameter("@DepartamentoEmpresa", DepartamentoEmpresaFilter)); }
                    if (!string.IsNullOrEmpty(EstadoFilter)) { queryBuilder.Append("AND e.id_estado = @Estado "); parameters.Add(new SqlParameter("@Estado", EstadoFilter)); }
                    if (!string.IsNullOrEmpty(EmpleadoFilter)) { queryBuilder.Append("AND emp.id_empleado = @Empleado "); parameters.Add(new SqlParameter("@Empleado", EmpleadoFilter)); }
                    if (!string.IsNullOrEmpty(BusquedaFilter)) { queryBuilder.Append("AND (af.EtiquetaInv LIKE @Busqueda OR af.NumeroSerie LIKE @Busqueda) "); parameters.Add(new SqlParameter("@Busqueda", $"%{BusquedaFilter}%")); }

                    var countCommand = new SqlCommand($"SELECT COUNT(*) {queryBuilder.ToString()}", connection);
                    countCommand.Parameters.AddRange(parameters.ToArray());
                    var totalRegistros = (int)await countCommand.ExecuteScalarAsync();
                    TotalPaginas = (int)Math.Ceiling(totalRegistros / (double)RegistrosPorPagina);

                    var mainQuery = $@"
                        SELECT af.id_activofijo, af.EtiquetaInv, af.NumeroSerie, te.TipoEquipo, p.NombrePerfil,
                               m.Marca, mo.Modelo, ISNULL(de.NombreDepartamento, 'No asignado') AS Departamento, 
                               e.Estado, ISNULL(emp.Nombre, 'No asignado') AS AsignadoA
                        {queryBuilder.ToString()}
                        ORDER BY {columnasValidas[SortColumn]} {SortDirection}
                        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    var command = new SqlCommand(mainQuery, connection);
                    command.Parameters.AddRange(parameters.ToArray());
                    command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
                    command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Equipos.Add(new Equipo
                            {
                                id_activofijo = reader.GetInt32(0),
                                EtiquetaInv = reader.GetString(1),
                                NumeroSerie = reader.GetString(2),
                                TipoEquipo = reader.GetString(3),
                                NombrePerfil = reader.GetString(4),
                                Marca = reader.GetString(5),
                                Modelo = reader.GetString(6),
                                DepartamentoEmpresa = reader.GetString(7),
                                Estado = reader.GetString(8),
                                AsignadoA = reader.GetString(9)
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

        public class Equipo { public int id_activofijo { get; set; } public string EtiquetaInv { get; set; } public string NumeroSerie { get; set; } public string TipoEquipo { get; set; } public string NombrePerfil { get; set; } public string Marca { get; set; } public string Modelo { get; set; } public string DepartamentoEmpresa { get; set; } public string Estado { get; set; } public string AsignadoA { get; set; } }
        public class DepartamentoEmpresa { public int id_DE { get; set; } public string NombreDepartamento { get; set; } }
        public class Estado { public int id_estado { get; set; } public string EstadoActual { get; set; } }
        public class TipoEquipo { public int id_tipoequipo { get; set; } public string NombreTipoEquipo { get; set; } }
        public class Marca { public int id_marca { get; set; } public string NombreMarca { get; set; } }
        public class Empleado { public int id_empleado { get; set; } public string NombreEmpleado { get; set; } }
    }
}