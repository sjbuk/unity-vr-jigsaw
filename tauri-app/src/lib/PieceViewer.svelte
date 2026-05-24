<script lang="ts">
  import { onMount } from 'svelte';
  import * as THREE from 'three';
  import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  import { DRACOLoader } from 'three/addons/loaders/DRACOLoader.js';
  import { convertFileSrc } from '@tauri-apps/api/core';

  let { piecePaths = $bindable([]) }: { piecePaths?: string[] } = $props();

  let container: HTMLDivElement;
  let renderer: THREE.WebGLRenderer | null = null;
  let scene: THREE.Scene | null = null;
  let camera: THREE.PerspectiveCamera | null = null;
  let controls: OrbitControls | null = null;
  let ready = $state(false);

  const pieceMeshes: THREE.Mesh[] = [];
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

    const hemiLight = new THREE.HemisphereLight(0xffffff, 0x444444, 0.6);
    scene.add(hemiLight);

    const dirLight = new THREE.DirectionalLight(0xffffff, 1.0);
    dirLight.position.set(5, 10, 7);
    scene.add(dirLight);

    const dirLight2 = new THREE.DirectionalLight(0x4488ff, 0.3);
    dirLight2.position.set(-5, 0, 5);
    scene.add(dirLight2);

    const grid = new THREE.GridHelper(4, 20, 0x444466, 0x333355);
    scene.add(grid);

    loader = new GLTFLoader();
    const dracoLoader = new DRACOLoader();
    dracoLoader.setDecoderPath(
      'https://www.gstatic.com/draco/versioned/decoders/1.5.6/',
    );
    loader.setDRACOLoader(dracoLoader);

    animate();
  }

  function animate() {
    requestAnimationFrame(animate);
    controls?.update();
    if (renderer && scene && camera) {
      renderer.render(scene, camera);
    }
  }

  function clearPieces() {
    if (!scene) return;
    for (const m of pieceMeshes) {
      scene.remove(m);
      if (m.geometry) m.geometry.dispose();
      if (Array.isArray(m.material)) {
        m.material.forEach((mat) => mat.dispose());
      } else if (m.material) {
        m.material.dispose();
      }
    }
    pieceMeshes.length = 0;
  }

  function randomColor(): THREE.Color {
    const hue = Math.random();
    return new THREE.Color().setHSL(hue, 0.6, 0.45);
  }

  async function loadPieces(paths: string[]) {
    if (!scene || !controls || !camera) return;
    clearPieces();

    for (let i = 0; i < paths.length; i++) {
      try {
        const url = convertFileSrc(paths[i]);
        const gltf = await loader.loadAsync(url);
        gltf.scene.traverse((child) => {
          if (child instanceof THREE.Mesh) {
            const color = randomColor();
            child.material = new THREE.MeshStandardMaterial({
              color,
              roughness: 0.4,
              metalness: 0.1,
              flatShading: false,
            });
            child.castShadow = true;
            pieceMeshes.push(child);
            scene!.add(child);
          }
        });
      } catch (err) {
        console.error(`Failed to load piece ${i}:`, err);
      }
    }

    if (pieceMeshes.length > 0) {
      const box = new THREE.Box3();
      for (const m of pieceMeshes) {
        box.expandByObject(m);
      }
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
  }

  $effect(() => {
    if (ready && piecePaths.length > 0) {
      loadPieces(piecePaths);
    } else if (ready) {
      clearPieces();
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
  {#if piecePaths.length === 0}
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
