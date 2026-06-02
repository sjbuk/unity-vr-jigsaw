<script lang="ts">
  import * as THREE from 'three';
  import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
  import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
  import { outputUrl } from './api';
  import type { ViewMode, CameraOrientation, FixOrphanPayload } from '../types';

  let {
    piecePaths = $bindable([]),
    backPiecePaths = $bindable([]),
    consolidatedPath = $bindable(''),
    jobId = $bindable(''),
    viewMode = $bindable<ViewMode>('split'),
    pieceVisible = $bindable<boolean[]>([]),
    showTexture = $bindable(false),
    cameraCaptureRef = $bindable(null as (() => CameraOrientation) | null),
    initialOrientation = $bindable(null as CameraOrientation | null),
    totalFaces = $bindable(0),
    previewPath = $bindable(''),
    showPreview = $bindable(false),
    fixOrphanMode = $bindable(false),
    destinationPiece = $bindable(null as number | null),
    brushRadius = $bindable(0.025),
    pendingEditCount = $bindable(0),
    pendingEditData = $bindable(null as FixOrphanPayload | null),
    paintAction = $bindable('none' as 'none' | 'undo' | 'reset' | 'apply' | 'detectIslands'),
    islandMinSize = $bindable(100 as number),
  }: {
    piecePaths?: string[];
    backPiecePaths?: string[];
    consolidatedPath?: string;
    jobId?: string;
    viewMode?: ViewMode;
    pieceVisible?: boolean[];
    showTexture?: boolean;
    cameraCaptureRef?: (() => CameraOrientation) | null;
    initialOrientation?: CameraOrientation | null;
    totalFaces?: number;
    previewPath?: string;
    showPreview?: boolean;
    fixOrphanMode?: boolean;
    destinationPiece?: number | null;
    brushRadius?: number;
    pendingEditCount?: number;
    pendingEditData?: FixOrphanPayload | null;
    paintAction?: 'none' | 'undo' | 'reset' | 'apply' | 'detectIslands';
    islandMinSize?: number;
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
  const meshIsFront: boolean[] = [];
  let loader: GLTFLoader;

  let raycaster: THREE.Raycaster | null = null;
  let dragOffset = new THREE.Vector3();
  let dragPlane = new THREE.Plane();
  let pieceTargets: Map<number, { pos: THREE.Vector3; quat: THREE.Quaternion }> = new Map();
  let relativeOffsets: Map<string, THREE.Vector3> = new Map();
  let clusterMembers: Map<number, Set<number>> = new Map();
  let pieceCluster: Map<number, number> = new Map();
  let draggedClusterId: number | null = null;
  let draggedPieceIndices: Set<number> | null = null;
  let dragRefPieceIdx: number | null = null;
  let mouseNDC = new THREE.Vector2();
  let isSimDragging = false;
  let cleanupSimListeners: (() => void) | null = null;
  let brushCursorDiameter = $derived.by(() => {
    if (!camera || !renderer) return Math.max(brushRadius * 400, 4);
    const dist = camera.position.distanceTo(controls?.target ?? new THREE.Vector3());
    const vFOV = (camera.fov * Math.PI) / 180;
    const heightAtTarget = 2 * Math.tan(vFOV / 2) * Math.max(dist, 0.1);
    const pxPerUnit = (renderer.domElement.clientHeight || 600) / heightAtTarget;
    return Math.max(brushRadius * pxPerUnit * 2, 4);
  });

  // Fix Orphan mode state
  let faceReassignments = new Map<number, Set<number>>();
  let paintStrokes: Array<Map<number, Set<number>>> = [];
  let originalVertexColors = new Map<number, Float32Array>();
  let originalGeometries = new Map<number, THREE.BufferGeometry>();
  let faceCentroids = new Map<number, THREE.Vector3[]>();
  let previousHighlighted = new Set<number>();
  let isPainting = false;
  let currentStroke = new Map<number, Set<number>>();
  let cleanupFixOrphanListeners: (() => void) | null = null;
  let brushCursorX = $state(0);
  let brushCursorY = $state(0);
  let isMouseOverViewer = $state(false);
  let islandHighlights = new Map<number, Set<number>>();
  const islandHighlightColor = new THREE.Color().setHSL(0.55, 0.9, 0.5);

  function srcUrl(relPath: string): string {
    return outputUrl(jobId, relPath);
  }

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

    cameraCaptureRef = () => ({
      position: [camera!.position.x, camera!.position.y, camera!.position.z],
      target: [controls!.target.x, controls!.target.y, controls!.target.z],
    });

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

  function addMesh(m: THREE.Mesh, pieceIdx: number, offset?: THREE.Vector3, isFront: boolean = true) {
    m.geometry.computeVertexNormals();
    const meshIdx = meshes.length;
    originalMaterials.push(m.material ?? null);
    meshPieceIndex.push(pieceIdx);
    meshIsFront.push(isFront);
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
      const pos = new THREE.Vector3(center.x + offsetX, center.y + offsetY + yOffset, center.z);
      for (let j = 0; j < meshes.length; j++) {
        if (meshPieceIndex[j] === pieceIdx) meshes[j].position.copy(pos);
      }
    }
  }

  function setupSimListeners() {
    cleanupSimListeners?.();
    if (!renderer || !camera || !controls) return;
    const el = renderer.domElement;
    const camDir = new THREE.Vector3();
    const planeHit = new THREE.Vector3();

    const updateMouseFromEvent = (e: PointerEvent) => {
      const rect = el.getBoundingClientRect();
      mouseNDC.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
      mouseNDC.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
    };

    const getPiecePosition = (pieceIdx: number): THREE.Vector3 | null => {
      for (let i = 0; i < meshes.length; i++) {
        if (meshPieceIndex[i] === pieceIdx) return meshes[i].position;
      }
      return null;
    };

    const onPointerDown = (e: PointerEvent) => {
      if (!raycaster) return;
      updateMouseFromEvent(e);
      raycaster.setFromCamera(mouseNDC, camera!);
      const intersects = raycaster.intersectObjects(meshes, false);
      if (intersects.length > 0) {
        const obj = intersects[0].object as THREE.Mesh;
        const idx = meshes.indexOf(obj);
        if (idx >= 0) {
          const pieceIdx = meshPieceIndex[idx];
          dragRefPieceIdx = pieceIdx;
          draggedClusterId = pieceCluster.get(pieceIdx)!;
          draggedPieceIndices = clusterMembers.get(draggedClusterId)!;
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
      if (!isSimDragging || draggedClusterId === null || draggedPieceIndices === null || dragRefPieceIdx === null) return;
      updateMouseFromEvent(e);
      raycaster!.setFromCamera(mouseNDC, camera!);
      if (raycaster!.ray.intersectPlane(dragPlane, planeHit)) {
        const newRefPos = planeHit.clone().add(dragOffset);
        let refMesh: THREE.Mesh | null = null;
        for (let i = 0; i < meshes.length; i++) {
          if (meshPieceIndex[i] === dragRefPieceIdx) { refMesh = meshes[i]; break; }
        }
        if (!refMesh) return;
        const delta = new THREE.Vector3().copy(newRefPos).sub(refMesh.position);
        for (let i = 0; i < meshes.length; i++) {
          if (draggedPieceIndices.has(meshPieceIndex[i])) meshes[i].position.add(delta);
        }

        let bestDist = Infinity;
        let bestSnapDelta = new THREE.Vector3();
        let bestTargetClusterId: number | null = null;

        for (const draggedIdx of draggedPieceIndices) {
          const draggedPos = getPiecePosition(draggedIdx);
          if (!draggedPos) continue;
          for (const [otherIdx, otherClusterId] of pieceCluster) {
            if (otherClusterId === draggedClusterId) continue;
            const relOffset = relativeOffsets.get(`${draggedIdx}|${otherIdx}`);
            if (!relOffset) continue;
            const otherPos = getPiecePosition(otherIdx);
            if (!otherPos) continue;
            const expectedPos = otherPos.clone().add(relOffset);
            const dist = draggedPos.distanceTo(expectedPos);
            if (dist < bestDist) {
              bestDist = dist;
              bestSnapDelta.copy(expectedPos).sub(draggedPos);
              bestTargetClusterId = otherClusterId;
            }
          }
        }

        if (bestDist < snapRadius && bestTargetClusterId !== null) {
          for (let i = 0; i < meshes.length; i++) {
            if (draggedPieceIndices.has(meshPieceIndex[i])) meshes[i].position.add(bestSnapDelta);
          }
          const targetCluster = clusterMembers.get(bestTargetClusterId)!;
          for (const pi of draggedPieceIndices) {
            targetCluster.add(pi);
            pieceCluster.set(pi, bestTargetClusterId);
          }
          clusterMembers.delete(draggedClusterId);
          isSimDragging = false;
          draggedClusterId = null;
          draggedPieceIndices = null;
          dragRefPieceIdx = null;
          controls!.enabled = true;
        }
      }
      e.stopPropagation();
      e.preventDefault();
    };

    const onPointerUp = (e: PointerEvent) => {
      if (isSimDragging) {
        isSimDragging = false;
        draggedClusterId = null;
        draggedPieceIndices = null;
        dragRefPieceIdx = null;
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

  function applyOrientation(ori: CameraOrientation | null) {
    if (!ori || !camera || !controls) return;
    camera.position.set(ori.position[0], ori.position[1], ori.position[2]);
    controls.target.set(ori.target[0], ori.target[1], ori.target[2]);
    controls.update();
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

  function countTotalFaces(meshes: THREE.Mesh[]): number {
    let count = 0;
    for (const m of meshes) {
      if (!m.geometry) continue;
      const idx = m.geometry.index;
      if (idx) {
        count += idx.count / 3;
      } else if (m.geometry.attributes.position) {
        count += m.geometry.attributes.position.count / 3;
      }
    }
    return Math.floor(count);
  }

  // ---------------------------------------------------------------------------
  // Fix Orphan helpers
  // ---------------------------------------------------------------------------

  function ensureVertexColors() {
    faceCentroids.clear();
    for (let i = 0; i < meshes.length; i++) {
      const mesh = meshes[i];
      const pieceIdx = meshPieceIndex[i];

      if (!originalGeometries.has(i)) {
        originalGeometries.set(i, mesh.geometry.clone());
        if (mesh.geometry.index) {
          mesh.geometry = mesh.geometry.toNonIndexed();
        }
      }

      const geo = mesh.geometry;
      const n = geo.attributes.position.count;

      const colors = new Float32Array(n * 3);
      const c = pieceColor(pieceIdx);
      for (let j = 0; j < n; j++) {
        colors[j * 3] = c.r;
        colors[j * 3 + 1] = c.g;
        colors[j * 3 + 2] = c.b;
      }
      geo.setAttribute('color', new THREE.BufferAttribute(colors, 3));

      if (!originalVertexColors.has(i)) {
        originalVertexColors.set(i, new Float32Array(geo.attributes.color.array));
      }

      if (mesh.material instanceof THREE.MeshPhongMaterial) {
        mesh.material.vertexColors = true;
        mesh.material.color.set(0xffffff);
        mesh.material.needsUpdate = true;
      }
    }
  }

  function restoreOriginalMaterials() {
    for (const [i, origGeo] of originalGeometries) {
      const mesh = meshes[i];
      if (!mesh) continue;
      mesh.geometry.dispose();
      mesh.geometry = origGeo;
    }
    originalGeometries.clear();
    originalVertexColors.clear();
    previousHighlighted.clear();
    faceReassignments.clear();
    paintStrokes = [];
    currentStroke.clear();
    faceCentroids.clear();
    islandHighlights.clear();
    isPainting = false;
    updateMaterials();
  }

  function computeFaceCentroids(mesh: THREE.Mesh): THREE.Vector3[] {
    mesh.updateMatrixWorld(true);
    const geo = mesh.geometry;
    const pos = geo.attributes.position;
    const idx = geo.index;
    const nFaces = idx ? idx.count / 3 : pos.count / 3;
    const centroids: THREE.Vector3[] = new Array(nFaces);
    const va = new THREE.Vector3();
    const vb = new THREE.Vector3();
    const vc = new THREE.Vector3();
    const center = new THREE.Vector3();

    for (let i = 0; i < nFaces; i++) {
      const a = idx ? idx.getX(i * 3) : i * 3;
      const b = idx ? idx.getX(i * 3 + 1) : i * 3 + 1;
      const cIdx = idx ? idx.getX(i * 3 + 2) : i * 3 + 2;
      va.set(pos.getX(a), pos.getY(a), pos.getZ(a));
      vb.set(pos.getX(b), pos.getY(b), pos.getZ(b));
      vc.set(pos.getX(cIdx), pos.getY(cIdx), pos.getZ(cIdx));
      center.copy(va).add(vb).add(vc).divideScalar(3);
      centroids[i] = center.clone().applyMatrix4(mesh.matrixWorld);
    }
    return centroids;
  }

  function paintFace(meshIdx: number, mesh: THREE.Mesh, faceIdx: number) {
    if (!faceReassignments.has(meshIdx)) {
      faceReassignments.set(meshIdx, new Set());
    }
    const existing = faceReassignments.get(meshIdx)!;
    if (existing.has(faceIdx)) return;

    existing.add(faceIdx);

    if (!currentStroke.has(meshIdx)) {
      currentStroke.set(meshIdx, new Set());
    }
    currentStroke.get(meshIdx)!.add(faceIdx);

    const geo = mesh.geometry;
    const idx = geo.index;
    const color = geo.attributes.color;
    const destColor = new THREE.Color().setHSL(0, 0.85, 0.5);

    const a = idx ? idx.getX(faceIdx * 3) : faceIdx * 3;
    const b = idx ? idx.getX(faceIdx * 3 + 1) : faceIdx * 3 + 1;
    const c = idx ? idx.getX(faceIdx * 3 + 2) : faceIdx * 3 + 2;

    color.setXYZ(a, destColor.r, destColor.g, destColor.b);
    color.setXYZ(b, destColor.r, destColor.g, destColor.b);
    color.setXYZ(c, destColor.r, destColor.g, destColor.b);
    color.needsUpdate = true;
  }

  function paintFacesAtIntersect(intersect: THREE.Intersection) {
    const mesh = intersect.object as THREE.Mesh;
    const meshIdx = meshes.indexOf(mesh);
    if (meshIdx < 0) return;
    if (meshIdx >= meshIsFront.length || !meshIsFront[meshIdx]) return;
    const sourcePiece = meshPieceIndex[meshIdx];
    if (sourcePiece === destinationPiece) return;

    if (!faceCentroids.has(meshIdx)) {
      faceCentroids.set(meshIdx, computeFaceCentroids(mesh));
    }
    const centroids = faceCentroids.get(meshIdx)!;
    const hitPoint = intersect.point;
    const brushRadiusSq = brushRadius * brushRadius;
    const hitFaceIdx = intersect.faceIndex!;

    paintFace(meshIdx, mesh, hitFaceIdx);

    for (let i = 0; i < centroids.length; i++) {
      if (i === hitFaceIdx) continue;
      if (centroids[i].distanceToSquared(hitPoint) <= brushRadiusSq) {
        paintFace(meshIdx, mesh, i);
      }
    }
  }

  function undoLastStroke() {
    const stroke = paintStrokes.pop();
    if (!stroke) return;
    for (const [meshIdx, faceSet] of stroke) {
      const mesh = meshes[meshIdx];
      if (!mesh) continue;
      const geo = mesh.geometry;
      const idx = geo.index;
      const color = geo.attributes.color;
      const origColor = originalVertexColors.get(meshIdx);
      if (!origColor) continue;

      for (const faceIdx of faceSet) {
        const a = idx ? idx.getX(faceIdx * 3) : faceIdx * 3;
        const b = idx ? idx.getX(faceIdx * 3 + 1) : faceIdx * 3 + 1;
        const c = idx ? idx.getX(faceIdx * 3 + 2) : faceIdx * 3 + 2;

        color.setXYZ(a, origColor[a * 3], origColor[a * 3 + 1], origColor[a * 3 + 2]);
        color.setXYZ(b, origColor[b * 3], origColor[b * 3 + 1], origColor[b * 3 + 2]);
        color.setXYZ(c, origColor[c * 3], origColor[c * 3 + 1], origColor[c * 3 + 2]);

        faceReassignments.get(meshIdx)?.delete(faceIdx);
      }
      color.needsUpdate = true;
    }
    pendingEditCount = paintStrokes.length;
  }

  function resetAllEdits() {
    for (const [meshIdx, origColors] of originalVertexColors) {
      const mesh = meshes[meshIdx];
      if (!mesh) continue;
      const col = mesh.geometry.attributes.color;
      if (col) {
        col.array.set(origColors);
        col.needsUpdate = true;
      }
    }
    faceReassignments.clear();
    paintStrokes = [];
    currentStroke.clear();
    pendingEditCount = 0;
    clearIslandHighlights();
  }

  function buildFaceAdjacency(meshIdx: number): Map<number, number[]> {
    const mesh = meshes[meshIdx];
    const geo = mesh.geometry;
    const pos = geo.attributes.position;
    const idx = geo.index;
    const nFaces = idx ? idx.count / 3 : pos.count / 3;

    const edgeToFaces = new Map<string, number[]>();

    for (let fi = 0; fi < nFaces; fi++) {
      const a = idx ? idx.getX(fi * 3) : fi * 3;
      const b = idx ? idx.getX(fi * 3 + 1) : fi * 3 + 1;
      const c = idx ? idx.getX(fi * 3 + 2) : fi * 3 + 2;

      const pa = `${pos.getX(a).toFixed(6)},${pos.getY(a).toFixed(6)},${pos.getZ(a).toFixed(6)}`;
      const pb = `${pos.getX(b).toFixed(6)},${pos.getY(b).toFixed(6)},${pos.getZ(b).toFixed(6)}`;
      const pc = `${pos.getX(c).toFixed(6)},${pos.getY(c).toFixed(6)},${pos.getZ(c).toFixed(6)}`;

      const edges = [[pa, pb], [pb, pc], [pc, pa]] as const;
      for (const [v1, v2] of edges) {
        const key = v1 < v2 ? `${v1}|${v2}` : `${v2}|${v1}`;
        if (!edgeToFaces.has(key)) edgeToFaces.set(key, []);
        edgeToFaces.get(key)!.push(fi);
      }
    }

    const adj = new Map<number, number[]>();
    for (let fi = 0; fi < nFaces; fi++) adj.set(fi, []);

    for (const [, faceList] of edgeToFaces) {
      if (faceList.length >= 2) {
        for (let i = 0; i < faceList.length; i++) {
          for (let j = i + 1; j < faceList.length; j++) {
            const f0 = faceList[i], f1 = faceList[j];
            adj.get(f0)!.push(f1);
            adj.get(f1)!.push(f0);
          }
        }
      }
    }

    return adj;
  }

  function findConnectedComponents(faceCount: number, adjacency: Map<number, number[]>): Set<number>[] {
    const visited = new Set<number>();
    const components: Set<number>[] = [];

    for (let fi = 0; fi < faceCount; fi++) {
      if (visited.has(fi)) continue;

      const component = new Set<number>();
      const queue = [fi];
      visited.add(fi);

      while (queue.length > 0) {
        const f = queue.shift()!;
        component.add(f);
        for (const neighbor of adjacency.get(f) || []) {
          if (!visited.has(neighbor)) {
            visited.add(neighbor);
            queue.push(neighbor);
          }
        }
      }

      components.push(component);
    }

    return components;
  }

  function clearIslandHighlights() {
    for (const [meshIdx, faceSet] of islandHighlights) {
      const mesh = meshes[meshIdx];
      if (!mesh) continue;
      const col = mesh.geometry.attributes.color;
      const orig = originalVertexColors.get(meshIdx);
      if (!col || !orig) continue;
      for (const fi of faceSet) {
        const a = fi * 3, b = fi * 3 + 1, c = fi * 3 + 2;
        col.setXYZ(a, orig[a * 3], orig[a * 3 + 1], orig[a * 3 + 2]);
        col.setXYZ(b, orig[b * 3], orig[b * 3 + 1], orig[b * 3 + 2]);
        col.setXYZ(c, orig[c * 3], orig[c * 3 + 1], orig[c * 3 + 2]);
      }
      col.needsUpdate = true;
    }
    islandHighlights.clear();
  }

  function reapplyIslandHighlights() {
    for (const [meshIdx, faceSet] of islandHighlights) {
      const mesh = meshes[meshIdx];
      if (!mesh) continue;
      const col = mesh.geometry.attributes.color;
      if (!col) continue;
      for (const fi of faceSet) {
        const a = fi * 3, b = fi * 3 + 1, c = fi * 3 + 2;
        col.setXYZ(a, islandHighlightColor.r, islandHighlightColor.g, islandHighlightColor.b);
        col.setXYZ(b, islandHighlightColor.r, islandHighlightColor.g, islandHighlightColor.b);
        col.setXYZ(c, islandHighlightColor.r, islandHighlightColor.g, islandHighlightColor.b);
      }
      col.needsUpdate = true;
    }
  }

  function detectAndPaintIslands(pieceIdx: number, minSize: number) {
    clearIslandHighlights();

    for (let i = 0; i < meshes.length; i++) {
      if (meshPieceIndex[i] !== pieceIdx || !meshIsFront[i]) continue;

      const mesh = meshes[i];
      const geo = mesh.geometry;
      const nFaces = geo.index ? geo.index.count / 3 : geo.attributes.position.count / 3;

      if (nFaces < 2) continue;

      const adjacency = buildFaceAdjacency(i);
      const components = findConnectedComponents(nFaces, adjacency);

      if (components.length <= 1) continue;

      const islandFaces = new Set<number>();
      for (const comp of components) {
        if (comp.size < minSize) {
          for (const fi of comp) islandFaces.add(fi);
        }
      }

      if (islandFaces.size === 0) continue;

      islandHighlights.set(i, islandFaces);

      const colorAttr = geo.attributes.color;
      for (const fi of islandFaces) {
        const a = fi * 3, b = fi * 3 + 1, c = fi * 3 + 2;
        colorAttr.setXYZ(a, islandHighlightColor.r, islandHighlightColor.g, islandHighlightColor.b);
        colorAttr.setXYZ(b, islandHighlightColor.r, islandHighlightColor.g, islandHighlightColor.b);
        colorAttr.setXYZ(c, islandHighlightColor.r, islandHighlightColor.g, islandHighlightColor.b);
      }
      colorAttr.needsUpdate = true;
    }
  }

  function buildPayload(): FixOrphanPayload | null {
    const sourceMap = new Map<number, Set<number>>();
    for (const [meshIdx, faceSet] of faceReassignments) {
      if (faceSet.size === 0 || !meshIsFront[meshIdx]) continue;
      const pieceIdx = meshPieceIndex[meshIdx];
      if (!sourceMap.has(pieceIdx)) {
        sourceMap.set(pieceIdx, new Set<number>());
      }
      const set = sourceMap.get(pieceIdx)!;
      for (const f of faceSet) set.add(f);
    }
    if (sourceMap.size === 0 || destinationPiece === null) return null;

    const assignments: { source_piece: number; face_indices: number[] }[] = [];
    for (const [pieceIdx, faceSet] of sourceMap) {
      assignments.push({
        source_piece: pieceIdx,
        face_indices: Array.from(faceSet).sort((a, b) => a - b),
      });
    }
    return { destination_piece: destinationPiece, assignments };
  }

  function setupFixOrphanListeners() {
    cleanupFixOrphanListeners?.();
    if (!renderer || !camera || !controls) return;
    if (!raycaster) raycaster = new THREE.Raycaster();
    const el = renderer.domElement;

    const updateMouse = (e: PointerEvent) => {
      const rect = el.getBoundingClientRect();
      const rx = (e.clientX - rect.left) / rect.width;
      const ry = (e.clientY - rect.top) / rect.height;
      brushCursorX = e.clientX - rect.left;
      brushCursorY = e.clientY - rect.top;
      mouseNDC.x = rx * 2 - 1;
      mouseNDC.y = -ry * 2 + 1;
    };

    const onPointerDown = (e: PointerEvent) => {
      if (!fixOrphanMode || destinationPiece === null || !e.shiftKey) return;
      e.stopImmediatePropagation();
      e.preventDefault();
      updateMouse(e);
      raycaster!.setFromCamera(mouseNDC, camera!);
      const intersects = raycaster!.intersectObjects(meshes, false);
      if (intersects.length > 0) {
        isPainting = true;
        currentStroke = new Map();
        controls!.enabled = false;
        el.setPointerCapture(e.pointerId);
        paintFacesAtIntersect(intersects[0]);
      }
    };

    const onPointerMove = (e: PointerEvent) => {
      updateMouse(e);
      if (!isPainting) return;
      if (!e.shiftKey) {
        endStroke();
        controls!.enabled = true;
        return;
      }
      e.stopImmediatePropagation();
      e.preventDefault();
      raycaster!.setFromCamera(mouseNDC, camera!);
      const intersects = raycaster!.intersectObjects(meshes, false);
      if (intersects.length > 0) {
        paintFacesAtIntersect(intersects[0]);
      }
    };

    const onPointerUp = (e: PointerEvent) => {
      if (isPainting) {
        endStroke();
        controls!.enabled = true;
        el.releasePointerCapture(e.pointerId);
        e.stopImmediatePropagation();
        e.preventDefault();
      }
    };

    el.addEventListener('pointerdown', onPointerDown, { capture: true });
    el.addEventListener('pointermove', onPointerMove);
    el.addEventListener('pointerup', onPointerUp);

    cleanupFixOrphanListeners = () => {
      el.removeEventListener('pointerdown', onPointerDown, { capture: true });
      el.removeEventListener('pointermove', onPointerMove);
      el.removeEventListener('pointerup', onPointerUp);
    };
  }

  function endStroke() {
    if (!isPainting) return;
    isPainting = false;
    paintStrokes.push(currentStroke);
    pendingEditCount = paintStrokes.length;
  }

  function updateTargetHighlight(pieceIdx: number | null) {
    for (const [i, origColors] of originalVertexColors) {
      if (!previousHighlighted.has(i)) continue;
      const mesh = meshes[i];
      if (!mesh) continue;
      const col = mesh.geometry.attributes.color;
      if (col) {
        col.array.set(origColors);
        col.needsUpdate = true;
      }
    }
    previousHighlighted.clear();

    reapplyIslandHighlights();

    if (pieceIdx === null || !fixOrphanMode) return;

    const red = new THREE.Color().setHSL(0, 0.85, 0.5);
    for (let i = 0; i < meshes.length; i++) {
      if (meshPieceIndex[i] === pieceIdx) {
        const mesh = meshes[i];
        const col = mesh.geometry.attributes.color;
        if (!col) continue;
        const islandSet = islandHighlights.get(i);
        const n = col.count;
        for (let j = 0; j < n; j++) {
          if (islandSet && islandSet.has(Math.floor(j / 3))) continue;
          col.setXYZ(j, red.r, red.g, red.b);
        }
        col.needsUpdate = true;
        previousHighlighted.add(i);
      }
    }
  }

  // ---------------------------------------------------------------------------

  function clearScene() {
    if (!scene) return;
    for (const m of meshes) {
      scene.remove(m);
      if (m.geometry) m.geometry.dispose();
    }
    meshes.length = 0;
    originalMaterials.length = 0;
    meshPieceIndex.length = 0;
    meshIsFront.length = 0;
    originalGeometries.clear();
    originalVertexColors.clear();
    previousHighlighted.clear();
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

    type LoadResult = { meshes: THREE.Mesh[]; center: THREE.Vector3; index: number };

    const allPaths: { path: string; index: number }[] = [];
    for (let i = 0; i < frontPaths.length; i++) allPaths.push({ path: frontPaths[i], index: i });
    for (let i = 0; i < backPaths.length; i++) allPaths.push({ path: backPaths[i], index: i });

    const results = await Promise.all(
      allPaths.map(async ({ path, index }) => {
    try {
      const url = `${srcUrl(path)}?ts=${Date.now()}`;
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
      for (const m of r.meshes) addMesh(m, r.index);
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
    applyOrientation(initialOrientation);
  }

  async function loadAssembled(path: string) {
    const gen = ++loadingGen;
    if (!scene) return;
    clearScene();

    try {
      const url = srcUrl(path);
      const gltf = await loader.loadAsync(url);
      if (gen !== loadingGen) return;
      if (!gltf || !gltf.scene) throw new Error('Failed to parse GLB: scene missing');

      let fallbackIdx = 0;
      const found: THREE.Mesh[] = [];
      gltf.scene.traverse((child) => {
        if (child instanceof THREE.Mesh) found.push(child);
      });
      for (const child of found) {
        const nameMatch = child.name.match(/^piece_(\d+)/);
        const pieceIdx = nameMatch ? parseInt(nameMatch[1], 10) : fallbackIdx++;
        const isFront = !child.name.includes('_back');
        addMesh(child, pieceIdx, undefined, isFront);
      }
      if (found.length > 0 && totalFaces === 0) totalFaces = countTotalFaces(found);
      if (fixOrphanMode && meshes.length > 0) {
        ensureVertexColors();
        setupFixOrphanListeners();
      }
      applyVisibility();
      fitCamera();
      applyOrientation(initialOrientation);
    } catch (err) {
      loadError = `Assembled: ${err instanceof Error ? err.message : String(err)}`;
    }
  }

  async function loadSimulate(path: string) {
    const gen = ++loadingGen;
    if (!scene) return;
    clearScene();
    pieceTargets.clear();
    relativeOffsets.clear();
    clusterMembers.clear();
    pieceCluster.clear();
    draggedClusterId = null;
    draggedPieceIndices = null;
    dragRefPieceIdx = null;
    isSimDragging = false;
    if (!raycaster) raycaster = new THREE.Raycaster();

    try {
      const url = srcUrl(path);
      const gltf = await loader.loadAsync(url);
      if (gen !== loadingGen) return;
      if (!gltf || !gltf.scene) throw new Error('Failed to parse GLB: scene missing');

      let fallbackIdx = 0;
      const found: THREE.Mesh[] = [];
      gltf.scene.traverse((child) => {
        if (child instanceof THREE.Mesh) found.push(child);
      });
      for (const child of found) {
        const nameMatch = child.name.match(/^piece_(\d+)/);
        const pieceIdx = nameMatch ? parseInt(nameMatch[1], 10) : fallbackIdx++;
        const isFront = !child.name.includes('_back');
        pieceTargets.set(pieceIdx, { pos: child.position.clone(), quat: child.quaternion.clone() });
        addMesh(child, pieceIdx, undefined, isFront);
      }

      const pieceBounds = new Map<number, THREE.Box3>();
      for (let i = 0; i < meshes.length; i++) {
        const pieceIdx = meshPieceIndex[i];
        if (!pieceBounds.has(pieceIdx)) pieceBounds.set(pieceIdx, new THREE.Box3());
        pieceBounds.get(pieceIdx)!.expandByObject(meshes[i]);
      }

      const adjacencyThreshold = 0.01;
      const neighborPairs = new Set<string>();
      for (const [idxA, boxA] of pieceBounds) {
        const expandedA = boxA.clone().expandByScalar(adjacencyThreshold);
        for (const [idxB, boxB] of pieceBounds) {
          if (idxA === idxB) continue;
          if (expandedA.intersectsBox(boxB)) { neighborPairs.add(`${idxA}|${idxB}`); neighborPairs.add(`${idxB}|${idxA}`); }
        }
      }

      for (const pair of neighborPairs) {
        const [idxA, idxB] = pair.split('|').map(Number);
        const ta = pieceTargets.get(idxA)!;
        const tb = pieceTargets.get(idxB)!;
        relativeOffsets.set(pair, new THREE.Vector3().copy(ta.pos).sub(tb.pos));
      }

      for (const [idx] of pieceTargets) {
        clusterMembers.set(idx, new Set([idx]));
        pieceCluster.set(idx, idx);
      }

      arrangeOnWall();
      applyVisibility();
      fitCamera();
      applyOrientation(initialOrientation);
      setupSimListeners();
    } catch (err) {
      loadError = `Simulate: ${err instanceof Error ? err.message : String(err)}`;
    }
  }

  async function loadPreview(path: string) {
    const gen = ++loadingGen;
    if (!scene) return;
    clearScene();

    try {
      const url = srcUrl(path);
      const gltf = await loader.loadAsync(url);
      if (gen !== loadingGen) return;
      if (!gltf || !gltf.scene) throw new Error('Failed to parse preview GLB: scene missing');

      const found: THREE.Mesh[] = [];
      gltf.scene.traverse((child) => {
        if (child instanceof THREE.Mesh) found.push(child);
      });
      for (let i = 0; i < found.length; i++) {
        addMesh(found[i], i);
      }
      fitCamera();
      applyOrientation(initialOrientation);
    } catch (err) {
      loadError = `Preview: ${err instanceof Error ? err.message : String(err)}`;
    }
  }

  $effect(() => {
    const paths = piecePaths;
    const bpaths = backPiecePaths;
    const cpath = consolidatedPath;
    const ppath = previewPath;
    const preview = showPreview;
    const mode = viewMode;
    const jid = jobId;

    if (!container || !jid) return;
    if (!renderer) init();
    if (!scene) return;

    if (preview && ppath) {
      loadPreview(ppath);
    } else if (mode === 'split' && paths.length > 0) {
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
      draggedClusterId = null;
      draggedPieceIndices = null;
      dragRefPieceIdx = null;
      clusterMembers.clear();
      pieceCluster.clear();
      relativeOffsets.clear();
      pieceTargets.clear();
      if (controls) controls.enabled = true;
    }
  });

  $effect(() => {
    const mode = fixOrphanMode;
    if (mode) {
      if (meshes.length > 0) {
        ensureVertexColors();
        setupFixOrphanListeners();
      }
    } else {
      cleanupFixOrphanListeners?.();
      cleanupFixOrphanListeners = null;
      isPainting = false;
      restoreOriginalMaterials();
      pendingEditCount = 0;
    }
  });

  $effect(() => {
    const action = paintAction;
    if (action === 'undo') {
      undoLastStroke();
      paintAction = 'none';
    } else if (action === 'reset') {
      resetAllEdits();
      paintAction = 'none';
    } else if (action === 'apply') {
      pendingEditData = buildPayload();
      paintAction = 'none';
    } else if (action === 'detectIslands') {
      if (destinationPiece !== null) {
        detectAndPaintIslands(destinationPiece, islandMinSize);
      }
      paintAction = 'none';
    }
  });

  $effect(() => {
    destinationPiece;
    fixOrphanMode;
    meshes.length;
    updateTargetHighlight(destinationPiece);
  });
</script>

<div
  bind:this={container}
  class="viewer"
  class:fix-orphan-active={fixOrphanMode}
  role="application"
  onmouseenter={() => (isMouseOverViewer = true)}
  onmouseleave={() => (isMouseOverViewer = false)}
>
  {#if !piecePaths.length && !consolidatedPath}
    <div class="placeholder">Select a model or job to begin</div>
  {/if}
  {#if loadError}
    <div class="error-msg">{loadError}</div>
  {/if}
  {#if fixOrphanMode && isMouseOverViewer && destinationPiece !== null}
    <div
      class="brush-cursor"
      style="left: {brushCursorX}px; top: {brushCursorY}px; width: {brushCursorDiameter}px; height: {brushCursorDiameter}px;"
    ></div>
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
  .fix-orphan-active {
    cursor: none;
  }
  .brush-cursor {
    position: absolute;
    pointer-events: none;
    border: 2px solid rgba(200, 200, 200, 0.7);
    background: rgba(255, 255, 255, 0.08);
    border-radius: 50%;
    transform: translate(-50%, -50%);
    z-index: 20;
  }
</style>
