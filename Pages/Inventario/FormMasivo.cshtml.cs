using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.Extensions.Logging;                  
using InventarioComputo.Data;                       
using InventarioComputo.Security;                    

namespace InventarioComputo.Pages.Inventario
{
    public class FormMasivoModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormMasivoModel> _logger;

        [BindProperty] public BulkEquipoViewModel EquipoMasivo { get; set; } = new();
        public List<TipoEquipo> TiposEquipos { get; set; } = new();

        [BindProperty] public List<SoftwareCheckbox> SoftwareDisponible { get; set; } = new();
        public List<SelectListItem> TiposSoftware { get; set; } = new();

        public FormMasivoModel(ConexionBDD dbConnection, ILogger<FormMasivoModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync()
        {
            TiposEquipos = await ObtenerTiposEquipos();
            await CargarSoftwareDisponible();
            await CargarTiposSoftware();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Validaciones adicionales de backend (coinciden con Formulario individual)
            if (EquipoMasivo.VidaUtilAnios.HasValue && EquipoMasivo.VidaUtilAnios.Value < 0)
                ModelState.AddModelError(nameof(EquipoMasivo.VidaUtilAnios), "La vida útil no puede ser negativa.");

            decimal? costoDecimal = null;
            if (!string.IsNullOrWhiteSpace(EquipoMasivo.Costo))
            {
                decimal parsed;
                if (!ValidacionesBackend.EsCosto(EquipoMasivo.Costo) ||
                    !ValidacionesBackend.TryParseCosto(EquipoMasivo.Costo, out parsed))
                {
                    ModelState.AddModelError("EquipoMisivo.Costo", "Costo inválido.");
                }
                else
                {
                    if (parsed < 0) ModelState.AddModelError("EquipoMasivo.Costo", "El costo no puede ser negativo.");
                    else costoDecimal = parsed;
                }
            }

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            // Calcular fecha de fin de vida útil
            DateTime? fechaFinVida = null;
            if (EquipoMasivo.VidaUtilAnios.HasValue)
            {
                var years = Math.Max(0, EquipoMasivo.VidaUtilAnios.Value);
                fechaFinVida = EquipoMasivo.FechaCompra.AddYears(years);
            }

            using var connection = await _dbConnection.GetConnectionAsync();
            await using var transaction = connection.BeginTransaction();
            try
            {
                int estadoActivoId = await ObtenerEstadoActivo(connection, transaction);

                for (int i = 0; i < EquipoMasivo.Cantidad; i++)
                {
                    var insert = @"
INSERT INTO ActivosFijos
    (id_perfil, NumeroSerie, EtiquetaInv, FechaCompra, Garantia, FechaFinVidaUtil, Costo, id_estado)
OUTPUT INSERTED.id_activofijo
VALUES
    (@IdPerfil, @NumeroSerie, @EtiquetaInv, @FechaCompra, @Garantia, @FechaFinVidaUtil, @Costo, @IdEstado);";

                    using var cmd = new SqlCommand(insert, connection, transaction);
                    string placeholder = $"PENDIENTE-{Guid.NewGuid().ToString().Substring(0, 8)}";

                    cmd.Parameters.AddWithValue("@IdPerfil", EquipoMasivo.IdPerfil);
                    cmd.Parameters.AddWithValue("@NumeroSerie", placeholder);
                    cmd.Parameters.AddWithValue("@EtiquetaInv", placeholder);
                    cmd.Parameters.AddWithValue("@FechaCompra", EquipoMasivo.FechaCompra);
                    cmd.Parameters.AddWithValue("@Garantia", (object?)EquipoMasivo.Garantia ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@FechaFinVidaUtil", (object?)fechaFinVida ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Costo", (object?)costoDecimal ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdEstado", estadoActivoId);

                    int nuevoActivoId = (int)await cmd.ExecuteScalarAsync();

                    // Software seleccionado
                    foreach (var s in SoftwareDisponible.Where(s => s.IsSelected))
                    {
                        var insSw = new SqlCommand(
                            "INSERT INTO SoftwaresEquipos (id_activofijo, id_software, FechaInstalacion, ClaveLicencia) VALUES (@Id, @Sw, GETDATE(), @Clave)",
                            connection, transaction);
                        insSw.Parameters.AddWithValue("@Id", nuevoActivoId);
                        insSw.Parameters.AddWithValue("@Sw", s.Id);
                        insSw.Parameters.AddWithValue("@Clave", (object?)s.ClaveLicencia ?? DBNull.Value);
                        await insSw.ExecuteNonQueryAsync();
                    }
                }

                await transaction.CommitAsync();

                // Bitácora
                try
                {
                    string detalles = $"Creación masiva de {EquipoMasivo.Cantidad} equipos (Perfil {EquipoMasivo.IdPerfil}).";
                    await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                        BitacoraConstantes.Modulos.Inventario,
                        BitacoraConstantes.Acciones.Creacion, detalles);
                }
                catch (Exception exBit)
                {
                    _logger.LogError(exBit, "Error registrando en bitácora (FormMasivo).");
                }

                TempData["SuccessMessage"] = $"{EquipoMasivo.Cantidad} equipos han sido creados exitosamente con datos pendientes.";
                return RedirectToPage("./Index");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error en creación masiva de activos. Usuario: {Username}", User?.Identity?.Name ?? "anon");
                ModelState.AddModelError("", $"Error del servidor al guardar en masa.");
                await OnGetAsync();
                return Page();
            }
        }

        public async Task<JsonResult> OnPostAddSoftwareAsync([FromBody] AddSoftwareRequest data)
        {
            if (string.IsNullOrWhiteSpace(data.Nombre) || data.IdTipoSoftware == 0)
                return new JsonResult(new { success = false, message = "Nombre y Tipo de Software son obligatorios." });

            try
            {
                using var connection = await _dbConnection.GetConnectionAsync();
                var q = @"INSERT INTO Softwares (Nombre, id_tiposoftware, Version, Proveedor)
                          OUTPUT INSERTED.id_software
                          VALUES (@n, @t, @v, @p)";
                var cmd = new SqlCommand(q, connection);
                cmd.Parameters.AddWithValue("@n", data.Nombre);
                cmd.Parameters.AddWithValue("@t", data.IdTipoSoftware);
                cmd.Parameters.AddWithValue("@v", (object?)data.Version ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@p", (object?)data.Proveedor ?? DBNull.Value);

                var newId = (int)await cmd.ExecuteScalarAsync();

                // Bitácora
                string detalles = $"Se agregó el nuevo software '{data.Nombre}' desde el formulario de creación masiva.";
                await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                    BitacoraConstantes.Modulos.Softwares,
                    BitacoraConstantes.Acciones.Creacion, detalles);

                return new JsonResult(new { success = true, id = newId, text = data.Nombre });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al agregar software desde modal en FormMasivo. Usuario: {Username}", User?.Identity?.Name ?? "anon");
                return new JsonResult(new { success = false, message = "Error del servidor al guardar el software." });
            }
        }
        private async Task CargarSoftwareDisponible()
        {
            using var connection = await _dbConnection.GetConnectionAsync();
            var cmd = new SqlCommand("SELECT id_software, Nombre FROM Softwares ORDER BY Nombre", connection);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                SoftwareDisponible.Add(new SoftwareCheckbox { Id = r.GetInt32(0), Nombre = r.GetString(1) });
        }

        private async Task CargarTiposSoftware()
        {
            using var connection = await _dbConnection.GetConnectionAsync();
            var cmd = new SqlCommand("SELECT id_tiposoftware, TipoSoftware FROM TiposSoftwares ORDER BY TipoSoftware", connection);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                TiposSoftware.Add(new SelectListItem { Value = r.GetInt32(0).ToString(), Text = r.GetString(1) });
        }

        private async Task<int> ObtenerEstadoActivo(SqlConnection connection, SqlTransaction transaction)
        {
            var cmd = new SqlCommand("SELECT id_estado FROM Estados WHERE Estado = 'Activo'", connection, transaction);
            var res = await cmd.ExecuteScalarAsync();
            return res != null ? (int)res : 1;
        }

        private async Task<List<TipoEquipo>> ObtenerTiposEquipos()
        {
            var list = new List<TipoEquipo>();
            using var c = await _dbConnection.GetConnectionAsync();
            var cmd = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo", c);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new TipoEquipo { Id = r.GetInt32(0), Nombre = r.GetString(1) });
            return list;
        }

        private async Task<List<Marca>> ObtenerMarcasPorTipo(int tipoId)
        {
            var list = new List<Marca>();
            using var c = await _dbConnection.GetConnectionAsync();
            var cmd = new SqlCommand(@"SELECT DISTINCT m.id_marca, m.Marca
                                       FROM Marcas m
                                       JOIN Modelos mo ON m.id_marca = mo.id_marca
                                       WHERE mo.id_tipoequipo = @t
                                       ORDER BY m.Marca", c);
            cmd.Parameters.AddWithValue("@t", tipoId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new Marca { Id = r.GetInt32(0), Nombre = r.GetString(1) });
            return list;
        }

        private async Task<List<ModeloInfo>> ObtenerModelosPorMarcaYTipo(int marcaId, int tipoId)
        {
            var list = new List<ModeloInfo>();
            using var c = await _dbConnection.GetConnectionAsync();
            var cmd = new SqlCommand(@"SELECT DISTINCT id_modelo, Modelo
                                       FROM Modelos
                                       WHERE id_marca = @m AND id_tipoequipo = @t
                                       ORDER BY Modelo", c);
            cmd.Parameters.AddWithValue("@m", marcaId);
            cmd.Parameters.AddWithValue("@t", tipoId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new ModeloInfo { Id = r.GetInt32(0), Nombre = r.GetString(1) });
            return list;
        }

        private async Task<List<PerfilInfo>> ObtenerPerfilesPorModelo(int modeloId)
        {
            var list = new List<PerfilInfo>();
            using var c = await _dbConnection.GetConnectionAsync();
            var cmd = new SqlCommand(@"SELECT id_perfil, NombrePerfil
                                       FROM Perfiles
                                       WHERE id_modelo = @mod AND NombrePerfil IS NOT NULL", c);
            cmd.Parameters.AddWithValue("@mod", modeloId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new PerfilInfo { Id = r.GetInt32(0), Nombre = r.GetString(1) });
            return list;
        }
        public async Task<JsonResult> OnGetMarcasPorTipo(int tipoId) =>
            new JsonResult((await ObtenerMarcasPorTipo(tipoId)).Select(m => new { id = m.Id, nombre = m.Nombre }));

        public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId) =>
            new JsonResult((await ObtenerModelosPorMarcaYTipo(marcaId, tipoId)).Select(m => new { id = m.Id, nombre = m.Nombre }));

        public async Task<JsonResult> OnGetPerfilesPorModelo(int modeloId) =>
            new JsonResult((await ObtenerPerfilesPorModelo(modeloId)).Select(p => new { id = p.Id, nombre = p.Nombre }));

        public async Task<JsonResult> OnGetCaracteristicasPorPerfil(int perfilId)
        {
            var list = new List<CaracteristicaViewModel>();
            using var c = await _dbConnection.GetConnectionAsync();
            var cmd = new SqlCommand(@"SELECT c.Caracteristica, cm.Valor
                                       FROM CaracteristicasModelos cm
                                       JOIN Caracteristicas c ON cm.id_caracteristica = c.id_caracteristica
                                       WHERE cm.id_perfil = @PerfilId", c);
            cmd.Parameters.AddWithValue("@PerfilId", perfilId);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(new CaracteristicaViewModel { Nombre = r.GetString(0), Valor = r.GetString(1) });
            return new JsonResult(list.Select(ca => new { nombre = ca.Nombre, valor = ca.Valor }));
        }

        public class BulkEquipoViewModel
        {
            [Required(ErrorMessage = "Debe seleccionar un tipo de equipo")] public int IdTipoEquipo { get; set; }
            [Required(ErrorMessage = "Debe seleccionar una marca")] public int IdMarca { get; set; }
            [Required(ErrorMessage = "Debe seleccionar un modelo")] public int IdModelo { get; set; }
            [Required(ErrorMessage = "Debe seleccionar un perfil")] public int IdPerfil { get; set; }

            [Required(ErrorMessage = "La fecha de compra es requerida")]
            [Display(Name = "Fecha de Compra")]
            [DataType(DataType.Date)]
            public DateTime FechaCompra { get; set; } = DateTime.Today;

            [Display(Name = "Garantía (opcional)")]
            public string? Garantia { get; set; }

            [Required(ErrorMessage = "Debe especificar la cantidad de equipos")]
            [Range(1, 100, ErrorMessage = "La cantidad debe estar entre 1 y 100")]
            public int Cantidad { get; set; }

            // NUEVOS CAMPOS
            [Display(Name = "Vida útil (años)")]
            [Range(0, 50, ErrorMessage = "Ingrese un valor entre 0 y 50 años.")]
            public int? VidaUtilAnios { get; set; }

            [Display(Name = "Costo")]
            public string? Costo { get; set; }
        }

        public class AddSoftwareRequest { public string Nombre { get; set; } public int IdTipoSoftware { get; set; } public string? Version { get; set; } public string? Proveedor { get; set; } }
        public class SoftwareCheckbox { public int Id { get; set; } public string Nombre { get; set; } public bool IsSelected { get; set; } public string? ClaveLicencia { get; set; } }

        public class TipoEquipo { public int Id { get; set; } public string Nombre { get; set; } }
        public class Marca { public int Id { get; set; } public string Nombre { get; set; } }
        public class ModeloInfo { public int Id { get; set; } public string Nombre { get; set; } }
        public class PerfilInfo { public int Id { get; set; } public string Nombre { get; set; } }
        public class CaracteristicaViewModel { public string Nombre { get; set; } public string Valor { get; set; } }
    }
}
