<script lang="ts">
  import { onMount } from 'svelte';
  import * as THREE from 'three';
  import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  import { readFile } from '@tauri-apps/plugin-fs';
  import type { ViewMode } from '../types';

  let {
    piecePaths = $bindable([]),
    consolidatedPath = $bindable(''),
    viewMode = $bindable<ViewMode>('split'),
  }: {
    piecePaths?: string[];
    consolidatedPath?: string;
    viewMode?: ViewMode;
  } = $props();

  let container: HTMLDivElement;
  let renderer: THREE.WebGLRenderer | null = null;
  let scene: THREE.Scene | null = null;
  let camera: THREE.PerspectiveCamera | null = null;
  let controls: OrbitControls | null = null;
  let ready = $state(false);

  const meshes: THREE.Mesh[] = [];
  let loader: GLTFLoader;

  function initScene() {
    if (!container) return;
    scene = new THREE.Scene();
    scene.background = new THREE.Color(0x1a1a2e);

    camera = new THREE.PerspectiveCamera(
      45,
      container.clientWidth / container.clientHeight,
      0.01,
      100,
    );
    camera.position.set(2, 1.5, 2);

    renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setSize(container.clientWidth, container.clientHeight);
    renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    renderer.toneMappingExposure = 1.0;
    container.appendChild(renderer.domElement);

    controls = new OrbitControls(camera, renderer.domElement);
    controls.target.set(0, 0, 0);
    controls.enableDamping = true;
    controls.dampingFactor = 0.05;
    controls.update();

    const hemi = new THREE.HemisphereLight(0xffffff, 0x444444, 0.6);
    scene.add(hemi);

    const dir = new THREE.DirectionalLight(0xffffff, 1.0);
    dir.position.set(5, 10, 7);
    scene.add(dir);

    const fill = new THREE.DirectionalLight(0x4488ff, 0.3);
    fill.position.set(-5, 0, 5);
    scene.add(fill);

    const grid = new THREE.GridHelper(4, 20, 0x444466, 0x333355);
    scene.add(grid);

    loader = new GLTFLoader();
    animate();
  }

  function animate() {
    requestAnimationFrame(animate);
    controls?.update();
    if (renderer && scene && camera) {
      renderer.render(scene, camera);
    }
  }

  function clearScene() {
    if (!scene) return;
    for (const m of meshes) {
      scene.remove(m);
      if (m.geometry) m.geometry.dispose();
      if (Array.isArray(m.material)) {
        m.material.forEach((mat) => mat.dispose());
      } else if (m.material) {
        m.material.dispose();
      }
    }
    meshes.length = 0;
  }

  function pieceColor(index: number): THREE.Color {
    const golden = (Math.sqrt(5) - 1) / 2;
    const hue = (index * golden) % 1.0;
    return new THREE.Color().setHSL(hue, 0.65, 0.45);
  }

  async function loadGLB(path: string): Promise<THREE.Group> {
    const data = await readFile(path);
    const buf = data.buffer.slice(
      data.byteOffset,
      data.byteOffset + data.byteLength,
    );
    return loader.parseAsync(buf, '');
  }

  function addMeshToScene(
    mesh: THREE.Mesh,
    color: THREE.Color,
    offset?: THREE.Vector3,
  ) {
    mesh.material = new THREE.MeshStandardMaterial({
      color,
      roughness: 0.4,
      metalness: 0.1,
    });
    mesh.castShadow = true;
    if (offset) mesh.position.copy(offset);
    scene!.add(mesh);
    meshes.push(mesh);
  }

  function fitCamera() {
    if (!controls || !camera || meshes.length === 0) return;
    const box = new THREE.Box3();
    for (const m of meshes) box.expandByObject(m);
    const size = box.getSize(new THREE.Vector3()).length();
    const center = box.getCenter(new THREE.Vector3());
    controls.target.copy(center);
    camera.position.set(
      center.x + size * 1.2,
      center.y + size * 0.5,
      center.z + size * 1.2,
    );
    controls.update();
  }

  async function loadSplitPieces(paths: string[]) {
    if (!scene) return;
    clearScene();

    const centers: THREE.Vector3[] = [];

    for (let i = 0; i < paths.length; i++) {
      try {
        const group = await loadGLB(paths[i]);
        let box = new THREE.Box3();

        group.traverse((child) => {
          if (child instanceof THREE.Mesh) {
            box.expandByObject(child);
            const color = pieceColor(i);
            addMeshToScene(child, color);
          }
        });

        if (!box.isEmpty()) {
          centers.push(box.getCenter(new THREE.Vector3()));
        } else {
          centers.push(new THREE.Vector3());
        }
      } catch (err) {
        console.error(`Failed to load piece ${i}:`, err);
      }
    }

    // Compute overall center and offset each piece outward
    if (centers.length > 0) {
      const avg = new THREE.Vector3();
      for (const c of centers) avg.add(c);
      avg.divideScalar(centers.length);

      const offsetAmount = 0.008;
      for (let i = 0; i < meshes.length && i < centers.length; i++) {
        const dir = new THREE.Vector3()
          .copy(centers[i])
          .sub(avg)
          .normalize();
        // If piece is exactly at center, nudge upward
        if (dir.length() < 0.001) dir.set(0, 1, 0);
        meshes[i].position.add(dir.multiplyScalar(offsetAmount));
      }
    }

    fitCamera();
  }

  async function loadAssembled(path: string) {
    if (!scene) return;
    clearScene();

    try {
      const group = await loadGLB(path);
      let idx = 0;
      group.traverse((child) => {
        if (child instanceof THREE.Mesh) {
          const color = pieceColor(idx++);
          addMeshToScene(child, color);
        }
      });
      // dispose the wrapper group
      group.traverse((child) => {
        if (child instanceof THREE.Group && child !== group) {
          child.removeFromParent();
        }
      });
    } catch (err) {
      console.error('Failed to load assembled pieces:', err);
    }

    fitCamera();
  }

  $effect(() => {
    if (!ready) return;

    if (viewMode === 'split' && piecePaths.length > 0) {
      loadSplitPieces(piecePaths);
    } else if (viewMode === 'assembled' && consolidatedPath) {
      loadAssembled(consolidatedPath);
    } else {
      clearScene();
    }
  });

  onMount(() => {
    initScene();
    ready = true;

    const ro = new ResizeObserver(() => {
      if (container && camera && renderer) {
        const w = container.clientWidth;
        const h = container.clientHeight;
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
        renderer.setSize(w, h);
      }
    });
    ro.observe(container);

    return () => {
      ro.disconnect();
      renderer?.dispose();
    };
  });
</script>

<div bind:this={container} class="viewer">
  {#if !piecePaths.length && !consolidatedPath}
    <div class="placeholder">
      <p>Select a model and click Slice to begin</p>
    </div>
  {/if}
</div>

<style>
  .viewer {
    width: 100%;
    height: 100%;
    position: relative;
    overflow: hidden;
    border-radius: 8px;
    background: #1a1a2e;
  }
  .placeholder {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #666;
    font-size: 1.1rem;
    pointer-events: none;
  }
</style>
