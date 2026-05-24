<script lang="ts">
  import type { PieceInfo } from '../types';

  let { pieces = $bindable([]), resultDir = '' }: { pieces?: PieceInfo[]; resultDir?: string } = $props();
</script>

<div class="piece-list">
  <h3>Pieces ({pieces.length})</h3>
  <ul>
    {#each pieces as piece}
      <li>
        <span class="piece-index">#{piece.index}</span>
        <span class="piece-verts">{piece.vertices.toLocaleString()} verts</span>
        <a
          href={piece.path}
          download={`piece_${String(piece.index).padStart(4, '0')}.glb`}
          class="btn-download"
        >Download</a>
      </li>
    {/each}
  </ul>
</div>

<style>
  .piece-list {
    padding: 1rem;
    border-top: 1px solid #333;
  }
  .piece-list h3 {
    margin: 0 0 0.5rem;
    font-size: 0.9rem;
    color: #aaa;
  }
  ul {
    list-style: none;
    padding: 0;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 0.3rem;
    max-height: 200px;
    overflow-y: auto;
  }
  li {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    font-size: 0.85rem;
    padding: 0.25rem 0.5rem;
    background: #222;
    border-radius: 4px;
  }
  .piece-index {
    color: #4f8cff;
    font-weight: 600;
    min-width: 2.5rem;
  }
  .piece-verts {
    color: #888;
    flex: 1;
  }
  .btn-download {
    padding: 0.2rem 0.6rem;
    background: #4f8cff;
    color: #fff;
    border-radius: 4px;
    text-decoration: none;
    font-size: 0.75rem;
  }
</style>
