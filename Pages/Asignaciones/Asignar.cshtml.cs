using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace InventarioComputo.Pages
{
    public class AsignarModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        // Propiedades para el formulario y las listas
        [BindProperty]
        public AsignacionViewModel Asignacion { get; set; } = new AsignacionViewModel();
        public List<ActivoFijoInfo> ActivosDisponibles { get; set; } = new List<ActivoFijoInfo>();
        public List<EmpleadoInfo> Empleados { get; set; } = new List<EmpleadoInfo>();
        public List<DepartamentoInfo> Departamentos { get; set; } = new List<DepartamentoInfo>();

        // Banderas para controlar la vista
        public ActivoFijoInfo ActivoPreseleccionado { get; set; }
        public bool EsActivoPreseleccionado { get; set; } = false;
        public string Modo { get; set; } = "Crear";

        public AsignarModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task<IActionResult> OnGetAsync(string handler, int? id, int? idActivoFijo)
        {
            Modo = handler switch { "Editar" => "Editar", "Ver" => "Ver", _ => "Crear" };
            await CargarDatosIniciales();

            if (id.HasValue) // Si viene un ID de asignación, es para ver/editar
            {
                await CargarAsignacionExistente(id.Value);
                if (Asignacion == null) return NotFound();
                EsActivoPreseleccionado = true;
                ActivoPreseleccionado = await CargarActivoPorId(Asignacion.IdActivoFijo);
            }
            else if (idActivoFijo.HasValue) // Si viene un ID de activo, es para una nueva asignación
            {
                ActivoPreseleccionado = await CargarActivoPorId(idActivoFijo.Value);
                if (ActivoPreseleccionado == null) return RedirectToPage("/Inventario/Index");

                EsActivoPreseleccionado = true;
                Asignacion.IdActivoFijo = idActivoFijo.Value;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await CargarDatosIniciales();
                if (Asignacion.IdActivoFijo > 0)
                {
                    ActivoPreseleccionado = await CargarActivoPorId(Asignacion.IdActivoFijo);
                    EsActivoPreseleccionado = true;
                }
                return Page();
            }

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                // Si el ID es 0, es un registro nuevo
                if (Asignacion.IdEmpleadoEquipo == 0)
                {
                    await using (var transaction = connection.BeginTransaction())
                    {
                        try
                        {
                            // 1. Desactivo la asignación anterior del equipo
                            var cmdDesactivar = new SqlCommand(@"UPDATE EmpleadosEquipos SET ResponsableActual = 0, FechaRetiro = GETDATE(), DetallesRetiro = @DetallesRetiro WHERE id_activofijo = @IdActivoFijo AND ResponsableActual = 1", connection, transaction);
                            cmdDesactivar.Parameters.AddWithValue("@IdActivoFijo", Asignacion.IdActivoFijo);
                            cmdDesactivar.Parameters.AddWithValue("@DetallesRetiro", Asignacion.DetallesRetiro ?? (object)DBNull.Value);
                            await cmdDesactivar.ExecuteNonQueryAsync();

                            // 2. Inserto la nueva asignación
                            var cmdInsertar = new SqlCommand(@"INSERT INTO EmpleadosEquipos (id_activofijo, id_empleado, id_DE, FechaAsignacion, ResponsableActual, TipoAsignacion, DetallesAsignacion) VALUES (@IdActivoFijo, @IdEmpleado, @IdDepartamento, @FechaAsignacion, 1, @TipoAsignacion, @DetallesAsignacion)", connection, transaction);
                            cmdInsertar.Parameters.AddWithValue("@IdActivoFijo", Asignacion.IdActivoFijo);
                            cmdInsertar.Parameters.AddWithValue("@IdEmpleado", Asignacion.IdEmpleado);
                            cmdInsertar.Parameters.AddWithValue("@IdDepartamento", Asignacion.IdDepartamento);
                            cmdInsertar.Parameters.AddWithValue("@FechaAsignacion", Asignacion.FechaAsignacion);
                            cmdInsertar.Parameters.AddWithValue("@TipoAsignacion", Asignacion.TipoAsignacion);
                            cmdInsertar.Parameters.AddWithValue("@DetallesAsignacion", Asignacion.DetallesAsignacion ?? (object)DBNull.Value);
                            await cmdInsertar.ExecuteNonQueryAsync();

                            // 3. Cambio el estado del activo a 'Activo'
                            var cmdActualizarEstado = new SqlCommand(@"UPDATE ActivosFijos SET id_estado = (SELECT id_estado FROM Estados WHERE Estado = 'Activo') WHERE id_activofijo = @IdActivoFijo", connection, transaction);
                            cmdActualizarEstado.Parameters.AddWithValue("@IdActivoFijo", Asignacion.IdActivoFijo);
                            await cmdActualizarEstado.ExecuteNonQueryAsync();

                            await transaction.CommitAsync();
                            TempData["SuccessMessage"] = "Equipo asignado correctamente.";
                            return RedirectToPage("/Asignaciones/Index");
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            ModelState.AddModelError("", $"Error al asignar el equipo: {ex.Message}");
                        }
                    }
                }
                else // Si el ID existe, es una actualización
                {
                    try
                    {
                        var cmdUpdate = new SqlCommand(@"UPDATE EmpleadosEquipos SET id_empleado = @IdEmpleado, id_DE = @IdDepartamento, FechaAsignacion = @FechaAsignacion, TipoAsignacion = @TipoAsignacion, DetallesAsignacion = @DetallesAsignacion, DetallesRetiro = @DetallesRetiro WHERE id_empleadoequipo = @IdEmpleadoEquipo", connection);
                        cmdUpdate.Parameters.AddWithValue("@IdEmpleadoEquipo", Asignacion.IdEmpleadoEquipo);
                        cmdUpdate.Parameters.AddWithValue("@IdEmpleado", Asignacion.IdEmpleado);
                        cmdUpdate.Parameters.AddWithValue("@IdDepartamento", Asignacion.IdDepartamento);
                        cmdUpdate.Parameters.AddWithValue("@FechaAsignacion", Asignacion.FechaAsignacion);
                        cmdUpdate.Parameters.AddWithValue("@TipoAsignacion", Asignacion.TipoAsignacion);
                        cmdUpdate.Parameters.AddWithValue("@DetallesAsignacion", Asignacion.DetallesAsignacion ?? (object)DBNull.Value);
                        cmdUpdate.Parameters.AddWithValue("@DetallesRetiro", Asignacion.DetallesRetiro ?? (object)DBNull.Value);
                        await cmdUpdate.ExecuteNonQueryAsync();

                        TempData["SuccessMessage"] = "Asignación actualizada correctamente.";
                        return RedirectToPage("/Asignaciones/Index");
                    }
                    catch (Exception ex)
                    {
                        ModelState.AddModelError("", $"Error al actualizar la asignación: {ex.Message}");
                    }
                }
            }

            await CargarDatosIniciales();
            return Page();
        }

        // Handler para el AJAX del dropdown de empleados
        public async Task<JsonResult> OnGetEmpleadoInfoAsync(int idEmpleado)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var cmd = new SqlCommand("SELECT id_DE FROM Empleados WHERE id_empleado = @Id", connection);
                cmd.Parameters.AddWithValue("@Id", idEmpleado);
                var departamentoId = await cmd.ExecuteScalarAsync();
                return new JsonResult(new { idDepartamento = departamentoId });
            }
        }

        // Carga los dropdowns
        private async Task CargarDatosIniciales()
        {
            ActivosDisponibles = await CargarActivosDisponibles();
            Empleados = await CargarEmpleados();
            Departamentos = await CargarDepartamentos();
        }

        // Carga los equipos con estado 'En Almacén'
        private async Task<List<ActivoFijoInfo>> CargarActivosDisponibles()
        {
            var lista = new List<ActivoFijoInfo>();
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"SELECT af.id_activofijo, af.EtiquetaInv, p.NombrePerfil 
                              FROM ActivosFijos af
                              JOIN Estados e ON af.id_estado = e.id_estado
                              JOIN Perfiles p ON af.id_perfil = p.id_perfil
                              ORDER BY af.EtiquetaInv";
                var cmd = new SqlCommand(query, connection);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        lista.Add(new ActivoFijoInfo { Id = reader.GetInt32(0), Identificador = $"{reader.GetString(1)} - {reader.GetString(2)}" });
                    }
                }
            }
            return lista;
        }

        // Carga una asignación existente para verla o editarla
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

        // Carga un activo por su ID
        private async Task<ActivoFijoInfo> CargarActivoPorId(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"SELECT af.id_activofijo, af.EtiquetaInv, p.NombrePerfil 
                              FROM ActivosFijos af
                              JOIN Perfiles p ON af.id_perfil = p.id_perfil
                              WHERE af.id_activofijo = @Id";
                var cmd = new SqlCommand(query, connection);
                cmd.Parameters.AddWithValue("@Id", id);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        return new ActivoFijoInfo { Id = reader.GetInt32(0), Identificador = $"{reader.GetString(1)} - {reader.GetString(2)}" };
                    }
                }
            }
            return null;
        }

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

        // Modelo para los datos del formulario
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
            [DataType(DataType.Date)]
            [Display(Name = "Fecha de Asignación")]
            public DateTime FechaAsignacion { get; set; } = DateTime.Today;

            [Required(ErrorMessage = "Debe seleccionar un tipo de asignación.")]
            [Display(Name = "Tipo de Asignación")]
            public string TipoAsignacion { get; set; }

            [Display(Name = "Detalles de la Asignación")]
            public string? DetallesAsignacion { get; set; }

            [Display(Name = "Detalles del Retiro (si aplica)")]
            public string? DetallesRetiro { get; set; }
        }

        // Clases simples para las listas de los dropdowns
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
    }
}