using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using InventarioComputo.Security;
using InventarioComputo.Data;     

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
        _logger.LogInformation("Intento de login para: {Username}", Username);

        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Usuario y contraseña son requeridos";
            return Page();
        }

        try
        {
            using (var connection = await _dbConnection.GetConnectionAsync())
            {
                var query = @"
SELECT u.id_usuario, u.Username, u.Password, rs.NombreRol 
FROM Usuarios u
INNER JOIN RolesSistema rs ON u.id_rol_sistema = rs.id_rol_sistema
WHERE u.Username = @Username";

                using var command = new SqlCommand(query, connection);
                // No alteramos el Username, lo pasamos tal cual
                command.Parameters.AddWithValue("@Username", Username);
                command.CommandTimeout = 15;

                using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow))
                {
                    if (!await reader.ReadAsync())
                    {
                        ErrorMessage = "Usuario o contraseña incorrectos";
                        _logger.LogWarning("Usuario no encontrado: {Username}", Username);
                        return Page();
                    }

                    var idUsuario = Convert.ToInt32(reader["id_usuario"]);
                    var nombreRol = reader["NombreRol"]?.ToString() ?? "";
                    var storedHash = reader["Password"]?.ToString() ?? "";

                    // Detectar si es hash PBKDF2 
                    bool esHash = storedHash.Split('.', 3).Length == 3;

                    // === COMPARACIÓN ESTRICTA (case-sensitive) ===
                    bool credencialesOk = esHash
                        ? PasswordHasher.Verify(Password, storedHash) // PBKDF2: siempre case-sensitive
                        : string.Equals(Password, storedHash, StringComparison.Ordinal); // LEGADO: case-sensitive exacto

                    if (!credencialesOk)
                    {
                        ErrorMessage = "Usuario o contraseña incorrectos";
                        _logger.LogWarning("Password inválido para {Username}", Username);
                        return Page();
                    }

                    // Si era texto plano, migrar a hash
                    if (!esHash)
                    {
                        try
                        {
                            await reader.CloseAsync();

                            var nuevoHash = PasswordHasher.Hash(Password);
                            using var upd = new SqlCommand(
                                "UPDATE Usuarios SET Password = @hash, FechaPassword = GETDATE() WHERE id_usuario = @id",
                                connection);
                            upd.Parameters.AddWithValue("@hash", nuevoHash);
                            upd.Parameters.AddWithValue("@id", idUsuario);
                            await upd.ExecuteNonQueryAsync();
                            _logger.LogInformation("Password migrado a hash para usuario {Username}", Username);
                        }
                        catch (Exception exMig)
                        {
                            _logger.LogError(exMig, "Error migrando password a hash para {Username}", Username);
                        }
                    }
                    else
                    {
                        await reader.CloseAsync();
                    }

                    // Tocar último acceso
                    using (var cmdTouch = new SqlCommand(
                        "UPDATE Usuarios SET UltimoAcceso = GETDATE() WHERE id_usuario = @Id", connection))
                    {
                        cmdTouch.Parameters.AddWithValue("@Id", idUsuario);
                        await cmdTouch.ExecuteNonQueryAsync();
                    }

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, Username),
                        new Claim(ClaimTypes.NameIdentifier, idUsuario.ToString()),
                        new Claim(ClaimTypes.Role, nombreRol),
                        new Claim("id_usuario", idUsuario.ToString())
                    };

                    var claimsIdentity = new ClaimsIdentity(
                        claims, CookieAuthenticationDefaults.AuthenticationScheme);

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity));

                    _logger.LogInformation("Autenticación exitosa");

                    // Bitácora
                    var userPrincipal = new ClaimsPrincipal(claimsIdentity);
                    string detalles = $"El usuario '{Username}' ha iniciado sesión exitosamente.";
                    await InventarioComputo.Data.BitacoraHelper.RegistrarAccionAsync(
                        _dbConnection, _logger, userPrincipal,
                        InventarioComputo.Data.BitacoraConstantes.Modulos.Autenticacion,
                        InventarioComputo.Data.BitacoraConstantes.Acciones.Login,
                        detalles
                    );

                    return RedirectToPage("/Principal");
                }
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
