using System.Globalization;
using System.Text.RegularExpressions;

namespace InventarioComputo.Security
{
    public static class ValidacionesBackend
    {
        private static readonly Regex ReDni = new(@"^[0-9]{1,13}$", RegexOptions.Compiled);
        private static readonly Regex ReSoloNum = new(@"^[0-9]*$", RegexOptions.Compiled);
        private static readonly Regex ReUsuario = new(@"^[a-zA-Z0-9_.-]+$", RegexOptions.Compiled);
        private static readonly Regex RePwd = new(@"^[a-zA-Z0-9!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]+$", RegexOptions.Compiled);
        private static readonly Regex ReTel = new(@"^[0-9]{1,8}$", RegexOptions.Compiled);
        private static readonly Regex ReNombre = new(@"^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]+$", RegexOptions.Compiled);
        private static readonly Regex ReCorreo = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

        // Acepta: 1.234,56 | 1,234.56 | 1234,56 | 1234.56 | 1234 | +1234,50 | -1.234
        private static readonly Regex ReCosto = new(
            @"^\s*[+-]?\d{1,3}([.,]\d{3})*([.,]\d{1,2})?\s*$|^\s*[+-]?\d+([.,]\d{1,2})?\s*$",
            RegexOptions.Compiled
        );

        public static bool EsDni(string? v) => string.IsNullOrEmpty(v) || ReDni.IsMatch(v);
        public static bool EsSoloNumeros(string? v) => string.IsNullOrEmpty(v) || ReSoloNum.IsMatch(v);
        public static bool EsUsuario(string? v) => string.IsNullOrEmpty(v) || ReUsuario.IsMatch(v);
        public static bool EsPassword(string? v) => string.IsNullOrEmpty(v) || RePwd.IsMatch(v);
        public static bool EsTelefono(string? v) => string.IsNullOrEmpty(v) || ReTel.IsMatch(v);
        public static bool EsNombre(string? v) => string.IsNullOrEmpty(v) || ReNombre.IsMatch(v);
        public static bool EsCorreo(string? v) => string.IsNullOrEmpty(v) || ReCorreo.IsMatch(v);

        // --- NUEVO: Costo ---
        public static bool EsCosto(string? v) => string.IsNullOrWhiteSpace(v) || ReCosto.IsMatch(v);

        public static bool TryParseCosto(string? input, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(input)) return false;
            var s = input.Trim();

            // 1) Cultura actual
            if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                                 CultureInfo.CurrentCulture, out value))
                return true;

            // 2) Invariant
            if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                                 CultureInfo.InvariantCulture, out value))
                return true;

            // 3) Cultura típica de coma decimal
            var es = CultureInfo.GetCultureInfo("es-ES");
            if (decimal.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign, es, out value))
                return true;

            // 4) Normalización manual → Invariant
            var norm = NormalizarAInvariant(s);
            return decimal.TryParse(norm, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                                    CultureInfo.InvariantCulture, out value);
        }

        // Overload por si prefieres double (ej. columna SQL FLOAT)
        public static bool TryParseCosto(string? input, out double value)
        {
            value = 0d;
            if (!TryParseCosto(input, out decimal dec)) return false;
            value = (double)dec;
            return true;
        }

        // Heurística: último '.' o ',' es decimal; los demás son miles. Devuelve '.' como decimal.
        private static string NormalizarAInvariant(string s)
        {
            s = s.Trim();

            int lastDot = s.LastIndexOf('.');
            int lastCom = s.LastIndexOf(',');
            int lastSep = Math.Max(lastDot, lastCom);

            if (lastSep < 0) return s.Replace(" ", "");

            char sepDecimal = s[lastSep]; // '.' o ','
            char sepMiles = sepDecimal == '.' ? ',' : '.';

            var sinMiles = s.Replace(" ", "").Replace(sepMiles.ToString(), "");
            if (sepDecimal == ',') sinMiles = sinMiles.Replace(',', '.');

            return sinMiles;
        }
    }
}
