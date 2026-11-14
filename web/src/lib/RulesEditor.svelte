<script>
  import { api } from "./api.js";

  let { id } = $props();
  let rules = $state([]);
  let dirty = $state(false);
  let saving = $state(false);
  let note = $state("");

  $effect(() => { api.rules(id).then((r) => (rules = r.rules.map(hydrate))); });

  const blank = () => ({
    id: "r_new" + Math.random().toString(36).slice(2, 8),
    name: "", enabled: true,
    match: { method: null, path: "", query: null, headers: null },
    respond: { status: 200, headers: null, body: "", contentType: null },
  });

  // form-friendly copies: maps ⇄ "k: v" / "k=v" line textareas
  function hydrate(r) {
    return {
      ...blank(), ...r,
      _q: objToLines(r.match.query, "="),
      _mh: objToLines(r.match.headers, ":"),
      _rh: objToLines(r.respond.headers, ":"),
    };
  }
  const objToLines = (o, sep) => Object.entries(o ?? {}).map(([k, v]) => `${k}${sep === ":" ? ": " : "="}${v ?? ""}`).join("\n");
  const linesToObj = (s) => {
    const o = {};
    for (const line of (s ?? "").split("\n")) {
      const i = line.search(/[:=]/);
      if (i <= 0) continue;
      o[line.slice(0, i).trim()] = line.slice(i + 1).trim();
    }
    return Object.keys(o).length ? o : null;
  };

  function payload(r) {
    return {
      id: r.id.startsWith("r_new") ? undefined : r.id,
      name: r.name || null,
      enabled: r.enabled,
      match: {
        method: r.match.method || null,
        path: r.match.path || null,
        query: linesToObj(r._q),
        headers: linesToObj(r._mh),
      },
      respond: {
        status: +r.respond.status || 200,
        headers: linesToObj(r._rh),
        body: r.respond.body === "" ? null : r.respond.body,
        contentType: r.respond.contentType || null,
      },
    };
  }

  async function save() {
    saving = true; note = "";
    try {
      rules = (await api.putRules(id, rules.map(payload))).rules.map(hydrate);
      dirty = false;
      note = "saved";
      setTimeout(() => (note = ""), 1500);
    } catch (e) { note = e.message; } finally { saving = false; }
  }

  const move = (i, d) => {
    const a = [...rules];
    [a[i], a[i + d]] = [a[i + d], a[i]];
    rules = a; dirty = true;
  };
</script>

<section>
  <p class="mb-4 text-[0.95rem] leading-relaxed text-gray">
    First enabled match wins — unless the request itself carries <code class="inline">mb_*</code> params.
    Path: glob <code class="inline">/api/*</code> or regex <code class="inline">~^/v\d+/x$</code>. Query/header values: exact,
    <code class="inline">*</code> = exists, <code class="inline">~regex</code>.
  </p>

  <div class="flex flex-col gap-3">
    {#each rules as r, i (r.id)}
      <div class="rounded-xl border border-line bg-canvas/40 p-3.5">
        <div class="flex flex-wrap items-center gap-2.5">
          <input type="checkbox" checked={r.enabled} onchange={(e) => { r.enabled = e.currentTarget.checked; dirty = true; }}
            class="h-4 w-4 accent-accent" title="enabled" />
          <select bind:value={r.match.method} onchange={() => (dirty = true)}
            class="inp !w-auto !px-2 !py-1 font-display !text-[0.8rem] font-bold {r.match.method ? '!text-ink' : '!text-faint'}">
            <option value={null}>ANY</option>
            {#each ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"] as m}<option>{m}</option>{/each}
          </select>
          <input type="text" bind:value={r.match.path} oninput={() => (dirty = true)} placeholder="path /glob/*" spellcheck="false"
            class="inp inp-mono min-w-24 flex-1" />
          <span class="font-display text-faint">→</span>
          <input type="number" bind:value={r.respond.status} oninput={() => (dirty = true)} min="100" max="599"
            class="inp inp-mono !w-[70px] text-right" />
          <span class="ml-auto flex items-center gap-1 text-faint">
            {#if i > 0}<button onclick={() => move(i, -1)} title="up" class="btn-quiet !px-1.5">↑</button>{/if}
            {#if i < rules.length - 1}<button onclick={() => move(i, 1)} title="down" class="btn-quiet !px-1.5">↓</button>{/if}
            <button onclick={() => { rules = rules.filter((_, j) => j !== i); dirty = true; }} class="btn-quiet !px-1.5 hover:!text-[#dc2626] dark:hover:!text-[#f87171]" title="remove">✕</button>
          </span>
        </div>

        <details class="mt-2">
          <summary class="cursor-pointer select-none font-ui text-xs text-faint transition hover:text-gray">
            body · headers · conditions · name
          </summary>
          <div class="mt-2 grid gap-3 sm:grid-cols-2">
            <label class="label !mt-0 !mb-1">name
              <input type="text" bind:value={r.name} oninput={() => (dirty = true)} placeholder="optional label" class="inp mt-1" />
            </label>
            <label class="label !mt-0 !mb-1">response content-type
              <input type="text" bind:value={r.respond.contentType} oninput={() => (dirty = true)} placeholder="application/json" class="inp inp-mono mt-1" />
            </label>
            <label class="label !mt-0 !mb-1 sm:col-span-2">response body
              <textarea rows="3" bind:value={r.respond.body} oninput={() => (dirty = true)} spellcheck="false" class="inp inp-mono mt-1"></textarea>
            </label>
            <label class="label !mt-0 !mb-1">match query (k=v per line)
              <textarea rows="2" bind:value={r._q} oninput={() => (dirty = true)} spellcheck="false" class="inp inp-mono mt-1"></textarea>
            </label>
            <label class="label !mt-0 !mb-1">match headers (Name: v per line)
              <textarea rows="2" bind:value={r._mh} oninput={() => (dirty = true)} spellcheck="false" class="inp inp-mono mt-1"></textarea>
            </label>
            <label class="label !mt-0 !mb-1 sm:col-span-2">response headers (Name: v per line)
              <textarea rows="2" bind:value={r._rh} oninput={() => (dirty = true)} spellcheck="false" class="inp inp-mono mt-1"></textarea>
            </label>
          </div>
        </details>
      </div>
    {/each}
  </div>

  <div class="mt-4 flex items-center gap-4">
    <button onclick={() => { rules = [...rules, blank()]; dirty = true; }} class="btn-quiet font-display text-[0.8rem] font-bold uppercase">+ Add rule</button>
    <button onclick={save} disabled={saving || (!dirty && note === "")} class="btn">{saving ? "saving…" : "Save"}</button>
    {#if note}<span class="font-display text-xs {note === 'saved' ? 'tag-ok' : 'tag-err'}">{note}</span>{/if}
  </div>
</section>
