using AspNetCore.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using InventarioComputo.Data;
using System.Xml.Linq;
using System.Linq;

namespace InventarioComputo.Pages.Reportes
{
    public class ReportesModel : PageModel
    {
        private readonly ConexionBDD _dbConnection;
        private readonly ILogger<ReportesModel> _logger;
        private readonly IWebHostEnvironment _env;

        private static readonly SemaphoreSlim _rdlcLock = new(1, 1);

        public ReportesModel(ConexionBDD dbConnection, ILogger<ReportesModel> logger, IWebHostEnvironment env)
        {
            _dbConnection = dbConnection;
            _logger = logger;
            _env = env;

            // Necesario para fuentes/encodings (RDLC)
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

        // Cambios solicitados
        [BindProperty(SupportsGet = true)] public int? IdModelo { get; set; }                 // AF: Modelo (en vez de Perfil)
        [BindProperty(SupportsGet = true)] public DateTime? FechaCompraInicio { get; set; }   // AF: Fecha Compra desde
        [BindProperty(SupportsGet = true)] public DateTime? FechaCompraFin { get; set; }      // AF: Fecha Compra hasta
        [BindProperty(SupportsGet = true)] public int? IdRol { get; set; }                    // Usuarios: Rol

        // ====== Combos ======
        public List<ItemSimple> Departamentos { get; set; } = new();
        public List<ItemSimple> Perfiles { get; set; } = new();
        public List<ItemSimple> EstadosLista { get; set; } = new();
        public List<EquipoItem> Equipos { get; set; } = new();
        public List<ItemSimple> Tecnicos { get; set; } = new();
        public List<ItemSimple> Modelos { get; set; } = new();  // AF
        public List<ItemSimple> Roles { get; set; } = new();    // Usuarios

        private static HashSet<string> GetRdlcFieldNames(string rdlcPath, string dataSetName)
        {
            var ns = XNamespace.Get("http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition");
            var doc = XDocument.Load(rdlcPath);

            var ds = doc
                .Descendants(ns + "DataSet")
                .FirstOrDefault(x => (string)x.Attribute("Name") == dataSetName);
            if (ds == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var fields = ds
                .Descendants(ns + "Field")
                .Select(f => (string)f.Attribute("Name"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return fields;
        }

        private static (List<string> faltantes, List<string> sobrantes) CompareColumns(
            DataTable dt,
            HashSet<string> rdlcFields)
        {
            var dtCols = dt.Columns.Cast<DataColumn>()
                .Select(c => c.ColumnName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var faltan = rdlcFields.Where(f => !dtCols.Contains(f)).ToList();
            var sobran = dtCols.Where(c => !rdlcFields.Contains(c)).ToList();
            return (faltan, sobran);
        }

        // Agrega placeholder para cualquier campo que el RDLC declare y no esté en el DataTable (evita crashes)
        private static void EnsureAllRdlcFieldsExist(DataTable dt, IEnumerable<string> rdlcFields, ILogger logger)
        {
            foreach (var f in rdlcFields)
            {
                if (!dt.Columns.Contains(f))
                {
                    var col = new DataColumn(f, typeof(string)) { AllowDBNull = true };
                    dt.Columns.Add(col);
                    foreach (DataRow r in dt.Rows) r[f] = DBNull.Value;
                    logger?.LogWarning("RDLC -> Columna faltante '{Col}' agregada (string/null) para evitar error de render.", f);
                }
            }
        }

        // Reordena columnas del DataTable al orden exacto que declara el RDLC (para motores que indexan por posición)
        private static void AlignColumnsToRdlcOrder(DataTable dt, IEnumerable<string> rdlcFields)
        {
            int pos = 0;
            foreach (var name in rdlcFields)
            {
                if (!dt.Columns.Contains(name)) continue;
                dt.Columns[name].SetOrdinal(pos++);
            }
        }

        public async Task OnGet()
        {
            using var conn = await _dbConnection.GetConnectionAsync();
            await CargarCombosAsync(conn);
        }

        // -------------------------
        // Parámetros de reporte (Usuario + FechaHora)
        // -------------------------
        private Dictionary<string, string> GetReportParameters()
        {
            var usuario = User?.Identity?.IsAuthenticated == true
                ? (User.Identity?.Name ?? "Usuario")
                : "Usuario";

            string fechaHora;
            try
            {
                // Guatemala/Honduras (Windows): Central America Standard Time
                var tz = TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time");
                var now = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
                fechaHora = now.ToString("dd/MM/yyyy HH:mm");
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
            _logger.LogInformation("Descarga solicitada: {Query}", Request.QueryString.ToString());

            // Mantener combos poblados al volver
            using (var conn = await _dbConnection.GetConnectionAsync())
                await CargarCombosAsync(conn);

            // ---- VALIDACIÓN 1: Seccion obligatoria ----
            if (string.IsNullOrWhiteSpace(Seccion))
            {
                if (Request.Query.TryGetValue("debug", out var dbg) && dbg == "1")
                    return Content("DEBUG: Falta el parámetro 'Seccion'. URL debe incluir ?Seccion=ActivosFijos|Asignaciones|Bitacora|Empleados|Mantenimientos|Usuarios",
                                   "text/plain; charset=utf-8");

                TempData["Error"] = "Sección no válida.";
                return RedirectToPage("./Index");
            }

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
                if (Request.Query.TryGetValue("debug", out var dbg2) && dbg2 == "1")
                    return Content($"DEBUG: Valor de Seccion no reconocido: '{Seccion}'", "text/plain; charset=utf-8");

                TempData["Error"] = "Sección no válida.";
                return RedirectToPage("./Index");
            }

            var rdlcPath = Path.Combine(_env.ContentRootPath, "Pages", "Reportes", rdlcName);
            if (!System.IO.File.Exists(rdlcPath))
            {
                if (Request.Query.TryGetValue("debug", out var dbg3) && dbg3 == "1")
                    return Content($"DEBUG: No se encontró el archivo RDLC en: {rdlcPath}\n" +
                                   "Verifica que el archivo exista y que su 'Copy to Output Directory' esté correcto.",
                                   "text/plain; charset=utf-8");

                TempData["Error"] = $"No se encontró {rdlcName} en Pages/Reportes. Verifica 'Copy to Output Directory'.";
                return RedirectToPage("./Index");
            }

            try
            {
                var (dataSetName, dataSource) = await ObtenerDatosReporteAsync(
                    Seccion, IdDepartamento, IdPerfil, Estado, TextoBusqueda,
                    FechaInicio, FechaFin, IdEquipo, IdTecnico,
                    idModelo: IdModelo, fechaCompraInicio: FechaCompraInicio, fechaCompraFin: FechaCompraFin, idRol: IdRol);

                _logger.LogInformation("RDLC {Seccion} -> DS {DS} con {Cols} cols, {Rows} filas",
                    Seccion, dataSetName, dataSource.Columns.Count, dataSource.Rows.Count);

                await _rdlcLock.WaitAsync();
                try
                {
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

                    // VALIDACIÓN RDLC vs DataTable (solo para depurar)
                    var rdlcFields = GetRdlcFieldNames(rdlcPath, dataSetName);
                    var (faltan, sobran) = CompareColumns(dataSource, rdlcFields);

                    if (faltan.Count > 0 || sobran.Count > 0)
                    {
                        var diag =
                            $"[DIAGNÓSTICO RDLC]\n" +
                            $"RDLC: {rdlcName}\n" +
                            $"DataSet RDLC: {dataSetName}\n" +
                            $"Columnas DataTable: {string.Join(", ", dataSource.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}\n" +
                            $"Campos RDLC: {string.Join(", ", rdlcFields)}\n\n" +
                            $"FALTAN en DataTable (usados por RDLC): {string.Join(", ", faltan)}\n" +
                            $"SOBRAN en DataTable (no definidos en RDLC): {string.Join(", ", sobran)}\n";

                        if (Request.Query.TryGetValue("debug", out var dbg) && dbg == "1")
                            return Content(diag, "text/plain; charset=utf-8");

                        _logger.LogError(diag);
                    }

                    // Asegurar campos y alinear orden de columnas al esperado por el RDLC
                    EnsureAllRdlcFieldsExist(dataSource, rdlcFields, _logger);
                    AlignColumnsToRdlcOrder(dataSource, rdlcFields);

                    var result = report.Execute(renderType, 1, parameters, null);

                    var (mime, ext) = renderType switch
                    {
                        RenderType.ExcelOpenXml => ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
                        RenderType.WordOpenXml => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"),
                        _ => ("application/pdf", "pdf")
                    };

                    var fileName = $"{Seccion}_{DateTime.Now:yyyyMMddHHmm}.{ext}";
                    return File(result.MainStream, mime, fileName);
                }
                finally
                {
                    _rdlcLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generando RDLC local para {Seccion}", Seccion);

                if (Request.Query.TryGetValue("debug", out var dbg) && dbg == "1")
                {
                    var msg = $"ERROR Generar: {ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
                    return Content(msg, "text/plain; charset=utf-8");
                }

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
                    FechaInicio, FechaFin, IdEquipo, IdTecnico,
                    idModelo: IdModelo, fechaCompraInicio: FechaCompraInicio, fechaCompraFin: FechaCompraFin, idRol: IdRol);

                await _rdlcLock.WaitAsync();
                try
                {
                    var report = new LocalReport(rdlcPath);
                    report.AddDataSource(dataSetName, dataSource);

                    var parameters = GetReportParameters();

                    // VALIDACIÓN RDLC vs DataTable (solo para depurar)
                    var rdlcFields = GetRdlcFieldNames(rdlcPath, dataSetName);
                    var (faltan, sobran) = CompareColumns(dataSource, rdlcFields);

                    if (faltan.Count > 0 || sobran.Count > 0)
                    {
                        var diag =
                            $"[DIAGNÓSTICO RDLC]\n" +
                            $"RDLC: {rdlcName}\n" +
                            $"DataSet RDLC: {dataSetName}\n" +
                            $"Columnas DataTable: {string.Join(", ", dataSource.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}\n" +
                            $"Campos RDLC: {string.Join(", ", rdlcFields)}\n\n" +
                            $"FALTAN en DataTable (usados por RDLC): {string.Join(", ", faltan)}\n" +
                            $"SOBRAN en DataTable (no definidos en RDLC): {string.Join(", ", sobran)}\n";

                        if (Request.Query.TryGetValue("debug", out var dbg) && dbg == "1")
                            return Content(diag, "text/plain; charset=utf-8");

                        _logger.LogError(diag);
                    }

                    // Asegurar campos y alinear orden de columnas al esperado por el RDLC
                    EnsureAllRdlcFieldsExist(dataSource, rdlcFields, _logger);
                    AlignColumnsToRdlcOrder(dataSource, rdlcFields);

                    var result = report.Execute(RenderType.Pdf, 1, parameters, null);
                    Response.Headers["content-disposition"] = "inline; filename=Preview.pdf";
                    return File(result.MainStream, "application/pdf");
                }
                finally
                {
                    _rdlcLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en Preview para {Seccion}", Seccion);
                return BadRequest("No fue posible generar la previsualización.");
            }
        }

        // =============================================================================
        // Obtención de datos por sección (SQL + parámetros) -> (DataSetName, DataTable)
        // =============================================================================
        private async Task<(string dataSetName, DataTable dataSource)> ObtenerDatosReporteAsync(
            string seccion,
            int? idDepartamento, int? idPerfil, string estado, string texto,
            DateTime? fechaInicio, DateTime? fechaFin, int? idEquipo, int? idTecnico,
            int? idModelo = null, DateTime? fechaCompraInicio = null, DateTime? fechaCompraFin = null, int? idRol = null)
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
WHERE (@IdModelo IS NULL OR M.id_modelo = @IdModelo)
  AND (@Texto    IS NULL OR (AF.EtiquetaInv LIKE '%'+@Texto+'%' OR AF.NumeroSerie LIKE '%'+@Texto+'%'))
  AND (@IdDepartamento IS NULL OR EXISTS(
        SELECT 1 
        FROM dbo.EmpleadosEquipos EE 
        WHERE EE.id_activofijo = AF.id_activofijo 
          AND EE.ResponsableActual = 1 
          AND EE.id_DE = @IdDepartamento
  ))
  AND (@DesdeCompra IS NULL OR AF.FechaCompra >= @DesdeCompra)
  AND (@HastaCompra IS NULL OR AF.FechaCompra < DATEADD(day, 1, @HastaCompra))
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
                        cmd.Parameters.AddWithValue("@IdModelo", (object?)idModelo ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@DesdeCompra", (object?)fechaCompraInicio ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@HastaCompra", (object?)fechaCompraFin ?? DBNull.Value);

                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);

                        return (DS, dt);
                    }

                case "Asignaciones":
                    {
                        const string DS = "DsAsignaciones";
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
  AND (@Desde IS NULL OR EE.FechaAsignacion >= @Desde)
  AND (@Hasta IS NULL OR EE.FechaAsignacion < DATEADD(day, 1, @Hasta))
ORDER BY EE.FechaAsignacion DESC;";

                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@IdDepartamento", (object?)idDepartamento ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Desde", (object?)fechaInicio ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Hasta", (object?)fechaFin ?? DBNull.Value);
                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);
                        return (DS, dt);
                    }

                case "Bitacora":
                    {
                        const string DS = "DsBitacora";
                        var dt = new DataTable();

                        var sql = @"
SELECT 
    B.id_evento, 
    B.FechaHora, 
    M.Modulo, 
    A.Accion, 
    U.Username, 
    B.Detalles
FROM dbo.Bitacora B
JOIN dbo.Modulos M ON B.id_modulo = M.id_modulo
JOIN dbo.Acciones A ON B.id_accion = A.id_accion
JOIN dbo.Usuarios U ON B.id_usuario = U.id_usuario
WHERE (@Desde IS NULL OR B.FechaHora >= @Desde)
  AND (@Hasta IS NULL OR B.FechaHora < DATEADD(day,1,@Hasta))
  AND (@Texto IS NULL OR (B.Detalles LIKE '%'+@Texto+'%' OR U.Username LIKE '%'+@Texto+'%'))
ORDER BY B.FechaHora DESC;";

                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@Desde", (object?)FechaInicio ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Hasta", (object?)FechaFin ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);
                        using var da = new SqlDataAdapter(cmd);
                        da.Fill(dt);
                        return (DS, dt);
                    }

                case "Empleados":
                    {
                        const string DS = "DsEmpleados";
                        var dt = new DataTable();

                        var sql = @"
SELECT 
    E.id_empleado, 
    E.Nombre, 
    P.NombrePuesto, 
    DE.NombreDepartamento, 
    E.Email, 
    E.Telefono
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
                        const string DS = "DsMantenimientos";
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
                        const string DS = "DsUsuarios";

                        var dt = new DataTable(DS);
                        dt.Columns.Add("id_usuario", typeof(int));
                        dt.Columns.Add("Username", typeof(string));
                        var col_fecha = dt.Columns.Add("FechaPassword", typeof(DateTime));
                        col_fecha.AllowDBNull = true;
                        var col_emp = dt.Columns.Add("Empleado", typeof(string));
                        col_emp.AllowDBNull = true;
                        dt.Columns.Add("NombreRol", typeof(string));

                        var sql = @"
SELECT 
    CONVERT(int, U.id_usuario)           AS id_usuario,
    CONVERT(nvarchar(256), U.Username)   AS Username,
    CONVERT(datetime, U.FechaPassword)   AS FechaPassword,
    CONVERT(nvarchar(256), E.Nombre)     AS Empleado,
    CONVERT(nvarchar(256), RS.NombreRol) AS NombreRol
FROM dbo.Usuarios U
LEFT JOIN dbo.Empleados E ON U.id_empleado = E.id_empleado
JOIN dbo.RolesSistema RS  ON U.id_rol_sistema = RS.id_rol_sistema
WHERE (@Texto IS NULL OR (U.Username LIKE '%'+@Texto+'%' OR E.Nombre LIKE '%'+@Texto+'%'))
  AND (@IdRol IS NULL OR U.id_rol_sistema = @IdRol)
ORDER BY U.Username;";

                        using var cmd = new SqlCommand(sql, conn);
                        cmd.Parameters.AddWithValue("@Texto", (object?)texto ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@IdRol", (object?)idRol ?? DBNull.Value);

                        using var rdr = await cmd.ExecuteReaderAsync();
                        int o_id = rdr.GetOrdinal("id_usuario");
                        int o_user = rdr.GetOrdinal("Username");
                        int o_fecha = rdr.GetOrdinal("FechaPassword");
                        int o_emp = rdr.GetOrdinal("Empleado");
                        int o_rol = rdr.GetOrdinal("NombreRol");

                        while (await rdr.ReadAsync())
                        {
                            var row = dt.NewRow();
                            row["id_usuario"] = rdr.IsDBNull(o_id) ? 0 : rdr.GetInt32(o_id);
                            row["Username"] = rdr.IsDBNull(o_user) ? null : rdr.GetString(o_user);

                            if (rdr.IsDBNull(o_fecha))
                            {
                                row["FechaPassword"] = DBNull.Value;
                            }
                            else
                            {
                                var d = rdr.GetDateTime(o_fecha);
                                row["FechaPassword"] = (d < new DateTime(1900, 1, 1)) ? (object)DBNull.Value : d;
                            }

                            row["Empleado"] = rdr.IsDBNull(o_emp) ? null : rdr.GetString(o_emp);
                            row["NombreRol"] = rdr.IsDBNull(o_rol) ? null : rdr.GetString(o_rol);
                            dt.Rows.Add(row);
                        }

                        return (DS, dt);
                    }

            }

            throw new InvalidOperationException("Sección no implementada.");
        }

        // =============================================================================
        // Carga de combos (Departamentos, Perfiles, Estados, Equipos, Técnicos, Modelos, Roles)
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

            // Modelos
            using (var cmd = new SqlCommand(@"
SELECT M.id_modelo,
       CONCAT(TE.TipoEquipo, N' / ', MAR.Marca, N' ', M.Modelo) AS Nombre
FROM dbo.Modelos M
JOIN dbo.Marcas MAR       ON M.id_marca = MAR.id_marca
JOIN dbo.TiposEquipos TE  ON M.id_tipoequipo = TE.id_tipoequipo
ORDER BY TE.TipoEquipo, MAR.Marca, M.Modelo;", conn))
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    Modelos.Add(new ItemSimple { Id = rd.GetInt32(0), Nombre = rd.GetString(1) });

            // Roles
            using (var cmd = new SqlCommand("SELECT id_rol_sistema, NombreRol FROM RolesSistema ORDER BY NombreRol", conn))
            using (var rd = await cmd.ExecuteReaderAsync())
                while (await rd.ReadAsync())
                    Roles.Add(new ItemSimple { Id = rd.GetInt32(0), Nombre = rd.GetString(1) });
        }

        // ====== ViewModels de combos ======
        public class ItemSimple { public int Id { get; set; } public string Nombre { get; set; } }
        public class EquipoItem { public int Id { get; set; } public string EtiquetaInv { get; set; } public string NumeroSerie { get; set; } }
    }
}
