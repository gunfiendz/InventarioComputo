using Microsoft.Data.SqlClient;
using System.Data;

public class ConexionBDD
{
    private readonly IConfiguration _configuration;
    private readonly string _connectionString;

    public ConexionBDD(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection");
    }

    public SqlConnection GetConnection()
    {
        var connection = new SqlConnection(_connectionString);
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
        var connection = new SqlConnection(_connectionString);
        try
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // Timeout de 30 segundos

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
}