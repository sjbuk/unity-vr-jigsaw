<script lang="ts">
  import type { PieceInfo } from '../types';

  let { pieces = [] }: { pieces?: PieceInfo[] } = $props();
</script>

<div class="piece-list">
  <h3>Pieces ({pieces.length})</h3>
  <ul>
    {#each pieces as piece}
      <li>
        <span class="piece-index">#{piece.index}</span>
        <span class="piece-verts">{piece.vertices.toLocaleString()} verts</span>
      </li>
    {/each}
  </ul>
</div>

<style>
  .piece-list {
    padding: 1rem;
  }
  .piece-list h3 {
    margin: 0 0 0.5rem;
    font-size: 0.85rem;
    color: #aaa;
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
  ul {
    list-style: none;
    padding: 0;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    max-height: 300px;
    overflow-y: auto;
  }
  li {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    font-size: 0.8rem;
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
  }
</style>
