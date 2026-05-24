use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::io::Read;

use serde::{Deserialize, Serialize};
use tauri::Emitter;

/// Path to the Python tools directory relative to Cargo manifest.
const PYTHON_TOOLS_DIR: &str =
    concat!(env!("CARGO_MANIFEST_DIR"), "/../../tools/jigsaw_generator");

#[derive(Debug, Serialize, Deserialize)]
pub struct SliceParams {
    pub input_path: String,
    pub output_dir: String,
    pub pieces: u32,
    pub mode: String,
    pub shell_thickness: f64,
    pub gap: f64,
    pub peg_clearance: f64,
    pub tab_density: f64,
    pub snap_radius_min: f64,
    pub snap_radius_max: f64,
    pub snap_angle_tolerance: f64,
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
    pub mode: String,
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
    std::fs::create_dir_all(&params.output_dir).map_err(|e| e.to_string())?;

    // 5. Spawn the Python subprocess
    let mut child = Command::new(python)
        .arg("run_slice.py")
        .arg(&config_path)
        .current_dir(&tools_dir_canonical)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|e| format!("Failed to start Python: {e}"))?;

    // 6. Read stderr (progress) and emit as events
    let stderr = child.stderr.take().unwrap();
    let app_handle = app.clone();
    std::thread::spawn(move || {
        let mut reader = std::io::BufReader::new(stderr);
        let mut line = String::new();
        use std::io::BufRead;
        while reader.read_line(&mut line).unwrap_or(0) > 0 {
            let trimmed = line.trim().to_string();
            if !trimmed.is_empty() {
                let _ = app_handle.emit("slice-progress", &trimmed);
            }
            line.clear();
        }
    });

    // 7. Read stdout (result JSON)
    let mut stdout = String::new();
    child
        .stdout
        .take()
        .unwrap()
        .read_to_string(&mut stdout)
        .map_err(|e| e.to_string())?;

    // 8. Wait for exit
    let status = child
        .wait()
        .map_err(|e| format!("Python process error: {e}"))?;

    // 9. Clean up temp config
    let _ = std::fs::remove_file(&config_path);

    if !status.success() {
        return Err(format!("Python slicing failed with exit code: {:?}", status.code()));
    }

    // 10. Parse result JSON
    let result: SliceResult =
        serde_json::from_str(&stdout).map_err(|e| format!("Failed to parse Python output: {e}\nOutput: {stdout}"))?;

    Ok(result)
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .invoke_handler(tauri::generate_handler![slice_model])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
