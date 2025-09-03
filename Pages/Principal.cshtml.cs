using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic; // Asegúrate de tener este using
using System; // Y este también

public class PrincipalModel : PageModel
{
    private readonly ConexionBDD _dbConnection;

    // Propiedades existentes
    public int TotalActivosFijos { get; set; }
    public int TotalMantenimientos { get; set; }
    public int TotalEmpleados { get; set; }
    public int TotalEquiposRegistrados { get; set; }
    public int TotalDepartamentos { get; set; }
    public int TotalUsuarios { get; set; }
    public string NombreUsuario { get; set; }
    public string RolUsuario { get; set; }

    // --- NUEVAS PROPIEDADES PARA ESTADÍSTICAS ---
    public int ActivosPorVencer { get; set; }
    public List<DepartamentoEstadistica> TopDepartamentos { get; set; } = new List<DepartamentoEstadistica>();
    public int EquiposAsignados { get; set; }
    public int EquiposEnAlmacen { get; set; }
    public int MantenimientosUltimoMes { get; set; }


    public PrincipalModel(ConexionBDD dbConnection)
    {
        _dbConnection = dbConnection;
    }

    public async Task OnGetAsync()
    {
        // Obtener nombre y rol del usuario autenticado
        NombreUsuario = User.Identity.Name;
        RolUsuario = User.FindFirst(ClaimTypes.Role)?.Value;

        try
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                // Consulta para el total de activos fijos
                var cmdActivos = new SqlCommand("SELECT COUNT(*) FROM ActivosFijos", connection);
                TotalActivosFijos = (int)await cmdActivos.ExecuteScalarAsync();

                // Consulta para el total de mantenimientos
                var cmdMantenimientos = new SqlCommand("SELECT COUNT(*) FROM MantenimientosEquipos", connection);
                TotalMantenimientos = (int)await cmdMantenimientos.ExecuteScalarAsync();

                // Consulta para el total de empleados
                var cmdEmpleados = new SqlCommand("SELECT COUNT(*) FROM Empleados", connection);
                TotalEmpleados = (int)await cmdEmpleados.ExecuteScalarAsync();

                // Consulta para el total de equipos registrados (Perfiles)
                var cmdEquiposRegistrados = new SqlCommand("SELECT COUNT(*) FROM Perfiles", connection);
                TotalEquiposRegistrados = (int)await cmdEquiposRegistrados.ExecuteScalarAsync();

                // Consulta para el total de departamentos
                var cmdDepartamentos = new SqlCommand("SELECT COUNT(*) FROM DepartamentosEmpresa", connection);
                TotalDepartamentos = (int)await cmdDepartamentos.ExecuteScalarAsync();

                // Consulta para el total de usuarios
                var cmdUsuarios = new SqlCommand("SELECT COUNT(*) FROM Usuarios", connection);
                TotalUsuarios = (int)await cmdUsuarios.ExecuteScalarAsync();


                // --- NUEVAS CONSULTAS PARA ESTADÍSTICAS ---

                // 1. Activos con menos de 1 año de vida útil
                var cmdVidaUtil = new SqlCommand(
                    "SELECT COUNT(*) FROM ActivosFijos WHERE FechaFinVidaUtil IS NOT NULL AND FechaFinVidaUtil BETWEEN GETDATE() AND DATEADD(year, 1, GETDATE())",
                    connection);
                ActivosPorVencer = (int)await cmdVidaUtil.ExecuteScalarAsync();

                // 2. Top 5 Departamentos con más activos
                var cmdTopDepas = new SqlCommand(
                    @"SELECT TOP 5 d.NombreDepartamento, COUNT(ee.id_activofijo) AS Total 
                      FROM EmpleadosEquipos ee 
                      JOIN DepartamentosEmpresa d ON ee.id_DE = d.id_DE 
                      WHERE ee.ResponsableActual = 1 
                      GROUP BY d.NombreDepartamento 
                      ORDER BY Total DESC",
                    connection);
                using (var reader = await cmdTopDepas.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        TopDepartamentos.Add(new DepartamentoEstadistica
                        {
                            NombreDepartamento = reader.GetString(0),
                            CantidadEquipos = reader.GetInt32(1)
                        });
                    }
                }

                // 3. Equipos Asignados vs. En Almacén
                var cmdAsignados = new SqlCommand(
                    "SELECT COUNT(DISTINCT id_activofijo) FROM EmpleadosEquipos WHERE ResponsableActual = 1",
                    connection);
                EquiposAsignados = (int)await cmdAsignados.ExecuteScalarAsync();

                var cmdAlmacen = new SqlCommand(
                    "SELECT COUNT(*) FROM ActivosFijos WHERE id_estado = (SELECT id_estado FROM Estados WHERE Estado = 'En Almacén')",
                    connection);
                EquiposEnAlmacen = (int)await cmdAlmacen.ExecuteScalarAsync();

                // 4. Mantenimientos en los últimos 30 días
                var cmdMantenimientosMes = new SqlCommand(
                    "SELECT COUNT(*) FROM MantenimientosEquipos WHERE Fecha >= DATEADD(day, -30, GETDATE())",
                    connection);
                MantenimientosUltimoMes = (int)await cmdMantenimientosMes.ExecuteScalarAsync();
            }
        }
        catch (Exception ex)
        {
            // En caso de error, los valores se mantendrán en 0
            TotalActivosFijos = 0;
            TotalMantenimientos = 0;
            TotalEmpleados = 0;
            TotalEquiposRegistrados = 0;
            TotalDepartamentos = 0;
            TotalUsuarios = 0;
            // También para las nuevas estadísticas
            ActivosPorVencer = 0;
            EquiposAsignados = 0;
            EquiposEnAlmacen = 0;
            MantenimientosUltimoMes = 0;
        }
    }
}

// --- NUEVA CLASE AUXILIAR ---
public class DepartamentoEstadistica
{
    public string NombreDepartamento { get; set; }
    public int CantidadEquipos { get; set; }
}