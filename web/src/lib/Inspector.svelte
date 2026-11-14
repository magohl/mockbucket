<script>
  import { api } from "./api.js";
  import { toCurl, copyText } from "./curl.js";
  import { clock, statusClass, bytes } from "./fmt.js";

  let { entry, onclose } = $props();
  let detail = $state(null);
  let view = $state("pretty"); // pretty | raw | hex
  let curlCopied = $state(false);
  let hex = $state("");

  $effect(() => {
    if (!entry) return;
    view = "pretty"; hex = "";
    api.entry(entry.id).then((d) => (detail = d)).catch(() => (detail = null));
  });

  const pretty = $derived.by(() => {
    if (!detail?.body.text) return "";
    if (detail.body.kind !== "json") return detail.body.text;
    try { return JSON.stringify(JSON.parse(detail.body.text), null, 2); } catch { return detail.body.text; }
  });

  async function showHex() {
    view = "hex";
    if (hex) return;
    const buf = new Uint8Array(await (await fetch(`/api/entries/${entry.id}/body`)).arrayBuffer());
    const rows = [];
    for (let i = 0; i < Math.min(buf.length, 4096); i += 16) {
      const slice = buf.slice(i, i + 16);
      const ascii = [...slice].map((b) => (b >= 32 && b < 127 ? String.fromCharCode(b) : "·")).join("");
      rows.push(`${i.toString(16).padStart(8, "0")}  ${[...slice].map((b) => b.toString(16).padStart(2, "0")).join(" ").padEnd(47)}  ${ascii}`);
    }
    if (buf.length > 4096) rows.push(`… ${bytes(buf.length - 4096)} more`);
    hex = rows.join("\n");
  }

  async function copyCurl() {
    curlCopied = await copyText(toCurl(detail));
    setTimeout(() => (curlCopied = false), 1500);
  }
</script>

{#if detail}
  <aside class="card self-start lg:sticky lg:top-[88px]">
    <header class="flex items-baseline gap-3">
      <h2 class="min-w-0 flex-1 truncate font-display text-[1.05rem] font-bold uppercase text-ink">
        {detail.method} <span class="font-normal">{detail.path}</span>
      </h2>
      <span class="pill {statusClass(detail.response.status)}">{detail.response.status}</span>
      <button onclick={onclose} class="btn-quiet text-faint" title="Close">✕</button>
    </header>

    <div class="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 font-display text-[0.72rem] tabular-nums text-faint">
      <span>{clock(entry.time)}</span>
      {#if detail.clientIp}<span>from {detail.clientIp}</span>{/if}
      <span>{bytes(detail.body.bytes)}</span>
      {#if detail.response.source !== "default"}<span class="uppercase tracking-[0.1em]">{detail.response.source}</span>{/if}
    </div>

    {#if detail.body.truncated}
      <p class="note-warn mt-4">
        body truncated at 1 MB — true size {bytes(detail.body.bytes)}, download raw for what was kept
      </p>
    {/if}

    {#if Object.keys(detail.query ?? {}).length}
      <p class="label">Query</p>
      <table class="w-full">
        <tbody>
        {#each Object.entries(detail.query) as [k, vs]}
          <tr>
            <td class="w-1/3 py-0.5 pr-3 align-top font-ui text-sm text-gray">{k}</td>
            <td class="py-0.5 font-display text-[0.8rem] text-ink">{vs.join(", ") || "∅"}</td>
          </tr>
        {/each}
        </tbody>
      </table>
    {/if}

    <p class="label">Headers <span class="text-faint/70">({Object.keys(detail.headers ?? {}).length})</span></p>
    <details>
      <summary class="cursor-pointer select-none font-ui text-sm text-faint transition hover:text-gray">show</summary>
      <table class="mt-1 w-full">
        <tbody>
        {#each Object.entries(detail.headers ?? {}) as [k, vs]}
          <tr>
            <td class="w-1/3 py-0.5 pr-3 align-top font-ui text-[0.82rem] text-gray">{k}</td>
            <td class="py-0.5 font-display text-[0.78rem] break-all text-ink">{vs.join(", ")}</td>
          </tr>
        {/each}
        </tbody>
      </table>
    </details>

    <p class="label">Body <span class="normal-case tracking-normal" style="text-transform:none">· {detail.body.kind}</span></p>
    <div class="mb-2 flex gap-1 text-xs">
      {#each ["pretty", "raw"] as v}
        <button onclick={() => (view = v)} class="btn-quiet !px-2 !py-0.5 font-display {view === v ? '!bg-ink !text-canvas' : ''}">{v}</button>
      {/each}
      <button onclick={showHex} class="btn-quiet !px-2 !py-0.5 font-display {view === 'hex' ? '!bg-ink !text-canvas' : ''}">hex</button>
    </div>
    {#if view === "hex"}
      <pre class="max-h-64 overflow-auto rounded-xl bg-canvas p-4 font-display text-[0.72rem] leading-relaxed text-gray">{hex || "…"}</pre>
    {:else if detail.body.kind === "none"}
      <p class="text-sm text-faint">no body</p>
    {:else if view === "pretty" && detail.body.kind === "binary"}
      <p class="text-sm text-faint">binary body — use <button class="underline decoration-faint" onclick={showHex}>hex</button> or raw download</p>
    {:else}
      <pre class="max-h-64 overflow-auto rounded-xl bg-canvas p-4 font-display text-[0.78rem] leading-relaxed break-all whitespace-pre-wrap text-gray">{view === "pretty" ? pretty : detail.body.text}</pre>
    {/if}

    <div class="mt-5 flex items-center gap-3">
      <button onclick={copyCurl} class="btn">{curlCopied ? "copied ✓" : "Copy as cURL"}</button>
      <a href="/api/entries/{entry.id}/raw" download class="btn-quiet">raw ⬇</a>
    </div>
  </aside>
{:else if entry}
  <aside class="card self-start text-center text-gray">
    <p class="kicker mb-2">Gone from the ring</p>
    entry {entry.id} was evicted while you looked at it.
    <div><button onclick={onclose} class="btn mt-4">Close</button></div>
  </aside>
{/if}
