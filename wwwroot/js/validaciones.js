class Validaciones {
    static DNI(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (!/^[0-9]*$/.test(cadena)) { console.error('Cadena contiene caracteres inválidos.'); return false; }
        if (cadena.length > 13) { console.error('DNI excede los 13 dígitos.'); return false; }
        return true;
    }

    static SoloNumeros(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (!/^[0-9]*$/.test(cadena)) { console.error('Cadena contiene caracteres inválidos.'); return false; }
        return true;
    }

    static Costo(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (cadena === '') return true;
        if (!/^\d+([.,]\d{1,2})?$/.test(cadena)) {
            console.error('Precio inválido (use enteros o decimales con punto o coma, máx. 2 decimales).');
            return false;
        }
        return true;
    }

    static Usuario(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (!/^[a-zA-Z0-9_.-]*$/.test(cadena)) { console.error('Cadena contiene caracteres inválidos.'); return false; }
        return true;
    }

    static Contrasena(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (!/^[a-zA-Z0-9!@#$%^&*()_+\-=\[\]{};':"\\|,.<>\/?]*$/.test(cadena)) {
            console.error('Cadena contiene caracteres inválidos.');
            return false;
        }
        return true;
    }

    static Telefono(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (!/^[0-9]{1,8}$/.test(cadena)) { console.error('Cadena contiene caracteres inválidos.'); return false; }
        return true;
    }

    static Nombre_Apellido(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (!/^[a-zA-ZáéíóúÁÉÍÓÚñÑ ]*$/.test(cadena)) { console.error('Cadena contiene caracteres inválidos.'); return false; }
        return true;
    }

    static SoloLetras(cadena) {
        cadena = (cadena ?? '').toString().trim();
        if (!/^[a-zA-Z]*$/.test(cadena)) { console.error('Cadena contiene caracteres inválidos.'); return false; }
        return true;
    }

    static Correo(email) {
        email = (email ?? '').toString().trim();
        const regex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!regex.test(email)) { console.error('Correo electrónico inválido.'); return false; }
        return true;
    }
}
window.Validaciones = Validaciones;

(function () {
    function validarInput(input) {
        const reglas = (input.dataset.validate || '').split(',').map(s => s.trim()).filter(Boolean);
        let ok = true;
        for (const regla of reglas) {
            const fn = window.Validaciones?.[regla];
            if (typeof fn === 'function') {
                if (!fn(input.value ?? '')) { ok = false; break; }
            }
        }
        // Feedback Bootstrap
        const container = input.closest('.form-group, .mb-3, .form-floating, .col, .input-group') || input.parentElement;
        let fe = container?.querySelector('.invalid-feedback');
        if (!fe) {
            fe = document.createElement('div');
            fe.className = 'invalid-feedback';
            fe.textContent = input.dataset.errorMessage || 'Valor inválido.';
            container?.appendChild(fe);
        }
        input.classList.toggle('is-invalid', !ok);
        return ok;
    }

    function wireForm(form) {
        form.addEventListener('submit', function (e) {
            let valid = true;
            const inputs = form.querySelectorAll('[data-validate]');
            inputs.forEach(inp => { if (!validarInput(inp)) valid = false; });
            if (!valid) {
                e.preventDefault();
                e.stopPropagation();
            }
        });
        // Validación en tiempo real
        form.querySelectorAll('[data-validate]').forEach(inp => {
            inp.addEventListener('input', () => validarInput(inp));
            inp.addEventListener('blur', () => validarInput(inp));
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        document.querySelectorAll('form').forEach(wireForm);
    });
})();
