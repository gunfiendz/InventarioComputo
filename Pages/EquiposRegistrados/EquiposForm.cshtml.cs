using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace InventarioComputo.Pages.EquiposRegistrados
{
    public class EquiposFormModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        [BindProperty]
        public int Id { get; set; } // id_perfil

        [BindProperty]
        [Required(ErrorMessage = "El nombre del perfil es obligatorio")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres")]
        public string NombrePerfil { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Debe seleccionar un modelo")]
        public int? IdModelo { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Debe seleccionar una marca")]
        public int? IdMarca { get; set; }

        [BindProperty]
        [Required(ErrorMessage = "Debe seleccionar un tipo de equipo")]
        public int? IdTipoEquipo { get; set; }

        [BindProperty]
        public List<CaracteristicaSeleccionada> CaracteristicasSeleccionadas { get; set; } = new List<CaracteristicaSeleccionada>();

        public List<SelectListItem> Marcas { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> TiposEquipos { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Modelos { get; set; } = new List<SelectListItem>();
        public List<CaracteristicaInfo> TodasCaracteristicas { get; set; } = new List<CaracteristicaInfo>();
        public string Modo { get; set; } = "Crear";

        public EquiposFormModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task OnGetAsync(string handler, int? id)
        {
            Modo = handler switch
            {
                "Editar" => "Editar",
                "Ver" => "Ver",
                _ => "Crear"
            };

            await CargarDatos();

            if (id.HasValue)
            {
                await CargarPerfil(id.Value);
            }
            else
            {
                CaracteristicasSeleccionadas.Add(new CaracteristicaSeleccionada());
            }
        }

        private async Task CargarDatos()
        {
            try
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

                    // Cargar todas las características disponibles
                    var cmdCaracteristicas = new SqlCommand(
                        "SELECT id_caracteristica, Caracteristica FROM Caracteristicas",
                        connection);
                    using (var reader = await cmdCaracteristicas.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            TodasCaracteristicas.Add(new CaracteristicaInfo
                            {
                                Id = reader.GetInt32(0),
                                Nombre = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cargar datos: {ex}");
                ModelState.AddModelError("", $"Error al cargar datos: {ex.Message}");
            }
        }

        private async Task CargarPerfil(int idPerfil)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    // Cargar datos básicos del perfil
                    var cmdPerfil = new SqlCommand(
                        "SELECT p.id_perfil, p.NombrePerfil, p.id_modelo, " +
                        "m.id_marca, m.id_tipoequipo " +
                        "FROM Perfiles p " +
                        "JOIN Modelos m ON p.id_modelo = m.id_modelo " +
                        "WHERE p.id_perfil = @Id", connection);
                    cmdPerfil.Parameters.AddWithValue("@Id", idPerfil);

                    using (var reader = await cmdPerfil.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            Id = reader.GetInt32(0);
                            NombrePerfil = reader.GetString(1);
                            IdModelo = reader.GetInt32(2);
                            IdMarca = reader.GetInt32(3);
                            IdTipoEquipo = reader.GetInt32(4);
                        }
                    }

                    // Cargar características del perfil
                    var cmdCaracteristicas = new SqlCommand(
                        "SELECT cm.id_caracteristica, c.Caracteristica, cm.Valor " +
                        "FROM CaracteristicasModelos cm " +
                        "JOIN Caracteristicas c ON cm.id_caracteristica = c.id_caracteristica " +
                        "WHERE cm.id_perfil = @Id", connection);
                    cmdCaracteristicas.Parameters.AddWithValue("@Id", idPerfil);

                    CaracteristicasSeleccionadas.Clear();
                    using (var reader = await cmdCaracteristicas.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            CaracteristicasSeleccionadas.Add(new CaracteristicaSeleccionada
                            {
                                IdCaracteristica = reader.GetInt32(0),
                                Valor = reader.GetString(2)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al cargar perfil: {ex}");
                ModelState.AddModelError("", $"Error al cargar perfil: {ex.Message}");
            }
        }

        public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId)
        {
            var modelos = new List<SelectListItem>();
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var cmd = new SqlCommand(
                        "SELECT DISTINCT id_modelo, Modelo FROM Modelos " +
                        "WHERE id_marca = @MarcaId AND id_tipoequipo = @TipoId",
                        connection);
                    cmd.Parameters.AddWithValue("@MarcaId", marcaId);
                    cmd.Parameters.AddWithValue("@TipoId", tipoId);

                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            modelos.Add(new SelectListItem
                            {
                                Value = reader.GetInt32(0).ToString(),
                                Text = reader.GetString(1)
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cargando modelos: {ex}");
            }
            return new JsonResult(modelos);
        }

        public async Task<JsonResult> OnPostAgregarModelo([FromBody] NuevoModeloRequest request)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var cmd = new SqlCommand(
                        "INSERT INTO Modelos (Modelo, id_marca, id_tipoequipo) " +
                        "OUTPUT INSERTED.id_modelo " +
                        "VALUES (@Modelo, @MarcaId, @TipoId)",
                        connection);

                    cmd.Parameters.AddWithValue("@Modelo", request.NombreModelo);
                    cmd.Parameters.AddWithValue("@MarcaId", request.MarcaId);
                    cmd.Parameters.AddWithValue("@TipoId", request.TipoId);

                    var nuevoId = (int)await cmd.ExecuteScalarAsync();

                    return new JsonResult(new
                    {
                        success = true,
                        id = nuevoId,
                        nombre = request.NombreModelo
                    });
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, error = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join(" | ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                Console.WriteLine($"Errores de validación: {errors}");
                await CargarDatos();
                return Page();
            }

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    if (connection.State != ConnectionState.Open)
                    {
                        Console.WriteLine("Conexión cerrada. Volviendo a abrir...");
                        await connection.OpenAsync();
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // Guardar perfil
                            if (Id == 0) // Nuevo perfil
                            {
                                var cmdInsert = new SqlCommand(
                                    "INSERT INTO Perfiles (NombrePerfil, id_modelo) " +
                                    "VALUES (@NombrePerfil, @IdModelo); " +
                                    "SELECT SCOPE_IDENTITY();",  // Cambio clave aquí
                                    connection, transaction);

                                cmdInsert.Parameters.AddWithValue("@NombrePerfil", NombrePerfil);
                                cmdInsert.Parameters.AddWithValue("@IdModelo", IdModelo);

                                var result = await cmdInsert.ExecuteScalarAsync();
                                if (result != null)
                                {
                                    Id = Convert.ToInt32(result);  // Convertir a int
                                    Console.WriteLine($"Nuevo perfil creado con ID: {Id}");
                                }
                                else
                                {
                                    throw new Exception("No se pudo obtener el ID del nuevo perfil");
                                }
                            }
                            else // Editar perfil existente
                            {
                                var cmdUpdate = new SqlCommand(
                                    "UPDATE Perfiles SET " +
                                    "NombrePerfil = @NombrePerfil, " +
                                    "id_modelo = @IdModelo " +
                                    "WHERE id_perfil = @Id",
                                    connection, transaction);

                                cmdUpdate.Parameters.AddWithValue("@Id", Id);
                                cmdUpdate.Parameters.AddWithValue("@NombrePerfil", NombrePerfil);
                                cmdUpdate.Parameters.AddWithValue("@IdModelo", IdModelo);

                                int rowsAffected = await cmdUpdate.ExecuteNonQueryAsync();
                                Console.WriteLine($"Perfil actualizado. Filas afectadas: {rowsAffected}");

                                if (rowsAffected == 0)
                                {
                                    throw new Exception("No se actualizó ningún registro");
                                }

                                // Eliminar características anteriores
                                await EliminarCaracteristicas(connection, transaction);
                            }

                            // Guardar características
                            await GuardarCaracteristicas(connection, transaction);

                            await transaction.CommitAsync();
                            Console.WriteLine("Transacción completada exitosamente");
                            return RedirectToPage("Index");
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            Console.WriteLine($"Error en transacción: {ex}\n{ex.StackTrace}");
                            ModelState.AddModelError("", $"Error al guardar: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error general: {ex}\n{ex.StackTrace}");
                ModelState.AddModelError("", $"Error general: {ex.Message}");
            }

            await CargarDatos();
            return Page();
        }

        private async Task EliminarCaracteristicas(SqlConnection connection, SqlTransaction transaction)
        {
            var cmdDelete = new SqlCommand(
                "DELETE FROM CaracteristicasModelos WHERE id_perfil = @Id",
                connection, transaction);
            cmdDelete.Parameters.AddWithValue("@Id", Id);
            int rowsDeleted = await cmdDelete.ExecuteNonQueryAsync();
            Console.WriteLine($"Características eliminadas: {rowsDeleted}");
        }

        private async Task GuardarCaracteristicas(SqlConnection connection, SqlTransaction transaction)
        {
            int count = 0;
            foreach (var car in CaracteristicasSeleccionadas)
            {
                if (car.IdCaracteristica.HasValue && !string.IsNullOrWhiteSpace(car.Valor))
                {
                    var cmdValor = new SqlCommand(
                        "INSERT INTO CaracteristicasModelos (id_perfil, id_caracteristica, Valor) " +
                        "VALUES (@IdPerfil, @IdCaracteristica, @Valor)",
                        connection, transaction);

                    cmdValor.Parameters.AddWithValue("@IdPerfil", Id);
                    cmdValor.Parameters.AddWithValue("@IdCaracteristica", car.IdCaracteristica.Value);
                    cmdValor.Parameters.AddWithValue("@Valor", car.Valor);

                    await cmdValor.ExecuteNonQueryAsync();
                    count++;
                }
            }
            Console.WriteLine($"Características guardadas: {count}");
        }

        // Clases internas
        public class CaracteristicaSeleccionada
        {
            public int? IdCaracteristica { get; set; }
            public string Valor { get; set; }
        }

        public class CaracteristicaInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class NuevoModeloRequest
        {
            public string NombreModelo { get; set; }
            public int MarcaId { get; set; }
            public int TipoId { get; set; }
        }
    }
}