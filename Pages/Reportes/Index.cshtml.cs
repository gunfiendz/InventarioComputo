using AspNetCore.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;

using InventarioComputo.Data; // Ajusta si tu ConexionBDD está en otro namespace

namespace InventarioComputo.Pages.Reportes
{
    public class ReportesModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<ReportesModel> _logger;
        private readonly IWebHostEnvironment _env;

        public ReportesModel(ConexionBDD dbConnection, ILogger<ReportesModel> logger, IWebHostEnvironment env)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _env = env;

            // Necesario para algunas fuentes/encodings en RDLC
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        // ====== Filtros (GET) ======
        [BindProperty(SupportsGet = true)] public string Seccion { get; set; }           // ActivosFijos | Asignaciones | Bitacora | Empleados | Mantenimientos | Usuarios
        [BindProperty(SupportsGet = true)] public int? IdDepartamento { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdPerfil { get; set; }
        [BindProperty(SupportsGet = true)] public string Estado { get; set; }
        [BindProperty(SupportsGet = true)] public string TextoBusqueda { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? FechaInicio { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? FechaFin { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdEquipo { get; set; }
        [BindProperty(SupportsGet = true)] public int? IdTecnico { get; set; }

        // ====== Combos ======
        public List<ItemSimple> Departamentos { get; set; } = new();
        public List<ItemSimple> Perfiles { get; set; } = new();
        public List<ItemSimple> EstadosLista { get; set; } = new();
        public List<EquipoItem> Equipos { get; set; } = new();
        public List<ItemSimple> Tecnicos { get; set; } = new();

        public async Task OnGet()
        {
            using var conn = await _dbConnection.GetConnectionAsync();
            await CargarCombosAsync(conn);
        }

        // -------------------------
        // Parámetros de reporte
        // -------------------------
        private Dictionary<string, string> GetReportParameters()
        {
            // Usuario autenticado (o valor por defecto)
            var usuario = User?.Identity?.IsAuthenticated == true
                ? (User.Identity?.Name ?? "Usuario")
                : "Usuario";

            // Fecha/Hora local (Guatemala). Si no está la zona, usamos la local.
            string fechaHora;
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time"); // Guatemala (Windows)
                var now = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
                fechaHora = now.ToString("dd/MM/yyyy HH:mm"); // Fecha + Hora:Min
            }
            catch
            {
                fechaHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
            }

            return new Dictionary<string, string>
            {
                ["Usuario"] = usuario,
                ["FechaHora"] = fechaHora
            };
        }

        // =========================
        // Handler: Descargar (PDF/Excel/Word)
        // =========================
        public async Task<IActionResult> OnGetGenerarAsync(string formato = "PDF")
        {
            // Cargar combos para que el Index siga poblado al volver
            using (var conn = await _dbConnection.GetConnectionAsync())
                await CargarCombosAsync(conn);

            var rdlcName = Seccion switch
            {
                "ActivosFijos" => "ActivosFijos.rdlc",
                "Asignaciones" => "Asignaciones.rdlc",
                "Bitacora" => "Bitacora.rdlc",
                "Empleados" => "Empleados.rdlc",
                "Mantenimientos" => "Mantenimientos.rdlc",
                "Usuarios" => "Usuarios.rdlc",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(rdlcName))
            {
                TempData["Error"] = "Sección no válida.";
                return RedirectToPage("./Index");
            }

            var rdlcPath = Path.Combine(_env.ContentRootPath, "Pages", "Reportes", rdlcName);
            if (!System.IO.File.Exists(rdlcPath))
            {
                TempData["Error"] = $"No se encontró {rdlcName} en Pages/Reportes. Verifica 'Copy to Output Directory'.";
                return RedirectToPage("./Index");
            }

            try
            {
                var (dataSetName, dataSource) = await ObtenerDatosReporteAsync(
                    Seccion, IdDepartamento, IdPerfil, Estado, TextoBusqueda,
                    FechaInicio, FechaFin, IdEquipo, IdTecnico);

                var report = new LocalReport(rdlcPath);
                report.AddDataSource(dataSetName, dataSource);

                var parameters = GetReportParameters();

                formato = (formato ?? "PDF").ToUpperInvariant();
                var renderType = formato switch
                {
                    "EXCELOPENXML" => RenderType.ExcelOpenXml,
                    "WORDOPENXML" => RenderType.WordOpenXml,
                    _ => RenderType.Pdf
                };

                var result = report.Execute(renderType, 1, parameters, null);

                var (mime, ext) = renderType switch
                {
                    RenderType.ExcelOpenXml => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
                    RenderType.WordOpenXml => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"),
                    _ => ("application/pdf", "pdf")
                };

                var fileName = $"{Seccion}_{DateTime.Now:yyyyMMddHHmm}.{ext}";
                return File(result.MainStream, mime, fileName); // descarga
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando RDLC local para {Seccion}", Seccion);
                TempData["Error"] = "No fue posible generar el reporte.";
                return RedirectToPage("./Index");
            }
        }

        // =========================
        // Handler: Previsualizar (PDF inline para iframe)
        // =========================
        public async Task<IActionResult> OnGetPreviewAsync()
        {
            if (string.IsNullOrWhiteSpace(Seccion))
                return BadRequest("Seleccione una sección para previsualizar.");

            var rdlcName = Seccion switch
            {
                "ActivosFijos" => "ActivosFijos.rdlc",
                "Asignaciones" => "Asignaciones.rdlc",
                "Bitacora" => "Bitacora.rdlc",
                "Empleados" => "Empleados.rdlc",
                "Mantenimientos" => "Mantenimientos.rdlc",
                "Usuarios" => "Usuarios.rdlc",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(rdlcName))
                return BadRequest("Sección no válida.");

            var rdlcPath = Path.Combine(_env.ContentRootPath, "Pages", "Reportes", rdlcName);
            if (!System.IO.File.Exists(rdlcPath))
                return BadRequest($"No se encontró {rdlcName} en Pages/Reportes.");

            try
            {
                var (dataSetName, dataSource) = await ObtenerDatosReporteAsync(
                    Seccion, IdDepartamento, IdPerfil, Estado, TextoBusqueda,
                    FechaInicio, FechaFin, IdEquipo, IdTecnico);

                var report = new LocalReport(rdlcPath);
                report.AddDataSource(dataSetName, dataSource);

                var parameters = GetReportParameters();

                var result = report.Execute(RenderType.Pdf, 1, parameters, null);
                Response.Headers["content-disposition"] = "inline; filename=Preview.pdf";
                return File(result.MainStream, "application/pdf"); // inline para iframe
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Preview para {Seccion}", Seccion);
                return BadRequest("No fue posible generar la previsualización.");
            }
        }

        // =============================================================================
        // Obtención de datos por sección (SQL + parámetros). Retorna (DataSetName, DataTable)
        // =============================================================================
        private async Task<(string dataSetName, DataTable dataSource)> ObtenerDatosReporteAsync(
            string seccion,
            int? idDepartamento, int? idPerfil, string estado, string texto,
            DateTime? fechaInicio, DateTime? fechaFin, int? idEquipo, int? idTecnico)
        {
            using var conn = await _dbConnection.GetConnectionAsync();

            switch (seccion)
            {
                case "ActivosFijos":
                    {
                        const string DS = "DsActivosFijos";
                        var dt = new DataTable();

                        var sql = @"
                        SELECT 
                            AF.id_activofijo,
                            AF.NumeroSerie,
                            AF.EtiquetaInv,
                            AF.FechaCompra,
                            CONCAT(TE.TipoEquipo, N' / ', MAR.Marca, N' ', M.Modelo) AS Equipo,
                            ISNULL(
                                STRING_AGG(
                                    CONCAT(N'• ', C.Caracteristica, N': ', COALESCE(CM.Valor, N'')),
                                    CHAR(13) + CHAR(10)
                                ) WITHIN GROUP (ORDER BY C.Caracteristica),
                                N''
                            ) AS Caracteristicas
                        FROM dbo.ActivosFijos AF
                        JOIN dbo.Perfiles P       ON AF.id_perfil = P.id_perfil
                        JOIN dbo.Modelos M        ON P.id_modelo = M.id_modelo
                        JOIN dbo.Marcas MAR       ON M.id_marca = MAR.id_marca
                        JOIN dbo.TiposEquipos TE  ON M.id_tipoequipo = TE.id_tipoequipo
                        LEFT JOIN dbo.CaracteristicasModelos CM ON P.id_perfil = CM.id_perfil
                        LEFT JOIN dbo.Caracteristicas C        ON CM.id_caracteristica = C.id_caracteristica
                        WHERE (@IdPerfil IS NULL OR P.id_perfil = @IdPerfil)
                          AND (@Texto    IS NULL OR (AF.EtiquetaInv LIKE '%'+@Texto+'%' OR AF.NumeroSerie LIKE '%'+@Texto+'%'))
                          AND (@IdDepartamento IS NULL OR EXISTS(
                                SELECT 1 
                                FROM dbo.EmpleadosEquipos EE 
                                WHERE EE.id_activofijo = AF.id_activofijo 
                                  AND EE.ResponsableActual = 1 
                                  AND EE.id_DE = @IdDepartamento
                          ))
                        GROUP BY 
                            AF.id_activofijo,
                            AF.NumeroSerie,
                            AF.EtiquetaInv,
                            AF.FechaCompra,
                            TE.TipoEquipo,
                            MAR.Marca,
                            M.Modelo
                        ORDER BY AF.EtiquetaInv;";

                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@IdDepartamento", (object?)idDepartamento ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IdPerfil", (object?)idPerfil ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);

                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);

                        return (DS, dt);
                    }

                case "Asignaciones":
                    {
                        const string DS = "DsAsignaciones"; // DataSetName de tu Asignaciones.rdlc
                        var dt = new DataTable();

                        var sql = @"
                        SELECT 
                            EE.id_empleadoequipo,
                            EE.FechaAsignacion,
                            EE.FechaRetiro,
                            EE.ResponsableActual,
                            AF.EtiquetaInv,
                            EMP.Nombre AS Empleado,
                            DE.NombreDepartamento
                        FROM dbo.EmpleadosEquipos EE
                        JOIN dbo.ActivosFijos AF         ON EE.id_activofijo = AF.id_activofijo
                        JOIN dbo.Empleados EMP           ON EE.id_empleado = EMP.id_empleado
                        JOIN dbo.DepartamentosEmpresa DE ON EE.id_DE = DE.id_DE
                        WHERE (@IdDepartamento IS NULL OR EE.id_DE = @IdDepartamento)
                        ORDER BY EE.FechaAsignacion DESC;";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@IdDepartamento", (object?)idDepartamento ?? DBNull.Value);
                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);
                        return (DS, dt);
                    }

                case "Bitacora":
                    {
                        const string DS = "DsBitacora"; // DataSetName de tu Bitacora.rdlc
                        var dt = new DataTable();

                        var sql = @"
                        SELECT 
                            B.id_evento, B.FechaHora, M.Modulo, A.Accion, U.Username, B.Detalles
                        FROM dbo.Bitacora B
                        JOIN dbo.Modulos M ON B.id_modulo = M.id_modulo
                        JOIN dbo.Acciones A ON B.id_accion = A.id_accion
                        JOIN dbo.Usuarios U ON B.id_usuario = U.id_usuario
                        WHERE (@Desde IS NULL OR B.FechaHora >= @Desde)
                          AND (@Hasta IS NULL OR B.FechaHora < DATEADD(day,1,@Hasta))
                          AND (@Texto IS NULL OR (B.Detalles LIKE '%'+@Texto+'%' OR U.Username LIKE '%'+@Texto+'%'))
                        ORDER BY B.FechaHora DESC;";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@Desde", (object?)fechaInicio ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Hasta", (object?)fechaFin ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);
                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);
                        return (DS, dt);
                    }

                case "Empleados":
                    {
                        const string DS = "DsEmpleados"; // DataSetName de tu Empleados.rdlc
                        var dt = new DataTable();

                        var sql = @"
                        SELECT 
                            E.id_empleado, E.Nombre, P.NombrePuesto, DE.NombreDepartamento, E.Email, E.Telefono
                        FROM dbo.Empleados E
                        JOIN dbo.Puestos P               ON E.id_puesto = P.id_puesto
                        JOIN dbo.DepartamentosEmpresa DE ON E.id_DE = DE.id_DE
                        WHERE (@IdDepartamento IS NULL OR E.id_DE = @IdDepartamento)
                          AND (@Texto IS NULL OR (E.Nombre LIKE '%'+@Texto+'%' OR E.Email LIKE '%'+@Texto+'%'))
                        ORDER BY E.Nombre;";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@IdDepartamento", (object?)idDepartamento ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);
                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);
                        return (DS, dt);
                    }

                case "Mantenimientos":
                    {
                        const string DS = "DsMantenimientos"; // DataSetName de tu Mantenimientos.rdlc
                        var dt = new DataTable();

                        var sql = @"
                        SELECT 
                            ME.id_mantenimientoequipo,
                            ME.Fecha,
                            ME.Descripcion,
                            ME.Costo,
                            MT.Tipo AS TipoMantenimiento,
                            AF.EtiquetaInv,
                            EMP.Nombre AS Tecnico
                        FROM dbo.MantenimientosEquipos ME
                        JOIN dbo.Mantenimientos MT ON ME.id_mantenimiento = MT.id_mantenimiento
                        JOIN dbo.ActivosFijos AF   ON ME.id_activofijo = AF.id_activofijo
                        LEFT JOIN dbo.Empleados EMP ON ME.id_empleado = EMP.id_empleado
                        WHERE (@Desde IS NULL OR ME.Fecha >= @Desde)
                          AND (@Hasta IS NULL OR ME.Fecha <= @Hasta)
                          AND (@IdEquipo IS NULL OR ME.id_activofijo = @IdEquipo)
                          AND (@IdTecnico IS NULL OR ME.id_empleado = @IdTecnico)
                        ORDER BY ME.Fecha DESC;";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@Desde", (object?)fechaInicio ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Hasta", (object?)fechaFin ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IdEquipo", (object?)idEquipo ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IdTecnico", (object?)idTecnico ?? DBNull.Value);
                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);
                        return (DS, dt);
                    }

                case "Usuarios":
                    {
                        const string DS = "DsUsuarios"; // DataSetName de tu Usuarios.rdlc
                        var dt = new DataTable();

                        var sql = @"
SELECT 
    U.id_usuario,
    U.Username,
    U.FechaPassword,
    E.Nombre AS Empleado,
    RS.NombreRol
FROM dbo.Usuarios U
LEFT JOIN dbo.Empleados E ON U.id_empleado = E.id_empleado
JOIN dbo.RolesSistema RS   ON U.id_rol_sistema = RS.id_rol_sistema
WHERE (@Texto IS NULL OR (U.Username LIKE '%'+@Texto+'%' OR E.Nombre LIKE '%'+@Texto+'%'))
ORDER BY U.Username;";
                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);
                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);
                        return (DS, dt);
                    }
            }

            throw new InvalidOperationException("Sección no implementada.");
        }

        // =============================================================================
        // Carga de combos (Departamentos, Perfiles, Estados, Equipos, Técnicos)
        // =============================================================================
        private async Task CargarCombosAsync(SqlConnection conn)
        {
            // Departamentos
            using (var cmd = new SqlCommand("SELECT id_DE, NombreDepartamento FROM DepartamentosEmpresa ORDER BY NombreDepartamento", conn))
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    Departamentos.Add(new ItemSimple { Id = rd.GetInt32(0), Nombre = rd.GetString(1) });

            // Perfiles
            using (var cmd = new SqlCommand("SELECT id_perfil, NombrePerfil FROM Perfiles ORDER BY NombrePerfil", conn))
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    Perfiles.Add(new ItemSimple { Id = rd.GetInt32(0), Nombre = rd.GetString(1) });

            // Estados
            using (var cmd = new SqlCommand("SELECT id_estado, Estado FROM Estados ORDER BY Estado", conn))
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    EstadosLista.Add(new ItemSimple { Id = rd.GetInt32(0), Nombre = rd.GetString(1) });

            // Equipos
            using (var cmd = new SqlCommand("SELECT id_activofijo, EtiquetaInv, ISNULL(NumeroSerie,'') FROM ActivosFijos ORDER BY EtiquetaInv", conn))
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    Equipos.Add(new EquipoItem
                    {
                        Id = rd.GetInt32(0),
                        EtiquetaInv = rd.GetString(1),
                        NumeroSerie = rd.GetString(2)
                    });

            // Técnicos (empleados)
            using (var cmd = new SqlCommand("SELECT id_empleado, Nombre FROM Empleados ORDER BY Nombre", conn))
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    Tecnicos.Add(new ItemSimple { Id = rd.GetInt32(0), Nombre = rd.GetString(1) });
        }

        // ====== ViewModels de combos ======
        public class ItemSimple { public int Id { get; set; } public string Nombre { get; set; } }
        public class EquipoItem { public int Id { get; set; } public string EtiquetaInv { get; set; } public string NumeroSerie { get; set; } }
    }
}
