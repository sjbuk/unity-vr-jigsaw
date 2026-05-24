<script lang="ts">
  import type { PieceInfo } from '../types';

  let {
    pieces = [],
    visible = $bindable([] as boolean[]),
  }: {
    pieces?: PieceInfo[];
    visible?: boolean[];
  } = $props();

  function toggle(idx: number) {
    visible[idx] = !visible[idx];
    visible = [...visible]; // trigger reactivity
  }
</script>

<div class="piece-list">
  <h3>Pieces ({pieces.length})</h3>
  <ul>
    {#each pieces as piece, i}
      <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
      <li
        class:hidden={i < visible.length && !visible[i]}
        onclick={() => toggle(i)}
        onkeydown={(e) => e.key === 'Enter' && toggle(i)}
        tabindex="0"
        role="button"
      >
        <span class="dot" class:off={i < visible.length && !visible[i]}>●</span>
        <span class="piece-index">#{piece.index}</span>
        <span class="piece-verts">{piece.vertices.toLocaleString()} verts</span>
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
    color: #4f8cff;
    font-size: 0.6rem;
  }
  .dot.off {
    color: #555;
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
