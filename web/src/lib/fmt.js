export function timeAgo(iso) {
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 60) return "just now";
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  if (s < 86400) return `${Math.floor(s / 3600)} h ago`;
  return new Date(iso).toLocaleDateString();
}

export function clock(iso) {
  return new Date(iso).toLocaleTimeString([], { hour12: false });
}

/** Status pill classes — color is data, kept to the four semantic bins. */
export function statusClass(n) {
  if (n >= 500) return "pill-err";
  if (n >= 400) return [401, 403, 404].includes(n) ? "pill-warn" : "pill-err";
  if (n >= 300) return "pill-info";
  return "pill-ok";
}

export function bytes(n) {
  if (n < 1024) return `${n} B`;
  if (n < 1 << 20) return `${(n / 1024).toFixed(1)} KB`;
  return `${(n / (1 << 20)).toFixed(1)} MB`;
}
