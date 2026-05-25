<script lang="ts">
  import * as THREE from 'three';
  import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  import { convertFileSrc } from '@tauri-apps/api/core';
  import type { ViewMode } from '../types';

  let {
    piecePaths = $bindable([]),
    backPiecePaths = $bindable([]),
    consolidatedPath = $bindable(''),
    viewMode = $bindable<ViewMode>('split'),
    pieceVisible = $bindable<boolean[]>([]),
    showTexture = $bindable(false),
  }: {
    piecePaths?: string[];
    backPiecePaths?: string[];
    consolidatedPath?: string;
    viewMode?: ViewMode;
    pieceVisible?: boolean[];
    showTexture?: boolean;
  } = $props();

  let container: HTMLDivElement;
  let renderer: THREE.WebGLRenderer | null = null;
  let scene: THREE.Scene | null = null;
  let camera: THREE.PerspectiveCamera | null = null;
  let controls: OrbitControls | null = null;
  let loadError = $state('');
  let loadingGen = 0;

  const meshes: THREE.Mesh[] = [];
  const originalMaterials: (THREE.Material | THREE.Material[] | null)[] = [];
const meshPieceIndex: number[] = [];
let loader: GLTFLoader;

let raycaster: THREE.Raycaster | null = null;
let draggedPieceIndex: number | null = null;
let dragOffset = new THREE.Vector3();
let dragPlane = new THREE.Plane();
let snappedPieces = new Set<number>();
let pieceTargets: Map<number, { pos: THREE.Vector3; quat: THREE.Quaternion }> = new Map();
let mouseNDC = new THREE.Vector2();
let isSimDragging = false;
let cleanupSimListeners: (() => void) | null = null;

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

    scene.add(new THREE.AmbientLight(0xffffff, 0.5));
    scene.add(new THREE.HemisphereLight(0xffffff, 0x444444, 0.8));
    const dl = new THREE.DirectionalLight(0xffffff, 1.5);
    dl.position.set(5, 10, 7);
    scene.add(dl);
    const fl = new THREE.DirectionalLight(0x8888ff, 0.5);
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

  function pieceColor(index: number): THREE.Color {
    return new THREE.Color().setHSL((index * 0.618033988749895) % 1.0, 0.65, 0.45);
  }

  function addMesh(m: THREE.Mesh, pieceIdx: number, offset?: THREE.Vector3) {
    m.geometry.computeVertexNormals();
    const meshIdx = meshes.length;
    originalMaterials.push(m.material ?? null);
    meshPieceIndex.push(pieceIdx);
    applyMeshMaterial(m, pieceIdx, meshIdx);
    m.castShadow = true;
    if (offset) m.position.copy(offset);
    scene!.add(m);
    meshes.push(m);
  }

  function applyMeshMaterial(m: THREE.Mesh, pieceIdx: number, meshIdx: number) {
    if (showTexture && originalMaterials[meshIdx]) {
      const orig = originalMaterials[meshIdx];
      m.material = Array.isArray(orig) ? orig[0] : orig!;
    } else {
      m.material = new THREE.MeshPhongMaterial({
        color: pieceColor(pieceIdx),
        shininess: 30,
        flatShading: false,
      });
    }
  }

  function updateMaterials() {
    for (let i = 0; i < meshes.length; i++) {
      applyMeshMaterial(meshes[i], meshPieceIndex[i], i);
    }
  }

  const snapRadius = 0.2;

  function arrangeOnWall() {
    if (pieceTargets.size === 0 || meshes.length === 0) return;
    const bbox = new THREE.Box3();
    for (const [, target] of pieceTargets) {
      bbox.expandByPoint(target.pos);
    }
    const center = bbox.getCenter(new THREE.Vector3());
    const size = bbox.getSize(new THREE.Vector3());
    const n = pieceTargets.size;
    const cols = Math.ceil(Math.sqrt(n));
    const rows = Math.ceil(n / cols);

    const pieceApprox = Math.max(size.x, size.y) / Math.max(cols, rows);
    const cellSize = Math.max(pieceApprox * 4.0, 0.4);

    const gridHeight = (rows - 1) * cellSize;
    const minY = center.y - gridHeight / 2;
    const yOffset = Math.max(0, 0.1 - minY);

    const sortedIndices = Array.from(pieceTargets.keys()).sort((a, b) => a - b);
    for (let i = 0; i < sortedIndices.length; i++) {
      const pieceIdx = sortedIndices[i];
      const col = i % cols;
      const row = Math.floor(i / cols);
      const offsetX = (col - (cols - 1) / 2) * cellSize;
      const offsetY = ((rows - 1) / 2 - row) * cellSize;
      const pos = new THREE.Vector3(
        center.x + offsetX,
        center.y + offsetY + yOffset,
        center.z,
      );
      for (let j = 0; j < meshes.length; j++) {
        if (meshPieceIndex[j] === pieceIdx) {
          meshes[j].position.copy(pos);
        }
      }
    }
  }

  function setupSimListeners() {
    cleanupSimListeners?.();
    if (!renderer || !camera || !controls) return;
    const el = renderer.domElement;

    const camDir = new THREE.Vector3();
    const planeHit = new THREE.Vector3();
    const screenTarget = new THREE.Vector3();

    const updateMouseFromEvent = (e: PointerEvent) => {
      const rect = el.getBoundingClientRect();
      mouseNDC.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
      mouseNDC.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
    };

    const onPointerDown = (e: PointerEvent) => {
      if (!raycaster) return;
      updateMouseFromEvent(e);
      raycaster.setFromCamera(mouseNDC, camera!);
      const draggableIndices: number[] = [];
      for (let i = 0; i < meshes.length; i++) {
        if (!snappedPieces.has(meshPieceIndex[i])) {
          draggableIndices.push(i);
        }
      }
      const draggable = draggableIndices.map((i) => meshes[i]);
      const intersects = raycaster.intersectObjects(draggable, false);
      if (intersects.length > 0) {
        const obj = intersects[0].object as THREE.Mesh;
        const idx = meshes.indexOf(obj);
        if (idx >= 0) {
          draggedPieceIndex = meshPieceIndex[idx];
          isSimDragging = true;
          controls!.enabled = false;
          camera!.getWorldDirection(camDir);
          dragPlane.setFromNormalAndCoplanarPoint(camDir, obj.position);
          if (raycaster.ray.intersectPlane(dragPlane, planeHit)) {
            dragOffset.copy(obj.position).sub(planeHit);
          }
          e.stopPropagation();
          e.preventDefault();
        }
      }
    };

    const onPointerMove = (e: PointerEvent) => {
      if (!isSimDragging || draggedPieceIndex === null) return;
      updateMouseFromEvent(e);
      raycaster!.setFromCamera(mouseNDC, camera!);
      if (raycaster!.ray.intersectPlane(dragPlane, planeHit)) {
        camDir.copy(planeHit).add(dragOffset);
        for (let i = 0; i < meshes.length; i++) {
          if (meshPieceIndex[i] === draggedPieceIndex) {
            meshes[i].position.copy(camDir);
          }
        }

        const target = pieceTargets.get(draggedPieceIndex);
        if (target) {
          screenTarget.copy(target.pos).project(camera!);
          const dx = screenTarget.x - mouseNDC.x;
          const dy = screenTarget.y - mouseNDC.y;
          const distScreen = Math.sqrt(dx * dx + dy * dy);
          if (camDir.distanceTo(target.pos) < snapRadius || distScreen < 0.08) {
            for (let i = 0; i < meshes.length; i++) {
              if (meshPieceIndex[i] === draggedPieceIndex) {
                meshes[i].position.copy(target.pos);
                meshes[i].quaternion.copy(target.quat);
              }
            }
            snappedPieces.add(draggedPieceIndex);
            isSimDragging = false;
            draggedPieceIndex = null;
            controls!.enabled = true;
            e.stopPropagation();
            e.preventDefault();
            return;
          }
        }
      }
      e.stopPropagation();
      e.preventDefault();
    };

    const onPointerUp = (e: PointerEvent) => {
      if (isSimDragging) {
        isSimDragging = false;
        draggedPieceIndex = null;
        controls!.enabled = true;
        e.stopPropagation();
        e.preventDefault();
      }
    };

    el.addEventListener('pointerdown', onPointerDown, { capture: true });
    el.addEventListener('pointermove', onPointerMove, { capture: true });
    el.addEventListener('pointerup', onPointerUp, { capture: true });

    cleanupSimListeners = () => {
      el.removeEventListener('pointerdown', onPointerDown, { capture: true });
      el.removeEventListener('pointermove', onPointerMove, { capture: true });
      el.removeEventListener('pointerup', onPointerUp, { capture: true });
    };
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
    }
    meshes.length = 0;
    originalMaterials.length = 0;
    meshPieceIndex.length = 0;
    loadError = '';
  }

  function applyVisibility() {
    for (let i = 0; i < meshes.length; i++) {
      const pieceIdx = meshPieceIndex[i];
      meshes[i].visible = pieceIdx < pieceVisible.length ? pieceVisible[pieceIdx] : true;
    }
  }

  async function loadSplitPieces(frontPaths: string[], backPaths: string[]) {
    const gen = ++loadingGen;
    if (!scene) return;
    clearScene();

    type LoadResult = {
      meshes: THREE.Mesh[];
      center: THREE.Vector3;
      index: number;
    };

    const allPaths: { path: string; index: number }[] = [];
    for (let i = 0; i < frontPaths.length; i++) {
      allPaths.push({ path: frontPaths[i], index: i });
    }
    for (let i = 0; i < backPaths.length; i++) {
      allPaths.push({ path: backPaths[i], index: i });
    }

    const results = await Promise.all(
      allPaths.map(async ({ path, index }) => {
        try {
          const url = convertFileSrc(path);
          const gltf = await loader.loadAsync(url);
          if (gen !== loadingGen) return null;

          let box = new THREE.Box3();
          const found: THREE.Mesh[] = [];
          gltf.scene.traverse((child) => {
            if (child instanceof THREE.Mesh) {
              box.expandByObject(child);
              found.push(child);
            }
          });
          return { meshes: found, center: box.isEmpty() ? new THREE.Vector3() : box.getCenter(new THREE.Vector3()), index } satisfies LoadResult;
        } catch (err) {
          console.error(`Piece ${index}:`, err);
          return null;
        }
      }),
    );

    if (gen !== loadingGen) return;

    const centers: THREE.Vector3[] = [];
    for (const r of results) {
      if (!r) continue;
      for (const m of r.meshes) {
        addMesh(m, r.index);
      }
      if (!centers[r.index]) centers[r.index] = r.center;
    }

    if (centers.length > 0) {
      const validCenters = centers.filter(Boolean);
      const avg = new THREE.Vector3();
      for (const c of validCenters) avg.add(c);
      avg.divideScalar(validCenters.length);
      for (let i = 0; i < meshes.length; i++) {
        const pieceIdx = meshPieceIndex[i];
        const c = centers[pieceIdx];
        if (!c) continue;
        const dir = new THREE.Vector3().copy(c).sub(avg).normalize();
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
      const url = convertFileSrc(path);
      const gltf = await loader.loadAsync(url);
      if (gen !== loadingGen) return;

      if (!gltf || !gltf.scene) {
        throw new Error('Failed to parse GLB: scene missing');
      }

      let fallbackIdx = 0;
      const found: THREE.Mesh[] = [];
      gltf.scene.traverse((child) => {
        if (child instanceof THREE.Mesh) {
          found.push(child);
        }
      });
      for (const child of found) {
        const nameMatch = child.name.match(/^piece_(\d+)/);
        const pieceIdx = nameMatch ? parseInt(nameMatch[1], 10) : fallbackIdx++;
        addMesh(child, pieceIdx);
      }
      applyVisibility();
      fitCamera();
    } catch (err) {
      loadError = `Assembled: ${err instanceof Error ? err.message : String(err)}`;
    }
  }

  async function loadSimulate(path: string) {
    const gen = ++loadingGen;
    if (!scene) return;
    clearScene();
    snappedPieces.clear();
    pieceTargets.clear();
    isSimDragging = false;
    draggedPieceIndex = null;
    if (!raycaster) raycaster = new THREE.Raycaster();

    try {
      const url = convertFileSrc(path);
      const gltf = await loader.loadAsync(url);
      if (gen !== loadingGen) return;
      if (!gltf || !gltf.scene) {
        throw new Error('Failed to parse GLB: scene missing');
      }

      let fallbackIdx = 0;
      const found: THREE.Mesh[] = [];
      gltf.scene.traverse((child) => {
        if (child instanceof THREE.Mesh) {
          found.push(child);
        }
      });
      for (const child of found) {
        const nameMatch = child.name.match(/^piece_(\d+)/);
        const pieceIdx = nameMatch ? parseInt(nameMatch[1], 10) : fallbackIdx++;
        pieceTargets.set(pieceIdx, {
          pos: child.position.clone(),
          quat: child.quaternion.clone(),
        });
        addMesh(child, pieceIdx);
      }

      arrangeOnWall();
      applyVisibility();
      fitCamera();
      setupSimListeners();
    } catch (err) {
      loadError = `Simulate: ${err instanceof Error ? err.message : String(err)}`;
    }
  }

  $effect(() => {
    const paths = piecePaths;
    const bpaths = backPiecePaths;
    const cpath = consolidatedPath;
    const mode = viewMode;

    if (!container) return;
    if (!renderer) init();
    if (!scene) return;

    if (mode === 'split' && paths.length > 0) {
      loadSplitPieces(paths, bpaths);
    } else if (mode === 'assembled' && cpath) {
      loadAssembled(cpath);
    } else if (mode === 'simulate' && cpath) {
      loadSimulate(cpath);
    } else {
      clearScene();
    }
  });

  $effect(() => {
    pieceVisible;
    if (meshes.length > 0) applyVisibility();
  });

  $effect(() => {
    showTexture;
    if (meshes.length > 0) updateMaterials();
  });

  $effect(() => {
    if (viewMode !== 'simulate') {
      cleanupSimListeners?.();
      cleanupSimListeners = null;
      isSimDragging = false;
      draggedPieceIndex = null;
      snappedPieces.clear();
      pieceTargets.clear();
      if (controls) controls.enabled = true;
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
