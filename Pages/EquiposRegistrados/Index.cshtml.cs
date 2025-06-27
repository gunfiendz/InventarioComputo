using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace InventarioComputo.Pages.EquiposRegistrados
{
    public class EquiposRegistradosModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

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

        public EquiposRegistradosModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
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
                // Manejar error
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
                    m.id_modelo,
                    m.Modelo,
                    ma.Marca,
                    te.TipoEquipo,
                    STRING_AGG(c.Caracteristica + ': ' + ce.Valor, ', ') AS Caracteristicas,
                    COUNT(*) OVER() AS TotalRegistros
                FROM Modelos m
                JOIN Marcas ma ON m.id_marca = ma.id_marca
                JOIN TiposEquipos te ON m.id_tipoequipo = te.id_tipoequipo
                LEFT JOIN CaracteristicasEquipos ce ON m.id_modelo = ce.id_activofijo
                LEFT JOIN Caracteristicas c ON ce.id_caracteristica = c.id_caracteristica
                WHERE (@Tipo IS NULL OR m.id_tipoequipo = @Tipo)
                AND (@Marca IS NULL OR m.id_marca = @Marca)
                AND (@Busqueda = '' OR m.Modelo LIKE '%' + @Busqueda + '%' OR ma.Marca LIKE '%' + @Busqueda + '%')
                GROUP BY m.id_modelo, m.Modelo, ma.Marca, te.TipoEquipo
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
                        Nombre = reader.GetString(1),
                        Marca = reader.GetString(2),
                        Tipo = reader.GetString(3)
                    };

                    if (!reader.IsDBNull(4))
                    {
                        var caracteristicas = reader.GetString(4).Split(", ");
                        modelo.Caracteristicas.AddRange(caracteristicas);
                    }

                    if (!reader.IsDBNull(5))
                    {
                        TotalPaginas = (int)Math.Ceiling((double)reader.GetInt32(5) / RegistrosPorPagina);
                    }

                    Modelos.Add(modelo);
                }
            }
        }

        public class ModeloViewModel
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
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