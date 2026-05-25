<script lang="ts">
  import { listen } from '@tauri-apps/api/event';
  import { open } from '@tauri-apps/plugin-dialog';
  import FilePicker from './lib/FilePicker.svelte';
  import ParamForm from './lib/ParamForm.svelte';
  import PieceViewer from './lib/PieceViewer.svelte';
  import PieceList from './lib/PieceList.svelte';
  import { exists } from '@tauri-apps/plugin-fs';
  import { sliceModel, readTextFile } from './lib/api';
  import type { SliceParams, SliceResult, PieceInfo, ViewMode } from './types';
  import { DEFAULT_PARAMS } from './types';

  let params: SliceParams = $state({ ...DEFAULT_PARAMS });
  let slicing = $state(false);
  let result: SliceResult | null = $state(null);
  let progress = $state('');
  let error = $state('');
  let viewMode: ViewMode = $state('split');
  let showTexture = $state(false);
  let pieceVisibility = $state<boolean[]>([]);

  let piecePaths: string[] = $derived(result ? result.pieces.map(p => p.path) : []);
  let backPiecePaths: string[] = $derived(result ? result.pieces.map(p => p.back_path ?? null).filter((p): p is string => p !== null) : []);
  let consolidatedPath: string = $derived(result?.consolidated ?? '');

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
    pieceVisibility = [];

    const sep = params.input_path.includes('\\') ? '\\' : '/';
    const dir = params.input_path.substring(0, params.input_path.lastIndexOf(sep));
    const basename = params.input_path.substring(params.input_path.lastIndexOf(sep) + 1, params.input_path.lastIndexOf('.'));
    const countStr = String(params.pieces).padStart(4, '0');
    const modeLabel = 'planar';

    let outputDir = '';
    let counter = 0;
    while (true) {
      const candidate = `${dir}${sep}${basename}_${modeLabel}_pieces_${countStr}_${String(counter).padStart(3, '0')}`;
      if (!(await exists(candidate))) {
        outputDir = candidate;
        break;
      }
      counter++;
      if (counter > 999) {
        outputDir = `${dir}${sep}${basename}_${modeLabel}_pieces_${countStr}_${Date.now()}`;
        break;
      }
    }

    try {
      result = await sliceModel({ ...params, output_path: outputDir });
      pieceVisibility = result.pieces.map(() => true);
      progress = 'Done!';
    } catch (e) {
      error = typeof e === 'string' ? e : String(e);
      progress = '';
    } finally {
      slicing = false;
    }
  }

  async function handleLoadFolder() {
    const folder = await open({
      directory: true,
      multiple: false,
    });
    if (!folder) return;

    const checkpointPath = `${folder}/checkpoint.json`;

    try {
      const text = await readTextFile(checkpointPath);
      const data = JSON.parse(text);

      const pieces: PieceInfo[] = [];
      const counts: number[] = data.piece_vertex_counts ?? [];

      const files = data.piece_count ?? counts.length;
      for (let i = 0; i < files; i++) {
        const backPath = `${folder}/pieces/piece_${String(i).padStart(4, '0')}_back.glb`;
        pieces.push({
          index: i,
          path: `${folder}/pieces/piece_${String(i).padStart(4, '0')}.glb`,
          vertices: counts[i] ?? 0,
          back_path: backPath,
        });
      }

      result = {
        piece_count: pieces.length,
        output_dir: folder,
        consolidated: `${folder}/pieces.glb`,
        checkpoint: checkpointPath,
        colour_atlas: `${folder}/colour_atlas.png`,
        pieces,
      };

      pieceVisibility = pieces.map(() => true);
      error = '';
    } catch (e) {
      error = `Failed to load from folder: ${e instanceof Error ? e.message : String(e)}`;
    }
  }
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

    <button class="btn btn-slice" onclick={handleSlice} disabled={slicing || !params.input_path}>
      {slicing ? 'Slicing…' : 'Slice'}
    </button>

    <button class="btn btn-secondary" onclick={handleLoadFolder}>
      Load From Folder…
    </button>

    {#if progress && slicing}
      <div class="progress">{progress}</div>
    {/if}
    {#if error}
      <div class="error">{error}</div>
    {/if}
  </aside>

  <main class="main">
    <PieceViewer
      bind:piecePaths
      bind:backPiecePaths
      bind:consolidatedPath
      bind:viewMode
      bind:pieceVisible={pieceVisibility}
      bind:showTexture
    />
  </main>

  {#if result}
    <aside class="results-panel">
        <div class="view-toggle">
          <button
            class="toggle-btn" class:active={viewMode === 'split'}
            onclick={() => (viewMode = 'split')}>Split</button>
          <button
            class="toggle-btn" class:active={viewMode === 'assembled'}
            onclick={() => (viewMode = 'assembled')}>Assembled</button>
          <button
            class="toggle-btn" class:active={viewMode === 'simulate'}
            onclick={() => (viewMode = 'simulate')}>Simulate</button>
        </div>
      <div class="texture-toggle">
        <button
          class="toggle-btn" class:active={showTexture}
          onclick={() => (showTexture = !showTexture)}>Textures</button>
      </div>
      <PieceList pieces={result.pieces} bind:visible={pieceVisibility} />
    </aside>
  {/if}
</div>

<style>
  .app-layout {
    display: grid;
    grid-template-columns: 340px 1fr 280px;
    height: 100vh;
    overflow: hidden;
  }
  .sidebar {
    background: #1e1e2e;
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
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
  .btn-slice:hover:not(:disabled) { background: #3a7bff; }
  .btn-slice:disabled { opacity: 0.4; cursor: not-allowed; }
  .btn-secondary {
    padding: 0.5rem 1rem;
    font-size: 0.85rem;
    background: #2a2a3e;
    color: #ccc;
    border: 1px solid #444;
    border-radius: 6px;
    cursor: pointer;
    transition: background 0.15s;
  }
  .btn-secondary:hover { background: #333; }
  .main {
    flex: 1;
    position: relative;
    overflow: hidden;
  }
  .results-panel {
    background: #1e1e2e;
    padding: 1rem;
    border-left: 1px solid #333;
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
    overflow: hidden;
  }
  .view-toggle {
    display: flex;
    gap: 0.25rem;
    background: #2a2a3e;
    border-radius: 6px;
    padding: 2px;
    flex-shrink: 0;
  }
  .toggle-btn {
    flex: 1;
    padding: 0.35rem 0.25rem;
    border: none;
    border-radius: 4px;
    background: transparent;
    color: #888;
    font-size: 0.7rem;
    cursor: pointer;
    text-transform: uppercase;
    letter-spacing: 0.03em;
    transition: all 0.15s;
    white-space: nowrap;
    overflow: hidden;
  }
  .toggle-btn.active { background: #4f8cff; color: #fff; }
  .toggle-btn:hover:not(.active) { color: #ccc; }
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
