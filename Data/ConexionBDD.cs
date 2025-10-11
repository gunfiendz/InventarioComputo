using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public class ConexionBDD
{
    private readonly IConfiguration _configuration;
    private string _connectionString;             
    private readonly object _lock = new();        

    public ConexionBDD(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("No se encontró la cadena 'DefaultConnection'.");
    }

    public SqlConnection GetConnection()
    {
        string cs;
        lock (_lock) { cs = _connectionString; }

        var connection = new SqlConnection(cs);
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Timeout de 30 segundos
            await connection.OpenAsync(cts.Token);

            if (connection.State != ConnectionState.Open)
            {
                throw new Exception("No se pudo establecer la conexión");
            }

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
}
