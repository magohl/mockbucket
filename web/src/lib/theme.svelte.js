export const theme = $state({ dark: typeof document !== "undefined" && document.documentElement.classList.contains("dark") });

export function toggleTheme() {
  theme.dark = !theme.dark;
  document.documentElement.classList.toggle("dark", theme.dark);
  try { localStorage.setItem("mb_theme", theme.dark ? "dark" : "light"); } catch {}
}
