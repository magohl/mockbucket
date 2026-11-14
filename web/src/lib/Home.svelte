<script>
  import { api } from "./api.js";
  import { navigate, href } from "./nav.svelte.js";
  import { timeAgo } from "./fmt.js";

  let buckets = $state([]);
  let error = $state("");
  let busy = $state(false);

  $effect(() => { api.buckets().then((r) => (buckets = r.buckets)).catch((e) => (error = e.message)); });

  async function create() {
    busy = true;
    try {
      const b = await api.createBucket(null);
      navigate(b.urls.ui);
    } catch (e) { error = e.message; } finally { busy = false; }
  }

  async function remove(b) {
    if (!confirm(`Delete bucket ${b.id} and its ${b.entryCount} entries?`)) return;
    await api.deleteBucket(b.id);
    buckets = buckets.filter((x) => x.id !== b.id);
  }
</script>

<main class="mx-auto flex max-w-[1200px] flex-col gap-5 px-6 py-10 sm:px-10">
  <section class="card flex flex-col gap-3 sm:flex-row sm:items-center">
    <div class="min-w-0 flex-1">
      <p class="kicker">API and HTTP Test tool</p>
      <h1 class="statement mt-1 text-[1.9rem] uppercase leading-[1.4] tracking-[0.01em]">Send anything.<br />See everything.</h1>
      <p class="mt-3 max-w-prose text-[1.05rem] leading-relaxed text-gray">
        This is inspired by my old <a href="https://github.com/magohl/httpbucket" class="min-w-0 flex-1 px-2 py-3 transition hover:bg-ink/5 hover:rounded-xl">HttpBucket</a> 
        Point any HTTP client at a bucket URL - every request lands here.
        Override response easy from client or use advanced store mock rules.
      </p>
    </div>
    <button onclick={create} disabled={busy} class="btn shrink-0 self-start sm:self-auto">+ New bucket</button>
  </section>

  {#if error}<p class="note-err">{error}</p>{/if}

  <section class="card">
    <div class="mb-3 flex items-baseline">
      <h2 class="font-display text-[1.1rem] font-bold uppercase text-ink">Buckets</h2>
      <span class="ml-auto font-display text-[0.75rem] text-faint">{buckets.length}</span>
    </div>

    {#if buckets.length === 0}
      <p class="py-6 text-center text-gray">None yet — make one and start firing.</p>
    {:else}
      <ul class="divide-line">
        {#each buckets as b (b.id)}
          <li class="group flex items-center gap-4">
            <a href="/buckets/{b.id}" onclick={href} class="min-w-0 flex-1 px-2 py-3 transition hover:bg-ink/5 hover:rounded-xl">
              <div class="flex items-baseline gap-3">
                <span class="truncate font-ui font-medium text-ink">{b.name ?? "unnamed"}</span>
                <span class="shrink-0 font-display text-[0.8rem] text-gray">/b/{b.id}</span>
              </div>
              <div class="mt-0.5 font-display text-[0.72rem] tabular-nums text-faint">
                {b.entryCount} entries · last seen {timeAgo(b.lastSeen)}
              </div>
            </a>
            <button
              onclick={() => remove(b)}
              class="btn-quiet opacity-0 transition group-hover:opacity-100"
              title="Delete bucket"
            >✕</button>
          </li>
        {/each}
      </ul>
    {/if}
  </section>
</main>
