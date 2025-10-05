using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data; // Asumo que aqu� tienes tu clase ConexionBDD
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace InventarioComputo.Pages
{
    public class AsignarModel : PageModel
    {
        #region Dependencias y Propiedades

        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<AsignarModel> _logger;

        [BindProperty]
        public AsignacionViewModel Asignacion { get; set; } = new AsignacionViewModel();

        // --- Listas para los Dropdowns ---
        public List<ActivoFijoInfo> ActivosDisponibles { get; set; } = new List<ActivoFijoInfo>();
        public List<EmpleadoInfo> Empleados { get; set; } = new List<EmpleadoInfo>();
        public List<DepartamentoInfo> Departamentos { get; set; } = new List<DepartamentoInfo>();

        // --- Banderas para controlar la l�gica de la vista ---
        public ActivoFijoInfo ActivoPreseleccionado { get; set; }
        public bool EsActivoPreseleccionado { get; set; } = false;
        public string Modo { get; set; } = "Crear"; // Puede ser Crear, Editar o Ver

        #endregion

        #region Constructor

        public AsignarModel(ConexionBDD dbConnection, ILogger<AsignarModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        #endregion

        #region Handlers de la P�gina (OnGet y OnPost)

        /// <summary>
        /// Maneja las solicitudes GET. Determina si se va a crear, editar o ver una asignaci�n.
        /// </summary>
        /// <param name="handler">Define el modo: "Editar" o "Ver". Si es nulo, el modo es "Crear".</param>
        /// <param name="id">El ID de la asignaci�n (EmpleadoEquipo) para editar o ver.</param>
        /// <param name="idActivoFijo">El ID de un activo preseleccionado para una nueva asignaci�n.</param>
        public async Task<IActionResult> OnGetAsync(string handler, int? id, int? idActivoFijo)
        {
            Modo = handler switch { "Editar" => "Editar", "Ver" => "Ver", _ => "Crear" };
            await CargarDatosIniciales();

            if (id.HasValue) // Modo Editar o Ver una asignaci�n existente
            {
                await CargarAsignacionExistente(id.Value);
                if (Asignacion == null) return NotFound();

                EsActivoPreseleccionado = true;
                ActivoPreseleccionado = await CargarActivoPorId(Asignacion.IdActivoFijo);
            }
            else if (idActivoFijo.HasValue) // Modo Crear, pero con un activo ya definido
            {
                ActivoPreseleccionado = await CargarActivoPorId(idActivoFijo.Value);
                if (ActivoPreseleccionado == null) return RedirectToPage("/Inventario/Index");

                EsActivoPreseleccionado = true;
                Asignacion.IdActivoFijo = idActivoFijo.Value;
            }

            return Page();
        }

        /// <summary>
        /// Maneja el env�o del formulario para crear o actualizar una asignaci�n.
        /// </summary>
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState inv�lido en Asignar. Usuario: {User}", User?.Identity?.Name ?? "anon");
                await RecargarPaginaPorError();
                return Page();
            }

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                // Si el ID es 0, es un registro nuevo (Crear)
                if (Asignacion.IdEmpleadoEquipo == 0)
                {
                    await using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Desactivar asignaci�n previa del activo (si la hay)
                            var cmdDesactivar = new SqlCommand(
                                @"UPDATE EmpleadosEquipos
                                  SET ResponsableActual = 0, FechaRetiro = GETDATE(), DetallesRetiro = @DetallesRetiro
                                  WHERE id_activofijo = @IdActivoFijo AND ResponsableActual = 1", connection, transaction);
                            cmdDesactivar.Parameters.AddWithValue("@IdActivoFijo", Asignacion.IdActivoFijo);
                            cmdDesactivar.Parameters.AddWithValue("@DetallesRetiro", "Reasignado a otro usuario.");
                            await cmdDesactivar.ExecuteNonQueryAsync();

                            // 2. Insertar nueva asignaci�n
                            var cmdInsertar = new SqlCommand(
                                @"INSERT INTO EmpleadosEquipos (id_activofijo, id_empleado, id_DE, FechaAsignacion, ResponsableActual, TipoAsignacion, DetallesAsignacion)
                                  VALUES (@IdActivoFijo, @IdEmpleado, @IdDepartamento, @FechaAsignacion, 1, @TipoAsignacion, @DetallesAsignacion)", connection, transaction);
                            cmdInsertar.Parameters.AddWithValue("@IdActivoFijo", Asignacion.IdActivoFijo);
                            cmdInsertar.Parameters.AddWithValue("@IdEmpleado", Asignacion.IdEmpleado);
                            cmdInsertar.Parameters.AddWithValue("@IdDepartamento", Asignacion.IdDepartamento);
                            cmdInsertar.Parameters.AddWithValue("@FechaAsignacion", Asignacion.FechaAsignacion);
                            cmdInsertar.Parameters.AddWithValue("@TipoAsignacion", Asignacion.TipoAsignacion);
                            cmdInsertar.Parameters.AddWithValue("@DetallesAsignacion", Asignacion.DetallesAsignacion ?? (object)DBNull.Value);
                            await cmdInsertar.ExecuteNonQueryAsync();

                           /*// 3. Cambiar estado del activo a 'Asignado' (o 'Activo')
                            var cmdActualizarEstado = new SqlCommand(
                                @"UPDATE ActivosFijos
                                  SET id_estado = (SELECT TOP 1 id_estado FROM Estados WHERE Estado = 'Activo')
                                  WHERE id_activofijo = @IdActivoFijo", connection, transaction);
                            cmdActualizarEstado.Parameters.AddWithValue("@IdActivoFijo", Asignacion.IdActivoFijo);
                            await cmdActualizarEstado.ExecuteNonQueryAsync();*/

                            await transaction.CommitAsync();

                            _logger.LogInformation("Asignaci�n creada por {User}: ActivoId={ActivoId}, EmpleadoId={EmpleadoId}", User?.Identity?.Name ?? "anon", Asignacion.IdActivoFijo, Asignacion.IdEmpleado);
                            string detalles = $"Se le asigno el Activo con el Id: '{Asignacion.IdActivoFijo}' al Empleado (ID: {Asignacion.IdEmpleado}).";
                            await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                                BitacoraConstantes.Modulos.Asignaciones,
                                BitacoraConstantes.Acciones.Creacion,
                                detalles);
                            TempData["SuccessMessage"] = "Equipo asignado correctamente.";
                            return RedirectToPage("/Asignaciones/Index");
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            _logger.LogError(ex, "Error al crear asignaci�n por {User}. ActivoId={ActivoId}", User?.Identity?.Name ?? "anon", Asignacion.IdActivoFijo);
                            ModelState.AddModelError("", $"Error al asignar el equipo: {ex.Message}");
                        }
                    }
                }
                else // Si el ID existe, es una actualizaci�n (Editar)
                {
                    try
                    {
                        var cmdUpdate = new SqlCommand(
                            @"UPDATE EmpleadosEquipos
                              SET id_empleado = @IdEmpleado, id_DE = @IdDepartamento, FechaAsignacion = @FechaAsignacion,
                                  TipoAsignacion = @TipoAsignacion, DetallesAsignacion = @DetallesAsignacion, DetallesRetiro = @DetallesRetiro
                              WHERE id_empleadoequipo = @IdEmpleadoEquipo", connection);

                        cmdUpdate.Parameters.AddWithValue("@IdEmpleadoEquipo", Asignacion.IdEmpleadoEquipo);
                        cmdUpdate.Parameters.AddWithValue("@IdEmpleado", Asignacion.IdEmpleado);
                        cmdUpdate.Parameters.AddWithValue("@IdDepartamento", Asignacion.IdDepartamento);
                        cmdUpdate.Parameters.AddWithValue("@FechaAsignacion", Asignacion.FechaAsignacion);
                        cmdUpdate.Parameters.AddWithValue("@TipoAsignacion", Asignacion.TipoAsignacion);
                        cmdUpdate.Parameters.AddWithValue("@DetallesAsignacion", Asignacion.DetallesAsignacion ?? (object)DBNull.Value);
                        cmdUpdate.Parameters.AddWithValue("@DetallesRetiro", Asignacion.DetallesRetiro ?? (object)DBNull.Value);
                        await cmdUpdate.ExecuteNonQueryAsync();

                        // CORRECCI�N: El n�mero de argumentos coincid�a con el n�mero de placeholders en el mensaje.
                        _logger.LogInformation(
                            "Asignacion {AsignacionId} modificada por {User}. ActivoId={ActivoId}, EmpleadoId={EmpleadoId}",
                            Asignacion.IdEmpleadoEquipo, User?.Identity?.Name ?? "anon", Asignacion.IdActivoFijo, Asignacion.IdEmpleado);
                        string detalles = $"Se le modific� la Asignacion con el Id: '{Asignacion.IdEmpleadoEquipo}' correspondiente al Activo con el Id: '{Asignacion.IdActivoFijo}'.";
                        await BitacoraHelper.RegistrarAccionAsync(_dbConnection, _logger, User,
                            BitacoraConstantes.Modulos.Asignaciones,
                            BitacoraConstantes.Acciones.Modificacion,
                            detalles);
                        TempData["SuccessMessage"] = "Asignaci�n actualizada correctamente.";
                        return RedirectToPage("/Asignaciones/Index");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al actualizar asignaci�n {AsignacionId} por {User}", Asignacion.IdEmpleadoEquipo, User?.Identity?.Name ?? "anon");
                        ModelState.AddModelError("", $"Error al actualizar la asignaci�n: {ex.Message}");
                    }
                }
            }
            await RecargarPaginaPorError();
            return Page();
        }

        #endregion

        #region M�todos de Carga de Datos (BDD)

        /// Carga todas las listas necesarias para los dropdowns de la p�gina.
        private async Task CargarDatosIniciales()
        {
            ActivosDisponibles = await CargarActivosDisponibles();
            Empleados = await CargarEmpleados();
            Departamentos = await CargarDepartamentos();
        }

        /// Obtiene los activos que pueden ser asignados.
        private async Task<List<ActivoFijoInfo>> CargarActivosDisponibles()
        {
            var lista = new List<ActivoFijoInfo>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT af.id_activofijo, af.EtiquetaInv, p.NombrePerfil
                    FROM ActivosFijos af
                    JOIN Perfiles p ON af.id_perfil = p.id_perfil
                    ORDER BY af.EtiquetaInv";
                var cmd = new SqlCommand(query, connection);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        lista.Add(new ActivoFijoInfo
                        {
                            Id = reader.GetInt32(0),
                            Identificador = $"{reader.GetString(1)} - {reader.GetString(2)}"
                        });
                    }
                }
            }
            return lista;
        }

        /// Carga los datos de una asignaci�n existente para los modos 'Editar' o 'Ver'.
        private async Task CargarAsignacionExistente(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT * FROM EmpleadosEquipos WHERE id_empleadoequipo = @Id";
                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        Asignacion = new AsignacionViewModel
                        {
                            IdEmpleadoEquipo = reader.GetInt32(reader.GetOrdinal("id_empleadoequipo")),
                            IdActivoFijo = reader.GetInt32(reader.GetOrdinal("id_activofijo")),
                            IdEmpleado = reader.GetInt32(reader.GetOrdinal("id_empleado")),
                            IdDepartamento = reader.GetInt32(reader.GetOrdinal("id_DE")),
                            FechaAsignacion = reader.GetDateTime(reader.GetOrdinal("FechaAsignacion")),
                            TipoAsignacion = reader.IsDBNull(reader.GetOrdinal("TipoAsignacion")) ? "" : reader.GetString(reader.GetOrdinal("TipoAsignacion")),
                            DetallesAsignacion = reader.IsDBNull(reader.GetOrdinal("DetallesAsignacion")) ? null : reader.GetString(reader.GetOrdinal("DetallesAsignacion")),
                            DetallesRetiro = reader.IsDBNull(reader.GetOrdinal("DetallesRetiro")) ? null : reader.GetString(reader.GetOrdinal("DetallesRetiro")),
                        };
                    }
                }
            }
        }

        /// Carga la informaci�n de un activo espec�fico por su ID.
        private async Task<ActivoFijoInfo> CargarActivoPorId(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT af.id_activofijo, af.EtiquetaInv, p.NombrePerfil
                    FROM ActivosFijos af
                    JOIN Perfiles p ON af.id_perfil = p.id_perfil
                    WHERE af.id_activofijo = @Id";
                var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@Id", id);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new ActivoFijoInfo
                        {
                            Id = reader.GetInt32(0),
                            Identificador = $"{reader.GetString(1)} - {reader.GetString(2)}"
                        };
                    }
                }
            }
            return null;
        }

        /// Carga la lista de todos los empleados.
        private async Task<List<EmpleadoInfo>> CargarEmpleados()
        {
            var lista = new List<EmpleadoInfo>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmd = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre", connection);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        lista.Add(new EmpleadoInfo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
            return lista;
        }

        /// Carga la lista de todos los departamentos.
        private async Task<List<DepartamentoInfo>> CargarDepartamentos()
        {
            var lista = new List<DepartamentoInfo>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmd = new SqlCommand("SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa ORDER BY NombreDepartamento", connection);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        lista.Add(new DepartamentoInfo { Id = reader.GetInt32(0), Nombre = reader.GetString(1) });
                    }
                }
            }
            return lista;
        }

        /// M�todo auxiliar para recargar los datos necesarios cuando ocurre un error en el Post.
        private async Task RecargarPaginaPorError()
        {
            await CargarDatosIniciales();
            if (Asignacion.IdActivoFijo > 0)
            {
                ActivoPreseleccionado = await CargarActivoPorId(Asignacion.IdActivoFijo);
                EsActivoPreseleccionado = true;
            }
        }

        #endregion

        #region Handlers AJAX

        /// Endpoint para AJAX que devuelve el departamento de un empleado.
        public async Task<JsonResult> OnGetEmpleadoInfoAsync(int idEmpleado)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmd = new SqlCommand("SELECT id_DE FROM Empleados WHERE id_empleado = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", idEmpleado);
                var departamentoId = await cmd.ExecuteScalarAsync() ?? 0;
                return new JsonResult(new { idDepartamento = departamentoId });
            }
        }

        #endregion

        #region ViewModels y Clases de Soporte

        public class AsignacionViewModel
        {
            public int IdEmpleadoEquipo { get; set; }

            [Required(ErrorMessage = "Debe seleccionar un equipo.")]
            [Display(Name = "Equipo")]
            public int IdActivoFijo { get; set; }

            [Required(ErrorMessage = "Debe seleccionar un empleado.")]
            [Display(Name = "Empleado")]
            public int IdEmpleado { get; set; }

            [Required(ErrorMessage = "Debe seleccionar un departamento.")]
            [Display(Name = "Departamento")]
            public int IdDepartamento { get; set; }

            [Required(ErrorMessage = "La fecha es requerida.")]
            [DataType(DataType.DateTime)]
            [Display(Name = "Fecha de Asignaci�n")]
            public DateTime FechaAsignacion { get; set; } = DateTime.Now; 

            [Required(ErrorMessage = "Debe seleccionar un tipo de asignaci�n.")]
            [Display(Name = "Tipo de Asignaci�n")]
            public string TipoAsignacion { get; set; }

            [Display(Name = "Detalles de la Asignaci�n")]
            [MaxLength(500)]
            public string? DetallesAsignacion { get; set; }

            [Display(Name = "Detalles del Retiro (si aplica)")]
            [MaxLength(500)]
            public string? DetallesRetiro { get; set; }
        }

        public class ActivoFijoInfo
        {
            public int Id { get; set; }
            public string Identificador { get; set; }
        }

        public class EmpleadoInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        public class DepartamentoInfo
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
        }

        #endregion
    }
}