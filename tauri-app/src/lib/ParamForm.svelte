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
    <label for="mode">Mode</label>
    <select id="mode" bind:value={params.mode}>
      <option value="full_3d">Full 3D</option>
      <option value="shell">Shell</option>
    </select>
  </div>

  {#if params.mode === 'shell'}
    <div class="field">
      <label for="shell_thickness">Shell Thickness</label>
      <input
        id="shell_thickness"
        type="range"
        min="0.001"
        max="0.1"
        step="0.001"
        bind:value={params.shell_thickness}
      />
      <span class="value">{params.shell_thickness.toFixed(3)}</span>
    </div>
  {/if}

  <div class="field">
    <label for="gap">Gap</label>
    <input id="gap" type="range" min="0" max="0.01" step="0.0001" bind:value={params.gap} />
    <span class="value">{params.gap.toFixed(4)}</span>
  </div>

  <div class="field">
    <label for="tab_density">Tab Density</label>
    <input id="tab_density" type="range" min="0" max="1" step="0.05" bind:value={params.tab_density} />
    <span class="value">{params.tab_density.toFixed(2)}</span>
  </div>

  <div class="field">
    <label for="peg_clearance">Peg Clearance</label>
    <input
      id="peg_clearance"
      type="range"
      min="0"
      max="0.01"
      step="0.0001"
      bind:value={params.peg_clearance}
    />
    <span class="value">{params.peg_clearance.toFixed(4)}</span>
  </div>

  <div class="field">
    <label for="snap_radius_min">Snap Radius Min</label>
    <input
      id="snap_radius_min"
      type="range"
      min="0.001"
      max="0.1"
      step="0.001"
      bind:value={params.snap_radius_min}
    />
    <span class="value">{params.snap_radius_min.toFixed(3)}</span>
  </div>

  <div class="field">
    <label for="snap_radius_max">Snap Radius Max</label>
    <input
      id="snap_radius_max"
      type="range"
      min="0.01"
      max="0.5"
      step="0.001"
      bind:value={params.snap_radius_max}
    />
    <span class="value">{params.snap_radius_max.toFixed(3)}</span>
  </div>

  <div class="field">
    <label for="snap_angle_tolerance">Snap Angle Tolerance</label>
    <input
      id="snap_angle_tolerance"
      type="range"
      min="0"
      max="180"
      step="1"
      bind:value={params.snap_angle_tolerance}
    />
    <span class="value">{params.snap_angle_tolerance.toFixed(0)}°</span>
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
