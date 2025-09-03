using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System; // Agregado por si acaso
using System.Collections.Generic; // Agregado por si acaso
using Microsoft.Extensions.Logging; // Agregado por si acaso


public class IndexModel : PageModel
{
    private readonly ConexionBDD _dbConnection;
    private readonly ILogger<IndexModel> _logger;

    [BindProperty]
    public string Username { get; set; }

    [BindProperty]
    public string Password { get; set; }

    [TempData]
    public string ErrorMessage { get; set; }

    public IndexModel(ConexionBDD dbConnection, ILogger<IndexModel> logger)
    {
        _dbConnection = dbConnection;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        _logger.LogInformation($"Intento de login para: {Username}");

        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Usuario y contraseña son requeridos";
            return Page();
        }

        try
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"SELECT u.id_usuario, u.Username, u.Password, rs.NombreRol 
                            FROM Usuarios u
                            INNER JOIN RolesSistema rs ON u.id_rol_sistema = rs.id_rol_sistema
                            WHERE u.Username = @Username AND u.Password = @Password";

                var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@Username", Username);
                command.Parameters.AddWithValue("@Password", Password);
                command.CommandTimeout = 15;

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (reader.Read())
                    {
                        var claims = new List<Claim>
                        {
                            new Claim(ClaimTypes.Name, Username),
                            new Claim(ClaimTypes.NameIdentifier, reader["id_usuario"].ToString()),
                            new Claim(ClaimTypes.Role, reader["NombreRol"].ToString()),
                            // --- LÍNEA CORREGIDA ---
                            new Claim("id_usuario", reader["id_usuario"].ToString())
                        };

                        var claimsIdentity = new ClaimsIdentity(
                            claims, CookieAuthenticationDefaults.AuthenticationScheme);

                        await HttpContext.SignInAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme,
                            new ClaimsPrincipal(claimsIdentity));

                        _logger.LogInformation("Autenticación exitosa");
                        return RedirectToPage("/Principal");
                    }
                }

                ErrorMessage = "Usuario o contraseña incorrectos";
                _logger.LogWarning("Credenciales incorrectas");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "Error al procesar la solicitud";
            _logger.LogError(ex, "Error en el login");
        }

        return Page();
    }
}