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
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages.EquiposRegistrados
{
    public class EquiposFormModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<EquiposFormModel> _logger;

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

        public EquiposFormModel(ConexionBDD dbConnection, ILogger<EquiposFormModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync(string handler, int? id)
        {
            Modo = handler switch
            {
                "Editar" => "Editar",
                "Ver" => "Ver",
                _ => "Crear"
            };

            await CargarDatosIniciales();

            if (id.HasValue)
            {
                await CargarPerfil(id.Value);
            }
            else
            {
                // Agrega una fila vacía para la primera característica al crear
                CaracteristicasSeleccionadas.Add(new CaracteristicaSeleccionada());
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState inválido en EquiposForm. Usuario {Username}", User?.Identity?.Name ?? "anon");
                await CargarDatosIniciales();
                return Page();
            }

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                await using (var transaction = connection.BeginTransaction())
                {
                    // <- NUEVO: para saber si es creación o edición
                    bool esCreacion = (Id == 0);
                    int caracteristicasInsertadas = 0;

                    try
                    {
                        if (Id == 0) // Crear Perfil
                        {
                            var cmdInsert = new SqlCommand(
                                "INSERT INTO Perfiles (NombrePerfil, id_modelo) OUTPUT INSERTED.id_perfil VALUES (@NombrePerfil, @IdModelo)",
                                connection, transaction);
                            cmdInsert.Parameters.AddWithValue("@NombrePerfil", NombrePerfil);
                            cmdInsert.Parameters.AddWithValue("@IdModelo", IdModelo);
                            Id = (int)await cmdInsert.ExecuteScalarAsync();
                        }
                        else // Editar Perfil
                        {
                            var cmdUpdate = new SqlCommand(
                                "UPDATE Perfiles SET NombrePerfil = @NombrePerfil, id_modelo = @IdModelo WHERE id_perfil = @Id",
                                connection, transaction);
                            cmdUpdate.Parameters.AddWithValue("@Id", Id);
                            cmdUpdate.Parameters.AddWithValue("@NombrePerfil", NombrePerfil);
                            cmdUpdate.Parameters.AddWithValue("@IdModelo", IdModelo);
                            await cmdUpdate.ExecuteNonQueryAsync();

                            var cmdDeleteCaracteristicas = new SqlCommand(
                                "DELETE FROM CaracteristicasModelos WHERE id_perfil = @Id",
                                connection, transaction);
                            cmdDeleteCaracteristicas.Parameters.AddWithValue("@Id", Id);
                            await cmdDeleteCaracteristicas.ExecuteNonQueryAsync();
                        }

                        // Guardar características
                        foreach (var car in CaracteristicasSeleccionadas.Where(c => c.IdCaracteristica.HasValue && !string.IsNullOrWhiteSpace(c.Valor)))
                        {
                            var cmdCar = new SqlCommand(
                                "INSERT INTO CaracteristicasModelos (id_perfil, id_caracteristica, Valor) VALUES (@IdPerfil, @IdCaracteristica, @Valor)",
                                connection, transaction);
                            cmdCar.Parameters.AddWithValue("@IdPerfil", Id);
                            cmdCar.Parameters.AddWithValue("@IdCaracteristica", car.IdCaracteristica.Value);
                            cmdCar.Parameters.AddWithValue("@Valor", car.Valor);
                            await cmdCar.ExecuteNonQueryAsync();
                            caracteristicasInsertadas++;
                        }

                        await transaction.CommitAsync();

                        // ---- Bitácora (después de commit) ----
                        string detalles = esCreacion
                            ? $"Se creó el perfil '{NombrePerfil}' (ID: {Id}), con {caracteristicasInsertadas} característica(s)."
                            : $"Se modificó el perfil '{NombrePerfil}' (ID: {Id}), se reestablecieron {caracteristicasInsertadas} característica(s).";

                        try
                        {
                            await BitacoraHelper.RegistrarAccionAsync(
                                _dbConnection,
                                _logger,
                                User,
                                BitacoraConstantes.Modulos.PerfilesEquipos,
                                esCreacion ? BitacoraConstantes.Acciones.Creacion : BitacoraConstantes.Acciones.Modificacion,
                                detalles
                            );
                        }
                        catch (Exception exBit)
                        {
                            _logger.LogWarning(exBit, "No se pudo registrar Bitácora para perfil Id {PerfilId}", Id);
                        }

                        _logger.LogInformation("Perfil {Operacion} correctamente. Id {PerfilId}, Usuario {Username}",
                            esCreacion ? "creado" : "modificado",
                            Id, User?.Identity?.Name ?? "anon");

                        return RedirectToPage("Index");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, "Error al guardar perfil Id {PerfilId}. Usuario {Username}", Id, User?.Identity?.Name ?? "anon");
                        ModelState.AddModelError("", $"Error al guardar: {ex.Message}");
                        await CargarDatosIniciales();
                        return Page();
                    }
                }
            }
        }


        public async Task<JsonResult> OnPostAddMarcaAsync([FromBody] AddRequest data)
        {
            if (string.IsNullOrWhiteSpace(data.Nombre))
                return new JsonResult(new { success = false, message = "El nombre no puede estar vacío." });

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var cmd = new SqlCommand("INSERT INTO Marcas (Marca) OUTPUT INSERTED.id_marca VALUES (@Nombre)", connection);
                    cmd.Parameters.AddWithValue("@Nombre", data.Nombre);
                    var newId = (int)await cmd.ExecuteScalarAsync();

                    // Bitácora
                    try
                    {
                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Marcas,
                            BitacoraConstantes.Acciones.Creacion,
                            $"Se creó la marca '{data.Nombre}' (ID: {newId})."
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogWarning(exBit, "No se pudo registrar Bitácora para creación de marca Id {MarcaId}", newId);
                    }

                    _logger.LogInformation("Marca creada. Id {MarcaId}, Nombre {Nombre}, Usuario {Username}",
                        newId, data.Nombre, User?.Identity?.Name ?? "anon");

                    return new JsonResult(new { success = true, id = newId, text = data.Nombre });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear marca {Nombre}. Usuario {Username}", data.Nombre, User?.Identity?.Name ?? "anon");
                return new JsonResult(new { success = false, message = "Error al crear la marca." });
            }
        }


        public async Task<JsonResult> OnPostAddTipoAsync([FromBody] AddRequest data)
        {
            if (string.IsNullOrWhiteSpace(data.Nombre))
                return new JsonResult(new { success = false, message = "El nombre no puede estar vacío." });

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var cmd = new SqlCommand("INSERT INTO TiposEquipos (TipoEquipo) OUTPUT INSERTED.id_tipoequipo VALUES (@Nombre)", connection);
                    cmd.Parameters.AddWithValue("@Nombre", data.Nombre);
                    var newId = (int)await cmd.ExecuteScalarAsync();

                    // Bitácora
                    try
                    {
                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.TiposEquipos,
                            BitacoraConstantes.Acciones.Creacion,
                            $"Se creó el tipo de equipo '{data.Nombre}' (ID: {newId})."
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogWarning(exBit, "No se pudo registrar Bitácora para creación de tipo Id {TipoId}", newId);
                    }

                    _logger.LogInformation("Tipo de equipo creado. Id {TipoId}, Nombre {Nombre}, Usuario {Username}",
                        newId, data.Nombre, User?.Identity?.Name ?? "anon");

                    return new JsonResult(new { success = true, id = newId, text = data.Nombre });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear tipo de equipo {Nombre}. Usuario {Username}", data.Nombre, User?.Identity?.Name ?? "anon");
                return new JsonResult(new { success = false, message = "Error al crear el tipo de equipo." });
            }
        }


        public async Task<JsonResult> OnPostAgregarModelo([FromBody] NuevoModeloRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.NombreModelo) || request.MarcaId == 0 || request.TipoId == 0)
                return new JsonResult(new { success = false, message = "Todos los campos son obligatorios." });

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var cmd = new SqlCommand("INSERT INTO Modelos (Modelo, id_marca, id_tipoequipo) OUTPUT INSERTED.id_modelo VALUES (@Modelo, @MarcaId, @TipoId)", connection);
                    cmd.Parameters.AddWithValue("@Modelo", request.NombreModelo);
                    cmd.Parameters.AddWithValue("@MarcaId", request.MarcaId);
                    cmd.Parameters.AddWithValue("@TipoId", request.TipoId);
                    var nuevoId = (int)await cmd.ExecuteScalarAsync();

                    // Bitácora
                    try
                    {
                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Modelos,
                            BitacoraConstantes.Acciones.Creacion,
                            $"Se creó el modelo '{request.NombreModelo}' (ID: {nuevoId})."
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogWarning(exBit, "No se pudo registrar Bitácora para creación de modelo Id {ModeloId}", nuevoId);
                    }

                    _logger.LogInformation("Modelo creado. Id {ModeloId}, Nombre {Nombre}, MarcaId {MarcaId}, TipoId {TipoId}, Usuario {Username}",
                        nuevoId, request.NombreModelo, request.MarcaId, request.TipoId, User?.Identity?.Name ?? "anon");

                    return new JsonResult(new { success = true, id = nuevoId, nombre = request.NombreModelo });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear modelo {Nombre}. Usuario {Username}", request.NombreModelo, User?.Identity?.Name ?? "anon");
                return new JsonResult(new { success = false, error = "Error al crear el modelo." });
            }
        }


        public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId)
        {
            var modelos = new List<SelectListItem>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmd = new SqlCommand("SELECT DISTINCT id_modelo, Modelo FROM Modelos WHERE id_marca = @MarcaId AND id_tipoequipo = @TipoId", connection);
                cmd.Parameters.AddWithValue("@MarcaId", marcaId);
                cmd.Parameters.AddWithValue("@TipoId", tipoId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        modelos.Add(new SelectListItem { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) });
                    }
                }
            }
            return new JsonResult(modelos);
        }

        private async Task CargarDatosIniciales()
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmdMarcas = new SqlCommand("SELECT id_marca, Marca FROM Marcas ORDER BY Marca", connection);
                using (var reader = await cmdMarcas.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) { Marcas.Add(new SelectListItem { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) }); }
                }

                var cmdTipos = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo", connection);
                using (var reader = await cmdTipos.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) { TiposEquipos.Add(new SelectListItem { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) }); }
                }

                var cmdCaracteristicas = new SqlCommand("SELECT id_caracteristica, Caracteristica FROM Caracteristicas ORDER BY Caracteristica", connection);
                using (var reader = await cmdCaracteristicas.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) { TodasCaracteristicas.Add(new CaracteristicaInfo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); }
                }
            }
        }

        private async Task CargarPerfil(int idPerfil)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmdPerfil = new SqlCommand("SELECT p.id_perfil, p.NombrePerfil, p.id_modelo, m.id_marca, m.id_tipoequipo FROM Perfiles p JOIN Modelos m ON p.id_modelo = m.id_modelo WHERE p.id_perfil = @Id", connection);
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

                CaracteristicasSeleccionadas.Clear();
                var cmdCaracteristicas = new SqlCommand("SELECT cm.id_caracteristica, c.Caracteristica, cm.Valor FROM CaracteristicasModelos cm JOIN Caracteristicas c ON cm.id_caracteristica = c.id_caracteristica WHERE cm.id_perfil = @Id", connection);
                cmdCaracteristicas.Parameters.AddWithValue("@Id", idPerfil);
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
    }

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

    public class AddRequest
    {
        public string Nombre { get; set; }
    }
}