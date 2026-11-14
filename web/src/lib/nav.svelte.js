// 30-line router. Routes: / (home) and /buckets/:id. API + capture paths never enter here.
export const route = $state({ path: typeof location === "undefined" ? "/" : location.pathname });

export function navigate(path) {
  if (path === route.path) return;
  history.pushState({}, "", path);
  route.path = path;
}

export function back() {
  history.pushState({}, "", "/");
  route.path = "/";
}

addEventListener("popstate", () => (route.path = location.pathname));

export function href(e) {
  // onclick={href} on <a href="…"> — SPA navigation without full reload.
  if (e.currentTarget.dataset.external) return;
  e.preventDefault();
  navigate(new URL(e.currentTarget.href).pathname);
}
