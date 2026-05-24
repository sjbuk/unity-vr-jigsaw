export interface SliceParams {
  input_path: string;
  output_path: string;
  pieces: number;
  mode: 'shell' | 'full_3d';
  shell_thickness: number;
  gap: number;
  peg_clearance: number;
  tab_density: number;
  snap_radius_min: number;
  snap_radius_max: number;
  snap_angle_tolerance: number;
  seed: number | null;
}

export interface PieceInfo {
  index: number;
  path: string;
  vertices: number;
}

export interface SliceResult {
  piece_count: number;
  output_dir: string;
  consolidated: string;
  checkpoint: string;
  pieces: PieceInfo[];
  mode: string;
}

export type ViewMode = 'split' | 'assembled';

export const DEFAULT_PARAMS: SliceParams = {
  input_path: '',
  output_path: '',
  pieces: 24,
  mode: 'full_3d',
  shell_thickness: 0.02,
  gap: 0.001,
  peg_clearance: 0.003,
  tab_density: 0.3,
  snap_radius_min: 0.02,
  snap_radius_max: 0.08,
  snap_angle_tolerance: 25.0,
  seed: null,
};
