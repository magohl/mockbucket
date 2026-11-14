// "Copy as cURL" — rebuild a replayable command from a recorded request.
const HOP_BY_HOP = new Set(["host", "content-length", "connection", "accept-encoding"]);

export function toCurl(detail, origin) {
  const host = detail.headers?.host?.[0];
  const qs = new URLSearchParams();
  for (const [k, vs] of Object.entries(detail.query ?? {})) for (const v of vs) qs.append(k, v ?? "");
  const suffix = qs.toString() ? `?${qs}` : "";
  const url = `http://${host ?? location.host}${detail.rawPath ?? detail.path}${suffix}`;

  const parts = [`curl -X ${detail.method}`, `'${url}'`];
  for (const [k, vs] of Object.entries(detail.headers ?? {}))
    if (!HOP_BY_HOP.has(k.toLowerCase())) for (const v of vs ?? []) parts.push(`-H '${k}: ${v}'`);

  if (detail.body?.kind === "text" || detail.body?.kind === "json") {
    const esc = (detail.body.text ?? "").replaceAll("'", `'\\''`);
    parts.push(`--data-raw '${esc}'`);
  } else if (detail.body?.kind === "binary")
    parts.push(`--data-binary @${detail.id}.bin  # via GET /api/entries/${detail.id}/body`);

  return parts.join(" \\\n  ");
}

export async function copyText(text) {
  try { await navigator.clipboard.writeText(text); return true; }
  catch {
    const ta = document.createElement("textarea");
    ta.value = text;
    document.body.append(ta);
    ta.select();
    const ok = document.execCommand("copy");
    ta.remove();
    return ok;
  }
}
