export interface SliceParams {
  input_path: string;
  output_path: string;
  pieces: number;
  gap: number;
  seed: number | null;
  reassign_orphans: boolean;
}

export interface PieceInfo {
  index: number;
  path: string;
  vertices: number;
  back_path?: string;
  back_vertices?: number;
}

export interface SliceResult {
  piece_count: number;
  output_dir: string;
  consolidated: string;
  checkpoint: string;
  pieces: PieceInfo[];
  colour_atlas?: string;
}

export type ViewMode = 'split' | 'assembled';

export const DEFAULT_PARAMS: SliceParams = {
  input_path: '',
  output_path: '',
  pieces: 24,
  gap: 0.001,
  seed: null,
  reassign_orphans: true,
};
