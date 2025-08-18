using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Threading.Tasks;

public class PrincipalModel : PageModel
{
    private readonly ConexionBDD _dbConnection;

    public int TotalActivosFijos { get; set; }
    public int TotalMantenimientos { get; set; }
    public int TotalEmpleados { get; set; }
    public int TotalEquiposRegistrados { get; set; }
    public int TotalDepartamentos { get; set; }
    public int TotalUsuarios { get; set; }

    public string NombreUsuario { get; set; }
    public string RolUsuario { get; set; }

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
        }
    }
}