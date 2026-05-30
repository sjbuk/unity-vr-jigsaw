<script lang="ts">
  import type { SliceParams } from '../types';

  let { params = $bindable() }: { params: SliceParams } = $props();
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
  .field select,
  .field input[type='number'] {
    padding: 0.3rem 0.5rem;
    background: #2a2a2a;
    border: 1px solid #444;
    border-radius: 4px;
    color: #eee;
    font-size: 0.85rem;
  }
  .field input[type='number'] {
    width: 100%;
  }
</style>
