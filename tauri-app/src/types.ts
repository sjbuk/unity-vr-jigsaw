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
}

export interface JobSummary {
  job_id: string;
  piece_count: number;
  source_model: string;
  name: string;
  created_at: string;
}

export type ViewMode = 'split' | 'assembled' | 'simulate';

export const DEFAULT_PARAMS: SliceParams = {
  input_path: '',
  output_path: '',
  pieces: 24,
  gap: 0.001,
  seed: null,
  reassign_orphans: false,
  preview_faces: 2000,
};
