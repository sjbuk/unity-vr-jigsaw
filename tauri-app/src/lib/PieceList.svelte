<script lang="ts">
  import * as THREE from 'three';
  import type { PieceInfo } from '../types';

  let {
    pieces = [],
    visible = $bindable([] as boolean[]),
    fixOrphanMode = $bindable(false),
    destinationPiece = $bindable(null as number | null),
  }: {
    pieces?: PieceInfo[];
    visible?: boolean[];
    fixOrphanMode?: boolean;
    destinationPiece?: number | null;
  } = $props();

  function pieceColor(index: number): string {
    return new THREE.Color().setHSL((index * 0.618033988749895) % 1.0, 0.65, 0.45).getStyle();
  }

  function toggle(idx: number) {
    if (fixOrphanMode) {
      destinationPiece = idx;
    } else {
      visible[idx] = !visible[idx];
      visible = [...visible]; // trigger reactivity
    }
  }
</script>

<div class="piece-list">
  <h3>Pieces ({pieces.length})</h3>
  <ul>
    {#each pieces as piece, i}
      <!-- svelte-ignore a11y_no_noninteractive_element_interactions a11y_no_noninteractive_tabindex -->
      <li
        class:hidden={i < visible.length && !visible[i]}
        class:selected={fixOrphanMode && destinationPiece === piece.index}
        onclick={() => toggle(i)}
        onkeydown={(e) => e.key === 'Enter' && toggle(i)}
        tabindex="0"
      >
        <span class="dot" class:off={i < visible.length && !visible[i]} style="color: {i < visible.length && !visible[i] ? '#555' : pieceColor(piece.index)}">●</span>
        <span class="piece-index" style="color: {pieceColor(piece.index)}">#{piece.index}</span>
        <span class="piece-verts">{piece.vertices.toLocaleString()} verts</span>
        {#if fixOrphanMode && destinationPiece === piece.index}
          <span class="dest-badge">→ Target</span>
        {/if}
      </li>
    {/each}
  </ul>
</div>

<style>
  .piece-list {
    padding: 0;
    flex: 1;
    display: flex;
    flex-direction: column;
    min-height: 0;
  }
  .piece-list h3 {
    margin: 0 0 0.5rem;
    font-size: 0.85rem;
    color: #aaa;
    text-transform: uppercase;
    letter-spacing: 0.05em;
    flex-shrink: 0;
  }
  ul {
    list-style: none;
    padding: 0;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 0.2rem;
    overflow-y: auto;
    flex: 1;
    min-height: 0;
  }
  li {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    font-size: 0.8rem;
    padding: 0.35rem 0.5rem;
    background: #222;
    border-radius: 4px;
    cursor: pointer;
    transition: background 0.1s, opacity 0.15s;
  }
  li:hover {
    background: #2a2a3e;
  }
  li.hidden {
    opacity: 0.35;
  }
  .dot {
    font-size: 0.6rem;
  }
  .dot.off {
    color: #555;
  }
  .piece-index {
    font-weight: 600;
    min-width: 2.5rem;
  }
  .piece-verts {
    color: #888;
  }
  li.selected {
    background: #2a4a0a;
    border-left: 4px solid #8fbc3a;
    padding-left: calc(0.5rem - 4px);
  }
  .dest-badge {
    margin-left: auto;
    font-size: 0.7rem;
    color: #b0e050;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    text-shadow: 0 0 6px rgba(143, 188, 58, 0.5);
  }
</style>
