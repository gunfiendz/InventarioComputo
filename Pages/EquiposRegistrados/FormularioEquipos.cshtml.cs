using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using System.Linq;

namespace InventarioComputo.Pages.EquiposRegistrados
{
    public class FormularioEquiposModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        [BindProperty]
        public int Id { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "El nombre del modelo es obligatorio")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres")]
        public string Nombre { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Debe seleccionar una marca")]
        public int? IdMarca { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Debe seleccionar un tipo de equipo")]
        public int? IdTipoEquipo { get; set; }

        [BindProperty]
        public List<CaracteristicaModel> Caracteristicas { get; set; } = new List<CaracteristicaModel>();

        [BindProperty]
        public List<int> SoftwareSeleccionados { get; set; } = new List<int>();

        [BindProperty]
        public Dictionary<int, string> Licencias { get; set; } = new Dictionary<int, string>();

        public List<SelectListItem> Marcas { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> TiposEquipos { get; set; } = new List<SelectListItem>();
        public List<SoftwareInfo> SoftwaresDisponibles { get; set; } = new List<SoftwareInfo>();
        public string Modo { get; set; } = "Crear"; // Nuevo: Para manejar modos

        public FormularioEquiposModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        // MODIFICACIÓN CLAVE: Añadir parámetro 'handler'
        public async Task OnGetAsync(string handler, int? id)
        {
            // Determinar el modo basado en el handler
            Modo = handler switch
            {
                "Editar" => "Editar",
                "Ver" => "Ver",
                _ => "Crear"
            };

            await CargarDatos();

            if (id.HasValue)
            {
                await CargarModelo(id.Value);
            }
            else
            {
                Caracteristicas.Add(new CaracteristicaModel());
            }
        }

        private async Task CargarDatos()
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                // Cargar marcas
                var cmdMarcas = new SqlCommand("SELECT id_marca, Marca FROM Marcas", connection);
                using (var reader = await cmdMarcas.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        Marcas.Add(new SelectListItem
                        {
                            Value = reader.GetInt32(0).ToString(),
                            Text = reader.GetString(1)
                        });
                    }
                }

                // Cargar tipos de equipo
                var cmdTipos = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos", connection);
                using (var reader = await cmdTipos.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        TiposEquipos.Add(new SelectListItem
                        {
                            Value = reader.GetInt32(0).ToString(),
                            Text = reader.GetString(1)
                        });
                    }
                }

                // Cargar softwares disponibles
                var cmdSoftwares = new SqlCommand(
                    "SELECT id_software, Nombre, Version FROM Softwares", connection);
                using (var reader = await cmdSoftwares.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        SoftwaresDisponibles.Add(new SoftwareInfo
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            Version = reader.GetString(2)
                        });
                    }
                }
            }
        }

        private async Task CargarModelo(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                // Cargar datos básicos del modelo
                var cmdModelo = new SqlCommand(
                    "SELECT id_modelo, Modelo, id_marca, id_tipoequipo " +
                    "FROM Modelos WHERE id_modelo = @Id", connection);
                cmdModelo.Parameters.AddWithValue("@Id", id);

                using (var reader = await cmdModelo.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        Id = reader.GetInt32(0);
                        Nombre = reader.GetString(1);
                        IdMarca = reader.GetInt32(2);
                        IdTipoEquipo = reader.GetInt32(3);
                    }
                }

                // Cargar características del modelo
                var cmdCaracteristicas = new SqlCommand(
                    "SELECT c.Caracteristica, ce.Valor " +
                    "FROM CaracteristicasEquipos ce " +
                    "JOIN Caracteristicas c ON ce.id_caracteristica = c.id_caracteristica " +
                    "WHERE ce.id_modelo = @Id", connection);
                cmdCaracteristicas.Parameters.AddWithValue("@Id", id);

                Caracteristicas.Clear();
                using (var reader = await cmdCaracteristicas.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        Caracteristicas.Add(new CaracteristicaModel
                        {
                            Nombre = reader.GetString(0),
                            Valor = reader.GetString(1)
                        });
                    }
                }

                // Cargar softwares asociados
                var cmdSoftwares = new SqlCommand(
                    "SELECT se.id_software, se.ClaveLicencia " +
                    "FROM SoftwaresEquipos se " +
                    "WHERE se.id_modelo = @Id", connection);
                cmdSoftwares.Parameters.AddWithValue("@Id", id);

                SoftwareSeleccionados.Clear();
                Licencias.Clear();
                using (var reader = await cmdSoftwares.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        int softwareId = reader.GetInt32(0);
                        SoftwareSeleccionados.Add(softwareId);
                        Licencias[softwareId] = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await CargarDatos();
                return Page();
            }

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                SqlTransaction transaction = connection.BeginTransaction();

                try
                {
                    // Guardar modelo
                    if (Id == 0) // Nuevo modelo
                    {
                        var cmdInsert = new SqlCommand(
                            "INSERT INTO Modelos (Modelo, id_marca, id_tipoequipo) " +
                            "OUTPUT INSERTED.id_modelo " +
                            "VALUES (@Nombre, @IdMarca, @IdTipoEquipo)",
                            connection, transaction);

                        cmdInsert.Parameters.AddWithValue("@Nombre", Nombre);
                        cmdInsert.Parameters.AddWithValue("@IdMarca", IdMarca);
                        cmdInsert.Parameters.AddWithValue("@IdTipoEquipo", IdTipoEquipo);

                        Id = (int)await cmdInsert.ExecuteScalarAsync();
                    }
                    else // Editar modelo existente
                    {
                        var cmdUpdate = new SqlCommand(
                            "UPDATE Modelos SET " +
                            "Modelo = @Nombre, " +
                            "id_marca = @IdMarca, " +
                            "id_tipoequipo = @IdTipoEquipo " +
                            "WHERE id_modelo = @Id",
                            connection, transaction);

                        cmdUpdate.Parameters.AddWithValue("@Id", Id);
                        cmdUpdate.Parameters.AddWithValue("@Nombre", Nombre);
                        cmdUpdate.Parameters.AddWithValue("@IdMarca", IdMarca);
                        cmdUpdate.Parameters.AddWithValue("@IdTipoEquipo", IdTipoEquipo);

                        await cmdUpdate.ExecuteNonQueryAsync();

                        // Eliminar características y softwares anteriores
                        await EliminarCaracteristicas(connection, transaction);
                        await EliminarSoftwares(connection, transaction);
                    }

                    // Guardar características
                    await GuardarCaracteristicas(connection, transaction);

                    // Guardar softwares
                    await GuardarSoftwares(connection, transaction);

                    transaction.Commit();
                    return RedirectToPage("Index");
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    ModelState.AddModelError("", "Error al guardar: " + ex.Message);
                    await CargarDatos();
                    return Page();
                }
            }
        }

        private async Task EliminarCaracteristicas(SqlConnection connection, SqlTransaction transaction)
        {
            var cmdDelete = new SqlCommand(
                "DELETE FROM CaracteristicasEquipos WHERE id_modelo = @Id",
                connection, transaction);
            cmdDelete.Parameters.AddWithValue("@Id", Id);
            await cmdDelete.ExecuteNonQueryAsync();
        }

        private async Task EliminarSoftwares(SqlConnection connection, SqlTransaction transaction)
        {
            var cmdDelete = new SqlCommand(
                "DELETE FROM SoftwaresEquipos WHERE id_modelo = @Id",
                connection, transaction);
            cmdDelete.Parameters.AddWithValue("@Id", Id);
            await cmdDelete.ExecuteNonQueryAsync();
        }

        private async Task GuardarCaracteristicas(SqlConnection connection, SqlTransaction transaction)
        {
            foreach (var car in Caracteristicas)
            {
                if (!string.IsNullOrWhiteSpace(car.Nombre) && !string.IsNullOrWhiteSpace(car.Valor))
                {
                    // Buscar o crear característica
                    var cmdCaracteristica = new SqlCommand(
                        "MERGE INTO Caracteristicas WITH (HOLDLOCK) AS target " +
                        "USING (VALUES (@Nombre)) AS source (Caracteristica) " +
                        "ON target.Caracteristica = source.Caracteristica " +
                        "WHEN NOT MATCHED THEN " +
                        "INSERT (Caracteristica) VALUES (source.Caracteristica) " +
                        "OUTPUT inserted.id_caracteristica;",
                        connection, transaction);

                    cmdCaracteristica.Parameters.AddWithValue("@Nombre", car.Nombre);
                    int caracteristicaId = (int)await cmdCaracteristica.ExecuteScalarAsync();

                    // Guardar valor
                    var cmdValor = new SqlCommand(
                        "INSERT INTO CaracteristicasEquipos (id_modelo, id_caracteristica, Valor) " +
                        "VALUES (@IdModelo, @IdCaracteristica, @Valor)",
                        connection, transaction);

                    cmdValor.Parameters.AddWithValue("@IdModelo", Id);
                    cmdValor.Parameters.AddWithValue("@IdCaracteristica", caracteristicaId);
                    cmdValor.Parameters.AddWithValue("@Valor", car.Valor);
                    await cmdValor.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task GuardarSoftwares(SqlConnection connection, SqlTransaction transaction)
        {
            foreach (int softwareId in SoftwareSeleccionados)
            {
                var cmdInsert = new SqlCommand(
                    "INSERT INTO SoftwaresEquipos (id_modelo, id_software, ClaveLicencia) " +
                    "VALUES (@IdModelo, @IdSoftware, @Licencia)",
                    connection, transaction);

                cmdInsert.Parameters.AddWithValue("@IdModelo", Id);
                cmdInsert.Parameters.AddWithValue("@IdSoftware", softwareId);
                cmdInsert.Parameters.AddWithValue("@Licencia",
                    Licencias.ContainsKey(softwareId) && !string.IsNullOrWhiteSpace(Licencias[softwareId])
                    ? Licencias[softwareId]
                    : (object)DBNull.Value);

                await cmdInsert.ExecuteNonQueryAsync();
            }
        }

        public class CaracteristicaModel
        {
            public string Nombre { get; set; }
            public string Valor { get; set; }
        }

        public class SoftwareInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public string Version { get; set; }
        }
    }
}