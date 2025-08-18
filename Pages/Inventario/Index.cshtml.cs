using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace InventarioComputo.Pages.Inventario
{
    public class InventarioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

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

        // Propiedades para ordenamiento
        public string SortColumn { get; set; } = "EtiquetaInv";
        public string SortDirection { get; set; } = "ASC";

        // Propiedades para filtros
        public string TipoEquipoFilter { get; set; }
        public string MarcaFilter { get; set; }
        public string DepartamentoEmpresaFilter { get; set; }
        public string AreaFilter { get; set; }
        public string EstadoFilter { get; set; }
        public string EmpleadoFilter { get; set; }
        public string BusquedaFilter { get; set; }

        public InventarioModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync()
        {
            // Obtener parámetros de la URL
            PaginaActual = Convert.ToInt32(Request.Query["pagina"].FirstOrDefault() ?? "1");
            TipoEquipoFilter = Request.Query["tipoequipo"].FirstOrDefault() ?? "";
            MarcaFilter = Request.Query["marca"].FirstOrDefault() ?? "";
            DepartamentoEmpresaFilter = Request.Query["departamentoempresa"].FirstOrDefault() ?? "";
            EstadoFilter = Request.Query["estado"].FirstOrDefault() ?? "";
            EmpleadoFilter = Request.Query["empleado"].FirstOrDefault() ?? "";
            BusquedaFilter = Request.Query["busqueda"].FirstOrDefault() ?? "";
            SortColumn = Request.Query["sortColumn"].FirstOrDefault() ?? "EtiquetaInv";
            SortDirection = Request.Query["sortDirection"].FirstOrDefault() ?? "ASC";

            // Obtener nombre y rol del usuario
            NombreUsuario = User.Identity.Name;
            RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

            // Validar y mapear columnas para evitar SQL injection
            var columnasValidas = new Dictionary<string, string>
            {
                {"EtiquetaInv", "af.EtiquetaInv"},
                {"NumeroSerie", "af.NumeroSerie"},
                {"TipoEquipo", "te.TipoEquipo"},
                {"NombrePerfil", "p.NombrePerfil"},
                {"Marca", "m.Marca"},
                {"DepartamentoEmpresa", "ISNULL(de.NombreDepartamento, 'No asignado')"},
                {"Estado", "e.Estado"},
                {"AsignadoA", "ISNULL(emp.Nombre, 'No asignado')"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "EtiquetaInv";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // Obtener datos para los dropdowns
                    var cmdDepartamentosEmpresa = new SqlCommand("SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa ORDER BY NombreDepartamento", connection);
                    using (var reader = await cmdDepartamentosEmpresa.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            DepartamentosEmpresa.Add(new DepartamentoEmpresa
                            {
                                id_DE = reader.GetInt32(0),
                                NombreDepartamento = reader.GetString(1)
                            });
                        }
                    }

                    var cmdEstados = new SqlCommand("SELECT id_estado, Estado FROM Estados ORDER BY Estado", connection);
                    using (var reader = await cmdEstados.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Estados.Add(new Estado
                            {
                                id_estado = reader.GetInt32(0),
                                EstadoActual = reader.GetString(1)
                            });
                        }
                    }

                    var cmdTiposEquipos = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo", connection);
                    using (var reader = await cmdTiposEquipos.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            TiposEquipos.Add(new TipoEquipo
                            {
                                id_tipoequipo = reader.GetInt32(0),
                                NombreTipoEquipo = reader.GetString(1)
                            });
                        }
                    }

                    var cmdMarcas = new SqlCommand("SELECT id_marca, Marca FROM Marcas ORDER BY Marca", connection);
                    using (var reader = await cmdMarcas.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Marcas.Add(new Marca
                            {
                                id_marca = reader.GetInt32(0),
                                NombreMarca = reader.GetString(1)
                            });
                        }
                    }

                    var cmdEmpleados = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre", connection);
                    using (var reader = await cmdEmpleados.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            Empleados.Add(new Empleado
                            {
                                id_empleado = reader.GetInt32(0),
                                NombreEmpleado = reader.GetString(1)
                            });
                        }
                    }

                    // Consulta para contar el total de registros (paginación)
                    var countQuery = @"
                SELECT COUNT(*)
                FROM ActivosFijos af
                JOIN Perfiles p ON af.id_perfil = p.id_perfil
                JOIN Modelos mo ON p.id_modelo = mo.id_modelo
                JOIN Marcas m ON mo.id_marca = m.id_marca
                JOIN TiposEquipos te ON mo.id_tipoequipo = te.id_tipoequipo
                JOIN Estados e ON af.id_estado = e.id_estado
                LEFT JOIN EmpleadosEquipos ee ON af.id_activofijo = ee.id_activofijo AND ee.ResponsableActual = 1
                LEFT JOIN Empleados emp ON ee.id_empleado = emp.id_empleado
                LEFT JOIN DepartamentosEmpresa de ON ee.id_DE = de.id_DE
                WHERE (@TipoEquipo = 0 OR mo.id_tipoequipo = @TipoEquipo)
                AND (@Marca = 0 OR mo.id_marca = @Marca)
                AND (@DepartamentoEmpresa = 0 OR de.id_DE = @DepartamentoEmpresa OR de.id_DE IS NULL)
                AND (@Estado = 0 OR af.id_estado = @Estado)
                AND (@Empleado = 0 OR ee.id_empleado = @Empleado OR ee.id_empleado IS NULL)
                AND (@Busqueda = '' OR af.EtiquetaInv LIKE '%' + @Busqueda + '%' OR af.NumeroSerie LIKE '%' + @Busqueda + '%')";

                    var countCommand = new SqlCommand(countQuery, connection);
                    countCommand.Parameters.AddWithValue("@TipoEquipo", string.IsNullOrEmpty(TipoEquipoFilter) ? 0 : int.Parse(TipoEquipoFilter));
                    countCommand.Parameters.AddWithValue("@Marca", string.IsNullOrEmpty(MarcaFilter) ? 0 : int.Parse(MarcaFilter));
                    countCommand.Parameters.AddWithValue("@DepartamentoEmpresa", string.IsNullOrEmpty(DepartamentoEmpresaFilter) ? 0 : int.Parse(DepartamentoEmpresaFilter));
                    countCommand.Parameters.AddWithValue("@Estado", string.IsNullOrEmpty(EstadoFilter) ? 0 : int.Parse(EstadoFilter));
                    countCommand.Parameters.AddWithValue("@Empleado", string.IsNullOrEmpty(EmpleadoFilter) ? 0 : int.Parse(EmpleadoFilter));
                    countCommand.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");

                    var totalRegistros = (int)await countCommand.ExecuteScalarAsync();
                    TotalPaginas = (int)Math.Ceiling((double)totalRegistros / RegistrosPorPagina);

                    // Consulta principal con parámetros
                    var query = $@"
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
                    ISNULL(emp.Nombre, 'No asignado') AS AsignadoA
                FROM ActivosFijos af
                JOIN Perfiles p on af.id_perfil = p.id_perfil
                JOIN Modelos mo ON p.id_modelo = mo.id_modelo
                JOIN Marcas m ON mo.id_marca = m.id_marca
                JOIN TiposEquipos te ON mo.id_tipoequipo = te.id_tipoequipo
                JOIN Estados e ON af.id_estado = e.id_estado
                LEFT JOIN EmpleadosEquipos ee ON af.id_activofijo = ee.id_activofijo AND ee.ResponsableActual = 1
                LEFT JOIN Empleados emp ON ee.id_empleado = emp.id_empleado
                LEFT JOIN DepartamentosEmpresa de ON ee.id_DE = de.id_DE
                WHERE (@TipoEquipo = 0 OR mo.id_tipoequipo = @TipoEquipo)
                AND (@Marca = 0 OR mo.id_marca = @Marca)
                AND (@DepartamentoEmpresa = 0 OR de.id_DE = @DepartamentoEmpresa)
                AND (@Estado = 0 OR af.id_estado = @Estado)
                AND (@Empleado = 0 OR ee.id_empleado = @Empleado)
                AND (@Busqueda = '' OR af.EtiquetaInv LIKE '%' + @Busqueda + '%' OR af.NumeroSerie LIKE '%' + @Busqueda + '%')
                ORDER BY {columnasValidas[SortColumn]} {SortDirection}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

                    var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@TipoEquipo", string.IsNullOrEmpty(TipoEquipoFilter) ? 0 : int.Parse(TipoEquipoFilter));
                    command.Parameters.AddWithValue("@Marca", string.IsNullOrEmpty(MarcaFilter) ? 0 : int.Parse(MarcaFilter));
                    command.Parameters.AddWithValue("@DepartamentoEmpresa", string.IsNullOrEmpty(DepartamentoEmpresaFilter) ? 0 : int.Parse(DepartamentoEmpresaFilter));
                    command.Parameters.AddWithValue("@Estado", string.IsNullOrEmpty(EstadoFilter) ? 0 : int.Parse(EstadoFilter));
                    command.Parameters.AddWithValue("@Empleado", string.IsNullOrEmpty(EmpleadoFilter) ? 0 : int.Parse(EmpleadoFilter));
                    command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
                    command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
                    command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

                    Equipos = new List<Equipo>();
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var equipo = new Equipo
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
                            };
                            Equipos.Add(equipo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cargar inventario: {ex.Message}");
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // La lógica de eliminación debe considerar las dependencias. 
                    // Se asume que no hay asignaciones ni mantenimientos activos para este equipo.
                    // Si existen, se debe manejar la eliminación en cascada o la validación.

                    // Primero, eliminar registros en tablas relacionadas (si existen)
                    string deleteAsignacionesQuery = "DELETE FROM EmpleadosEquipos WHERE id_activofijo = @Id";
                    using (var cmd = new SqlCommand(deleteAsignacionesQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    string deleteMantenimientosQuery = "DELETE FROM MantenimientosEquipos WHERE id_activofijo = @Id";
                    using (var cmd = new SqlCommand(deleteMantenimientosQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }

                    // Después, eliminar el activo fijo
                    string deleteQuery = "DELETE FROM ActivosFijos WHERE id_activofijo = @Id";
                    using (var cmd = new SqlCommand(deleteQuery, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                TempData["Mensaje"] = "¡El activo fijo ha sido eliminado correctamente!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Error al eliminar el activo fijo: {ex.Message}";
                Console.WriteLine($"Error al eliminar el activo fijo: {ex.Message}");
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
        }

        public class DepartamentoEmpresa
        {
            public int id_DE { get; set; }
            public string NombreDepartamento { get; set; }
        }

        public class Estado
        {
            public int id_estado { get; set; }
            public string EstadoActual { get; set; }
        }

        public class TipoEquipo
        {
            public int id_tipoequipo { get; set; }
            public string NombreTipoEquipo { get; set; }
        }

        public class Marca
        {
            public int id_marca { get; set; }
            public string NombreMarca { get; set; }
        }

        public class Empleado
        {
            public int id_empleado { get; set; }
            public string NombreEmpleado { get; set; }
        }
    }
}