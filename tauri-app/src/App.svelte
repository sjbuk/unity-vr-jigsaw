<script lang="ts">
  import { listen } from '@tauri-apps/api/event';
  import FilePicker from './lib/FilePicker.svelte';
  import ParamForm from './lib/ParamForm.svelte';
  import PieceViewer from './lib/PieceViewer.svelte';
  import PieceList from './lib/PieceList.svelte';
  import { sliceModel } from './lib/api';
  import type { SliceParams, SliceResult } from './types';
  import { DEFAULT_PARAMS } from './types';

  let params: SliceParams = $state({ ...DEFAULT_PARAMS });
  let slicing = $state(false);
  let result: SliceResult | null = $state(null);
  let progress = $state('');
  let error = $state('');

  $effect(() => {
    const unlisten = listen<string>('slice-progress', (event) => {
      progress = event.payload;
    });
    return () => { unlisten.then(fn => fn()); };
  });

  async function handleSlice() {
    if (!params.input_path) {
      error = 'Please select a model file first.';
      return;
    }
    slicing = true;
    error = '';
    progress = 'Starting…';
    result = null;

    const outputDir = `${params.input_path.replace(/\.[^.]+$/, '')}_pieces_${Date.now()}`;

    try {
      result = await sliceModel({
        ...params,
        output_dir: outputDir,
      });
      progress = 'Done!';
    } catch (e) {
      error = typeof e === 'string' ? e : String(e);
      progress = '';
    } finally {
      slicing = false;
    }
  }

  let piecePaths = $derived(result ? result.pieces.map(p => p.path) : []);
</script>

<div class="app-layout">
  <aside class="sidebar">
    <h1>Jigsaw Slicer</h1>

    <section class="section">
      <h2>Model</h2>
      <FilePicker bind:selectedPath={params.input_path} />
    </section>

    <section class="section">
      <h2>Parameters</h2>
      <ParamForm bind:params />
    </section>

    <button
      class="btn btn-slice"
      onclick={handleSlice}
      disabled={slicing || !params.input_path}
    >
      {slicing ? 'Slicing…' : 'Slice'}
    </button>

    {#if progress && slicing}
      <div class="progress">{progress}</div>
    {/if}
    {#if error}
      <div class="error">{error}</div>
    {/if}
  </aside>

  <main class="main">
    <PieceViewer bind:piecePaths />
  </main>

  {#if result}
    <aside class="results-panel">
      <h2>Results</h2>
      <p class="summary">{result.piece_count} pieces ({result.mode})</p>
      <PieceList pieces={result.pieces} resultDir={result.output_dir} />
    </aside>
  {/if}
</div>

<style>
  .app-layout {
    display: grid;
    grid-template-columns: 340px 1fr 260px;
    height: 100vh;
    overflow: hidden;
  }
  .sidebar {
    background: #1e1e2e;
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 1rem;
    overflow-y: auto;
    border-right: 1px solid #333;
  }
  .sidebar h1 {
    font-size: 1.1rem;
    margin: 0;
    color: #4f8cff;
  }
  .section {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }
  .section h2 {
    font-size: 0.8rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: #888;
    margin: 0;
  }
  .btn-slice {
    padding: 0.6rem 1rem;
    font-size: 1rem;
    font-weight: 600;
    background: #4f8cff;
    color: #fff;
    border: none;
    border-radius: 6px;
    cursor: pointer;
    transition: background 0.15s;
  }
  .btn-slice:hover:not(:disabled) {
    background: #3a7bff;
  }
  .btn-slice:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }
  .main {
    flex: 1;
    position: relative;
    overflow: hidden;
  }
  .results-panel {
    background: #1e1e2e;
    padding: 1rem;
    border-left: 1px solid #333;
    overflow-y: auto;
  }
  .results-panel h2 {
    font-size: 0.8rem;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    color: #888;
    margin: 0 0 0.5rem;
  }
  .summary {
    font-size: 0.9rem;
    color: #aaa;
    margin: 0;
  }
  .progress {
    padding: 0.5rem;
    background: #2a2a3e;
    border-radius: 4px;
    font-size: 0.85rem;
    color: #4f8cff;
    font-family: monospace;
  }
  .error {
    padding: 0.5rem;
    background: #3e1a1a;
    border-radius: 4px;
    font-size: 0.85rem;
    color: #ff6b6b;
  }
</style>
