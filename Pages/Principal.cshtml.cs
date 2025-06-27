using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

public class PrincipalModel : PageModel
{
    private readonly ConexionBDD _dbConnection;

    public int TotalEquipos { get; set; }
    public int TotalSucursales { get; set; }
    public int MantenimientosPendientes { get; set; }
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
                var cmdEquipos = new SqlCommand("SELECT COUNT(*) FROM ActivosFijos", connection);
                TotalEquipos = (int)await cmdEquipos.ExecuteScalarAsync();

                var cmdSucursales = new SqlCommand("SELECT COUNT(*) FROM Sucursales", connection);
                TotalSucursales = (int)await cmdSucursales.ExecuteScalarAsync();

                var cmdMantenimientos = new SqlCommand(
                    "SELECT COUNT(*) FROM MantenimientosEquipos WHERE Fecha >= DATEADD(day, -30, GETDATE())",
                    connection);
                MantenimientosPendientes = (int)await cmdMantenimientos.ExecuteScalarAsync();
            }
        }
        catch (Exception ex)
        {
            TotalEquipos = 0;
            TotalSucursales = 0;
            MantenimientosPendientes = 0;
        }
    }
}