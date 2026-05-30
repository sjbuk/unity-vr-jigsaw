using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public static class PuzzleApiClient
{
    private const string PlayerPrefsKey = "PuzzleApiUrl";
    private const string DefaultUrl = "http://10.111.1.20:8000";

    private static string _baseUrl;
    public static string BaseUrl
    {
        get
        {
            if (_baseUrl == null)
                _baseUrl = PlayerPrefs.GetString(PlayerPrefsKey, DefaultUrl);
            return _baseUrl;
        }
        set
        {
            _baseUrl = value;
            PlayerPrefs.SetString(PlayerPrefsKey, value);
            PlayerPrefs.Save();
        }
    }

    public static string DownloadUrl(string jobId, string filePath)
    {
        return $"{BaseUrl}/api/outputs/{jobId}/{filePath}";
    }

    public static string ListJobsUrl()
    {
        return $"{BaseUrl}/api/jobs";
    }

    public static async Task<List<JobSummary>> ListJobs()
    {
        try
        {
            string url = ListJobsUrl();
            var req = UnityWebRequest.Get(url);
            req.timeout = 10;

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PuzzleApiClient] Failed to list jobs: {req.error}");
                req.Dispose();
                return new List<JobSummary>();
            }

            string text = req.downloadHandler != null ? req.downloadHandler.text : "";
            req.Dispose();

            var wrapped = "{\"jobs\":" + text + "}";
            var wrapper = JsonUtility.FromJson<JobListWrapper>(wrapped);
            return wrapper?.jobs ?? new List<JobSummary>();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PuzzleApiClient] Error listing jobs: {e.Message}");
            return new List<JobSummary>();
        }
    }

    public static async Task<byte[]> DownloadFile(string jobId, string filePath)
    {
        try
        {
            string url = DownloadUrl(jobId, filePath);
            var req = UnityWebRequest.Get(url);
            req.timeout = 30;

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            byte[] result = null;
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[PuzzleApiClient] Failed to download {filePath}: {req.error}");
            }
            else if (req.downloadHandler != null)
            {
                result = req.downloadHandler.data;
            }

            req.Dispose();
            return result;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PuzzleApiClient] Error downloading {filePath}: {e.Message}");
            return null;
        }
    }

    public static async Task<bool> DownloadPuzzle(string jobId, string localDir, Action<float> onProgress)
    {
        try
        {
            Directory.CreateDirectory(localDir);

            string[] files = { "checkpoint.json", "preview.png", "pieces.glb", "colour_atlas.png" };
            float[] weights = { 0.05f, 0.05f, 0.85f, 0.05f };
            float cumulative = 0f;

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                byte[] data = await DownloadFile(jobId, file);

                if (data == null || data.Length == 0)
                {
                    if (file == "checkpoint.json" || file == "pieces.glb")
                    {
                        Debug.LogError($"[PuzzleApiClient] Required file missing: {file}");
                        return false;
                    }
                    cumulative += weights[i];
                    TryInvoke(onProgress, cumulative);
                    continue;
                }

                string localPath = Path.Combine(localDir, file);
                File.WriteAllBytes(localPath, data);
                cumulative += weights[i];
                TryInvoke(onProgress, cumulative);
            }

            TryInvoke(onProgress, 1f);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PuzzleApiClient] Download error: {e.Message}");
            return false;
        }
    }

    private static void TryInvoke(Action<float> action, float value)
    {
        try { action?.Invoke(value); }
        catch (Exception e) { Debug.LogWarning($"[PuzzleApiClient] Progress callback error: {e.Message}"); }
    }

    [Serializable]
    private class JobListWrapper
    {
        public List<JobSummary> jobs;
    }
}
