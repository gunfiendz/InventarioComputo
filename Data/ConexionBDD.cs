using System;
using System.Data;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;              // IWebHostEnvironment
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public class ConexionBDD
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;   // ← para ubicar ContentRoot/App_Data
    private string _connectionString;            // ← editable en runtime
    private readonly object _lock = new();

    public ConexionBDD(IConfiguration configuration, IWebHostEnvironment env)
    {
        _configuration = configuration;
        _env = env;

        // 1) Valor base desde appsettings.json
        var fromConfig = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(fromConfig))
            throw new InvalidOperationException("No se encontró la cadena 'DefaultConnection'.");

        _connectionString = fromConfig;

        // 2) Si existe persistencia previa en App_Data/conn.json, la cargamos
        try
        {
            var persisted = LoadPersistedConnectionString();
            if (!string.IsNullOrWhiteSpace(persisted))
            {
                _connectionString = persisted!;
            }
        }
        catch
        {
            // Si falla la lectura, seguimos con la de appsettings.json
        }
    }

    // ======= Uso normal en toda la app =======
    public SqlConnection GetConnection()
    {
        string cs;
        lock (_lock) { cs = _connectionString; }

        var connection = new SqlConnection(cs);
        try
        {
            if (connection.State != ConnectionState.Open)
                connection.Open();
            return connection;
        }
        catch (SqlException ex)
        {
            Console.WriteLine($"Error de conexión: {ex.Message}");
            throw;
        }
    }

    public async Task<SqlConnection> GetConnectionAsync()
    {
        string cs;
        lock (_lock) { cs = _connectionString; }

        var connection = new SqlConnection(cs);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await connection.OpenAsync(cts.Token);

            if (connection.State != ConnectionState.Open)
                throw new Exception("No se pudo establecer la conexión");

            return connection;
        }
        catch (OperationCanceledException)
        {
            throw new Exception("Timeout al conectar a la base de datos");
        }
        catch (SqlException ex)
        {
            throw new Exception($"Error SQL: {ex.Message}");
        }
    }

    // ======= Runtime: cambia la cadena activa (en memoria) =======
    public Task UpdateConnectionStringAsync(string newConnectionString)
    {
        if (string.IsNullOrWhiteSpace(newConnectionString))
            throw new ArgumentException("Cadena de conexión inválida.", nameof(newConnectionString));

        lock (_lock)
        {
            _connectionString = newConnectionString;
        }
        return Task.CompletedTask;
    }

    // ======= Persistencia en App_Data/conn.json =======
    public async Task PersistConnectionStringAsync(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Cadena de conexión inválida.", nameof(connectionString));

        var path = GetStorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var doc = new
        {
            ConnectionStrings = new
            {
                DefaultConnection = connectionString
            }
        };

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private string? LoadPersistedConnectionString()
    {
        var path = GetStorePath();
        if (!File.Exists(path)) return null;

        using var fs = File.OpenRead(path);
        using var doc = JsonDocument.Parse(fs);
        if (doc.RootElement.TryGetProperty("ConnectionStrings", out var csNode) &&
            csNode.TryGetProperty("DefaultConnection", out var val) &&
            val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }
        return null;
    }

    private string GetStorePath()
    {
        // App_Data/conn.json en el ContentRoot (carpeta del proyecto publicado)
        return Path.Combine(_env.ContentRootPath, "App_Data", "conn.json");
    }

    // ======= Para precargar el modal =======
    public (string Servidor, string BaseDatos, bool IntegratedSecurity, bool TrustServerCertificate)? GetCurrentInfo()
    {
        try
        {
            string cs;
            lock (_lock) { cs = _connectionString; }

            var b = new SqlConnectionStringBuilder(cs);
            return (b.DataSource, b.InitialCatalog, b.IntegratedSecurity, b.TrustServerCertificate);
        }
        catch
        {
            return null;
        }
    }
}
