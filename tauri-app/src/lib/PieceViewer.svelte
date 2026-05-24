<script lang="ts">
  import * as THREE from 'three';
  import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  import { readBinaryFile } from './api';
  import type { ViewMode } from '../types';

  let {
    piecePaths = $bindable([]),
    consolidatedPath = $bindable(''),
    viewMode = $bindable<ViewMode>('split'),
    pieceVisible = $bindable<boolean[]>([]),
  }: {
    piecePaths?: string[];
    consolidatedPath?: string;
    viewMode?: ViewMode;
    pieceVisible?: boolean[];
  } = $props();

  let container: HTMLDivElement;
  let renderer: THREE.WebGLRenderer | null = null;
  let scene: THREE.Scene | null = null;
  let camera: THREE.PerspectiveCamera | null = null;
  let controls: OrbitControls | null = null;
  let loadError = $state('');
  let loadingGen = 0;

  const meshes: THREE.Mesh[] = [];
  let loader: GLTFLoader;

  function init() {
    if (!container) return;
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x1a1a2e);

    camera = new THREE.PerspectiveCamera(45, container.clientWidth / container.clientHeight, 0.01, 100);
    camera.position.set(2, 1.5, 2);

    renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(container.clientWidth, container.clientHeight);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    container.appendChild(renderer.domElement);

    controls = new OrbitControls(camera, renderer.domElement);
    controls.target.set(0, 0, 0);
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;
    controls.update();

    scene.add(new THREE.HemisphereLight(0xffffff, 0x444444, 0.6));
    const dl = new THREE.DirectionalLight(0xffffff, 1.0);
    dl.position.set(5, 10, 7);
    scene.add(dl);
    const fl = new THREE.DirectionalLight(0x4488ff, 0.3);
    fl.position.set(-5, 0, 5);
    scene.add(fl);
    scene.add(new THREE.GridHelper(4, 20, 0x444466, 0x333355));

    loader = new GLTFLoader();

    const ro = new ResizeObserver(() => {
      if (container && camera && renderer) {
        camera.aspect = container.clientWidth / container.clientHeight;
        camera.updateProjectionMatrix();
        renderer.setSize(container.clientWidth, container.clientHeight);
      }
    });
    ro.observe(container);

    requestAnimationFrame(function animate() {
      controls?.update();
      if (renderer && scene && camera) renderer.render(scene, camera);
      requestAnimationFrame(animate);
    });
  }

  function base64ToBuffer(b64: string): ArrayBuffer {
    const bin = atob(b64);
    const buf = new ArrayBuffer(bin.length);
    const view = new Uint8Array(buf);
    for (let i = 0; i < bin.length; i++) view[i] = bin.charCodeAt(i);
    return buf;
  }

  function pieceColor(index: number): THREE.Color {
    return new THREE.Color().setHSL((index * 0.618033988749895) % 1.0, 0.65, 0.45);
  }

  function addMesh(m: THREE.Mesh, color: THREE.Color, offset?: THREE.Vector3) {
    m.material = new THREE.MeshStandardMaterial({ color, roughness: 0.4, metalness: 0.1 });
    m.castShadow = true;
    if (offset) m.position.copy(offset);
    scene!.add(m);
    meshes.push(m);
  }

  function fitCamera() {
    if (!controls || !camera || meshes.length === 0) return;
    const box = new THREE.Box3();
    for (const m of meshes) box.expandByObject(m);
    const size = box.getSize(new THREE.Vector3()).length();
    const center = box.getCenter(new THREE.Vector3());
    controls.target.copy(center);
    camera.position.set(center.x + size * 1.2, center.y + size * 0.5, center.z + size * 1.2);
    controls.update();
  }

  function clearScene() {
    if (!scene) return;
    for (const m of meshes) {
      scene.remove(m);
      if (m.geometry) m.geometry.dispose();
      if (Array.isArray(m.material)) m.material.forEach(x => x.dispose());
      else if (m.material) m.material.dispose();
    }
    meshes.length = 0;
    loadError = '';
  }

  function applyVisibility() {
    for (let i = 0; i < meshes.length; i++) {
      meshes[i].visible = i < pieceVisible.length ? pieceVisible[i] : true;
    }
  }

  async function loadSplitPieces(paths: string[]) {
    const gen = ++loadingGen;
    if (!scene) return;
    clearScene();

    const centers: THREE.Vector3[] = [];

    for (let i = 0; i < paths.length; i++) {
      if (gen !== loadingGen) return;
      try {
        const b64 = await readBinaryFile(paths[i]);
        if (gen !== loadingGen) return;
        const buf = base64ToBuffer(b64);
        const group = await loader.parseAsync(buf, '');
        if (gen !== loadingGen) return;

        let box = new THREE.Box3();
        group.scene.traverse(child => {
          if (child instanceof THREE.Mesh) {
            box.expandByObject(child);
            addMesh(child, pieceColor(i));
          }
        });
        centers.push(box.isEmpty() ? new THREE.Vector3() : box.getCenter(new THREE.Vector3()));
      } catch (err) {
        loadError = `Piece ${i}: ${err instanceof Error ? err.message : String(err)}`;
        console.error(err);
      }
    }

    if (gen !== loadingGen) return;

    if (centers.length > 0) {
      const avg = new THREE.Vector3();
      for (const c of centers) avg.add(c);
      avg.divideScalar(centers.length);
      for (let i = 0; i < meshes.length && i < centers.length; i++) {
        const dir = new THREE.Vector3().copy(centers[i]).sub(avg).normalize();
        if (dir.length() < 0.001) dir.set(0, 1, 0);
        meshes[i].position.add(dir.multiplyScalar(0.008));
      }
    }
    applyVisibility();
    fitCamera();
  }

  async function loadAssembled(path: string) {
    const gen = ++loadingGen;
    if (!scene) return;
    clearScene();

    try {
      const b64 = await readBinaryFile(path);
      if (gen !== loadingGen) return;
      const buf = base64ToBuffer(b64);
      const group = await loader.parseAsync(buf, '');
      if (gen !== loadingGen) return;

      let idx = 0;
      group.scene.traverse(child => {
        if (child instanceof THREE.Mesh) addMesh(child, pieceColor(idx++));
      });
      applyVisibility();
      fitCamera();
    } catch (err) {
      loadError = `Assembled: ${err instanceof Error ? err.message : String(err)}`;
    }
  }

  $effect(() => {
    const paths = piecePaths;
    const cpath = consolidatedPath;
    const mode = viewMode;
    const vis = pieceVisible;

    if (!container) return;
    if (!renderer) init();
    if (!scene) return;

    if (mode === 'split' && paths.length > 0) {
      loadSplitPieces(paths);
    } else if (mode === 'assembled' && cpath) {
      loadAssembled(cpath);
    } else {
      clearScene();
    }
  });
</script>

<div bind:this={container} class="viewer">
  {#if !piecePaths.length && !consolidatedPath}
    <div class="placeholder">Select a model or folder to begin</div>
  {/if}
  {#if loadError}
    <div class="error-msg">{loadError}</div>
  {/if}
</div>

<style>
  .viewer {
    width: 100%; height: 100%;
    position: relative; overflow: hidden;
    border-radius: 8px;
    background: #1a1a2e;
  }
  .placeholder {
    position: absolute; inset: 0;
    display: flex; align-items: center; justify-content: center;
    color: #666; font-size: 1.1rem;
    pointer-events: none;
  }
  .error-msg {
    position: absolute; bottom: 1rem; left: 1rem; right: 1rem;
    background: #3e1a1a; color: #ff6b6b;
    padding: 0.5rem; border-radius: 4px;
    font-size: 0.85rem; font-family: monospace;
    z-index: 10;
  }
</style>
