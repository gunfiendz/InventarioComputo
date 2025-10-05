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
    public class FormularioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormularioModel> _logger; // Inyectar el Logger específico para este modelo

        [BindProperty]
        public EquipoViewModel Equipo { get; set; } = new EquipoViewModel();
        public List<TipoEquipo> TiposEquipos { get; set; } = new List<TipoEquipo>();
        public List<Marca> Marcas { get; set; } = new List<Marca>();
        public List<ModeloInfo> Modelos { get; set; } = new List<ModeloInfo>();
        public List<PerfilInfo> Perfiles { get; set; } = new List<PerfilInfo>();
        public string Modo { get; set; } = "Crear";

        [BindProperty]
        public List<SoftwareCheckbox> SoftwareDisponible { get; set; } = new List<SoftwareCheckbox>();

        public List<SelectListItem> TiposSoftware { get; set; } = new List<SelectListItem>();

        public FormularioModel(ConexionBDD dbConnection, ILogger<FormularioModel> logger) // Constructor actualizado
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
                        if (Equipo.IdModelo > 0)
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
            if (!ModelState.IsValid)
            {
                await CargarDatosIniciales();
                await CargarSoftwareDisponible(Equipo.IdActivoFijo > 0 ? Equipo.IdActivoFijo : (int?)null);
                return Page();
            }

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                await using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string query;
                        int activoFijoId = Equipo.IdActivoFijo;
                        bool esCreacion = activoFijoId == 0;

                        if (esCreacion)
                        {
                            query = @"INSERT INTO ActivosFijos (id_perfil, NumeroSerie, EtiquetaInv, FechaCompra, Garantia, id_estado) 
                                      OUTPUT INSERTED.id_activofijo
                                      VALUES (@IdPerfil, @NumeroSerie, @EtiquetaInv, @FechaCompra, @Garantia, @IdEstado)";
                        }
                        else
                        {
                            query = @"UPDATE ActivosFijos SET id_perfil = @IdPerfil, NumeroSerie = @NumeroSerie, EtiquetaInv = @EtiquetaInv, 
                                      FechaCompra = @FechaCompra, Garantia = @Garantia, id_estado = @IdEstado
                                      WHERE id_activofijo = @IdActivoFijo";
                        }

                        var command = new SqlCommand(query, connection, transaction);
                        command.Parameters.AddWithValue("@IdPerfil", Equipo.IdPerfil);
                        command.Parameters.AddWithValue("@NumeroSerie", Equipo.NumeroSerie);
                        command.Parameters.AddWithValue("@EtiquetaInv", Equipo.EtiquetaInv);
                        command.Parameters.AddWithValue("@FechaCompra", Equipo.FechaCompra);
                        command.Parameters.AddWithValue("@Garantia", Equipo.Garantia ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@IdEstado", Equipo.IdEstado);

                        if (activoFijoId > 0)
                        {
                            command.Parameters.AddWithValue("@IdActivoFijo", activoFijoId);
                            await command.ExecuteNonQueryAsync();
                        }
                        else
                        {
                            activoFijoId = (int)await command.ExecuteScalarAsync();
                        }

                        var deleteCmd = new SqlCommand("DELETE FROM SoftwaresEquipos WHERE id_activofijo = @IdActivoFijo", connection, transaction);
                        deleteCmd.Parameters.AddWithValue("@IdActivoFijo", activoFijoId);
                        await deleteCmd.ExecuteNonQueryAsync();

                        foreach (var software in SoftwareDisponible.Where(s => s.IsSelected))
                        {
                            var insertCmd = new SqlCommand("INSERT INTO SoftwaresEquipos (id_activofijo, id_software, FechaInstalacion, ClaveLicencia) VALUES (@IdActivoFijo, @IdSoftware, GETDATE(), @ClaveLicencia)", connection, transaction);
                            insertCmd.Parameters.AddWithValue("@IdActivoFijo", activoFijoId);
                            insertCmd.Parameters.AddWithValue("@IdSoftware", software.Id);
                            insertCmd.Parameters.AddWithValue("@ClaveLicencia", software.ClaveLicencia ?? (object)DBNull.Value);
                            await insertCmd.ExecuteNonQueryAsync();
                        }

                        await transaction.CommitAsync();

                        // --- INTEGRACIÓN DE BITÁCORA ---
                        if (esCreacion)
                        {
                            string detalles = $"Se creó el activo fijo con etiqueta '{Equipo.EtiquetaInv}' y N/S '{Equipo.NumeroSerie}'.";
                            await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User, BitacoraConstantes.Modulos.Inventario, BitacoraConstantes.Acciones.Creacion, detalles);
                            TempData["SuccessMessage"] = "Activo fijo creado exitosamente.";
                        }
                        else
                        {
                            string detalles = $"Se modificó el activo fijo con etiqueta '{Equipo.EtiquetaInv}' (ID: {activoFijoId}).";
                            await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User, BitacoraConstantes.Modulos.Inventario, BitacoraConstantes.Acciones.Modificacion, detalles);
                            TempData["SuccessMessage"] = "Activo fijo actualizado exitosamente.";
                        }
                        // --- FIN DE LA INTEGRACIÓN ---

                        return RedirectToPage("./Index");
                    }
                    catch (Exception ex)
                    {
                        await transaction.RollbackAsync();
                        _logger.LogError(ex, "Error al guardar activo fijo. Usuario: {Username}", User.Identity.Name);
                        ModelState.AddModelError("", "Error del servidor al intentar guardar el activo fijo. Por favor, intente de nuevo.");
                        await CargarDatosIniciales();
                        await CargarSoftwareDisponible(Equipo.IdActivoFijo > 0 ? Equipo.IdActivoFijo : (int?)null);
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

                    // Registro en Bitácora
                    string detalles = $"Se agregó el nuevo software '{data.Nombre}' desde el formulario de inventario.";
                    await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User, BitacoraConstantes.Modulos.Softwares, BitacoraConstantes.Acciones.Creacion, detalles);

                    return new JsonResult(new { success = true, id = newId, text = data.Nombre });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al agregar nuevo software desde el modal.");
                return new JsonResult(new { success = false, message = "Error del servidor." });
            }
        }

        private async Task CargarSoftwareDisponible(int? activoFijoId)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmdSoftware = new SqlCommand("SELECT id_software, Nombre FROM Softwares ORDER BY Nombre", connection);
                using (var reader = await cmdSoftware.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) { SoftwareDisponible.Add(new SoftwareCheckbox { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); }
                }

                if (activoFijoId.HasValue && activoFijoId > 0)
                {
                    var softwareAsignado = new Dictionary<int, string>();
                    var cmdAsignado = new SqlCommand("SELECT id_software, ClaveLicencia FROM SoftwaresEquipos WHERE id_activofijo = @Id", connection);
                    cmdAsignado.Parameters.AddWithValue("@Id", activoFijoId.Value);
                    using (var reader = await cmdAsignado.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync()) { softwareAsignado[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1); }
                    }
                    foreach (var software in SoftwareDisponible)
                    {
                        if (softwareAsignado.ContainsKey(software.Id)) { software.IsSelected = true; software.ClaveLicencia = softwareAsignado[software.Id]; }
                    }
                }
            }
        }

        private async Task CargarEquipoExistente(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"SELECT af.id_activofijo, af.NumeroSerie, af.EtiquetaInv, af.FechaCompra, af.Garantia, af.id_estado, p.id_perfil, p.NombrePerfil, p.id_modelo, mo.id_marca, mo.id_tipoequipo FROM ActivosFijos af JOIN Perfiles p ON af.id_perfil = p.id_perfil JOIN Modelos mo ON p.id_modelo = mo.id_modelo WHERE af.id_activofijo = @Id";
                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        Equipo = new EquipoViewModel { IdActivoFijo = reader.GetInt32(0), NumeroSerie = reader.GetString(1), EtiquetaInv = reader.GetString(2), FechaCompra = reader.GetDateTime(3), Garantia = reader.IsDBNull(4) ? null : reader.GetString(4), IdEstado = reader.GetInt32(5), IdPerfil = reader.GetInt32(6), NombrePerfil = reader.GetString(7), IdModelo = reader.GetInt32(8), IdMarca = reader.GetInt32(9), IdTipoEquipo = reader.GetInt32(10) };
                    }
                }
            }
        }

        private async Task CargarDatosIniciales()
        {
            TiposEquipos = await ObtenerTiposEquipos();

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

            if (Equipo.IdEstado == 0) { Equipo.IdEstado = await ObtenerEstadoActivo(); }
        }

        private async Task<List<TipoEquipo>> ObtenerTiposEquipos() { 
            var list = new List<TipoEquipo>(); using (var connection = await _dbConnection.GetConnectionAsync()) { var command = new SqlCommand("SELECT id_tipoequipo, TipoEquipo FROM TiposEquipos ORDER BY TipoEquipo", connection); using (var reader = await command.ExecuteReaderAsync()) { while (await reader.ReadAsync()) { list.Add(new TipoEquipo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); } } } return list; }
        private async Task<List<Marca>> ObtenerMarcasPorTipo(int tipoId) { var list = new List<Marca>(); using (var connection = await _dbConnection.GetConnectionAsync()) { var command = new SqlCommand("SELECT DISTINCT m.id_marca, m.Marca FROM Marcas m JOIN Modelos mo ON m.id_marca = mo.id_marca WHERE mo.id_tipoequipo = @TipoId ORDER BY m.Marca", connection); command.Parameters.AddWithValue("@TipoId", tipoId); using (var reader = await command.ExecuteReaderAsync()) { while (await reader.ReadAsync()) { list.Add(new Marca { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); } } } return list; }
        private async Task<List<ModeloInfo>> ObtenerModelosPorMarcaYTipo(int marcaId, int tipoId) { var list = new List<ModeloInfo>(); using (var connection = await _dbConnection.GetConnectionAsync()) { var command = new SqlCommand("SELECT DISTINCT id_modelo, Modelo FROM Modelos WHERE id_marca = @MarcaId AND id_tipoequipo = @TipoId ORDER BY Modelo", connection); command.Parameters.AddWithValue("@MarcaId", marcaId); command.Parameters.AddWithValue("@TipoId", tipoId); using (var reader = await command.ExecuteReaderAsync()) { while (await reader.ReadAsync()) { list.Add(new ModeloInfo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); } } } return list; }
        private async Task<List<PerfilInfo>> ObtenerPerfilesPorModelo(int modeloId) { var list = new List<PerfilInfo>(); using (var connection = await _dbConnection.GetConnectionAsync()) { var command = new SqlCommand("SELECT id_perfil, NombrePerfil FROM Perfiles WHERE id_modelo = @ModeloId AND NombrePerfil IS NOT NULL", connection); command.Parameters.AddWithValue("@ModeloId", modeloId); using (var reader = await command.ExecuteReaderAsync()) { while (await reader.ReadAsync()) { list.Add(new PerfilInfo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) }); } } } return list; }
        private async Task<int> ObtenerEstadoActivo() { using (var connection = await _dbConnection.GetConnectionAsync()) { var command = new SqlCommand("SELECT id_estado FROM Estados WHERE Estado = 'Activo'", connection); var result = await command.ExecuteScalarAsync(); return result != null ? (int)result : 1; } }
        public async Task<JsonResult> OnGetMarcasPorTipo(int tipoId) { var data = await ObtenerMarcasPorTipo(tipoId); return new JsonResult(data.Select(i => new { id = i.Id, nombre = i.Nombre })); }
        public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId) { var data = await ObtenerModelosPorMarcaYTipo(marcaId, tipoId); return new JsonResult(data.Select(i => new { id = i.Id, nombre = i.Nombre })); }
        public async Task<JsonResult> OnGetPerfilesPorModelo(int modeloId) { var data = await ObtenerPerfilesPorModelo(modeloId); return new JsonResult(data.Select(i => new { id = i.Id, nombre = i.Nombre })); }
        public async Task<JsonResult> OnGetCaracteristicasPorPerfil(int perfilId) { var caracteristicas = new List<CaracteristicaViewModel>(); using (var connection = await _dbConnection.GetConnectionAsync()) { var command = new SqlCommand("SELECT c.Caracteristica, cm.Valor FROM CaracteristicasModelos cm JOIN Caracteristicas c ON cm.id_caracteristica = c.id_caracteristica WHERE cm.id_perfil = @PerfilId", connection); command.Parameters.AddWithValue("@PerfilId", perfilId); using (var reader = await command.ExecuteReaderAsync()) { while (await reader.ReadAsync()) { caracteristicas.Add(new CaracteristicaViewModel { Nombre = reader.GetString(0), Valor = reader.GetString(1) }); } } } return new JsonResult(caracteristicas.Select(c => new { nombre = c.Nombre, valor = c.Valor })); }

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
        [Required] public int IdEstado { get; set; }
        public string? NombrePerfil { get; set; }
    }

    public class TipoEquipo { public int Id { get; set; } public string Nombre { get; set; } }
    public class Marca { public int Id { get; set; } public string Nombre { get; set; } }
    public class ModeloInfo { public int Id { get; set; } public string Nombre { get; set; } }
    public class PerfilInfo { public int Id { get; set; } public string Nombre { get; set; } }
    public class CaracteristicaViewModel { public string Nombre { get; set; } public string Valor { get; set; } }
}