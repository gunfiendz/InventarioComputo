// Función para inicializar el ordenamiento en tablas
function initTableSorting() {
    document.querySelectorAll('.sortable-table th[data-sort-column]').forEach(header => {
        const sortLink = header.querySelector('.sort-link');
        if (sortLink) {
            sortLink.addEventListener('click', function (e) {
                e.preventDefault();
                const column = header.getAttribute('data-sort-column');
                const url = new URL(window.location.href);

                // Si ya estamos ordenando por esta columna, alternar la dirección
                if (url.searchParams.get('sortColumn') === column) {
                    const currentDir = url.searchParams.get('sortDirection');
                    url.searchParams.set('sortDirection', currentDir === 'ASC' ? 'DESC' : 'ASC');
                } else {
                    // Nueva columna, dirección ascendente por defecto
                    url.searchParams.set('sortColumn', column);
                    url.searchParams.set('sortDirection', 'ASC');
                }

                // Resetear a la primera página
                url.searchParams.set('pagina', '1');

                // Recargar la página con los nuevos parámetros
                window.location.href = url.toString();
            });
        }
    });
}

// Función para mantener los parámetros en los enlaces de paginación
function initPaginationLinks() {
    document.querySelectorAll('.pagination .page-link').forEach(link => {
        link.addEventListener('click', function (e) {
            e.preventDefault();
            window.location.href = this.href;
        });
    });
}

// Función para inicializar filtros
function initTableFilters(btnSelector) {
    const btn = document.querySelector(btnSelector);
    if (btn) {
        btn.addEventListener('click', function () {
            const params = new URLSearchParams();
            params.set('pagina', '1');
            params.set('sortColumn', '@Model.SortColumn');
            params.set('sortDirection', '@Model.SortDirection');

            // Agregar valores de filtros
            document.querySelectorAll('.filtro-item select, .filtro-item input').forEach(el => {
                if (el.value) params.set(el.id.replace('filtro-', ''), el.value);
            });

            window.location.href = `/Inventario?${params.toString()}`;
        });
    }
}

// Inicializar cuando el DOM esté cargado
document.addEventListener('DOMContentLoaded', function () {
    initTableSorting();
    initPaginationLinks();
    initTableFilters('#btn-aplicar-filtros');
});