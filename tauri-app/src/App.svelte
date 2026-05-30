
<script lang="ts">
  import FilePicker from './lib/FilePicker.svelte';
  import ParamForm from './lib/ParamForm.svelte';
  import PieceViewer from './lib/PieceViewer.svelte';
  import PieceList from './lib/PieceList.svelte';
  import { uploadModel, sliceJob, reassignOrphans, progressStream, listJobs, getJob, updateJobMeta } from './lib/api';
  import type { SliceParams, SliceResult, ViewMode, JobSummary, CameraOrientation } from './types';
  import { DEFAULT_PARAMS } from './types';

  let activeTab: 'new' | 'previous' = $state('new');
  let file: File | null = $state(null);
  let params: SliceParams = $state({ ...DEFAULT_PARAMS });
  let processing = $state(false);
  let progress = $state('');
  let error = $state('');
  let result: SliceResult | null = $state(null);
  let jobId = $state('');
  let processed = $state(false);
  let viewMode: ViewMode = $state('assembled');
  let showTexture = $state(true);
  let pieceVisibility = $state<boolean[]>([]);
  let jobs: JobSummary[] = $state([]);
  let loadingJobs = $state(false);

  let prevFile: File | null = $state(null);
  $effect(() => {
    if (file && file !== prevFile) {
      prevFile = file;
      result = null;
      processed = false;
      jobId = '';
      pieceVisibility = [];
    }
  });

  let piecePaths: string[] = $state([]);
  let backPiecePaths: string[] = $state([]);
  let consolidatedPath = $state('');

  $effect(() => {
    if (result?.piece_count) {
      piecePaths = result.pieces.map(p => p.path);
      backPiecePaths = result.pieces.map(p => p.back_path ?? null).filter((p): p is string => p !== null) ?? [];
      consolidatedPath = result.consolidated ?? '';
    } else if (result?.normalized_glb) {
      piecePaths = [];
      backPiecePaths = [];
      consolidatedPath = result.normalized_glb;
    } else {
      piecePaths = [];
      backPiecePaths = [];
      consolidatedPath = '';
    }
  });

  let progressCleanup: (() => void) | null = null;

  let captureCamera: (() => CameraOrientation) | null = $state(null);
  let jobName = $state('');
  let orientation: CameraOrientation | null = $state(null);
  let nameSaved = $state(false);

  function resultFromJob(data: SliceResult, initialMode?: ViewMode) {
    result = data;
    jobId = data.job_id;
    jobName = data.name ?? '';
    orientation = data.orientation ?? null;
    nameSaved = false;
    pieceVisibility = data.pieces.map(() => true);
    viewMode = initialMode ?? (data.piece_count > 0 ? 'assembled' : 'assembled');
    processed = data.piece_count > 0;
  }

  function awaitProgress(
    jid: string,
    onComplete: (res: SliceResult) => void,
  ) {
    progressCleanup = progressStream(
      jid,
      (msg) => { progress = msg; },
      (res) => {
        processing = false;
        onComplete(res);
        progress = '';
      },
      (err) => {
        processing = false;
        error = err;
        progress = '';
      },
    );
  }

  async function handleUpload() {
    if (!file) {
      error = 'Please select a model file first.';
      return;
    }
    processing = true;
    error = '';
    progress = 'Uploading...';
    result = null;
    processed = false;
    pieceVisibility = [];

    try {
      const { job_id } = await uploadModel(file);
      jobId = job_id;
      awaitProgress(job_id, (res) => {
        resultFromJob(res);
      });
    } catch (e) {
      processing = false;
      error = e instanceof Error ? e.message : String(e);
      progress = '';
    }
  }

  async function handleSlice() {
    if (!jobId) return;
    processing = true;
    error = '';
    progress = 'Slicing...';
    pieceVisibility = [];

    try {
      await sliceJob(jobId, params);
      awaitProgress(jobId, (res) => {
        resultFromJob(res);
      });
    } catch (e) {
      processing = false;
      error = e instanceof Error ? e.message : String(e);
      progress = '';
    }
  }

  async function handleOrphans() {
    if (!jobId) return;
    processing = true;
    error = '';
    progress = 'Reassigning orphans...';

    try {
      await reassignOrphans(jobId);
      awaitProgress(jobId, (res) => {
        resultFromJob(res);
      });
    } catch (e) {
      processing = false;
      error = e instanceof Error ? e.message : String(e);
      progress = '';
    }
  }

  async function handleLoadPrevious() {
    activeTab = 'previous';
    loadingJobs = true;
    error = '';
    try {
      jobs = await listJobs();
    } catch (e) {
      error = 'Failed to load previous jobs';
    } finally {
      loadingJobs = false;
    }
  }

  async function handleSelectJob(jid: string) {
    error = '';
    progress = '';
    try {
      const data = await getJob(jid);
      resultFromJob(data, 'assembled');
      activeTab = 'new';
      jobId = data.job_id;
    } catch (e) {
      error = e instanceof Error ? e.message : String(e);
    }
  }

  function cancelProgress() {
    progressCleanup?.();
    progressCleanup = null;
    processing = false;
    progress = '';
  }

  async function saveView() {
    const cam = captureCamera?.();
    if (!cam || !result) return;
    try {
      await updateJobMeta(result.job_id, { orientation: cam });
      orientation = cam;
    } catch (e) {
      error = 'Failed to save view angle';
    }
  }

  async function saveName() {
    if (!result) return;
    try {
      await updateJobMeta(result.job_id, { name: jobName });
      nameSaved = true;
      setTimeout(() => (nameSaved = false), 1500);
    } catch (e) {
      error = 'Failed to save name';
    }
  }
</script>

<div class="app-layout">
  <aside class="sidebar">
    <h1>Jigsaw Slicer</h1>

    <div class="tab-bar">
      <button class="tab-btn" class:active={activeTab === 'new'}
              onclick={() => (activeTab = 'new')}>Slice New</button>
      <button class="tab-btn" class:active={activeTab === 'previous'}
              onclick={handleLoadPrevious}>Load Previous</button>
    </div>

    {#if activeTab === 'new'}
      <section class="section">
        <h2>Model</h2>
        <FilePicker bind:file />
        <button class="btn btn-upload" onclick={handleUpload} disabled={processing || !file}>
          {processing && !result && progress ? 'Uploading...' : 'Upload'}
        </button>
        {#if result}
          <span class="model-loaded">{result.piece_count > 0 ? `${result.piece_count} pieces` : 'Loaded'}</span>
        {/if}
      </section>

      {#if result}
        <section class="section">
          <h2>Parameters</h2>
          <ParamForm bind:params />
        </section>

        <button class="btn btn-slice" onclick={handleSlice} disabled={processing}>
          {processing ? 'Slicing...' : processed ? 'Re-Slice' : 'Slice'}
        </button>

        {#if processed}
          <button class="btn btn-orphans" onclick={handleOrphans} disabled={processing}>
            Reassign Orphans
          </button>
        {/if}
      {/if}

      {#if progress && processing}
        <div class="progress">
          {progress}
          <button class="cancel-btn" onclick={cancelProgress}>Cancel</button>
        </div>
      {/if}
      {#if error}
        <div class="error">{error}</div>
      {/if}
    {:else}
      <section class="section">
        <h2>Previous Jobs</h2>
        {#if loadingJobs}
          <p class="info-text">Loading...</p>
        {:else if jobs.length === 0}
          <p class="info-text">No previous jobs found.</p>
        {:else}
          <ul class="job-list">
            {#each jobs as job}
              <li>
                <button class="job-item" onclick={() => handleSelectJob(job.job_id)}>
                  <span class="job-name" title={job.name || job.source_model}>{job.name || job.source_model || job.job_id}</span>
                  <span class="job-meta">{job.piece_count} pieces &middot; {job.created_at}</span>
                </button>
              </li>
            {/each}
          </ul>
        {/if}
      </section>
      {#if error}
        <div class="error">{error}</div>
      {/if}
    {/if}
  </aside>

  <main class="main">
    <PieceViewer
      bind:piecePaths
      bind:backPiecePaths
      bind:consolidatedPath
      bind:jobId
      bind:viewMode
      bind:pieceVisible={pieceVisibility}
      bind:showTexture
      bind:cameraCaptureRef={captureCamera}
      bind:initialOrientation={orientation}
    />
  </main>

  {#if processed && result}
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

      <section class="meta-section">
        <div class="meta-field">
          <label for="job-name">Name</label>
          <div class="meta-row">
            <input id="job-name" type="text" placeholder="Enter name..." bind:value={jobName} />
            <button class="meta-btn" onclick={saveName}>
              {nameSaved ? 'Saved' : 'Save'}
            </button>
          </div>
        </div>
        <button class="meta-btn meta-btn-wide" onclick={saveView}>
          Set Default View
        </button>
      </section>

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
  .tab-bar {
    display: flex;
    gap: 0;
    background: #2a2a3e;
    border-radius: 6px;
    overflow: hidden;
    flex-shrink: 0;
  }
  .tab-btn {
    flex: 1;
    padding: 0.45rem 0.25rem;
    border: none;
    background: transparent;
    color: #888;
    font-size: 0.75rem;
    cursor: pointer;
    text-transform: uppercase;
    letter-spacing: 0.03em;
    transition: all 0.15s;
  }
  .tab-btn.active {
    background: #4f8cff;
    color: #fff;
  }
  .tab-btn:hover:not(.active) {
    color: #ccc;
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
  .btn-upload {
    padding: 0.5rem 1rem;
    font-size: 0.9rem;
    font-weight: 500;
    background: #3a7b5f;
    color: #fff;
    border: none;
    border-radius: 6px;
    cursor: pointer;
    transition: background 0.15s;
  }
  .btn-upload:hover:not(:disabled) { background: #2e694e; }
  .btn-upload:disabled { opacity: 0.4; cursor: not-allowed; }
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
  .btn-orphans {
    padding: 0.5rem 1rem;
    font-size: 0.85rem;
    font-weight: 500;
    background: #8c6a3f;
    color: #fff;
    border: none;
    border-radius: 6px;
    cursor: pointer;
    transition: background 0.15s;
  }
  .btn-orphans:hover:not(:disabled) { background: #6e502e; }
  .btn-orphans:disabled { opacity: 0.4; cursor: not-allowed; }
  .model-loaded {
    font-size: 0.75rem;
    color: #4f8cff;
    font-family: monospace;
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
  .texture-toggle {
    flex-shrink: 0;
  }
  .meta-section {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    padding: 0.5rem 0;
    border-bottom: 1px solid #333;
    flex-shrink: 0;
  }
  .meta-field label {
    display: block;
    font-size: 0.7rem;
    text-transform: uppercase;
    color: #888;
    margin-bottom: 0.25rem;
  }
  .meta-row {
    display: flex;
    gap: 0.35rem;
  }
  .meta-row input {
    flex: 1;
    padding: 0.35rem 0.5rem;
    background: #222;
    border: 1px solid #444;
    border-radius: 4px;
    color: #eee;
    font-size: 0.8rem;
    min-width: 0;
  }
  .meta-row input:focus {
    outline: none;
    border-color: #4f8cff;
  }
  .meta-btn {
    padding: 0.35rem 0.6rem;
    background: #2a2a3e;
    color: #ccc;
    border: 1px solid #444;
    border-radius: 4px;
    font-size: 0.7rem;
    cursor: pointer;
    white-space: nowrap;
    transition: background 0.15s;
  }
  .meta-btn:hover { background: #333; }
  .meta-btn-wide {
    width: 100%;
  }
  .progress {
    padding: 0.5rem;
    background: #2a2a3e;
    border-radius: 4px;
    font-size: 0.85rem;
    color: #4f8cff;
    font-family: monospace;
    display: flex;
    justify-content: space-between;
    align-items: center;
  }
  .cancel-btn {
    padding: 0.2rem 0.5rem;
    font-size: 0.7rem;
    background: #444;
    color: #ccc;
    border: none;
    border-radius: 3px;
    cursor: pointer;
  }
  .cancel-btn:hover { background: #555; }
  .error {
    padding: 0.5rem;
    background: #3e1a1a;
    border-radius: 4px;
    font-size: 0.85rem;
    color: #ff6b6b;
  }
  .info-text {
    font-size: 0.85rem;
    color: #888;
    padding: 0.5rem 0;
  }
  .job-list {
    list-style: none;
    padding: 0;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
  }
  .job-item {
    width: 100%;
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 0.15rem;
    padding: 0.5rem;
    background: #222;
    border: 1px solid #333;
    border-radius: 4px;
    cursor: pointer;
    text-align: left;
    color: #ccc;
    font-size: 0.8rem;
    transition: background 0.15s;
  }
  .job-item:hover { background: #2a2a3e; }
  .job-name {
    font-weight: 600;
    color: #eee;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    max-width: 100%;
  }
  .job-meta {
    font-size: 0.7rem;
    color: #888;
  }
</style>
