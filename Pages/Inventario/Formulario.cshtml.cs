using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

public class FormularioModel : PageModel
{
    private readonly ConexionBDD _dbConnection;

    [BindProperty]
    public EquipoViewModel Equipo { get; set; } = new EquipoViewModel();
    public List<TipoEquipo> TiposEquipos { get; set; } = new List<TipoEquipo>();
    public List<Marca> Marcas { get; set; } = new List<Marca>();
    public List<ModeloInfo> Modelos { get; set; } = new List<ModeloInfo>();
    public List<PerfilInfo> Perfiles { get; set; } = new List<PerfilInfo>();
    public string Modo { get; set; } = "Crear";

    [BindProperty]
    public List<SoftwareCheckbox> SoftwareDisponible { get; set; } = new List<SoftwareCheckbox>();

    public FormularioModel(ConexionBDD dbConnection)
    {
        _dbConnection = dbConnection;
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

                    if (activoFijoId == 0)
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
                    return RedirectToPage("./Index");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    ModelState.AddModelError("", "Error al guardar: " + ex.Message);
                    await CargarDatosIniciales();
                    await CargarSoftwareDisponible(Equipo.IdActivoFijo > 0 ? Equipo.IdActivoFijo : (int?)null);
                    return Page();
                }
            }
        }
    }

    private async Task CargarSoftwareDisponible(int? activoFijoId)
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

            if (activoFijoId.HasValue && activoFijoId > 0)
            {
                var softwareAsignado = new Dictionary<int, string>();
                var cmdAsignado = new SqlCommand("SELECT id_software, ClaveLicencia FROM SoftwaresEquipos WHERE id_activofijo = @Id", connection);
                cmdAsignado.Parameters.AddWithValue("@Id", activoFijoId.Value);
                using (var reader = await cmdAsignado.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        softwareAsignado[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }

                foreach (var software in SoftwareDisponible)
                {
                    if (softwareAsignado.ContainsKey(software.Id))
                    {
                        software.IsSelected = true;
                        software.ClaveLicencia = softwareAsignado[software.Id];
                    }
                }
            }
        }
    }

    private async Task CargarEquipoExistente(int id)
    {
        using (var connection = await _dbConnection.GetConnectionAsync())
        {
            var query = @"
                SELECT af.id_activofijo, af.NumeroSerie, af.EtiquetaInv, af.FechaCompra, af.Garantia, af.id_estado,
                       p.id_perfil, p.NombrePerfil, p.id_modelo, mo.id_marca, mo.id_tipoequipo
                FROM ActivosFijos af
                JOIN Perfiles p ON af.id_perfil = p.id_perfil
                JOIN Modelos mo ON p.id_modelo = mo.id_modelo
                WHERE af.id_activofijo = @Id";
            var command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);
            using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    Equipo = new EquipoViewModel
                    {
                        IdActivoFijo = reader.GetInt32(0),
                        NumeroSerie = reader.GetString(1),
                        EtiquetaInv = reader.GetString(2),
                        FechaCompra = reader.GetDateTime(3),
                        Garantia = reader.IsDBNull(4) ? null : reader.GetString(4),
                        IdEstado = reader.GetInt32(5),
                        IdPerfil = reader.GetInt32(6),
                        NombrePerfil = reader.GetString(7),
                        IdModelo = reader.GetInt32(8),
                        IdMarca = reader.GetInt32(9),
                        IdTipoEquipo = reader.GetInt32(10)
                    };
                }
            }
        }
    }

    private async Task CargarDatosIniciales()
    {
        TiposEquipos = await ObtenerTiposEquipos();
        if (Equipo.IdEstado == 0)
        {
            Equipo.IdEstado = await ObtenerEstadoActivo();
        }
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
            var query = @"
                SELECT DISTINCT m.id_marca, m.Marca 
                FROM Marcas m
                JOIN Modelos mo ON m.id_marca = mo.id_marca
                WHERE mo.id_tipoequipo = @TipoId
                ORDER BY m.Marca";
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
            var query = @"
                SELECT DISTINCT id_modelo, Modelo 
                FROM Modelos
                WHERE id_marca = @MarcaId AND id_tipoequipo = @TipoId
                ORDER BY Modelo";
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

    private async Task<int> ObtenerEstadoActivo()
    {
        using (var connection = await _dbConnection.GetConnectionAsync())
        {
            var query = "SELECT id_estado FROM Estados WHERE Estado = 'Activo'";
            var command = new SqlCommand(query, connection);
            var result = await command.ExecuteScalarAsync();
            return result != null ? (int)result : 1;
        }
    }

    public async Task<JsonResult> OnGetMarcasPorTipo(int tipoId)
    {
        var data = await ObtenerMarcasPorTipo(tipoId);
        return new JsonResult(data.Select(i => new { id = i.Id, nombre = i.Nombre }));
    }

    public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId)
    {
        var data = await ObtenerModelosPorMarcaYTipo(marcaId, tipoId);
        return new JsonResult(data.Select(i => new { id = i.Id, nombre = i.Nombre }));
    }

    public async Task<JsonResult> OnGetPerfilesPorModelo(int modeloId)
    {
        var data = await ObtenerPerfilesPorModelo(modeloId);
        return new JsonResult(data.Select(i => new { id = i.Id, nombre = i.Nombre }));
    }

    public async Task<JsonResult> OnGetCaracteristicasPorPerfil(int perfilId)
    {
        var caracteristicas = new List<CaracteristicaViewModel>();
        using (var connection = await _dbConnection.GetConnectionAsync())
        {
            var query = @"
                SELECT c.Caracteristica, cm.Valor 
                FROM CaracteristicasModelos cm
                JOIN Caracteristicas c ON cm.id_caracteristica = c.id_caracteristica
                WHERE cm.id_perfil = @PerfilId";
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

    public class SoftwareCheckbox { public int Id { get; set; } public string Nombre { get; set; } public bool IsSelected { get; set; } public string? ClaveLicencia { get; set; } }
    public class EquipoViewModel { public int IdActivoFijo { get; set; } [Required] public string NumeroSerie { get; set; } [Required][Display(Name = "Etiqueta de Inventario")] public string EtiquetaInv { get; set; } [Required][Display(Name = "Tipo de Equipo")] public int? IdTipoEquipo { get; set; } public int? IdMarca { get; set; } public int? IdModelo { get; set; } [Required] public int IdPerfil { get; set; } [Required][Display(Name = "Fecha de Compra")] public DateTime FechaCompra { get; set; } = DateTime.Today; [Display(Name = "Garantía (opcional)")] public string? Garantia { get; set; } [Required] public int IdEstado { get; set; } public string? NombrePerfil { get; set; } }
    public class TipoEquipo { public int Id { get; set; } public string Nombre { get; set; } }
    public class Marca { public int Id { get; set; } public string Nombre { get; set; } }
    public class ModeloInfo { public int Id { get; set; } public string Nombre { get; set; } }
    public class PerfilInfo { public int Id { get; set; } public string Nombre { get; set; } }
    public class CaracteristicaViewModel { public string Nombre { get; set; } public string Valor { get; set; } }
}