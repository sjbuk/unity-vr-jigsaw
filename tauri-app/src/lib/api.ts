import { invoke } from '@tauri-apps/api/core';
import type { SliceParams, SliceResult } from '../types';

export async function sliceModel(params: SliceParams): Promise<SliceResult> {
  return invoke<SliceResult>('slice_model', { params });
}
