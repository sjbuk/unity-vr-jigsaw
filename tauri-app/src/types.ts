export interface CameraOrientation {
  position: [number, number, number];
  target: [number, number, number];
}

export interface SliceParams {
  input_path: string;
  output_path: string;
  pieces: number;
  gap: number;
  seed: number | null;
  reassign_orphans: boolean;
  preview_faces: number;
  smooth_edges: boolean;
  smooth_iterations: number;
  smooth_lambda: number;
  smooth_nu: number;
  strategy: string;
}

export interface PieceInfo {
  index: number;
  path: string;
  vertices: number;
  back_path?: string;
  back_vertices?: number;
}

export interface SliceResult {
  job_id: string;
  piece_count: number;
  output_dir: string;
  consolidated: string;
  checkpoint: string;
  pieces: PieceInfo[];
  colour_atlas?: string;
  name?: string;
  orientation?: CameraOrientation | null;
  normalized_glb?: string;
  preview_glb?: string;
}

export interface JobSummary {
  job_id: string;
  piece_count: number;
  source_model: string;
  name: string;
  created_at: string;
}

export type ViewMode = 'split' | 'assembled' | 'simulate' | 'fixOrphan';

export interface FixOrphanAssignment {
  source_piece: number;
  face_indices: number[];
}

export interface FixOrphanPayload {
  destination_piece: number;
  assignments: FixOrphanAssignment[];
}

export interface RemoveIslandsPayload {
  source_piece: number;
  min_island_size: number;
}

export interface CuttingPlane {
  normal: [number, number, number];
  origin: [number, number, number];
}

export interface PlanResult {
  shape_class: string;
  eigenvalues: [number, number, number];
  axis_ratios: [number, number];
  strategy: string;
  strategy_id: string;
  factorisation: number[];
  cut_plane_sequence: CuttingPlane[];
  bbox_extents: [number, number, number];
}

export const DEFAULT_PARAMS: SliceParams = {
  input_path: '',
  output_path: '',
  pieces: 24,
  gap: 0,
  seed: null,
  reassign_orphans: false,
  preview_faces: 2000,
  smooth_edges: false,
  smooth_iterations: 1,
  smooth_lambda: 0.5,
  smooth_nu: 0.5,
  strategy: 'Auto',
};
