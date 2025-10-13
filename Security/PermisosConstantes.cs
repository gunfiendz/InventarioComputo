namespace InventarioComputo.Security
{
    public static class PermisosConstantes
    {
        // Vistas / Lectura
        public const string VerEmpleados = nameof(VerEmpleados);
        public const string VerUsuarios = nameof(VerUsuarios);
        public const string VerConexionBDD = nameof(VerConexionBDD);
        public const string VerReportes = nameof(VerReportes);
        public const string VerBitacora = nameof(VerBitacora);

        // Modificaciones / Escritura
        public const string ModificarActivos = nameof(ModificarActivos);
        public const string ModificarMantenimientos = nameof(ModificarMantenimientos);
        public const string ModificarEquipos = nameof(ModificarEquipos);
        public const string ModificarDepartamentos = nameof(ModificarDepartamentos);
        public const string ModificarEmpleados = nameof(ModificarEmpleados);
        public const string ModificarUsuarios = nameof(ModificarUsuarios);

        // Permiso raíz
        public const string AccesoTotal = nameof(AccesoTotal);
    }
}
