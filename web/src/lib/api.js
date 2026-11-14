// Thin JSON wrapper over the public API — everything the UI does is a documented call.
async function handle(res) {
  if (res.status === 204) return null;
  if (!res.ok) {
    let detail = "";
    try { detail = (await res.json()).title ?? ""; } catch { /* not json */ }
    throw new Error(`${res.status} ${res.url?.split("/api/")[1] ?? ""} ${detail}`);
  }
  return res.json();
}

const json = { "content-type": "application/json" };

export const api = {
  info: () => fetch("/api/info").then(handle),
  buckets: () => fetch("/api/buckets").then(handle),
  createBucket: (name) => fetch("/api/buckets", { method: "POST", headers: json, body: JSON.stringify({ name }) }).then(handle),
  deleteBucket: (id) => fetch(`/api/buckets/${id}`, { method: "DELETE" }).then(handle),
  bucket: (id) => fetch(`/api/buckets/${id}`).then(handle),
  entries: (id, since = 0) => fetch(`/api/buckets/${id}/entries?since=${since}`).then(handle),
  purge: (id) => fetch(`/api/buckets/${id}/entries`, { method: "DELETE" }).then(handle),
  entry: (entryId) => fetch(`/api/entries/${entryId}`).then(handle),
  rules: (id) => fetch(`/api/buckets/${id}/rules`).then(handle),
  putRules: (id, rules) => fetch(`/api/buckets/${id}/rules`, { method: "PUT", headers: json, body: JSON.stringify(rules) }).then(handle),
};
