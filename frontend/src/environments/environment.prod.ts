export const environment = {
  production: true,
  // En Docker Compose nginx hace proxy /api → api:5017, así que same-origin funciona.
  // Para deploy externo (Vercel/Render), reemplazar en build con --configuration production
  // o via env: apiUrl = 'https://api.tu-dominio.com'
  apiUrl: '',
} as const;
