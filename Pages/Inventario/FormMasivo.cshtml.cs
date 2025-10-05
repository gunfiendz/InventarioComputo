using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.Extensions.Logging; // <-- Necesario para el Logger
using InventarioComputo.Data;      // <-- Necesario para la Bitácora

namespace InventarioComputo.Pages.Inventario
{
    public class FormMasivoModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormMasivoModel> _logger; // Inyectar Logger

        [BindProperty]
        public BulkEquipoViewModel EquipoMasivo { get; set; } = new BulkEquipoViewModel();
        public List<TipoEquipo> TiposEquipos { get; set; } = new List<TipoEquipo>();

        [BindProperty]
        public List<SoftwareCheckbox> SoftwareDisponible { get; set; } = new List<SoftwareCheckbox>();

        public List<SelectListItem> TiposSoftware { get; set; } = new List<SelectListItem>();

        public FormMasivoModel(ConexionBDD dbConnection, ILogger<FormMasivoModel> logger) // Constructor actualizado
        {
            _dbConnection = dbConnection;
            _logger = logger; // Asignar Logger
        }

        public async Task OnGetAsync()
        {
            TiposEquipos = await ObtenerTiposEquipos();
            await CargarSoftwareDisponible();
            await CargarTiposSoftware();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                await using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        int estadoActivoId = await ObtenerEstadoActivo(connection, transaction);

                        for (int i = 0; i < EquipoMasivo.Cantidad; i++)
                        {
                            var query = @"INSERT INTO ActivosFijos (id_perfil, NumeroSerie, EtiquetaInv, FechaCompra, Garantia, id_estado) 
                                          OUTPUT INSERTED.id_activofijo
                                          VALUES (@IdPerfil, @NumeroSerie, @EtiquetaInv, @FechaCompra, @Garantia, @IdEstado)";

                            var cmdActivo = new SqlCommand(query, connection, transaction);
                            string placeholder = $"PENDIENTE-{Guid.NewGuid().ToString().Substring(0, 8)}";
                            cmdActivo.Parameters.AddWithValue("@IdPerfil", EquipoMasivo.IdPerfil);
                            cmdActivo.Parameters.AddWithValue("@NumeroSerie", placeholder);
                            cmdActivo.Parameters.AddWithValue("@EtiquetaInv", placeholder);
                            cmdActivo.Parameters.AddWithValue("@FechaCompra", EquipoMasivo.FechaCompra);
                            cmdActivo.Parameters.AddWithValue("@Garantia", EquipoMasivo.Garantia ?? (object)DBNull.Value);
                            cmdActivo.Parameters.AddWithValue("@IdEstado", estadoActivoId);

                            int nuevoActivoId = (int)await cmdActivo.ExecuteScalarAsync();

                            foreach (var software in SoftwareDisponible.Where(s => s.IsSelected))
                            {
                                var cmdSoftware = new SqlCommand("INSERT INTO SoftwaresEquipos (id_activofijo, id_software, FechaInstalacion, ClaveLicencia) VALUES (@IdActivoFijo, @IdSoftware, GETDATE(), @ClaveLicencia)", connection, transaction);
                                cmdSoftware.Parameters.AddWithValue("@IdActivoFijo", nuevoActivoId);
                                cmdSoftware.Parameters.AddWithValue("@IdSoftware", software.Id);
                                cmdSoftware.Parameters.AddWithValue("@ClaveLicencia", software.ClaveLicencia ?? (object)DBNull.Value);
                                await cmdSoftware.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();

                        // --- INTEGRACIÓN DE BITÁCORA ---
                        string detalles = $"Creación masiva de {EquipoMasivo.Cantidad} equipos.";
                        await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User, BitacoraConstantes.Modulos.Inventario, BitacoraConstantes.Acciones.Creacion, detalles);
                        // --- FIN DE LA INTEGRACIÓN ---

                        TempData["SuccessMessage"] = $"{EquipoMasivo.Cantidad} equipos han sido creados exitosamente con datos pendientes.";
                        return RedirectToPage("./Index");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, "Error en creación masiva de activos. Usuario: {Username}", User.Identity.Name);
                        ModelState.AddModelError("", $"Error del servidor al guardar en masa: {ex.Message}");
                        await OnGetAsync();
                        return Page();
                    }
                }
            }
        }

        public async Task<JsonResult> OnPostAddSoftwareAsync([FromBody] AddSoftwareRequest data)
        {
            if (string.IsNullOrWhiteSpace(data.Nombre) || data.IdTipoSoftware == 0)
                return new JsonResult(new { success = false, message = "Nombre y Tipo de Software son obligatorios." });

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var query = "INSERT INTO Softwares (Nombre, id_tiposoftware, Version, Proveedor) OUTPUT INSERTED.id_software VALUES (@Nombre, @IdTipoSoftware, @Version, @Proveedor)";
                    var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@Nombre", data.Nombre);
                    command.Parameters.AddWithValue("@IdTipoSoftware", data.IdTipoSoftware);
                    command.Parameters.AddWithValue("@Version", (object)data.Version ?? DBNull.Value);
                    command.Parameters.AddWithValue("@Proveedor", (object)data.Proveedor ?? DBNull.Value);

                    var newId = (int)await command.ExecuteScalarAsync();

                    // --- INTEGRACIÓN DE BITÁCORA ---
                    string detalles = $"Se agregó el nuevo software '{data.Nombre}' desde el formulario de creación masiva.";
                    await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User, BitacoraConstantes.Modulos.Softwares, BitacoraConstantes.Acciones.Creacion, detalles);
                    // --- FIN DE LA INTEGRACIÓN ---

                    return new JsonResult(new { success = true, id = newId, text = data.Nombre });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al agregar software desde modal en FormMasivo. Usuario: {Username}", User.Identity.Name);
                return new JsonResult(new { success = false, message = "Error del servidor al guardar el software." });
            }
        }

        private async Task CargarSoftwareDisponible()
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmdSoftware = new SqlCommand("SELECT id_software, Nombre FROM Softwares ORDER BY Nombre", connection);
                using (var reader = await cmdSoftware.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        SoftwareDisponible.Add(new SoftwareCheckbox { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
        }

        private async Task CargarTiposSoftware()
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmdTipos = new SqlCommand("SELECT id_tiposoftware, TipoSoftware FROM TiposSoftwares ORDER BY TipoSoftware", connection);
                using (var reader = await cmdTipos.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        TiposSoftware.Add(new SelectListItem { Value = reader.GetInt32(0).ToString(), Text = reader.GetString(1) });
                    }
                }
            }
        }

        private async Task<int> ObtenerEstadoActivo(SqlConnection connection, SqlTransaction transaction)
        {
            var query = "SELECT id_estado FROM Estados WHERE Estado = 'Activo'";
            var command = new SqlCommand(query, connection, transaction);
            var result = await command.ExecuteScalarAsync();
            return result != null ? (int)result : 1;
        }

        private async Task<List<TipoEquipo>> ObtenerTiposEquipos()
        {
            var tipos = new List<TipoEquipo>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo";
                var command = new SqlCommand(query, connection);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        tipos.Add(new TipoEquipo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
            return tipos;
        }

        private async Task<List<Marca>> ObtenerMarcasPorTipo(int tipoId)
        {
            var marcas = new List<Marca>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"SELECT DISTINCT m.id_marca, m.Marca FROM Marcas m JOIN Modelos mo ON m.id_marca = mo.id_marca WHERE mo.id_tipoequipo = @TipoId ORDER BY m.Marca";
                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@TipoId", tipoId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        marcas.Add(new Marca { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
            return marcas;
        }

        private async Task<List<ModeloInfo>> ObtenerModelosPorMarcaYTipo(int marcaId, int tipoId)
        {
            var modelos = new List<ModeloInfo>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"SELECT DISTINCT id_modelo, Modelo FROM Modelos WHERE id_marca = @MarcaId AND id_tipoequipo = @TipoId ORDER BY Modelo";
                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@MarcaId", marcaId);
                command.Parameters.AddWithValue("@TipoId", tipoId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        modelos.Add(new ModeloInfo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
            return modelos;
        }

        private async Task<List<PerfilInfo>> ObtenerPerfilesPorModelo(int modeloId)
        {
            var perfiles = new List<PerfilInfo>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_perfil, NombrePerfil FROM Perfiles WHERE id_modelo = @ModeloId AND NombrePerfil IS NOT NULL";
                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@ModeloId", modeloId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        perfiles.Add(new PerfilInfo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
            return perfiles;
        }

        public async Task<JsonResult> OnGetMarcasPorTipo(int tipoId)
        {
            var marcas = await ObtenerMarcasPorTipo(tipoId);
            return new JsonResult(marcas.Select(m => new { id = m.Id, nombre = m.Nombre }));
        }

        public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId)
        {
            var modelos = await ObtenerModelosPorMarcaYTipo(marcaId, tipoId);
            return new JsonResult(modelos.Select(m => new { id = m.Id, nombre = m.Nombre }));
        }

        public async Task<JsonResult> OnGetCaracteristicasPorPerfil(int perfilId)
        {
            var caracteristicas = new List<CaracteristicaViewModel>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"SELECT c.Caracteristica, cm.Valor FROM CaracteristicasModelos cm JOIN Caracteristicas c ON cm.id_caracteristica = c.id_caracteristica WHERE cm.id_perfil = @PerfilId";
                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@PerfilId", perfilId);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        caracteristicas.Add(new CaracteristicaViewModel { Nombre = reader.GetString(0), Valor = reader.GetString(1) });
                    }
                }
            }
            return new JsonResult(caracteristicas.Select(c => new { nombre = c.Nombre, valor = c.Valor }));
        }

        public async Task<JsonResult> OnGetPerfilesPorModelo(int modeloId)
        {
            var perfiles = await ObtenerPerfilesPorModelo(modeloId);
            return new JsonResult(perfiles.Select(p => new { id = p.Id, nombre = p.Nombre }));
        }

        public class BulkEquipoViewModel
        {
            [Required(ErrorMessage = "Debe seleccionar un tipo de equipo")]
            public int IdTipoEquipo { get; set; }
            [Required(ErrorMessage = "Debe seleccionar una marca")]
            public int IdMarca { get; set; }
            [Required(ErrorMessage = "Debe seleccionar un modelo")]
            public int IdModelo { get; set; }
            [Required(ErrorMessage = "Debe seleccionar un perfil")]
            public int IdPerfil { get; set; }
            [Required(ErrorMessage = "La fecha de compra es requerida")]
            [Display(Name = "Fecha de Compra")]
            [DataType(DataType.Date)]
            public DateTime FechaCompra { get; set; } = DateTime.Today;
            [Display(Name = "Garantía (opcional)")]
            public string? Garantia { get; set; }
            [Required(ErrorMessage = "Debe especificar la cantidad de equipos")]
            [Range(1, 100, ErrorMessage = "La cantidad debe estar entre 1 y 100")]
            public int Cantidad { get; set; }
        }

        public class AddSoftwareRequest
        {
            public string Nombre { get; set; }
            public int IdTipoSoftware { get; set; }
            public string Version { get; set; }
            public string Proveedor { get; set; }
        }

        public class SoftwareCheckbox
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public bool IsSelected { get; set; }
            public string? ClaveLicencia { get; set; }
        }

        public class TipoEquipo { public int Id { get; set; } public string Nombre { get; set; } }
        public class Marca { public int Id { get; set; } public string Nombre { get; set; } }
        public class ModeloInfo { public int Id { get; set; } public string Nombre { get; set; } }
        public class PerfilInfo { public int Id { get; set; } public string Nombre { get; set; } }
        public class CaracteristicaViewModel { public string Nombre { get; set; } public string Valor { get; set; } }
    }
}