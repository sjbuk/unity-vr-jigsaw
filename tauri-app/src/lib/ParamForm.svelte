<script lang="ts">
  import type { SliceParams } from '../types';

  let { params = $bindable(), totalFaces = 0 }:
    { params: SliceParams; totalFaces?: number } = $props();

  const percents = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

  let previewPercent = $state(50);

  $effect(() => {
    if (totalFaces > 0) {
      params.preview_faces = Math.max(4, Math.floor(totalFaces * previewPercent / 100));
    }
  });
</script>

<div class="param-form">
  <div class="field">
    <label for="pieces">Pieces</label>
    <input id="pieces" type="range" min="2" max="100" bind:value={params.pieces} />
    <span class="value">{params.pieces}</span>
  </div>

  <div class="field">
    <label for="gap">Gap</label>
    <input id="gap" type="range" min="0" max="0.01" step="0.0001" bind:value={params.gap} />
    <span class="value">{params.gap.toFixed(4)}</span>
  </div>

  <div class="field field-block">
    <span class="field-label">Preview Faces</span>
    {#if totalFaces > 0}
      <div class="pct-bar">
        {#each percents as pct}
          <button
            class="pct-btn"
            class:active={previewPercent === pct}
            onclick={() => (previewPercent = pct)}
          >{pct}%</button>
        {/each}
      </div>
      <span class="pct-result">{params.preview_faces.toLocaleString()} faces at {previewPercent}%</span>
    {:else}
      <span class="na">Upload a model to configure</span>
    {/if}
  </div>

  <div class="field">
    <label for="seed">Seed (optional)</label>
    <input
      id="seed"
      type="number"
      min="0"
      placeholder="random"
      bind:value={params.seed}
    />
  </div>

</div>

<style>
  .param-form {
    display: flex;
    flex-direction: column;
    gap: 0.75rem;
  }
  .field {
    display: grid;
    grid-template-columns: 1fr 2fr auto;
    align-items: center;
    gap: 0.5rem;
  }
  .field label {
    font-size: 0.8rem;
    font-weight: 500;
    color: #ccc;
  }
  .field-label {
    font-size: 0.8rem;
    font-weight: 500;
    color: #ccc;
  }
  .field input[type='range'] {
    width: 100%;
    accent-color: #4f8cff;
  }
  .field .value {
    font-size: 0.8rem;
    font-family: monospace;
    color: #4f8cff;
    min-width: 3.5rem;
    text-align: right;
  }
  .field .na {
    font-size: 0.8rem;
    color: #666;
    font-family: monospace;
  }
  .field input[type='number'] {
    padding: 0.3rem 0.5rem;
    background: #2a2a2a;
    border: 1px solid #444;
    border-radius: 4px;
    color: #eee;
    font-size: 0.85rem;
    width: 100%;
  }
  .field-block {
    grid-template-columns: 1fr;
    gap: 0.3rem;
  }
  .pct-bar {
    display: flex;
    gap: 2px;
    flex-wrap: wrap;
  }
  .pct-btn {
    flex: 1;
    min-width: 2rem;
    padding: 0.2rem 0.15rem;
    border: 1px solid #444;
    border-radius: 3px;
    background: #2a2a2a;
    color: #888;
    font-size: 0.65rem;
    cursor: pointer;
    transition: all 0.15s;
  }
  .pct-btn.active {
    background: #4f8cff;
    color: #fff;
    border-color: #4f8cff;
  }
  .pct-btn:hover:not(.active) {
    background: #333;
    color: #ccc;
  }
  .pct-result {
    font-size: 0.7rem;
    color: #4f8cff;
    font-family: monospace;
  }
</style>
