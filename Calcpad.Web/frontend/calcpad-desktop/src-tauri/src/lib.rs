use std::process::Stdio;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde::Serialize;
use tauri::menu::{Menu, MenuBuilder, MenuItem, PredefinedMenuItem, Submenu};
use tauri::path::BaseDirectory;
use tauri::{AppHandle, Emitter, Manager, RunEvent, State, WindowEvent};
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::sync::mpsc;

// Name of the .NET apphost inside the resource dir. All ~200 sibling
// DLLs / native libs / deps.json / runtimeconfig.json land next to it so
// framework-dependent .NET resolves its deps the way it expects.
const SIDECAR_EXE_UNIX: &str = "Calcpad.Server";
const SIDECAR_EXE_WINDOWS: &str = "Calcpad.Server.exe";
const PORT_READY_TIMEOUT_MS: u64 = 30_000;

#[derive(Default)]
struct ServerState {
    // Send () to signal the running sidecar to shut down. Owned here so the
    // Server menu and window-close flow can trigger a kill without owning
    // the Child directly — the wait task inside spawn_sidecar owns the
    // Child and calls start_kill()+wait() when it receives.
    kill_tx: Mutex<Option<mpsc::Sender<()>>>,
    url: Mutex<Option<String>>,
    intentional_stop: Mutex<bool>,
}

#[derive(Clone, Serialize)]
struct ServerCrashPayload {
    code: Option<i32>,
    tail: String,
}

#[derive(Clone, Serialize)]
struct MenuClickPayload {
    id: String,
}

#[tauri::command]
fn server_url(state: State<'_, ServerState>) -> Option<String> {
    state.url.lock().ok().and_then(|g| g.clone())
}

#[tauri::command]
async fn restart_server(app: AppHandle) -> Result<String, String> {
    stop_sidecar(&app);
    spawn_sidecar(&app).await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn stop_server(app: AppHandle) -> Result<(), String> {
    stop_sidecar(&app);
    Ok(())
}

#[tauri::command]
fn get_env(name: String) -> Option<String> {
    std::env::var(name).ok()
}

#[tauri::command]
fn server_dir(app: AppHandle) -> Result<String, String> {
    // Directory where the sidecar was extracted at install time. Calcpad.Server
    // writes its logs, port file, and cached Chromium download here — the JS
    // bridge needs the path to surface log tails in the Output panel.
    app.path()
        .resolve("", BaseDirectory::Resource)
        .map(|p| p.to_string_lossy().to_string())
        .map_err(|e| e.to_string())
}

/// Launch the .NET calculation server as a background child process.
///
/// **Why not `tauri_plugin_shell::sidecar()`?** Framework-dependent .NET
/// publishes need ~200 sibling DLLs / native libs / deps.json in the same
/// directory as the apphost. Tauri's `externalBin` places binaries in
/// `usr/bin/` on Linux `.deb`, `Contents/MacOS/` on macOS `.app`, and the
/// install root on Windows — all read-only at runtime — while
/// `bundle.resources` land in a separate resource dir. There is no config
/// path that puts both in the same directory, and Tauri v2 has no post-bundle
/// hook (`beforeBundleCommand` runs before packaging). Spawning directly
/// from the resource dir via `tokio::process::Command` sidesteps the layout
/// mismatch entirely — the apphost and its siblings all live under
/// `BaseDirectory::Resource`.
///
/// **macOS limitation** — dropping `externalBin` also drops Tauri's automatic
/// codesigning of the child binary and its ~200 `.dylib` siblings. On macOS,
/// notarization will reject an unsigned bundle. macOS is not a primary
/// target for this project today, so this is deferred. When it becomes one,
/// either (a) add a `codesign --deep --force --sign "$IDENTITY" --options
/// runtime` pass over the publish tree in `beforeBundleCommand`, or (b)
/// revisit once Tauri v2 supports codesigning resource files directly.
/// Tracking upstream: tauri-apps/tauri#8501 (per-arch resources), #11992
/// (macOS notarization + externalBin bugs).
async fn spawn_sidecar(app: &AppHandle) -> Result<String, String> {
    let state: State<'_, ServerState> = app.state();
    *state.intentional_stop.lock().unwrap() = false;

    let parent_pid = std::process::id().to_string();
    // Explicit port-file path in temp so we don't depend on the child's CWD.
    // Rust polls this file — under piped stdio, ASP.NET Core's ConsoleLogger
    // can buffer "Now listening on:" for hundreds of ms; the port file lands
    // within one Kestrel binding cycle regardless.
    let port_file = std::env::temp_dir().join(format!(
        ".calcpad-server-{}-{}.port",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .map(|d| d.as_millis())
            .unwrap_or(0)
    ));
    let _ = std::fs::remove_file(&port_file);
    let port_file_str = port_file.to_string_lossy().into_owned();

    let exe_name = if cfg!(windows) { SIDECAR_EXE_WINDOWS } else { SIDECAR_EXE_UNIX };
    let exe_path = app
        .path()
        .resolve(exe_name, BaseDirectory::Resource)
        .map_err(|e| format!("resource path lookup failed for {exe_name}: {e}"))?;
    let exe_dir = exe_path
        .parent()
        .ok_or_else(|| format!("resolved apphost {exe_path:?} has no parent"))?
        .to_path_buf();

    let spawn_started = Instant::now();
    eprintln!("[sidecar-timing] spawning {:?}", exe_path);
    let mut command = tokio::process::Command::new(&exe_path);
    command
        .args([
            "--no-exit-on-stdin-close",
            "--parent-pid",
            parent_pid.as_str(),
            "--port-file",
            port_file_str.as_str(),
        ])
        // CWD must be the apphost's directory so .NET's dependency resolver
        // finds the sibling DLLs regardless of where this process was started.
        .current_dir(&exe_dir)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    #[cfg(windows)]
    {
        // The apphost is a console subsystem binary; without this it pops up
        // its own visible console window since it has no console to inherit.
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        command.creation_flags(CREATE_NO_WINDOW);
    }
    let mut child = command
        .spawn()
        .map_err(|e| format!("sidecar spawn failed: {e}"))?;
    eprintln!(
        "[sidecar-timing] spawn() returned after {}ms",
        spawn_started.elapsed().as_millis()
    );

    #[cfg(windows)]
    if let Some(pid) = child.id() {
        assign_to_job_object(pid);
    }

    let stdout = child.stdout.take();
    let stderr = child.stderr.take();

    // One-shot for start()'s port-ready promise; readers and the port-file
    // poller both race to fulfil it, whichever wins takes the tx.
    let (tx_url, rx_url) = tokio::sync::oneshot::channel::<Result<String, String>>();
    let tx_url = Arc::new(Mutex::new(Some(tx_url)));

    // Kill signal — stop_sidecar sends into this; the wait task translates
    // it into start_kill() on the Child.
    let (kill_tx, mut kill_rx) = mpsc::channel::<()>(1);
    *state.kill_tx.lock().unwrap() = Some(kill_tx);

    // Rolling tail of the child's combined stdio, shared with the wait task
    // so it can attach the last N bytes to the server-crashed payload.
    let tail = Arc::new(Mutex::new(String::new()));
    let saw_first_output = Arc::new(std::sync::atomic::AtomicBool::new(false));

    // Port-file poller — resolves as soon as Kestrel binds, independent of
    // stdio buffering.
    {
        let tx_url = tx_url.clone();
        let app_for_poll = app.clone();
        let port_file = port_file.clone();
        tauri::async_runtime::spawn(async move {
            for _ in 0..600 {
                if let Ok(bytes) = std::fs::read(&port_file) {
                    let url = String::from_utf8_lossy(&bytes).trim().to_string();
                    if url.starts_with("http") {
                        eprintln!(
                            "[sidecar-timing] port file appeared after {}ms — {}",
                            spawn_started.elapsed().as_millis(),
                            url
                        );
                        let state: State<'_, ServerState> = app_for_poll.state();
                        if let Ok(mut g) = state.url.lock() {
                            *g = Some(url.clone());
                        }
                        if let Some(tx) = tx_url.lock().ok().and_then(|mut g| g.take()) {
                            let _ = tx.send(Ok(url.clone()));
                        }
                        let _ = app_for_poll.emit("server-url", url);
                        eprintln!(
                            "[sidecar-timing] emitted server-url after {}ms",
                            spawn_started.elapsed().as_millis()
                        );
                        break;
                    }
                }
                tokio::time::sleep(Duration::from_millis(50)).await;
            }
        });
    }

    // Stdout/stderr line readers — replaces the shell plugin's CommandEvent
    // stream. Each stream drains on its own task so a chatty stderr doesn't
    // starve stdout. Both feed the shared tail buffer and scan for Kestrel's
    // "Now listening on:" marker as a fallback for the port file.
    //
    // `stdout` and `stderr` are different concrete types (ChildStdout /
    // ChildStderr), so we can't share a single non-generic closure. Type-erase
    // both to `Box<dyn AsyncRead + Send + Unpin>` and hand them to one reader
    // function; keeps the drain logic in one place.
    use tokio::io::AsyncRead;
    fn spawn_stream_reader(
        stream: Box<dyn AsyncRead + Send + Unpin>,
        label: &'static str,
        spawn_started: Instant,
        tx_url: Arc<Mutex<Option<tokio::sync::oneshot::Sender<Result<String, String>>>>>,
        app: AppHandle,
        tail: Arc<Mutex<String>>,
        saw_first: Arc<std::sync::atomic::AtomicBool>,
    ) {
        tauri::async_runtime::spawn(async move {
            let mut lines = BufReader::new(stream).lines();
            loop {
                match lines.next_line().await {
                    Ok(Some(line)) => {
                        if !saw_first.swap(true, std::sync::atomic::Ordering::Relaxed) {
                            eprintln!(
                                "[sidecar-timing] first stdio byte ({}) after {}ms",
                                label,
                                spawn_started.elapsed().as_millis()
                            );
                        }
                        if let Ok(mut t) = tail.lock() {
                            append_tail(&mut t, &line);
                            append_tail(&mut t, "\n");
                        }
                        if let Some(url) = extract_listening_url(&line) {
                            let state: State<'_, ServerState> = app.state();
                            if let Ok(mut g) = state.url.lock() {
                                *g = Some(url.clone());
                            }
                            if let Some(tx) = tx_url.lock().ok().and_then(|mut g| g.take()) {
                                let _ = tx.send(Ok(url.clone()));
                            }
                            let _ = app.emit("server-url", url);
                        }
                    }
                    Ok(None) | Err(_) => break,
                }
            }
        });
    }
    if let Some(s) = stdout {
        spawn_stream_reader(
            Box::new(s),
            "stdout",
            spawn_started,
            tx_url.clone(),
            app.clone(),
            tail.clone(),
            saw_first_output.clone(),
        );
    }
    if let Some(s) = stderr {
        spawn_stream_reader(
            Box::new(s),
            "stderr",
            spawn_started,
            tx_url.clone(),
            app.clone(),
            tail.clone(),
            saw_first_output.clone(),
        );
    }

    // Wait task — owns the Child, races natural exit against the kill signal,
    // and emits `server-crashed` on unintentional termination. This is the
    // piece the shell plugin used to give us for free via CommandEvent::Terminated.
    {
        let app_for_wait = app.clone();
        let tx_url = tx_url.clone();
        let tail = tail.clone();
        tauri::async_runtime::spawn(async move {
            let exit_code: Option<i32> = tokio::select! {
                r = child.wait() => r.ok().and_then(|s| s.code()),
                _ = kill_rx.recv() => {
                    let _ = child.start_kill();
                    child.wait().await.ok().and_then(|s| s.code())
                }
            };
            let state: State<'_, ServerState> = app_for_wait.state();
            if let Ok(mut g) = state.url.lock() {
                *g = None;
            }
            if let Ok(mut g) = state.kill_tx.lock() {
                *g = None;
            }
            let intentional = state
                .intentional_stop
                .lock()
                .map(|g| *g)
                .unwrap_or(false);
            if !intentional {
                let tail_snapshot = tail
                    .lock()
                    .ok()
                    .map(|t| t.clone())
                    .unwrap_or_default();
                let _ = app_for_wait.emit(
                    "server-crashed",
                    ServerCrashPayload {
                        code: exit_code,
                        tail: tail_snapshot,
                    },
                );
                if let Some(tx) = tx_url.lock().ok().and_then(|mut g| g.take()) {
                    let _ = tx.send(Err(format!(
                        "sidecar exited before port ready (code {:?})",
                        exit_code
                    )));
                }
            }
        });
    }

    tokio::time::timeout(Duration::from_millis(PORT_READY_TIMEOUT_MS), rx_url)
        .await
        .map_err(|_| "timed out waiting for server to bind port".to_string())?
        .map_err(|_| "server terminated before reporting url".to_string())?
}

fn stop_sidecar(app: &AppHandle) {
    let state: State<'_, ServerState> = app.state();
    // Mark BEFORE signalling kill so the wait task's post-exit branch reads
    // the correct value and skips the `server-crashed` emit for this shutdown.
    if let Ok(mut g) = state.intentional_stop.lock() {
        *g = true;
    }
    if let Ok(mut g) = state.url.lock() {
        *g = None;
    }
    let kill_tx = state.kill_tx.lock().ok().and_then(|mut g| g.take());
    if let Some(tx) = kill_tx {
        // try_send is fine: the channel has capacity 1 and the wait task
        // only needs to see a single signal to trigger start_kill().
        let _ = tx.try_send(());
    }
}

fn extract_listening_url(line: &str) -> Option<String> {
    // Kestrel logs: "Now listening on: http://127.0.0.1:12345"
    let marker = "Now listening on:";
    let idx = line.find(marker)?;
    let after = line[idx + marker.len()..].trim_start();
    let end = after
        .find(|c: char| c.is_whitespace())
        .unwrap_or(after.len());
    let url = after[..end].trim_end_matches('/').to_string();
    if url.starts_with("http") {
        Some(url)
    } else {
        None
    }
}

fn append_tail(tail: &mut String, chunk: &str) {
    tail.push_str(chunk);
    const MAX: usize = 8 * 1024;
    if tail.len() > MAX {
        let cut = tail.len() - MAX;
        tail.drain(..cut);
    }
}

#[cfg(windows)]
fn assign_to_job_object(pid: u32) {
    use std::sync::OnceLock;
    use windows_sys::Win32::Foundation::{CloseHandle, HANDLE};
    use windows_sys::Win32::System::JobObjects::{
        AssignProcessToJobObject, CreateJobObjectW, SetInformationJobObject,
        JobObjectExtendedLimitInformation, JOBOBJECT_BASIC_LIMIT_INFORMATION,
        JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    };
    use windows_sys::Win32::System::Threading::{OpenProcess, PROCESS_ALL_ACCESS};

    static JOB: OnceLock<isize> = OnceLock::new();
    let job = *JOB.get_or_init(|| unsafe {
        let handle = CreateJobObjectW(std::ptr::null(), std::ptr::null());
        if handle.is_null() {
            return 0;
        }
        let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(
            handle,
            JobObjectExtendedLimitInformation,
            &info as *const _ as *const _,
            std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
        );
        handle as isize
    });
    if job == 0 {
        return;
    }
    unsafe {
        let proc = OpenProcess(PROCESS_ALL_ACCESS, 0, pid);
        if !proc.is_null() {
            AssignProcessToJobObject(job as HANDLE, proc);
            CloseHandle(proc);
        }
    }
}

fn build_menu(app: &AppHandle) -> tauri::Result<Menu<tauri::Wry>> {
    let sep = || PredefinedMenuItem::separator(app);

    let file = Submenu::with_items(
        app,
        "File",
        true,
        &[
            &MenuItem::with_id(app, "new", "New Tab", true, Some("CmdOrCtrl+N"))?,
            &MenuItem::with_id(app, "open", "Open...", true, Some("CmdOrCtrl+O"))?,
            &sep()?,
            &MenuItem::with_id(app, "save", "Save", true, Some("CmdOrCtrl+S"))?,
            &MenuItem::with_id(
                app,
                "save-as",
                "Save As...",
                true,
                Some("CmdOrCtrl+Shift+S"),
            )?,
            &sep()?,
            &MenuItem::with_id(app, "close-tab", "Close Tab", true, Some("CmdOrCtrl+W"))?,
            &sep()?,
            &MenuItem::with_id(app, "export-pdf", "Export PDF...", true, None::<&str>)?,
            &sep()?,
            &MenuItem::with_id(app, "quit", "Quit", true, Some("CmdOrCtrl+Q"))?,
        ],
    )?;

    let edit = Submenu::with_items(
        app,
        "Edit",
        true,
        &[
            &MenuItem::with_id(app, "undo", "Undo", true, Some("CmdOrCtrl+Z"))?,
            &MenuItem::with_id(app, "redo", "Redo", true, Some("CmdOrCtrl+Shift+Z"))?,
            &sep()?,
            &MenuItem::with_id(app, "cut", "Cut", true, Some("CmdOrCtrl+X"))?,
            &MenuItem::with_id(app, "copy", "Copy", true, Some("CmdOrCtrl+C"))?,
            &MenuItem::with_id(app, "paste", "Paste", true, Some("CmdOrCtrl+V"))?,
            &sep()?,
            &MenuItem::with_id(app, "select-all", "Select All", true, Some("CmdOrCtrl+A"))?,
            &MenuItem::with_id(app, "find", "Find", true, Some("CmdOrCtrl+F"))?,
            &MenuItem::with_id(app, "replace", "Replace", true, Some("CmdOrCtrl+H"))?,
        ],
    )?;

    let view = Submenu::with_items(
        app,
        "View",
        true,
        &[
            &MenuItem::with_id(
                app,
                "toggle-sidebar",
                "Toggle Sidebar",
                true,
                Some("CmdOrCtrl+Shift+B"),
            )?,
            &MenuItem::with_id(
                app,
                "toggle-preview",
                "Toggle Preview",
                true,
                Some("CmdOrCtrl+P"),
            )?,
            &MenuItem::with_id(
                app,
                "preview-mode:wrapped",
                "Preview Mode: Wrapped",
                true,
                None::<&str>,
            )?,
            &MenuItem::with_id(
                app,
                "preview-mode:unwrapped",
                "Preview Mode: Unwrapped",
                true,
                None::<&str>,
            )?,
        ],
    )?;

    let server = Submenu::with_items(
        app,
        "Server",
        true,
        &[
            &MenuItem::with_id(app, "refresh", "Refresh", true, Some("F5"))?,
            &MenuItem::with_id(
                app,
                "show-server-log",
                "Show Server Log",
                true,
                None::<&str>,
            )?,
            &MenuItem::with_id(app, "stop-server", "Stop Server", true, None::<&str>)?,
            &MenuItem::with_id(
                app,
                "restart-server",
                "Restart Server",
                true,
                None::<&str>,
            )?,
            &MenuItem::with_id(app, "restart-app", "Restart App", true, None::<&str>)?,
        ],
    )?;

    MenuBuilder::new(app)
        .items(&[&file, &edit, &view, &server])
        .build()
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _argv, _cwd| {
            if let Some(w) = app.get_webview_window("main") {
                let _ = w.show();
                let _ = w.set_focus();
                let _ = w.unminimize();
            }
        }))
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_clipboard_manager::init())
        .plugin(tauri_plugin_store::Builder::default().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_os::init())
        .plugin(tauri_plugin_log::Builder::default().build())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .manage(ServerState::default())
        .invoke_handler(tauri::generate_handler![
            server_url,
            restart_server,
            stop_server,
            get_env,
            server_dir
        ])
        .setup(|app| {
            let menu = build_menu(app.handle())?;
            app.set_menu(menu)?;
            let handle_for_menu = app.handle().clone();
            app.on_menu_event(move |_app, event| {
                let _ = handle_for_menu.emit(
                    "menu-click",
                    MenuClickPayload {
                        id: event.id().0.clone(),
                    },
                );
            });

            let handle_for_spawn = app.handle().clone();
            tauri::async_runtime::spawn(async move {
                match spawn_sidecar(&handle_for_spawn).await {
                    Ok(url) => {
                        let _ = handle_for_spawn.emit("server-url", url);
                    }
                    Err(err) => {
                        let _ = handle_for_spawn.emit("server-startup-error", err);
                    }
                }
            });

            // Main window is configured `visible: true` in tauri.conf.json and
            // intentionally NOT hidden-then-shown here: on GNOME (X11 and
            // Wayland) that pattern leaves titlebar buttons unresponsive until
            // the user double-clicks the titlebar.
            // See tauri-apps/tauri#11856 and #13440.
            Ok(())
        })
        .on_window_event(|window, event| {
            if let WindowEvent::CloseRequested { .. } = event {
                stop_sidecar(&window.app_handle().clone());
            }
        })
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|app, event| {
            if let RunEvent::ExitRequested { .. } = event {
                stop_sidecar(app);
            }
            if let RunEvent::Exit = event {
                stop_sidecar(app);
            }
        });
}
