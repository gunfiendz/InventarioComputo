using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages.Mantenimientos
{
    public class PruebaFormularioModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<PruebaFormularioModel> _logger;

        [BindProperty]
        public MantenimientoViewModel Mantenimiento { get; set; } = new MantenimientoViewModel();
        public List<ActivoFijoInfo> Equipos { get; set; } = new List<ActivoFijoInfo>();
        public List<TipoMantenimiento> TiposMantenimiento { get; set; } = new List<TipoMantenimiento>();
        public List<EmpleadoInfo> Empleados { get; set; } = new List<EmpleadoInfo>();
        public string Modo { get; set; } = "Crear";

        public PruebaFormularioModel(ConexionBDD dbConnection, ILogger<PruebaFormularioModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(string handler, int? id, int? idEquipo)
        {
            try
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
                    await CargarMantenimientoExistente(id.Value);
                    if (Mantenimiento == null) return NotFound();
                }
                else if (idEquipo.HasValue)
                {
                    if (Equipos.Exists(e => e.Id == idEquipo.Value))
                    {
                        Mantenimiento.IdActivoFijo = idEquipo.Value;
                    }
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al cargar el formulario de mantenimiento");
                return StatusCode(500, "Error interno al cargar el formulario");
            }
        }

        private async Task CargarMantenimientoExistente(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT 
                        me.id_mantenimientoequipo, 
                        me.id_activofijo, 
                        me.Fecha, 
                        me.id_mantenimiento, 
                        me.Descripcion, 
                        me.id_empleado, 
                        me.Costo, 
                        me.Observaciones
                    FROM MantenimientosEquipos me
                    WHERE me.id_mantenimientoequipo = @Id";

                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        Mantenimiento = new MantenimientoViewModel
                        {
                            IdMantenimientoEquipo = reader.GetInt32(0),
                            IdActivoFijo = reader.GetInt32(1),
                            Fecha = reader.GetDateTime(2),
                            IdTipoMantenimiento = reader.GetInt32(3),
                            Descripcion = reader.GetString(4),
                            IdEmpleado = reader.GetInt32(5),
                            Costo = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                            Observaciones = reader.IsDBNull(7) ? null : reader.GetString(7)
                        };
                    }
                }
            }
        }

        private async Task CargarDatosIniciales()
        {
            Equipos = await ObtenerEquiposActivos();
            TiposMantenimiento = await ObtenerTiposMantenimiento();
            Empleados = await ObtenerEmpleados();
        }

        private async Task<List<ActivoFijoInfo>> ObtenerEquiposActivos()
        {
            var equipos = new List<ActivoFijoInfo>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT 
                        af.id_activofijo, 
                        af.EtiquetaInv, 
                        p.NombrePerfil,
                        mo.Modelo,
                        m.Marca
                    FROM ActivosFijos af
                    JOIN Perfiles p ON af.id_perfil = p.id_perfil
                    JOIN Modelos mo ON p.id_modelo = mo.id_modelo
                    JOIN Marcas m ON mo.id_marca = m.id_marca
                    ORDER BY af.EtiquetaInv";

                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        equipos.Add(new ActivoFijoInfo
                        {
                            Id = reader.GetInt32(0),
                            EtiquetaInv = reader.GetString(1),
                            NombrePerfil = reader.GetString(2),
                            Modelo = reader.GetString(3),
                            Marca = reader.GetString(4)
                        });
                    }
                }
            }
            return equipos;
        }

        private async Task<List<TipoMantenimiento>> ObtenerTiposMantenimiento()
        {
            var tipos = new List<TipoMantenimiento>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_mantenimiento, Tipo FROM Mantenimientos ORDER BY Tipo";
                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        tipos.Add(new TipoMantenimiento
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1)
                        });
                    }
                }
            }
            return tipos;
        }

        private async Task<List<EmpleadoInfo>> ObtenerEmpleados()
        {
            var empleados = new List<EmpleadoInfo>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre";
                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        empleados.Add(new EmpleadoInfo
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1)
                        });
                    }
                }
            }
            return empleados;
        }

        public async Task<IActionResult> OnGetBuscarEquiposAsync(string term, int? id, string exact)
        {
            using var conn = await _dbConnection.GetConnectionAsync();

            if (id.HasValue && id.Value > 0)
            {
                var q = @"SELECT TOP 1 af.id_activofijo,
                         CONCAT(af.EtiquetaInv,' - ',m.Marca,' ',mo.Modelo) AS Label
                  FROM ActivosFijos af
                  JOIN Perfiles p ON af.id_perfil = p.id_perfil
                  JOIN Modelos mo ON p.id_modelo = mo.id_modelo
                  JOIN Marcas m ON mo.id_marca = m.id_marca
                  WHERE af.id_activofijo = @id";
                using var cmd = new SqlCommand(q, conn);
                cmd.Parameters.AddWithValue("@id", id.Value);
                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rd.ReadAsync())
                    return new JsonResult(new { id = rd.GetInt32(0), label = rd.GetString(1) });
                return new JsonResult(null);
            }

            if (!string.IsNullOrWhiteSpace(exact))
            {
                var q = @"SELECT TOP 1 af.id_activofijo,
                         CONCAT(af.EtiquetaInv,' - ',m.Marca,' ',mo.Modelo) AS Label
                  FROM ActivosFijos af
                  JOIN Perfiles p ON af.id_perfil = p.id_perfil
                  JOIN Modelos mo ON p.id_modelo = mo.id_modelo
                  JOIN Marcas m ON mo.id_marca = m.id_marca
                  WHERE CONCAT(af.EtiquetaInv,' - ',m.Marca,' ',mo.Modelo) = @t";
                using var cmd = new SqlCommand(q, conn);
                cmd.Parameters.AddWithValue("@t", exact.Trim());
                using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
                if (await rd.ReadAsync())
                    return new JsonResult(new { id = rd.GetInt32(0), label = rd.GetString(1) });
                return new JsonResult(null);
            }

            term ??= "";
            var qSearch = @"
SELECT TOP 10 af.id_activofijo,
       CONCAT(af.EtiquetaInv,' - ',m.Marca,' ',mo.Modelo) AS Label
FROM ActivosFijos af
JOIN Perfiles p ON af.id_perfil = p.id_perfil
JOIN Modelos mo ON p.id_modelo = mo.id_modelo
JOIN Marcas m ON mo.id_marca = m.id_marca
WHERE af.EtiquetaInv LIKE @t OR m.Marca LIKE @t OR mo.Modelo LIKE @t OR p.NombrePerfil LIKE @t
ORDER BY af.EtiquetaInv";
            using (var cmd = new SqlCommand(qSearch, conn))
            {
                cmd.Parameters.AddWithValue("@t", $"%{term.Trim()}%");
                using var rd = await cmd.ExecuteReaderAsync();
                var list = new List<object>();
                while (await rd.ReadAsync())
                    list.Add(new { id = rd.GetInt32(0), label = rd.GetString(1) });
                return new JsonResult(list);
            }
        }


        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await CargarDatosIniciales();
                return Page();
            }

            var esCreacion = (Mantenimiento.IdMantenimientoEquipo == 0);

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string query;

                    if (Mantenimiento.IdMantenimientoEquipo == 0)
                    {
                        query = @"
                            INSERT INTO MantenimientosEquipos (
                                id_activofijo, 
                                Fecha, 
                                id_mantenimiento,
                                Descripcion, 
                                id_empleado, 
                                Costo, 
                                Observaciones
                            ) VALUES (
                                @IdActivoFijo, 
                                @Fecha, 
                                @IdTipoMantenimiento,
                                @Descripcion, 
                                @IdEmpleado, 
                                @Costo, 
                                @Observaciones
                            )";
                    }
                    else
                    {
                        query = @"
                            UPDATE MantenimientosEquipos SET
                                id_activofijo = @IdActivoFijo,
                                Fecha = @Fecha,
                                id_mantenimiento = @IdTipoMantenimiento,
                                Descripcion = @Descripcion,
                                id_empleado = @IdEmpleado,
                                Costo = @Costo,
                                Observaciones = @Observaciones
                            WHERE id_mantenimientoequipo = @IdMantenimientoEquipo";
                    }

                    var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@IdActivoFijo", Mantenimiento.IdActivoFijo);
                    command.Parameters.AddWithValue("@Fecha", Mantenimiento.Fecha);
                    command.Parameters.AddWithValue("@IdTipoMantenimiento", Mantenimiento.IdTipoMantenimiento);
                    command.Parameters.AddWithValue("@Descripcion", Mantenimiento.Descripcion);
                    command.Parameters.AddWithValue("@IdEmpleado", Mantenimiento.IdEmpleado);
                    command.Parameters.AddWithValue("@Costo", Mantenimiento.Costo ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Observaciones", Mantenimiento.Observaciones ?? (object)DBNull.Value);

                    if (Mantenimiento.IdMantenimientoEquipo > 0)
                    {
                        command.Parameters.AddWithValue("@IdMantenimientoEquipo", Mantenimiento.IdMantenimientoEquipo);
                    }

                    await command.ExecuteNonQueryAsync();

                    // Etiqueta y S/N para la Bitácora
                    string etiqueta = "", serie = "";
                    const string qAf = "SELECT EtiquetaInv, NumeroSerie FROM ActivosFijos WHERE id_activofijo = @IdAF";
                    using (var cmdAf = new SqlCommand(qAf, connection))
                    {
                        cmdAf.Parameters.AddWithValue("@IdAF", Mantenimiento.IdActivoFijo);
                        using var rd = await cmdAf.ExecuteReaderAsync();
                        if (await rd.ReadAsync())
                        {
                            etiqueta = rd.GetString(0);
                            serie = rd.GetString(1);
                        }
                    }

                    try
                    {
                        var detalles = esCreacion
                            ? $"Se creó el mantenimiento del equipo '{etiqueta}' (S/N: {serie})."
                            : $"Se modificó el mantenimiento del equipo '{etiqueta}' (S/N: {serie}).";

                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Mantenimientos,
                            esCreacion ? BitacoraConstantes.Acciones.Creacion : BitacoraConstantes.Acciones.Modificacion,
                            detalles
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogError(exBit, "Error al registrar Bitácora de {Op} de mantenimiento (Equipo Etiqueta='{Etiqueta}', S/N='{Serie}')",
                            esCreacion ? "creación" : "modificación", etiqueta, serie);
                    }

                    return RedirectToPage("Index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar mantenimiento (IdMantenimientoEquipo={Id}, IdActivoFijo={AF})",
                    Mantenimiento.IdMantenimientoEquipo, Mantenimiento.IdActivoFijo);
                ModelState.AddModelError("", "Error al guardar: " + ex.Message);
                await CargarDatosIniciales();
                return Page();
            }
        }

        public class MantenimientoViewModel
        {
            public int IdMantenimientoEquipo { get; set; }

            [Required(ErrorMessage = "Debe seleccionar un equipo")]
            [Display(Name = "Equipo")]
            public int IdActivoFijo { get; set; }

            [Required(ErrorMessage = "La fecha es requerida")]
            [Display(Name = "Fecha de Mantenimiento")]
            [DataType(DataType.DateTime)]
            public DateTime Fecha { get; set; } = DateTime.Now;

            [Required(ErrorMessage = "Debe seleccionar un tipo de mantenimiento")]
            [Display(Name = "Tipo de Mantenimiento")]
            public int IdTipoMantenimiento { get; set; }

            [Required(ErrorMessage = "La descripción es requerida")]
            [StringLength(500, ErrorMessage = "Máximo 500 caracteres")]
            [Display(Name = "Descripción")]
            public string Descripcion { get; set; }

            [Required(ErrorMessage = "El responsable es requerido")]
            [Display(Name = "Responsable")]
            public int IdEmpleado { get; set; }

            [Display(Name = "Costo (opcional)")]
            [Range(0, 1000000, ErrorMessage = "El costo debe ser positivo")]
            public decimal? Costo { get; set; }

            [Display(Name = "Observaciones (opcional)")]
            [StringLength(1000, ErrorMessage = "Máximo 1000 caracteres")]
            public string? Observaciones { get; set; }
        }

        public class ActivoFijoInfo
        {
            public int Id { get; set; }
            public string EtiquetaInv { get; set; }
            public string NombrePerfil { get; set; }
            public string Modelo { get; set; }
            public string Marca { get; set; }
        }

        public class TipoMantenimiento
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class EmpleadoInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }
    }
}
