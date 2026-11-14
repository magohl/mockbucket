<script>
  import { route, href } from "./lib/nav.svelte.js";
  import { theme, toggleTheme } from "./lib/theme.svelte.js";
  import Home from "./lib/Home.svelte";
  import BucketView from "./lib/BucketView.svelte";

  const bucketId = $derived(route.path.match(/^\/buckets\/([0-9A-Z]{6})\/?$/i)?.[1].toUpperCase() ?? null);
</script>

<header class="app-header sticky top-0 z-10 flex h-[68px] items-center gap-4 bg-card px-6 sm:px-10">
  <a href="/" onclick={href} class="font-display text-xl font-bold tracking-tight text-ink">
    MOCK<span class="text-accent">BUCKET</span>
  </a>
  <span class="hidden text-[0.95rem] text-faint sm:inline">http capture &amp; mock</span>
  <nav class="ml-auto flex items-center gap-1 text-sm text-gray">
    <a href="/" onclick={href} class="btn-quiet">buckets</a>
    <span class="text-faint">·</span>
    <a href="/openapi/v1.json" class="btn-quiet">api docs</a>
    <button onclick={toggleTheme} title="toggle theme" class="btn-quiet ml-2 font-display text-[0.75rem] uppercase tracking-[0.05em]">
      {theme.dark ? "☀ light" : "☾ dark"}
    </button>
    <!-- classic upper-right repo link (no onclick={href} — external, let the router stay out of it) -->
    <a href="https://github.com/magohl/mockbucket" target="_blank" rel="noopener" title="github.com/magohl/mockbucket"
      aria-label="MockBucket on GitHub" class="btn-quiet ml-1 inline-flex items-center hover:text-ink">
      <svg viewBox="0 0 16 16" class="h-[18px] w-[18px] fill-current" aria-hidden="true">
        <path d="M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.01 8.01 0 0 0 16 8c0-4.42-3.58-8-8-8Z"/>
      </svg>
    </a>
  </nav>
</header>

{#if bucketId}
  <BucketView id={bucketId} key={bucketId} />
{:else}
  <Home />
{/if}
