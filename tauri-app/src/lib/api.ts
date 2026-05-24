import { invoke } from '@tauri-apps/api/core';
import type { SliceParams, SliceResult } from '../types';

export async function sliceModel(params: SliceParams): Promise<SliceResult> {
  return invoke<SliceResult>('slice_model', { params });
}

export async function readTextFile(path: string): Promise<string> {
  return invoke<string>('read_text_file', { path });
}

