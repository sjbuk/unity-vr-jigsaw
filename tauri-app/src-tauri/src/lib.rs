use std::io::Read;
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::sync::{Arc, Mutex};

use serde::{Deserialize, Serialize};
use tauri::Emitter;

/// Path to the Python tools directory relative to Cargo manifest.
const PYTHON_TOOLS_DIR: &str =
    concat!(env!("CARGO_MANIFEST_DIR"), "/../../tools/jigsaw_generator");

#[derive(Debug, Serialize, Deserialize)]
pub struct SliceParams {
    pub input_path: String,
    pub output_path: String,
    pub pieces: u32,
    pub gap: f64,
    pub seed: Option<u32>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct PieceInfo {
    pub index: u32,
    pub path: String,
    pub vertices: u32,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct SliceResult {
    pub piece_count: u32,
    pub output_dir: String,
    pub consolidated: String,
    pub checkpoint: String,
    pub pieces: Vec<PieceInfo>,
}

/// Determine the Python command name available on this system.
fn find_python() -> Option<&'static str> {
    for cmd in &["python", "python3"] {
        if Command::new(cmd)
            .arg("--version")
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status()
            .is_ok()
        {
            return Some(cmd);
        }
    }
    None
}

#[tauri::command]
async fn slice_model(
    app: tauri::AppHandle,
    params: SliceParams,
) -> Result<SliceResult, String> {
    // 1. Locate Python
    let python = find_python().ok_or("Python not found. Please install Python 3.12+ and ensure it is on your PATH.")?;

    // 2. Validate that the tools directory exists
    let tools_dir = PathBuf::from(PYTHON_TOOLS_DIR);
    let tools_dir_canonical = tools_dir
        .canonicalize()
        .map_err(|_| format!("Tools directory not found: {}", PYTHON_TOOLS_DIR))?;

    // 3. Write a temporary config file
    let config_json = serde_json::to_string(&params).map_err(|e| e.to_string())?;
    let temp_dir = std::env::temp_dir().join("jigsaw_slicer");
    std::fs::create_dir_all(&temp_dir).map_err(|e| e.to_string())?;
    let config_path = temp_dir.join("slice_config.json");
    std::fs::write(&config_path, &config_json).map_err(|e| e.to_string())?;

    // 4. Create output directory
    std::fs::create_dir_all(&params.output_path).map_err(|e| e.to_string())?;

    // 5. Spawn the Python subprocess
    let mut child = Command::new(python)
        .arg("planar_step_09_run_slice.py")
        .arg(&config_path)
        .current_dir(&tools_dir_canonical)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|e| format!("Failed to start Python: {e}"))?;

    // 6. Read stderr (progress + error capture) in a background thread.
    //    Use binary reads to avoid UTF-8 decoding failures on Windows.
    let stderr = child.stderr.take().unwrap();
    let app_handle = app.clone();
    let err_buf = Arc::new(Mutex::new(String::new()));
    let err_buf_clone = err_buf.clone();
    std::thread::spawn(move || {
        use std::io::BufRead;
        let mut reader = std::io::BufReader::new(stderr);
        let mut buf: Vec<u8> = Vec::new();
        while reader.read_until(b'\n', &mut buf).unwrap_or(0) > 0 {
            let line = String::from_utf8_lossy(&buf).trim().to_string();
            if !line.is_empty() {
                let _ = app_handle.emit("slice-progress", &line);
                err_buf_clone.lock().unwrap().push_str(&line);
                err_buf_clone.lock().unwrap().push('\n');
            }
            buf.clear();
        }
    });

    // 7. Read stdout as raw bytes (handles non-UTF-8 on Windows)
    let mut stdout_bytes: Vec<u8> = Vec::new();
    child
        .stdout
        .take()
        .unwrap()
        .read_to_end(&mut stdout_bytes)
        .map_err(|e| e.to_string())?;
    let stdout = String::from_utf8_lossy(&stdout_bytes).to_string();

    // 8. Wait for exit
    let status = child
        .wait()
        .map_err(|e| format!("Python process error: {e}"))?;

    // 9. Clean up temp config
    let _ = std::fs::remove_file(&config_path);

    if !status.success() {
        let err = err_buf.lock().unwrap();
        let detail = if err.is_empty() { "No error output".into() } else { err.clone() };
        return Err(format!(
            "Python slicing failed with exit code: {:?}\n--- stderr ---\n{}",
            status.code(),
            detail,
        ));
    }

    // 10. Parse result JSON
    let result: SliceResult =
        serde_json::from_str(&stdout).map_err(|e| format!("Failed to parse Python output: {e}\nOutput: {stdout}"))?;

    Ok(result)
}

/// Read a text file and return its contents as a string.
#[tauri::command]
fn read_text_file(path: String) -> Result<String, String> {
    std::fs::read_to_string(&path).map_err(|e| format!("Failed to read text file: {e}"))
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .invoke_handler(tauri::generate_handler![slice_model, read_text_file])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
