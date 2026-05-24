<script lang="ts">
  import { open } from '@tauri-apps/plugin-dialog';

  let { selectedPath = $bindable('') }: { selectedPath?: string } = $props();

  async function pickFile() {
    const result = await open({
      multiple: false,
      filters: [
        {
          name: '3D Models',
          extensions: ['glb', 'gltf'],
        },
      ],
    });
    if (result) {
      selectedPath = result;
    }
  }
</script>

<div class="file-picker">
  <button onclick={pickFile} class="btn btn-primary">
    Browse…
  </button>
  {#if selectedPath}
    <span class="file-path" title={selectedPath}>
      {selectedPath.split(/[/\\]/).pop()}
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
