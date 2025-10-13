using System.Data;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace InventarioComputo.Security
{
    public sealed class PermisosService
    {
        private readonly ConexionBDD _db;
        private readonly IMemoryCache _cache;

        public PermisosService(ConexionBDD db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        private static bool TryGetUserId(ClaimsPrincipal user, out int idUsuario)
        {
            idUsuario = 0;
            var v = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrEmpty(v) && int.TryParse(v, out idUsuario);
        }

        private sealed class PermRow
        {
            // Vistas / Lectura
            public bool VerEmpleados { get; init; }
            public bool VerUsuarios { get; init; }
            public bool VerConexionBDD { get; init; }
            public bool VerReportes { get; init; }
            public bool VerBitacora { get; init; }

            // Modificaciones / Escritura
            public bool ModificarActivos { get; init; }
            public bool ModificarMantenimientos { get; init; }
            public bool ModificarEquipos { get; init; }
            public bool ModificarDepartamentos { get; init; }
            public bool ModificarEmpleados { get; init; }
            public bool ModificarUsuarios { get; init; }

            // Raíz
            public bool AccesoTotal { get; init; }
        }

        private async Task<PermRow?> GetFromDbAsync(int idUsuario)
        {
            using var conn = await _db.GetConnectionAsync(); // tu método actual
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT TOP 1
  ISNULL(VerEmpleados,0)            AS VerEmpleados,
  ISNULL(VerUsuarios,0)             AS VerUsuarios,
  ISNULL(VerConexionBDD,0)          AS VerConexionBDD,
  ISNULL(VerReportes,0)             AS VerReportes,
  ISNULL(VerBitacora,0)             AS VerBitacora,
  ISNULL(ModificarActivos,0)        AS ModificarActivos,
  ISNULL(ModificarMantenimientos,0) AS ModificarMantenimientos,
  ISNULL(ModificarEquipos,0)        AS ModificarEquipos,
  ISNULL(ModificarDepartamentos,0)  AS ModificarDepartamentos,
  ISNULL(ModificarEmpleados,0)      AS ModificarEmpleados,
  ISNULL(ModificarUsuarios,0)       AS ModificarUsuarios,
  ISNULL(AccesoTotal,0)             AS AccesoTotal
FROM dbo.PermisosUsuarios
WHERE id_usuario = @id";
            var p = cmd.CreateParameter();
            p.ParameterName = "@id";
            p.Value = idUsuario;
            cmd.Parameters.Add(p);

            using var rd = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (!await rd.ReadAsync()) return null;

            bool B(string n) => rd.GetBoolean(rd.GetOrdinal(n));
            return new PermRow
            {
                VerEmpleados = B(nameof(PermRow.VerEmpleados)),
                VerUsuarios = B(nameof(PermRow.VerUsuarios)),
                VerConexionBDD = B(nameof(PermRow.VerConexionBDD)),
                VerReportes = B(nameof(PermRow.VerReportes)),
                VerBitacora = B(nameof(PermRow.VerBitacora)),
                ModificarActivos = B(nameof(PermRow.ModificarActivos)),
                ModificarMantenimientos = B(nameof(PermRow.ModificarMantenimientos)),
                ModificarEquipos = B(nameof(PermRow.ModificarEquipos)),
                ModificarDepartamentos = B(nameof(PermRow.ModificarDepartamentos)),
                ModificarEmpleados = B(nameof(PermRow.ModificarEmpleados)),
                ModificarUsuarios = B(nameof(PermRow.ModificarUsuarios)),
                AccesoTotal = B(nameof(PermRow.AccesoTotal))
            };
        }

        private async Task<PermRow> GetAsync(int idUsuario)
        {
            var key = $"perms:{idUsuario}";
            if (_cache.TryGetValue(key, out PermRow row)) return row;
            row = await GetFromDbAsync(idUsuario) ?? new PermRow();
            _cache.Set(key, row, TimeSpan.FromMinutes(5)); // TTL corto
            return row;
        }

        public void Invalidate(int idUsuario) => _cache.Remove($"perms:{idUsuario}");

        public async Task<bool> TieneAsync(ClaimsPrincipal user, string permiso)
        {
            if (!TryGetUserId(user, out var id)) return false;
            var p = await GetAsync(id);
            if (p.AccesoTotal) return true;

            return permiso switch
            {
                // Ver / Lectura
                var s when s == PermisosConstantes.VerEmpleados => p.VerEmpleados,
                var s when s == PermisosConstantes.VerUsuarios => p.VerUsuarios,
                var s when s == PermisosConstantes.VerConexionBDD => p.VerConexionBDD,
                var s when s == PermisosConstantes.VerReportes => p.VerReportes,
                var s when s == PermisosConstantes.VerBitacora => p.VerBitacora,

                // Modificar / Escritura
                var s when s == PermisosConstantes.ModificarActivos => p.ModificarActivos,
                var s when s == PermisosConstantes.ModificarMantenimientos => p.ModificarMantenimientos,
                var s when s == PermisosConstantes.ModificarEquipos => p.ModificarEquipos,
                var s when s == PermisosConstantes.ModificarDepartamentos => p.ModificarDepartamentos,
                var s when s == PermisosConstantes.ModificarEmpleados => p.ModificarEmpleados,
                var s when s == PermisosConstantes.ModificarUsuarios => p.ModificarUsuarios,

                // Raíz
                var s when s == PermisosConstantes.AccesoTotal => p.AccesoTotal,

                _ => false
            };
        }

        // Helpers opcionales, útiles para vistas con varias condiciones

        public async Task<bool> TieneCualquieraAsync(ClaimsPrincipal user, params string[] permisos)
        {
            foreach (var perm in permisos)
                if (await TieneAsync(user, perm)) return true;
            return false;
        }

        public async Task<bool> TieneTodosAsync(ClaimsPrincipal user, params string[] permisos)
        {
            foreach (var perm in permisos)
                if (!await TieneAsync(user, perm)) return false;
            return true;
        }
    }
}
