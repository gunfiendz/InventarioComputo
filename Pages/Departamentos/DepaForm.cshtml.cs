using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace InventarioComputo.Pages.Departamentos
{
    public class DepaFormModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;

        [BindProperty]
        public DepartamentoViewModel Departamento { get; set; } = new DepartamentoViewModel();

        [BindProperty]
        public List<AreaCheckbox> Areas { get; set; } = new List<AreaCheckbox>();

        public string Modo { get; set; } = "Crear";

        public DepaFormModel(ConexionBDD dbConnection)
        {
            _dbConnection = dbConnection;
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

        private async Task CargarAreasDisponibles()
        {
            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string sql = "SELECT id_area, NombreArea FROM Areas";
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
                            while (await reader.ReadAsync())
                            {
                                int idArea = reader.GetInt32(0);
                                var area = Areas.FirstOrDefault(a => a.Id == idArea);
                                if (area != null)
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
                ModelState.AddModelError("", $"Error al cargar departamento: {ex.Message}");
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await CargarAreasDisponibles();
                return Page();
            }

            try
            {
                using (var connection = await _dbConnection.GetConnectionAsync())
                {
                    string sql;
                    if (Departamento.Id == 0) // Nuevo departamento
                    {
                        sql = "INSERT INTO DepartamentosEmpresa (NombreDepartamento, Descripcion) VALUES (@Nombre, @Descripcion); SELECT SCOPE_IDENTITY();";
                    }
                    else // Editar departamento
                    {
                        sql = "UPDATE DepartamentosEmpresa SET NombreDepartamento = @Nombre, Descripcion = @Descripcion WHERE id_DE = @Id";
                    }

                    using (var cmd = new SqlCommand(sql, connection))
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

                    // Actualizar áreas
                    string sqlDeleteAreas = "DELETE FROM DepartamentosAreas WHERE id_DE = @Id";
                    using (var cmdDelete = new SqlCommand(sqlDeleteAreas, connection))
                    {
                        cmdDelete.Parameters.AddWithValue("@Id", Departamento.Id);
                        await cmdDelete.ExecuteNonQueryAsync();
                    }

                    string sqlInsertArea = "INSERT INTO DepartamentosAreas (id_DE, id_area) VALUES (@IdDE, @IdArea)";
                    foreach (var area in Areas)
                    {
                        if (area.Seleccionada)
                        {
                            using (var cmdInsert = new SqlCommand(sqlInsertArea, connection))
                            {
                                cmdInsert.Parameters.AddWithValue("@IdDE", Departamento.Id);
                                cmdInsert.Parameters.AddWithValue("@IdArea", area.Id);
                                await cmdInsert.ExecuteNonQueryAsync();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error al guardar el departamento: {ex.Message}");
                await CargarAreasDisponibles();
                return Page();
            }

            return RedirectToPage("Index");
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
    }
}