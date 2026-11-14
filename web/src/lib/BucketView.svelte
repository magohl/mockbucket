<script>
  import { onMount } from "svelte";
  import { api } from "./api.js";
  import { href } from "./nav.svelte.js";
  import { copyText } from "./curl.js";
  import { clock, statusClass, bytes } from "./fmt.js";
  import Inspector from "./Inspector.svelte";
  import RulesEditor from "./RulesEditor.svelte";

  let { id } = $props();
  let bucket = $state(null);
  let entries = $state([]);
  let selected = $state(null);
  let tab = $state("feed"); // feed | rules
  let live = $state(false);
  let gone = $state(false);
  let copied = $state(false);
  let error = $state("");

  const captureUrl = $derived(`${location.origin}/b/${id}`);

  async function resync() {
    try { entries = (await api.entries(id)).entries; } catch (e) { error = e.message; }
  }

  onMount(() => {
    api.bucket(id).then((b) => (bucket = b)).catch(() => (gone = true));
    resync();

    const es = new EventSource(`/api/buckets/${id}/stream`);
    es.addEventListener("entry", (ev) => {
      const e = JSON.parse(ev.data);
      live = true;
      if (entries.length && e.seq > entries.at(-1).seq + 1) return resync(); // gap → realign
      entries = [...entries.filter((x) => x.seq < e.seq), e];
    });
    es.addEventListener("reset", resync);
    es.addEventListener("purge", () => (entries = []));
    es.addEventListener("gone", () => { gone = true; es.close(); });
    es.onopen = () => (live = true);
    es.onerror = () => (live = false);
    return () => es.close();
  });

  async function clear() {
    if (!entries.length) return;
    await api.purge(id);
    entries = [];
    selected = null;
  }

  async function copyCapture() {
    copied = await copyText(captureUrl);
    setTimeout(() => (copied = false), 1500);
  }
</script>

<main class="mx-auto flex max-w-[1200px] flex-col gap-5 px-6 py-10 sm:px-10">
  <nav class="flex items-baseline gap-2 font-display text-[0.75rem] uppercase tracking-[0.12em]">
    <a href="/" onclick={href} class="text-faint transition hover:text-ink">buckets</a>
    <span class="text-faint">/</span>
    <span class="font-bold text-ink">{id}</span>
    {#if bucket?.name}<span class="font-ui normal-case tracking-normal text-faint">· {bucket.name}</span>{/if}
  </nav>

  {#if gone}
    <section class="card py-14 text-center">
      <p class="kicker mb-2">This bucket is gone</p>
      <p class="text-gray">Deleted, or idle-expired past the retention window.</p>
      <a href="/" onclick={href} class="btn mt-6">Back to buckets</a>
    </section>
  {:else}
    <!-- hero: the one URL that matters -->
    <section class="card">
      <p class="kicker">Send anything here</p>
      <div class="mt-2 flex flex-wrap items-center gap-4">
        <code class="statement flex-1">{captureUrl}</code>
        <button onclick={copyCapture} class="btn">{copied ? "copied ✓" : "copy"}</button>
      </div>
      <p class="instruction mt-5 text-[1.02rem]">
        Control the reply per request: <code class="inline">?mb_status=401&amp;mb_body=…</code>
        {" "}— or store rules in <button class="cursor-pointer font-display text-[0.8em] font-bold uppercase tracking-wide text-accent" onclick={() => (tab = "rules")}>mock rules</button>.
      </p>
    </section>

    <div class="grid items-start gap-5 {tab === 'rules' ? 'lg:grid-cols-1' : 'lg:grid-cols-[minmax(0,7fr)_minmax(0,5fr)]'}">
      <section class="card">
        <div class="mb-3 flex items-center gap-2">
          {#if tab === "feed"}
            <h2 class="font-display text-[1.1rem] font-bold uppercase text-ink">Requests</h2>
            <span class="ml-auto {live ? 'live-dot' : 'live-dot live-off'}">{live ? "live" : "offline"}</span>
            <button onclick={clear} class="btn-quiet ml-3 text-xs" title="Purge all entries">Clear</button>
            <span class="mx-1 text-faint">·</span>
            <button onclick={() => (tab = "rules")} class="btn-quiet text-xs underline decoration-transparent hover:decoration-accent">rules →</button>
          {:else}
            <h2 class="font-display text-[1.1rem] font-bold uppercase text-ink">Mock rules</h2>
            <button onclick={() => (tab = "feed")} class="btn-quiet ml-auto text-xs">← requests</button>
          {/if}
        </div>

        {#if tab === "feed"}
          {#if error}<p class="note-err mb-1">{error}</p>{/if}
          {#if entries.length === 0}
            <p class="py-10 text-center text-gray">
              Nothing yet — <code class="inline">curl {captureUrl}</code><br />and it appears here.
            </p>
          {:else}
            <ul class="divide-line max-h-[62vh] overflow-y-auto">
              {#each entries as e (e.id)}
                <li>
                  <button
                    onclick={() => (selected = e)}
                    class="flex w-full items-baseline gap-3.5 rounded-lg px-2.5 py-2.5 text-left transition hover:bg-ink/5 {selected?.id === e.id ? 'bg-canvas' : ''}"
                  >
                    <span class="w-[52px] shrink-0 font-display text-[0.72rem] tabular-nums text-faint">{clock(e.time)}</span>
                    <span class="w-12 shrink-0 font-display text-[0.8rem] font-bold text-ink">{e.method}</span>
                    <span class="min-w-0 flex-1 truncate font-display text-[0.82rem] text-gray">{e.path}</span>
                    {#if e.source !== "default"}
                      <span class="shrink-0 font-display text-[0.62rem] uppercase tracking-[0.1em] {e.source === 'error' ? 'tag-err' : 'text-faint'}">{e.source}</span>
                    {/if}
                    <span class="pill {statusClass(e.status)} shrink-0">{e.status}</span>
                  </button>
                </li>
              {/each}
            </ul>
            <p class="label mt-3 text-right normal-case tracking-normal" style="text-transform:none">{entries.length} shown · {bytes(entries.reduce((a, e) => a + e.bodyBytes, 0))} captured</p>
          {/if}
        {:else}
          <RulesEditor {id} />
        {/if}
      </section>

      {#if tab === "feed" && selected}
        <Inspector entry={selected} onclose={() => (selected = null)} />
      {:else if tab === "feed"}
        <section class="card hidden text-center text-gray lg:block">
          <p class="kicker mb-2">Inspector</p>
          Select a request to inspect it.
        </section>
      {/if}
    </div>
  {/if}
</main>
