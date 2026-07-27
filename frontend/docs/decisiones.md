# Decisiones del Frontend

Registro de decisiones del frontend Angular de Synapse Learn.

## 2026-06-29 - Arranque del proyecto Angular

- El frontend Angular vive directamente en la raíz del repositorio `SynapseLearnAI`.
- El nombre de la aplicación Angular es `synapse-learn-frontend`.
- Se usan las últimas versiones estables disponibles al momento de crear el proyecto.
- Entorno inicial verificado:
  - Node.js `22.15.0`.
  - npm `10.9.2`.
  - Angular CLI `21.1.3`.
- El backend de desarrollo esperado corre en `http://localhost:5017`.
- El proyecto se creó con routing, SCSS, strict mode y sin inicializar git desde Angular CLI.

## 2026-06-29 - Tailwind CSS

- Se usa Tailwind CSS para estilos, sin Angular Material ni otra librería de UI.
- Se instaló Tailwind CSS 3 porque coincide con la configuración `tailwind.config.js`
  documentada y es más directa para una primera integración con tokens.
- Los colores del diseño se definen como CSS custom properties en `src/styles.scss`
  y se exponen como utilidades en `tailwind.config.js`.

## 2026-06-29 - Auditoría npm

- `npm audit` reporta vulnerabilidades en dependencias internas de Angular/Vite/esbuild/undici.
- `npm audit fix` no las resuelve sin cambios mayores.
- No se usa `npm audit fix --force` porque intenta instalar versiones incompatibles y puede romper Angular.

## 2026-06-29 - UI/UX abordado

- Los scrollbars se estilizan globalmente en `src/styles.scss` usando los tokens
  de color del proyecto.
- Las respuestas del modelo se renderizan visualmente como Markdown con un
  componente propio (`MarkdownMessageComponent`), sin dependencias externas y
  sin usar `innerHTML` para Markdown arbitrario.
- El render Markdown cubre lo necesario para respuestas típicas del tutor:
  párrafos, encabezados, listas, citas, separadores, bloques de código, negritas,
  cursivas y código inline.

## 2026-06-30 - Modelo Gemini en creación de tutores

- El modelo LLM se considera detalle técnico de infraestructura, no parte del
  diseño pedagógico del tutor.
- El formulario de creación de tutor no expone `geminiModel` al usuario.
- El frontend no envía `geminiModel` al crear tutor; el backend debe asignar el
  modelo por defecto desde configuración, por ejemplo `Gemini:DefaultModel`.
- La edición de `geminiModel` queda reservada para una vista avanzada/admin futura.
