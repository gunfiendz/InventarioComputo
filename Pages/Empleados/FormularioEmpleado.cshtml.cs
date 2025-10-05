using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;
using System;

namespace InventarioComputo.Pages.Empleados
{
    public class FormularioEmpleadoModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<FormularioEmpleadoModel> _logger;

        [BindProperty]
        public EmpleadoViewModel Empleado { get; set; } = new EmpleadoViewModel();

        public List<PuestoInfo> Puestos { get; set; } = new List<PuestoInfo>();
        public List<DepartamentoInfo> Departamentos { get; set; } = new List<DepartamentoInfo>();
        public string Modo { get; set; } = "Crear";

        public FormularioEmpleadoModel(ConexionBDD dbConnection, ILogger<FormularioEmpleadoModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<IActionResult> OnGetAsync(string handler, int? id)
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
                    await CargarEmpleadoExistente(id.Value);
                    if (Empleado == null) return NotFound();
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al cargar el formulario de empleado");
                return StatusCode(500, "Error interno al cargar el formulario");
            }
        }

        private async Task CargarEmpleadoExistente(int id)
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
                    SELECT 
                        id_empleado, 
                        Nombre, 
                        id_puesto, 
                        id_DE, 
                        Email, 
                        Telefono
                    FROM Empleados
                    WHERE id_empleado = @Id";

                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Id", id);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        Empleado = new EmpleadoViewModel
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1),
                            IdPuesto = reader.GetInt32(2),
                            IdDepartamento = reader.GetInt32(3),
                            Email = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Telefono = reader.IsDBNull(5) ? null : reader.GetString(5)
                        };
                    }
                }
            }
        }

        private async Task CargarDatosIniciales()
        {
            Puestos = await ObtenerPuestos();
            Departamentos = await ObtenerDepartamentos();
        }

        private async Task<List<PuestoInfo>> ObtenerPuestos()
        {
            var puestos = new List<PuestoInfo>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_puesto, NombrePuesto FROM Puestos ORDER BY NombrePuesto";
                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        puestos.Add(new PuestoInfo
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1)
                        });
                    }
                }
            }
            return puestos;
        }

        private async Task<List<DepartamentoInfo>> ObtenerDepartamentos()
        {
            var departamentos = new List<DepartamentoInfo>();

            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = "SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa ORDER BY NombreDepartamento";
                var command = new SqlCommand(query, connection);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        departamentos.Add(new DepartamentoInfo
                        {
                            Id = reader.GetInt32(0),
                            Nombre = reader.GetString(1)
                        });
                    }
                }
            }
            return departamentos;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await CargarDatosIniciales();
                return Page();
            }

            var esCreacion = (Empleado.Id == 0);

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    if (Empleado.Id == 0)
                    {
                        var insert = @"
                            INSERT INTO Empleados (
                                Nombre, 
                                id_puesto, 
                                id_DE, 
                                Email, 
                                Telefono
                            ) VALUES (
                                @Nombre, 
                                @IdPuesto, 
                                @IdDepartamento, 
                                @Email, 
                                @Telefono
                            );
                            SELECT SCOPE_IDENTITY();";

                        var cmd = new SqlCommand(insert, connection);
                        cmd.Parameters.AddWithValue("@Nombre", Empleado.Nombre);
                        cmd.Parameters.AddWithValue("@IdPuesto", Empleado.IdPuesto);
                        cmd.Parameters.AddWithValue("@IdDepartamento", Empleado.IdDepartamento);
                        cmd.Parameters.AddWithValue("@Email", Empleado.Email ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Telefono", Empleado.Telefono ?? (object)DBNull.Value);

                        var newIdObj = await cmd.ExecuteScalarAsync();
                        Empleado.Id = Convert.ToInt32(newIdObj);
                    }
                    else
                    {
                        var update = @"
                            UPDATE Empleados SET
                                Nombre = @Nombre,
                                id_puesto = @IdPuesto,
                                id_DE = @IdDepartamento,
                                Email = @Email,
                                Telefono = @Telefono
                            WHERE id_empleado = @Id";

                        var cmd = new SqlCommand(update, connection);
                        cmd.Parameters.AddWithValue("@Nombre", Empleado.Nombre);
                        cmd.Parameters.AddWithValue("@IdPuesto", Empleado.IdPuesto);
                        cmd.Parameters.AddWithValue("@IdDepartamento", Empleado.IdDepartamento);
                        cmd.Parameters.AddWithValue("@Email", Empleado.Email ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Telefono", Empleado.Telefono ?? (object)DBNull.Value);
                        cmd.Parameters.AddWithValue("@Id", Empleado.Id);

                        await cmd.ExecuteNonQueryAsync();
                    }

                    try
                    {
                        var detalles = esCreacion
                            ? $"Se creó el empleado '{Empleado.Nombre}' (ID: {Empleado.Id})."
                            : $"Se modificó el empleado '{Empleado.Nombre}' (ID: {Empleado.Id}).";

                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Empleados,
                            esCreacion ? BitacoraConstantes.Acciones.Creacion : BitacoraConstantes.Acciones.Modificacion,
                            detalles
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogError(exBit, "Error al registrar Bitácora de {Op} de empleado Id={Id}",
                            esCreacion ? "creación" : "modificación", Empleado.Id);
                    }

                    return RedirectToPage("./Index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar empleado Id={Id}", Empleado.Id);
                ModelState.AddModelError(string.Empty, "Error al guardar: " + ex.Message);
                await CargarDatosIniciales();
                return Page();
            }
        }

        public class EmpleadoViewModel
        {
            public int Id { get; set; }

            [Required(ErrorMessage = "El nombre es requerido")]
            [StringLength(100, ErrorMessage = "Máximo 100 caracteres")]
            public string Nombre { get; set; }

            [Required(ErrorMessage = "Debe seleccionar un puesto")]
            [Display(Name = "Puesto")]
            public int IdPuesto { get; set; }

            [Required(ErrorMessage = "Debe seleccionar un departamento")]
            [Display(Name = "Departamento")]
            public int IdDepartamento { get; set; }

            [EmailAddress(ErrorMessage = "Formato de email inválido")]
            [StringLength(100, ErrorMessage = "Máximo 100 caracteres")]
            public string? Email { get; set; }

            [StringLength(50, ErrorMessage = "Máximo 50 caracteres")]
            public string? Telefono { get; set; }
        }

        public class PuestoInfo
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
