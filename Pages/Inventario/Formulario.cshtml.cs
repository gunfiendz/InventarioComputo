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
    public string Modo { get; set; } = "Crear"; // Puede ser "Crear", "Editar" o "Ver"

    public FormularioModel(ConexionBDD dbConnection)
    {
        _dbConnection = dbConnection;
    }

    public async Task<IActionResult> OnGetAsync(string handler, int? id)
    {
        try
        {
            // Determinar el modo basado en el handler
            Modo = handler switch
            {
                "Editar" => "Editar",
                "Ver" => "Ver",
                _ => "Crear" // Default
            };

            bool esModoLectura = Modo == "Ver";

            // Cargar datos básicos
            await CargarDatosIniciales();

            if (id.HasValue)
            {
                await CargarEquipoExistente(id.Value);

                if (Equipo == null) return NotFound();

                // Cargar datos relacionados
                if (Equipo.IdTipoEquipo.HasValue)
                {
                    Marcas = await ObtenerMarcasPorTipo(Equipo.IdTipoEquipo.Value);

                    if (Equipo.IdMarca.HasValue)
                    {
                        Modelos = await ObtenerModelosPorMarcaYTipo(
                            Equipo.IdMarca.Value,
                            Equipo.IdTipoEquipo.Value);

                        // Cargar perfiles para el modelo seleccionado
                        if (Equipo.IdModelo > 0)
                        {
                            Perfiles = await ObtenerPerfilesPorModelo(Equipo.IdModelo.Value);
                        }
                    }
                }
            }

            return Page();
        }
        catch (Exception ex)
        {
            // Log del error
            return StatusCode(500, "Error interno al cargar el formulario");
        }
    }

    private async Task CargarEquipoExistente(int id)
    {
        using (var connection = await _dbConnection.GetConnectionAsync())
        {
            var query = @"
                SELECT 
                    af.id_activofijo, af.NumeroSerie, af.EtiquetaInv, 
                    af.FechaCompra, af.Garantia, af.id_estado,
                    p.id_perfil, p.NombrePerfil,
                    p.id_modelo, mo.id_marca, mo.id_tipoequipo
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
                    tipos.Add(new TipoEquipo
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
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
                    marcas.Add(new Marca
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
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
                    modelos.Add(new ModeloInfo
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }
        return modelos;
    }

    // Nuevo método para obtener perfiles por modelo
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
                    perfiles.Add(new PerfilInfo
                    {
                        Id = reader.GetInt32(0),
                        Nombre = reader.GetString(1)
                    });
                }
            }
        }
        return perfiles;
    }

    public async Task<JsonResult> OnGetMarcasPorTipo(int tipoId)
    {
        var marcas = await ObtenerMarcasPorTipo(tipoId);
        return new JsonResult(marcas.Select(m => new {
            id = m.Id,
            nombre = m.Nombre
        }));
    }

    public async Task<JsonResult> OnGetModelosPorMarcaYTipo(int marcaId, int tipoId)
    {
        var modelos = await ObtenerModelosPorMarcaYTipo(marcaId, tipoId);
        return new JsonResult(modelos.Select(m => new {
            id = m.Id,
            nombre = m.Nombre
        }));
    }

    // Nuevo método para obtener características por perfil
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
                    caracteristicas.Add(new CaracteristicaViewModel
                    {
                        Nombre = reader.GetString(0),
                        Valor = reader.GetString(1)
                    });
                }
            }
        }
        return new JsonResult(caracteristicas.Select(c => new {
            nombre = c.Nombre,
            valor = c.Valor
        }));
    }

    // Nuevo método para obtener perfiles (usado por AJAX)
    public async Task<JsonResult> OnGetPerfilesPorModelo(int modeloId)
    {
        var perfiles = await ObtenerPerfilesPorModelo(modeloId);
        return new JsonResult(perfiles.Select(p => new {
            id = p.Id,
            nombre = p.Nombre
        }));
    }

    private async Task<int> ObtenerEstadoActivo()
    {
        using (var connection = await _dbConnection.GetConnectionAsync())
        {
            var query = "SELECT id_estado FROM Estados WHERE Estado = 'Activo'";
            var command = new SqlCommand(query, connection);
            var result = await command.ExecuteScalarAsync();
            return result != null ? (int)result : 1; // Default to 1 if not found
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await CargarDatosIniciales();
            return Page();
        }

        try
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                string query;

                if (Equipo.IdActivoFijo == 0) // Crear nuevo
                {
                    query = @"
                        INSERT INTO ActivosFijos (
                            id_perfil, 
                            NumeroSerie, 
                            EtiquetaInv, 
                            FechaCompra, 
                            Garantia, 
                            id_estado
                        ) VALUES (
                            @IdPerfil, 
                            @NumeroSerie, 
                            @EtiquetaInv, 
                            @FechaCompra, 
                            @Garantia, 
                            @IdEstado)";

                }
                else // Actualizar existente
                {
                    query = @"
                        UPDATE ActivosFijos SET
                            id_perfil = @IdPerfil, 
                            NumeroSerie = @NumeroSerie, 
                            EtiquetaInv = @EtiquetaInv, 
                            FechaCompra = @FechaCompra, 
                            Garantia = @Garantia, 
                            id_estado = @IdEstado
                        WHERE id_activofijo = @IdActivoFijo";
                }

                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@IdPerfil", Equipo.IdPerfil);
                command.Parameters.AddWithValue("@NumeroSerie", Equipo.NumeroSerie);
                command.Parameters.AddWithValue("@EtiquetaInv", Equipo.EtiquetaInv);
                command.Parameters.AddWithValue("@FechaCompra", Equipo.FechaCompra);
                command.Parameters.AddWithValue("@Garantia", Equipo.Garantia ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@IdEstado", Equipo.IdEstado);

                if (Equipo.IdActivoFijo > 0)
                {
                    command.Parameters.AddWithValue("@IdActivoFijo", Equipo.IdActivoFijo);
                }

                await command.ExecuteNonQueryAsync();

                return RedirectToPage("./Index");
            }
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", "Error al guardar: " + ex.Message);
            await CargarDatosIniciales();
            return Page();
        }
    }

    public class EquipoViewModel
    {
        public int IdActivoFijo { get; set; }

        [Required(ErrorMessage = "El número de serie es requerido")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres")]
        public string NumeroSerie { get; set; }

        [Required(ErrorMessage = "La etiqueta de inventario es requerida")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres")]
        [Display(Name = "Etiqueta de Inventario")]
        public string EtiquetaInv { get; set; }

        [Required(ErrorMessage = "Debe seleccionar un tipo de equipo")]
        [Display(Name = "Tipo de Equipo")]
        public int? IdTipoEquipo { get; set; }

        [Display(Name = "Marca")]
        public int? IdMarca { get; set; }

        public int? IdModelo { get; set; }
        [Required(ErrorMessage = "Debe seleccionar un modelo")]
        public int IdPerfil { get; set; }

        [Required(ErrorMessage = "La fecha de compra es requerida")]
        [Display(Name = "Fecha de Compra")]
        [DataType(DataType.Date)]
        public DateTime FechaCompra { get; set; } = DateTime.Today;

        [Display(Name = "Garantía (opcional)")]
        [StringLength(100, ErrorMessage = "Máximo 100 caracteres")]
        public string? Garantia { get; set; }

        [Required]
        public int IdEstado { get; set; }

        [Display(Name = "Perfil")]
        public string? NombrePerfil { get; set; }  // Nueva propiedad

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

    public class ModeloInfo
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
    }

    public class PerfilInfo
    {
        public int Id { get; set; }
        public string Nombre { get; set; }
    }

    // Nueva clase para características
    public class CaracteristicaViewModel
    {
        public string Nombre { get; set; }
        public string Valor { get; set; }
    }
}