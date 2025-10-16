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
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;
using InventarioComputo.Security; 

namespace InventarioComputo.Pages.Inventario
{
    public class FormularioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormularioModel> _logger;

        [BindProperty] public EquipoViewModel Equipo { get; set; } = new();
        public List<TipoEquipo> TiposEquipos { get; set; } = new();
        public List<Marca> Marcas { get; set; } = new();
        public List<ModeloInfo> Modelos { get; set; } = new();
        public List<PerfilInfo> Perfiles { get; set; } = new();
        public string Modo { get; set; } = "Crear";

        [BindProperty] public List<SoftwareCheckbox> SoftwareDisponible { get; set; } = new();
        public List<SelectListItem> TiposSoftware { get; set; } = new();

        public FormularioModel(ConexionBDD dbConnection, ILogger<FormularioModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(string handler, int? id)
        {
            Modo = handler switch { "Editar" => "Editar", "Ver" => "Ver", _ => "Crear" };

            await CargarDatosIniciales();
            await CargarSoftwareDisponible(id);

            if (id.HasValue)
            {
                await CargarEquipoExistente(id.Value);
                if (Equipo == null) return NotFound();

                if (Equipo.IdTipoEquipo.HasValue)
                {
                    Marcas = await ObtenerMarcasPorTipo(Equipo.IdTipoEquipo.Value);
                    if (Equipo.IdMarca.HasValue)
                    {
                        Modelos = await ObtenerModelosPorMarcaYTipo(Equipo.IdMarca.Value, Equipo.IdTipoEquipo.Value);
                        if (Equipo.IdModelo.HasValue && Equipo.IdModelo.Value > 0)
                        {
                            Perfiles = await ObtenerPerfilesPorModelo(Equipo.IdModelo.Value);
                        }
                    }
                }
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (Equipo.VidaUtilAnios.HasValue && Equipo.VidaUtilAnios.Value < 0)
                ModelState.AddModelError("Equipo.VidaUtilAnios", "La vida útil no puede ser negativa.");

            decimal? costoDecimal = null;
            if (!string.IsNullOrWhiteSpace(Equipo.Costo))
            {
                decimal parsed;
                if (!ValidacionesBackend.EsCosto(Equipo.Costo) ||
                    !ValidacionesBackend.TryParseCosto(Equipo.Costo, out parsed))
                {
                    ModelState.AddModelError("Equipo.Costo", "Costo inválido.");
                }
                else
                {
                    if (parsed < 0) ModelState.AddModelError("Equipo.Costo", "El costo no puede ser negativo.");
                    else costoDecimal = parsed;
                }

            }

            if (!ModelState.IsValid)
            {
                await CargarDatosIniciales();
                await CargarSoftwareDisponible(Equipo.IdActivoFijo > 0 ? Equipo.IdActivoFijo : (int?)null);
                return Page();
            }

            using (var connection = await _dbConnection.GetConnectionAsync())
            await using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    string query;
                    int activoFijoId = Equipo.IdActivoFijo;
                    bool esCreacion = activoFijoId == 0;

                    DateTime? fechaFinVida = null;
                    if (Equipo.VidaUtilAnios.HasValue)
                    {
                        var years = Math.Max(0, Equipo.VidaUtilAnios.Value);
                        fechaFinVida = Equipo.FechaCompra.AddYears(years);
                    }

                    if (esCreacion)
                    {
                        query = @"
INSERT INTO ActivosFijos
    (id_perfil, NumeroSerie, EtiquetaInv, FechaCompra, Garantia, FechaFinVidaUtil, Costo, id_estado)
OUTPUT INSERTED.id_activofijo
VALUES
    (@IdPerfil, @NumeroSerie, @EtiquetaInv, @FechaCompra, @Garantia, @FechaFinVidaUtil, @Costo, @IdEstado);";
                    }
                    else
                    {
                        query = @"
UPDATE ActivosFijos SET
    id_perfil = @IdPerfil,
    NumeroSerie = @NumeroSerie,
    EtiquetaInv = @EtiquetaInv,
    FechaCompra = @FechaCompra,
    Garantia = @Garantia,
    FechaFinVidaUtil = @FechaFinVidaUtil,
    Costo = @Costo,
    id_estado = @IdEstado
WHERE id_activofijo = @IdActivoFijo;";
                    }

                    var cmd = new SqlCommand(query, connection, transaction);
                    cmd.Parameters.AddWithValue("@IdPerfil", Equipo.IdPerfil);
                    cmd.Parameters.AddWithValue("@NumeroSerie", Equipo.NumeroSerie);
                    cmd.Parameters.AddWithValue("@EtiquetaInv", Equipo.EtiquetaInv);
                    cmd.Parameters.AddWithValue("@FechaCompra", Equipo.FechaCompra);
                    cmd.Parameters.AddWithValue("@Garantia", (object?)Equipo.Garantia ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@FechaFinVidaUtil", (object?)fechaFinVida ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Costo", (object?)costoDecimal ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@IdEstado", Equipo.IdEstado);

                    if (!esCreacion)
                    {
                        cmd.Parameters.AddWithValue("@IdActivoFijo", Equipo.IdActivoFijo);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    else
                    {
                        activoFijoId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                    }

                    // Software (igual que antes)
                    var del = new SqlCommand("DELETE FROM SoftwaresEquipos WHERE id_activofijo = @Id", connection, transaction);
                    del.Parameters.AddWithValue("@Id", activoFijoId);
                    await del.ExecuteNonQueryAsync();

                    foreach (var s in SoftwareDisponible.Where(x => x.IsSelected))
                    {
                        var ins = new SqlCommand(
                            "INSERT INTO SoftwaresEquipos (id_activofijo, id_software, FechaInstalacion, ClaveLicencia) VALUES (@Id, @Soft, GETDATE(), @Clave)",
                            connection, transaction);
                        ins.Parameters.AddWithValue("@Id", activoFijoId);
                        ins.Parameters.AddWithValue("@Soft", s.Id);
                        ins.Parameters.AddWithValue("@Clave", (object?)s.ClaveLicencia ?? DBNull.Value);
                        await ins.ExecuteNonQueryAsync();
                    }

                    await transaction.CommitAsync();

                    // Bitácora
                    try
                    {
                        if (esCreacion)
                        {
                            var detalles = $"Se creó el activo fijo '{Equipo.EtiquetaInv}' N/S '{Equipo.NumeroSerie}'.";
                            await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                                BitacoraConstantes.Modulos.Inventario,
                                BitacoraConstantes.Acciones.Creacion, detalles);
                            TempData["SuccessMessage"] = "Activo fijo creado exitosamente.";
                        }
                        else
                        {
                            var detalles = $"Se modificó el activo fijo '{Equipo.EtiquetaInv}' (ID: {activoFijoId}).";
                            await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                                BitacoraConstantes.Modulos.Inventario,
                                BitacoraConstantes.Acciones.Modificacion, detalles);
                            TempData["SuccessMessage"] = "Activo fijo actualizado exitosamente.";
                        }
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogError(exBit, "Error registrando en bitácora (Inventario).");
                    }

                    return RedirectToPage("./Index");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error al guardar activo fijo. Usuario: {User}", User?.Identity?.Name ?? "anon");
                    ModelState.AddModelError("", "Error del servidor al intentar guardar el activo fijo. Intente de nuevo.");
                    await CargarDatosIniciales();
                    await CargarSoftwareDisponible(Equipo.IdActivoFijo > 0 ? Equipo.IdActivoFijo : (int?)null);
                    return Page();
                }
            }
        }

        // ==== Handlers auxiliares / cargas ====

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

                string detalles = $"Se agregó el nuevo software '{data.Nombre}' desde el formulario de inventario.";
                await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                    BitacoraConstantes.Modulos.Softwares,
                    BitacoraConstantes.Acciones.Creacion, detalles);

                return new JsonResult(new { success = true, id = newId, text = data.Nombre });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al agregar nuevo software desde el modal.");
                return new JsonResult(new { success = false, message = "Error del servidor." });
            }
        }

        private async Task CargarSoftwareDisponible(int? activoFijoId)
        {
            using var connection = await _dbConnection.GetConnectionAsync();

            var cmdSoftware = new SqlCommand("SELECT id_software, Nombre FROM Softwares ORDER BY Nombre", connection);
            using (var r = await cmdSoftware.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    SoftwareDisponible.Add(new SoftwareCheckbox { Id = r.GetInt32(0), Nombre = r.GetString(1) });

            if (activoFijoId.HasValue && activoFijoId > 0)
            {
                var softwareAsignado = new Dictionary<int, string?>();
                var cmdAsign = new SqlCommand("SELECT id_software, ClaveLicencia FROM SoftwaresEquipos WHERE id_activofijo = @Id", connection);
                cmdAsign.Parameters.AddWithValue("@Id", activoFijoId.Value);
                using var rd = await cmdAsign.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    softwareAsignado[rd.GetInt32(0)] = rd.IsDBNull(1) ? null : rd.GetString(1);

                foreach (var s in SoftwareDisponible)
                    if (softwareAsignado.TryGetValue(s.Id, out var clave))
                    { s.IsSelected = true; s.ClaveLicencia = clave; }
            }
        }

        private async Task CargarEquipoExistente(int id)
        {
            using var connection = await _dbConnection.GetConnectionAsync();
            var q = @"
SELECT
    af.id_activofijo, af.NumeroSerie, af.EtiquetaInv, af.FechaCompra, af.Garantia, af.id_estado,
    af.FechaFinVidaUtil, af.Costo,
    p.id_perfil, p.NombrePerfil, p.id_modelo,
    mo.id_marca, mo.id_tipoequipo
FROM ActivosFijos af
JOIN Perfiles p ON af.id_perfil = p.id_perfil
JOIN Modelos mo ON p.id_modelo = mo.id_modelo
WHERE af.id_activofijo = @Id";
            var cmd = new SqlCommand(q, connection);
            cmd.Parameters.AddWithValue("@Id", id);

            using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                DateTime fechaCompra = r.GetDateTime(3);
                DateTime? fechaFin = r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6);

                int? vidaAnios = null;
                if (fechaFin.HasValue)
                {
                    vidaAnios = Math.Max(0, fechaFin.Value.Year - fechaCompra.Year);
                }

                string? costoStr = null;
                if (!r.IsDBNull(7))
                {
                    var costo = r.GetDouble(7);
                    costoStr = costo.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                Equipo = new EquipoViewModel
                {
                    IdActivoFijo = r.GetInt32(0),
                    NumeroSerie = r.GetString(1),
                    EtiquetaInv = r.GetString(2),
                    FechaCompra = fechaCompra,
                    Garantia = r.IsDBNull(4) ? null : r.GetString(4),
                    IdEstado = r.GetInt32(5),
                    VidaUtilAnios = vidaAnios,
                    Costo = costoStr,
                    IdPerfil = r.GetInt32(8),
                    NombrePerfil = r.GetString(9),
                    IdModelo = r.GetInt32(10),
                    IdMarca = r.GetInt32(11),
                    IdTipoEquipo = r.GetInt32(12)
                };
            }
        }

        private async Task CargarDatosIniciales()
        {
            TiposEquipos = await ObtenerTiposEquipos();

            using var connection = await _dbConnection.GetConnectionAsync();
            var cmdTipos = new SqlCommand("SELECT id_tiposoftware, TipoSoftware FROM TiposSoftwares ORDER BY TipoSoftware", connection);
            using (var r = await cmdTipos.ExecuteReaderAsync())
                while (await r.ReadAsync())
                    TiposSoftware.Add(new SelectListItem { Value = r.GetInt32(0).ToString(), Text = r.GetString(1) });

            if (Equipo.IdEstado == 0)
                Equipo.IdEstado = await ObtenerEstadoActivo();
        }

        private async Task<List<TipoEquipo>> ObtenerTiposEquipos() { var list = new List<TipoEquipo>(); using var c = await _dbConnection.GetConnectionAsync(); var cmd = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo", c); using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) list.Add(new TipoEquipo { Id = r.GetInt32(0), Nombre = r.GetString(1) }); return list; }
        private async Task<List<Marca>> ObtenerMarcasPorTipo(int tipoId) { var list = new List<Marca>(); using var c = await _dbConnection.GetConnectionAsync(); var cmd = new SqlCommand("SELECT DISTINCT m.id_marca, m.Marca FROM Marcas m JOIN Modelos mo ON m.id_marca = mo.id_marca WHERE mo.id_tipoequipo = @TipoId ORDER BY m.Marca", c); cmd.Parameters.AddWithValue("@TipoId", tipoId); using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) list.Add(new Marca { Id = r.GetInt32(0), Nombre = r.GetString(1) }); return list; }
        private async Task<List<ModeloInfo>> ObtenerModelosPorMarcaYTipo(int marcaId, int tipoId) { var list = new List<ModeloInfo>(); using var c = await _dbConnection.GetConnectionAsync(); var cmd = new SqlCommand("SELECT DISTINCT id_modelo, Modelo FROM Modelos WHERE id_marca = @MarcaId AND id_tipoequipo = @TipoId ORDER BY Modelo", c); cmd.Parameters.AddWithValue("@MarcaId", marcaId); cmd.Parameters.AddWithValue("@TipoId", tipoId); using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) list.Add(new ModeloInfo { Id = r.GetInt32(0), Nombre = r.GetString(1) }); return list; }
        private async Task<List<PerfilInfo>> ObtenerPerfilesPorModelo(int modeloId) { var list = new List<PerfilInfo>(); using var c = await _dbConnection.GetConnectionAsync(); var cmd = new SqlCommand("SELECT id_perfil, NombrePerfil FROM Perfiles WHERE id_modelo = @ModeloId AND NombrePerfil IS NOT NULL", c); cmd.Parameters.AddWithValue("@ModeloId", modeloId); using var r = await cmd.ExecuteReaderAsync(); while (await r.ReadAsync()) list.Add(new PerfilInfo { Id = r.GetInt32(0), Nombre = r.GetString(1) }); return list; }
        private async Task<int> ObtenerEstadoActivo() { using var c = await _dbConnection.GetConnectionAsync(); var cmd = new SqlCommand("SELECT id_estado FROM Estados WHERE Estado = 'Activo'", c); var res = await cmd.ExecuteScalarAsync(); return res != null ? (int)res : 1; }
        public async Task<JsonResult> OnGetMarcasPorTipo(int tipoId) => new JsonResult((await ObtenerMarcasPorTipo(tipoId)).Select(i => new { id = i.Id, nombre = i.Nombre }));
        public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId) => new JsonResult((await ObtenerModelosPorMarcaYTipo(marcaId, tipoId)).Select(i => new { id = i.Id, nombre = i.Nombre }));
        public async Task<JsonResult> OnGetPerfilesPorModelo(int modeloId) => new JsonResult((await ObtenerPerfilesPorModelo(modeloId)).Select(i => new { id = i.Id, nombre = i.Nombre }));
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
    }

    public class AddSoftwareRequest { public string Nombre { get; set; } public int IdTipoSoftware { get; set; } public string? Version { get; set; } public string? Proveedor { get; set; } }
    public class SoftwareCheckbox { public int Id { get; set; } public string Nombre { get; set; } public bool IsSelected { get; set; } public string? ClaveLicencia { get; set; } }

    public class EquipoViewModel
    {
        public int IdActivoFijo { get; set; }

        [Required] public string NumeroSerie { get; set; }
        [Required] public string EtiquetaInv { get; set; }

        [Required] public int? IdTipoEquipo { get; set; }
        public int? IdMarca { get; set; }
        public int? IdModelo { get; set; }

        [Required] public int IdPerfil { get; set; }

        [Required] public DateTime FechaCompra { get; set; } = DateTime.Today;
        public string? Garantia { get; set; }

        [Range(0, 50, ErrorMessage = "Ingrese un valor entre 0 y 50 años.")]
        public int? VidaUtilAnios { get; set; }

        public string? Costo { get; set; }

        [Required] public int IdEstado { get; set; }
        public string? NombrePerfil { get; set; }
    }

    public class TipoEquipo { public int Id { get; set; } public string Nombre { get; set; } }
    public class Marca { public int Id { get; set; } public string Nombre { get; set; } }
    public class ModeloInfo { public int Id { get; set; } public string Nombre { get; set; } }
    public class PerfilInfo { public int Id { get; set; } public string Nombre { get; set; } }
    public class CaracteristicaViewModel { public string Nombre { get; set; } public string Valor { get; set; } }
}
