/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ['./src/**/*.{html,ts}'],
  theme: {
    extend: {
      colors: {
        canvas: 'var(--color-canvas)',
        surface: 'var(--color-surface)',
        'surface-2': 'var(--color-surface-2)',
        primary: 'var(--color-primary)',
        'primary-dim': 'var(--color-primary-dim)',
        'primary-light': 'var(--color-primary-light)',
        secondary: 'var(--color-secondary)',
        'text-primary': 'var(--color-text-primary)',
        'text-secondary': 'var(--color-text-secondary)',
        border: 'var(--color-border)',
        'border-strong': 'var(--color-border-strong)',
        'bubble-user-bg': 'var(--color-bubble-user-bg)',
        'bubble-user-text': 'var(--color-bubble-user-text)',
      },
    },
  },
  plugins: [],
};
