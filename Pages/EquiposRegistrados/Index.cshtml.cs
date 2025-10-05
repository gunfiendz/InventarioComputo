using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Data;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages.EquiposRegistrados
{
    public class EquiposRegistradosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<EquiposRegistradosModel> _logger; 

        public List<ModeloViewModel> Modelos { get; set; } = new List<ModeloViewModel>();
        public List<TipoEquipo> TiposEquipo { get; set; } = new List<TipoEquipo>();
        public List<Marca> Marcas { get; set; } = new List<Marca>();
        public int PaginaActual { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int RegistrosPorPagina { get; set; } = 15;
        public string SortColumn { get; set; } = "Modelo";
        public string SortDirection { get; set; } = "ASC";
        public string TipoFilter { get; set; }
        public string MarcaFilter { get; set; }
        public string BusquedaFilter { get; set; }

        public EquiposRegistradosModel(ConexionBDD dbConnection, ILogger<EquiposRegistradosModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync(
            int pagina = 1,
            string sortColumn = "Modelo",
            string sortDirection = "ASC",
            string tipo = null,
            string marca = null,
            string busqueda = null)
        {
            PaginaActual = pagina;
            SortColumn = sortColumn;
            SortDirection = sortDirection;
            TipoFilter = tipo;
            MarcaFilter = marca;
            BusquedaFilter = busqueda;

            // Validar columnas
            var columnasValidas = new Dictionary<string, string>
            {
                {"NombrePerfil", "p.NombrePerfil"},
                {"Modelo", "m.Modelo"},
                {"Marca", "ma.Marca"},
                {"Tipo", "te.TipoEquipo"}
            };

            if (!columnasValidas.ContainsKey(SortColumn))
            {
                SortColumn = "Modelo";
            }

            SortDirection = SortDirection.ToUpper() == "DESC" ? "DESC" : "ASC";

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await CargarDatosFiltros(connection);
                    await CargarModelos(connection, columnasValidas[SortColumn]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar los datos");
                TempData["ErrorMessage"] = "Ocurrió un error al cargar los datos";
            }
        }

        private async Task CargarDatosFiltros(SqlConnection connection)
        {
            // Tipos de equipo
            var cmdTipos = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos", connection);
            using (var reader = await cmdTipos.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    TiposEquipo.Add(new TipoEquipo
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }

            // Marcas
            var cmdMarcas = new SqlCommand("SELECT id_marca, Marca FROM Marcas", connection);
            using (var reader = await cmdMarcas.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Marcas.Add(new Marca
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }

        private async Task CargarModelos(SqlConnection connection, string sortColumn)
        {
            var query = $@"
                SELECT 
                    p.id_perfil,
                    p.NombrePerfil as 'Nombre del Perfil',
                    m.Modelo,
                    ma.Marca,
                    te.TipoEquipo,
                    STRING_AGG(c.Caracteristica + ': ' + cm.Valor, ', ') AS Caracteristicas,
                    COUNT(*) OVER() AS TotalRegistros
                FROM Perfiles p
                JOIN Modelos m ON p.id_modelo = m.id_modelo
                JOIN Marcas ma ON m.id_marca = ma.id_marca
                JOIN TiposEquipos te ON m.id_tipoequipo = te.id_tipoequipo
                LEFT JOIN CaracteristicasModelos cm ON p.id_perfil = cm.id_perfil
                LEFT JOIN Caracteristicas c ON cm.id_caracteristica = c.id_caracteristica
                WHERE (@Tipo IS NULL OR m.id_tipoequipo = @Tipo)
                AND (@Marca IS NULL OR m.id_marca = @Marca)
                AND (@Busqueda = '' OR m.Modelo LIKE '%' + @Busqueda + '%' OR ma.Marca LIKE '%' + @Busqueda + '%')
                GROUP BY p.id_perfil, p.NombrePerfil, m.Modelo, ma.Marca, te.TipoEquipo
                ORDER BY {sortColumn} {SortDirection}
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Tipo",
                string.IsNullOrEmpty(TipoFilter) ? DBNull.Value : (object)int.Parse(TipoFilter));
            command.Parameters.AddWithValue("@Marca",
                string.IsNullOrEmpty(MarcaFilter) ? DBNull.Value : (object)int.Parse(MarcaFilter));
            command.Parameters.AddWithValue("@Busqueda", BusquedaFilter ?? "");
            command.Parameters.AddWithValue("@Offset", (PaginaActual - 1) * RegistrosPorPagina);
            command.Parameters.AddWithValue("@PageSize", RegistrosPorPagina);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var modelo = new ModeloViewModel
                    {
                        Id = reader.GetInt32(0),
                        NombrePerfil = reader.GetString(1),
                        NombreModelo = reader.GetString(2),
                        Marca = reader.GetString(3),
                        Tipo = reader.GetString(4)
                    };

                    if (!reader.IsDBNull(5))
                    {
                        var caracteristicas = reader.GetString(5).Split(", ");
                        modelo.Caracteristicas.AddRange(caracteristicas);
                    }

                    if (!reader.IsDBNull(6))
                    {
                        TotalPaginas = (int)Math.Ceiling((double)reader.GetInt32(6) / RegistrosPorPagina);
                    }

                    Modelos.Add(modelo);
                }
            }
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // 1) Obtener nombre del perfil (para un detalle útil en Bitácora y logs)
                    string nombrePerfil = "(sin nombre)";
                    using (var cmdInfo = new SqlCommand(
                        "SELECT NombrePerfil FROM Perfiles WHERE id_perfil = @Id",
                        connection))
                    {
                        cmdInfo.Parameters.AddWithValue("@Id", id);
                        var res = await cmdInfo.ExecuteScalarAsync();
                        if (res != null) nombrePerfil = res.ToString();
                    }

                    // 2) Borrar dependencias del perfil (si aplica)
                    using (var cmdCaract = new SqlCommand(
                        "DELETE FROM CaracteristicasModelos WHERE id_perfil = @Id",
                        connection))
                    {
                        cmdCaract.Parameters.AddWithValue("@Id", id);
                        await cmdCaract.ExecuteNonQueryAsync();
                    }

                    // 3) Borrar el perfil
                    using (var cmdPerfil = new SqlCommand(
                        "DELETE FROM Perfiles WHERE id_perfil = @Id",
                        connection))
                    {
                        cmdPerfil.Parameters.AddWithValue("@Id", id);
                        await cmdPerfil.ExecuteNonQueryAsync();
                    }

                    var detalles = $"Se eliminó el perfil '{nombrePerfil}' (ID: {id}).";
                    try
                    {
                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.PerfilesEquipos,
                            BitacoraConstantes.Acciones.Eliminacion,
                            detalles
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogWarning(exBit, "No se pudo registrar la Bitácora al eliminar perfil Id {PerfilId}. Usuario {Username}",
                            id, User?.Identity?.Name ?? "anon");
                    }

                    _logger.LogInformation("Perfil eliminado. Id {PerfilId}, Nombre '{NombrePerfil}', Usuario {Username}",
                        id, nombrePerfil, User?.Identity?.Name ?? "anon");

                    TempData["Mensaje"] = "¡El perfil se eliminó correctamente!";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error al eliminar el perfil.";
                _logger.LogError(ex, "Error eliminando perfil Id {PerfilId}. Usuario {Username}",
                    id, User?.Identity?.Name ?? "anon");
            }

            return RedirectToPage();
        }


        public class ModeloViewModel
        {
            public int Id { get; set; }
            public string NombrePerfil { get; set; }
            public string NombreModelo { get; set; }
            public string Marca { get; set; }
            public string Tipo { get; set; }
            public List<string> Caracteristicas { get; set; } = new List<string>();
        }

        public class TipoEquipo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class Marca
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}
