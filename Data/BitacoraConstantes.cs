namespace InventarioComputo.Data
{
    /// Aqui se centralizan los IDs para los módulos y acciones de la bitácora.
    /// Los valores en las clases coinciden con los IDs de las tablas 'Modulos' y 'Acciones' en la BDD.
    public static class BitacoraConstantes
    {
        public static class Modulos
        {
            public const int Autenticacion = 1;        // Para Login y Logout
            public const int Inventario = 2;           // Formulario y lista de Activos Fijos
            public const int PerfilesEquipos = 3;      // Formulario y lista de Equipos Registrados
            public const int Asignaciones = 4;         // Formulario y lista de Asignaciones
            public const int Mantenimientos = 5;       // Formulario y lista de Mantenimientos
            public const int Empleados = 6;             // Formulario y lista de Empleados
            public const int Usuarios = 7;             // Formulario y lista de Usuarios
            public const int Departamentos = 8;        // Formulario y lista de Departamentos
            public const int Permisos = 9;             // Gestión de Permisos de Usuario
            public const int Reportes = 10;             // Gestion de Reportes
            public const int Softwares = 12;
            public const int Areas = 13;
            public const int TiposEquipos = 14;
            public const int Marcas = 15;
            public const int Modelos = 16;
        }

        public static class Acciones
        {
            public const int Login = 1;
            public const int Logout = 2;
            public const int Creacion = 3;          
            public const int Modificacion = 4;
            public const int Eliminacion = 5;
            public const int Generacion = 6;
            public const int CambioPermisos = 7;
            public const int ReseteoPassword = 8;
            public const int CambioPassword = 9;
        }
    }
}