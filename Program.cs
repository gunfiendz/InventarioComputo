using Microsoft.AspNetCore.Authentication.Cookies;
using InventarioComputo.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddScoped<ConexionBDD>();

builder.Services.AddMemoryCache();
builder.Services.AddScoped<PermisosService>();

// Configuración simplificada de autenticación
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Index";
        //EXPIRACION DE SESION
        //options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    });

builder.Services.AddAuthorization();
// En ConfigureServices (Startup.cs) o en builder.Services (Program.cs)
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Configuración del pipeline...
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);


app.Run();