<script lang="ts">
  let { file = $bindable(null as File | null) }: { file?: File | null } = $props();
  let inputEl: HTMLInputElement;

  function onChange() {
    file = inputEl.files?.[0] ?? null;
  }
</script>

<div class="file-picker">
  <button onclick={() => inputEl.click()} class="btn btn-primary">
    Browse...
  </button>
  <input bind:this={inputEl} type="file" accept=".glb,.gltf"
         onchange={onChange} style="display:none" />
  {#if file}
    <span class="file-path" title={file.name}>
      {file.name}
    </span>
  {:else}
    <span class="file-path placeholder">No model selected</span>
  {/if}
</div>

<style>
  .file-picker {
    display: flex;
    align-items: center;
    gap: 0.75rem;
  }
  .file-path {
    font-size: 0.875rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .file-path.placeholder {
    color: #888;
  }
</style>
