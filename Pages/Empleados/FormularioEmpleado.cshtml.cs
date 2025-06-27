using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace InventarioComputo.Pages.Empleados
{
    public class FormularioEmpleadoModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        [BindProperty]
        public EmpleadoViewModel Empleado { get; set; } = new EmpleadoViewModel();
        public List<PuestoInfo> Puestos { get; set; } = new List<PuestoInfo>();
        public List<DepartamentoInfo> Departamentos { get; set; } = new List<DepartamentoInfo>();
        public string Modo { get; set; } = "Crear"; // "Crear", "Editar" o "Ver"

        public FormularioEmpleadoModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
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

                // Cargar datos iniciales
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

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string query;

                    if (Empleado.Id == 0) // Crear nuevo
                    {
                        query = @"
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
                            )";
                    }
                    else // Actualizar existente
                    {
                        query = @"
                            UPDATE Empleados SET
                                Nombre = @Nombre,
                                id_puesto = @IdPuesto,
                                id_DE = @IdDepartamento,
                                Email = @Email,
                                Telefono = @Telefono
                            WHERE id_empleado = @Id";
                    }

                    var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@Nombre", Empleado.Nombre);
                    command.Parameters.AddWithValue("@IdPuesto", Empleado.IdPuesto);
                    command.Parameters.AddWithValue("@IdDepartamento", Empleado.IdDepartamento);
                    command.Parameters.AddWithValue("@Email", Empleado.Email ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@Telefono", Empleado.Telefono ?? (object)DBNull.Value);

                    if (Empleado.Id > 0)
                    {
                        command.Parameters.AddWithValue("@Id", Empleado.Id);
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