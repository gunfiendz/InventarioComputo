using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using InventarioComputo.Data;

namespace InventarioComputo.Pages.Departamentos
{
    public class DepaFormModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<DepaFormModel> _logger;

        [BindProperty]
        public DepartamentoViewModel Departamento { get; set; } = new DepartamentoViewModel();

        [BindProperty]
        public List<AreaCheckbox> Areas { get; set; } = new List<AreaCheckbox>();

        public string Modo { get; set; } = "Crear";

        public DepaFormModel(ConexionBDD dbConnection, ILogger<DepaFormModel> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task OnGetAsync(string handler, int? id)
        {
            Modo = handler switch
            {
                "Editar" => "Editar",
                "Ver" => "Ver",
                _ => "Crear"
            };

            await CargarAreasDisponibles();

            if (id.HasValue)
            {
                await CargarDepartamentoExistente(id.Value);
            }
        }

        public async Task<JsonResult> OnPostAddAreaAsync([FromBody] AddAreaRequest data)
        {
            if (string.IsNullOrWhiteSpace(data.NombreArea))
                return new JsonResult(new { success = false, message = "El nombre del área es obligatorio." });

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    var query = "INSERT INTO Areas (NombreArea, Descripcion) OUTPUT INSERTED.id_area VALUES (@Nombre, @Descripcion)";
                    var command = new SqlCommand(query, connection);
                    command.Parameters.AddWithValue("@Nombre", data.NombreArea);
                    command.Parameters.AddWithValue("@Descripcion", (object)data.Descripcion ?? DBNull.Value);

                    var newId = (int)await command.ExecuteScalarAsync();

                    try
                    {
                        var detallesArea = $"Se creó el área '{data.NombreArea}' (ID: {newId}).";
                        await BitacoraHelper.RegistrarAccionAsync(
                            _dbConnection,
                            _logger,
                            User,
                            BitacoraConstantes.Modulos.Areas,
                            BitacoraConstantes.Acciones.Creacion,
                            detallesArea
                        );
                    }
                    catch (Exception exBit)
                    {
                        _logger.LogError(exBit, "Error al registrar Bitácora de creación de Área Id={Id}", newId);
                    }

                    return new JsonResult(new { success = true, id = newId, text = data.NombreArea });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear área '{NombreArea}'", data.NombreArea);
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await CargarAreasDisponibles();
                return Page();
            }

            var esCreacion = (Departamento.Id == 0);

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    await using (var transaction = connection.BeginTransaction())
                    {
                        string sql;
                        if (Departamento.Id == 0)
                        {
                            sql = "INSERT INTO DepartamentosEmpresa (NombreDepartamento, Descripcion) VALUES (@Nombre, @Descripcion); SELECT SCOPE_IDENTITY();";
                        }
                        else
                        {
                            sql = "UPDATE DepartamentosEmpresa SET NombreDepartamento = @Nombre, Descripcion = @Descripcion WHERE id_DE = @Id";
                        }

                        using (var cmd = new SqlCommand(sql, connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@Nombre", Departamento.Nombre);
                            cmd.Parameters.AddWithValue("@Descripcion", Departamento.Descripcion ?? (object)DBNull.Value);

                            if (Departamento.Id == 0)
                            {
                                Departamento.Id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                            }
                            else
                            {
                                cmd.Parameters.AddWithValue("@Id", Departamento.Id);
                                await cmd.ExecuteNonQueryAsync();
                            }
                        }

                        string sqlDeleteAreas = "DELETE FROM DepartamentosAreas WHERE id_DE = @Id";
                        using (var cmdDelete = new SqlCommand(sqlDeleteAreas, connection, transaction))
                        {
                            cmdDelete.Parameters.AddWithValue("@Id", Departamento.Id);
                            await cmdDelete.ExecuteNonQueryAsync();
                        }

                        string sqlInsertArea = "INSERT INTO DepartamentosAreas (id_DE, id_area) VALUES (@IdDE, @IdArea)";
                        foreach (var area in Areas.Where(a => a.Seleccionada))
                        {
                            using (var cmdInsert = new SqlCommand(sqlInsertArea, connection, transaction))
                            {
                                cmdInsert.Parameters.AddWithValue("@IdDE", Departamento.Id);
                                cmdInsert.Parameters.AddWithValue("@IdArea", area.Id);
                                await cmdInsert.ExecuteNonQueryAsync();
                            }
                        }

                        await transaction.CommitAsync();

                        try
                        {
                            var detallesDep = esCreacion
                                ? $"Se creó el departamento '{Departamento.Nombre}' (ID: {Departamento.Id})."
                                : $"Se modificó el departamento '{Departamento.Nombre}' (ID: {Departamento.Id}).";

                            await BitacoraHelper.RegistrarAccionAsync(
                                _dbConnection,
                                _logger,
                                User,
                                BitacoraConstantes.Modulos.Departamentos,
                                esCreacion ? BitacoraConstantes.Acciones.Creacion : BitacoraConstantes.Acciones.Modificacion,
                                detallesDep
                            );
                        }
                        catch (Exception exBit)
                        {
                            _logger.LogError(exBit, "Error al registrar Bitácora de {Op} de Departamento Id={Id}",
                                esCreacion ? "creación" : "modificación", Departamento.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar el departamento Id={Id}", Departamento.Id);
                ModelState.AddModelError("", $"Error al guardar el departamento: {ex.Message}");
                await CargarAreasDisponibles();
                return Page();
            }

            return RedirectToPage("Index");
        }

        private async Task CargarAreasDisponibles()
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string sql = "SELECT id_area, NombreArea FROM Areas ORDER BY NombreArea";
                    using (var cmd = new SqlCommand(sql, connection))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                Areas.Add(new AreaCheckbox
                                {
                                    Id = reader.GetInt32(0),
                                    Nombre = reader.GetString(1),
                                    Seleccionada = false
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar áreas");
                ModelState.AddModelError("", $"Error al cargar áreas: {ex.Message}");
            }
        }

        private async Task CargarDepartamentoExistente(int id)
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string sqlDepartamento = "SELECT id_DE, NombreDepartamento, Descripcion FROM DepartamentosEmpresa WHERE id_DE = @Id";
                    using (var cmd = new SqlCommand(sqlDepartamento, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                Departamento.Id = reader.GetInt32(0);
                                Departamento.Nombre = reader.GetString(1);
                                Departamento.Descripcion = reader.IsDBNull(2) ? null : reader.GetString(2);
                            }
                        }
                    }

                    string sqlAreas = "SELECT id_area FROM DepartamentosAreas WHERE id_DE = @Id";
                    using (var cmd = new SqlCommand(sqlAreas, connection))
                    {
                        cmd.Parameters.AddWithValue("@Id", id);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            var areasAsociadasIds = new HashSet<int>();
                            while (await reader.ReadAsync())
                            {
                                areasAsociadasIds.Add(reader.GetInt32(0));
                            }
                            foreach (var area in Areas)
                            {
                                if (areasAsociadasIds.Contains(area.Id))
                                {
                                    area.Seleccionada = true;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cargar departamento Id={Id}", id);
                ModelState.AddModelError("", $"Error al cargar departamento: {ex.Message}");
            }
        }

        public class DepartamentoViewModel
        {
            public int Id { get; set; }
            [Required(ErrorMessage = "El nombre del departamento es obligatorio.")]
            [StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
            public string Nombre { get; set; }
            [StringLength(255, ErrorMessage = "Máximo 255 caracteres.")]
            public string? Descripcion { get; set; }
        }

        public class AreaCheckbox
        {
            public int Id { get; set; }
            public string Nombre { get; set; }
            public bool Seleccionada { get; set; }
        }

        public class AddAreaRequest
        {
            public string NombreArea { get; set; }
            public string Descripcion { get; set; }
        }
    }
}
