import type { SliceParams, SliceResult, JobSummary, CameraOrientation } from '../types';

const API = '/api';

export async function uploadModel(file: File): Promise<{ job_id: string }> {
  const fd = new FormData();
  fd.append('file', file);
  const r = await fetch(`${API}/upload`, { method: 'POST', body: fd });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `Upload failed with status ${r.status}`);
  }
  return r.json();
}

export async function sliceJob(
  jobId: string,
  config: SliceParams,
): Promise<{ job_id: string }> {
  const r = await fetch(`${API}/slice/${jobId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(config),
  });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `Slice failed with status ${r.status}`);
  }
  return r.json();
}

export async function reassignOrphans(
  jobId: string,
): Promise<{ job_id: string }> {
  const r = await fetch(`${API}/orphans/${jobId}`, { method: 'POST' });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `Orphan reassign failed with status ${r.status}`);
  }
  return r.json();
}

export async function startSlice(
  file: File,
  config: SliceParams,
): Promise<{ job_id: string }> {
  const fd = new FormData();
  fd.append('file', file);
  fd.append('config', JSON.stringify(config));
  const r = await fetch(`${API}/slice`, { method: 'POST', body: fd });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `Request failed with status ${r.status}`);
  }
  return r.json();
}

export function progressStream(
  jobId: string,
  onProgress: (msg: string) => void,
  onDone: (result: SliceResult) => void,
  onError: (err: string) => void,
): () => void {
  const es = new EventSource(`${API}/progress/${jobId}`);
  es.onmessage = (e) => {
    if (e.data === 'keepalive') return;
    if (e.data.startsWith('[DONE]')) {
      es.close();
      try {
        const resultStr = e.data.slice('[DONE]\n'.length);
        const result: SliceResult = JSON.parse(resultStr);
        onDone(result);
      } catch {
        onError('Failed to parse slice result');
      }
    } else if (e.data.startsWith('[ERROR]')) {
      es.close();
      onError(e.data);
    } else {
      onProgress(e.data);
    }
  };
  es.onerror = () => {
    es.close();
    onError('Connection lost');
  };
  return () => es.close();
}

export async function listJobs(): Promise<JobSummary[]> {
  const r = await fetch(`${API}/jobs`);
  if (!r.ok) throw new Error('Failed to list jobs');
  return r.json();
}

export async function getJob(jobId: string): Promise<SliceResult> {
  const r = await fetch(`${API}/jobs/${jobId}`);
  if (!r.ok) throw new Error('Failed to load job');
  return r.json();
}

export async function updateJobMeta(
  jobId: string,
  meta: { name?: string; orientation?: CameraOrientation },
): Promise<void> {
  const r = await fetch(`${API}/jobs/${jobId}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(meta),
  });
  if (!r.ok) throw new Error('Failed to update job metadata');
}

export async function saveJob(
  jobId: string,
  name: string,
): Promise<SliceResult> {
  const r = await fetch(`${API}/save/${jobId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `Save failed with status ${r.status}`);
  }
  return r.json();
}

export async function regeneratePreview(
  jobId: string,
  previewFaces: number,
): Promise<void> {
  const r = await fetch(`${API}/preview/${jobId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ preview_faces: previewFaces }),
  });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || 'Preview regeneration failed');
  }
}

export async function fixOrphans(
  jobId: string,
  payload: { destination_piece: number; assignments: { source_piece: number; face_indices: number[] }[] },
): Promise<{ job_id: string }> {
  const r = await fetch(`${API}/fix-orphans/${jobId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `Fix orphans failed with status ${r.status}`);
  }
  return r.json();
}

export async function removeIslands(
  jobId: string,
  payload: { source_piece: number; min_island_size: number },
): Promise<{ job_id: string }> {
  const r = await fetch(`${API}/remove-islands/${jobId}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!r.ok) {
    const text = await r.text();
    throw new Error(text || `Remove islands failed with status ${r.status}`);
  }
  return r.json();
}

export function outputUrl(jobId: string, relPath: string): string {
  return `${API}/outputs/${jobId}/${relPath}`;
}
