using System.Collections.Concurrent;
using Godot;

namespace Dw3Production;

/// <summary>
/// Shown when the data pack is absent, outdated, or corrupt.
/// Prompts the user to select the source disc (or uses DW3_TEST_DISC for automated testing),
/// spawns Dw3.Extractor in a child process, displays progress, and emits PackReady
/// when the game may continue loading.
/// </summary>
public partial class FirstLaunchScreen : CanvasLayer
{
    private readonly string _dataDir;

    private readonly string _extractorPath;

    // Main-thread actions queued from the extraction background thread.
    private readonly ConcurrentQueue<Action> _mainActions = new();

    // Probe support: DW3_FIRST_LAUNCH_PROBE=1 enables screenshot captures at key
    // lifecycle points for automated visual verification.  All capture code is
    // guarded by this flag and has no effect on nominal gameplay.
    private readonly bool _probeActive;

    private readonly string? _captureDir;

    private Label? _statusLabel;

    private Thread? _extractionThread;

    private bool _progressCaptured;

    /// <summary>
    /// Initializes a new instance of the <see cref="FirstLaunchScreen"/> class.
    /// Shown when the data pack is absent or invalid; the screen manages extraction
    /// and emits <see cref="PackReady"/> once the pack is usable.
    /// </summary>
    /// <param name="dataDir">Directory where the extractor writes the data pack and where progress captures are saved.</param>
    /// <param name="extractorPath">Path to the Dw3.Extractor binary launched as a child process.</param>
    public FirstLaunchScreen(string dataDir, string extractorPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractorPath);
        _dataDir = dataDir;
        _extractorPath = extractorPath;
        Layer = 128;  // Draw on top of everything.
        _probeActive = System.Environment.GetEnvironmentVariable("DW3_FIRST_LAUNCH_PROBE") == "1";
        if (_probeActive)
        {
            _captureDir = System.Environment.GetEnvironmentVariable("DW3_CAPTURE_DIR");
        }
    }

    /// <summary>Emitted on the main thread when extraction completes and the pack is ready.</summary>
    [Signal]
    public delegate void PackReadyEventHandler();

    /// <inheritdoc/>
    public override void _Ready()
    {
        BuildOverlay();

        // When DW3_TEST_DISC is set the file dialog is bypassed so automated
        // tests under Xvfb can drive the full first-launch flow headlessly.
        string? testDisc = System.Environment.GetEnvironmentVariable("DW3_TEST_DISC");
        if (!string.IsNullOrWhiteSpace(testDisc))
        {
            SetStatus($"Test mode: extracting from {testDisc}...");
            if (_probeActive)
            {
                // Probe: capture the pre-extraction state, then start extraction.
                _ = CaptureAfterFrameAsync(
                    "first-launch-initial.png",
                    thenDo: () => BeginExtraction(testDisc));
            }
            else
            {
                BeginExtraction(testDisc);
            }
        }
        else
        {
            SetStatus("Select your Digimon World 2003 (Europe) disc to continue.");

            // FileDialog needs the window to be visible; defer by one frame.
            CallDeferred(MethodName.OpenDiscDialog);

            // Probe: capture the initial "select disc" prompt.
            if (_probeActive)
            {
                _ = CaptureAfterFrameAsync("first-launch-initial.png");
            }
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        // Drain actions queued from the background extraction thread.
        while (_mainActions.TryDequeue(out Action? action))
        {
            action();
        }
    }

    // ---------------------------------------------------------------------------
    // UI helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Adds the semi-transparent backdrop and the status label as direct children of this
    /// <see cref="Godot.CanvasLayer"/>. Reads the viewport size explicitly because a
    /// <see cref="Godot.Control"/> child of a CanvasLayer does not receive the viewport rect
    /// via anchors before the first layout pass.
    /// </summary>
    private void BuildOverlay()
    {
        // In Godot 4, a Control added as a direct CanvasLayer child does not automatically
        // receive the viewport size via anchors before the first layout pass.  Read the
        // viewport rectangle explicitly and pass it down to every child so that the overlay
        // is correctly sized even when _Ready runs before the layout pass.
        Rect2 vp = GetViewport().GetVisibleRect();

        // Semi-transparent black background covering the full viewport.
        var root = new Control { Position = vp.Position, Size = vp.Size };
        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.88f),
            Position = Vector2.Zero,
            Size = vp.Size,
        };
        root.AddChild(background);

        var label = new Label
        {
            Position = new Vector2(vp.Size.X * 0.1f, vp.Size.Y * 0.4f),
            Size = new Vector2(vp.Size.X * 0.8f, vp.Size.Y * 0.2f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        root.AddChild(label);
        _statusLabel = label;

        AddChild(root);
    }

    /// <summary>Updates the status label text on the main thread.</summary>
    /// <param name="text">User-facing message displayed in the overlay label.</param>
    private void SetStatus(string text)
    {
        _statusLabel?.Text = text;
    }

    // ---------------------------------------------------------------------------
    // File dialog
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Opens a native file picker constrained to <c>.cue</c> files so the user can select
    /// the source disc. Connected to <see cref="OnDiscSelected"/> on confirmation and to a
    /// closure that shows an error message on cancellation.
    /// </summary>
    private void OpenDiscDialog()
    {
        var dialog = new FileDialog
        {
            Title = "Select Digimon World 2003 (Europe) disc",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.cue ; Disc cue sheet (*.cue)"],
            Size = new Vector2I(700, 450),
        };
        dialog.FileSelected += OnDiscSelected;
        dialog.Canceled += () =>
        {
            SetStatus("No disc selected. Close and restart the game to try again.");
            dialog.QueueFree();
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>Called when the user confirms the disc selection in the file picker.</summary>
    /// <param name="path">Absolute path to the chosen <c>.cue</c> file.</param>
    private void OnDiscSelected(string path)
    {
        SetStatus($"Extracting from {path}...");
        BeginExtraction(path);
    }

    // ---------------------------------------------------------------------------
    // Extraction
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Spawns an <see cref="ExtractionController"/> and starts the extraction background thread.
    /// Progress and completion events are marshalled back to the main thread via the action queue.
    /// </summary>
    /// <param name="discPath">Absolute path to the disc cue sheet file passed to the extractor.</param>
    private void BeginExtraction(string discPath)
    {
        var controller = new ExtractionController(new SystemExtractorLauncher(_extractorPath));

        controller.ProgressUpdated += (domain, done, total) =>
            _mainActions.Enqueue(() =>
            {
                SetStatus($"Extracting: {domain} ({done}/{total})");

                // Probe: capture the first progress update to show an in-progress state.
                if (_probeActive && !_progressCaptured)
                {
                    _progressCaptured = true;
                    _ = CaptureAfterFrameAsync("first-launch-progress.png");
                }
            });

        controller.Completed += () =>
            _mainActions.Enqueue(OnExtractionComplete);

        controller.Failed += message =>
            _mainActions.Enqueue(() =>
            {
                SetStatus($"Extraction failed:\n{message}\n\nRestart the game and try again.");

                // Probe: capture the error state then quit successfully (proof of error UI).
                if (_probeActive)
                {
                    _ = CaptureAfterFrameAsync("first-launch-error.png", quitAfter: true);
                }
            });

        _extractionThread = new Thread(() => controller.Extract(discPath, _dataDir))
        {
            IsBackground = true,
            Name = "ExtractorThread",
        };
        _extractionThread.Start();
    }

    /// <summary>Called on the main thread when the extraction controller fires its Completed event.</summary>
    private void OnExtractionComplete()
    {
        SetStatus("Extraction complete. Loading...");
        EmitSignal(SignalName.PackReady);
    }

    // ---------------------------------------------------------------------------
    // Probe helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Waits for one fully-rendered frame, then saves a viewport screenshot to
    /// <c>DW3_CAPTURE_DIR/{filename}</c>. Only called when <c>_probeActive</c> is true.
    /// </summary>
    /// <param name="filename">File name written under the active capture directory.</param>
    /// <param name="quitAfter">When <see langword="true"/>, calls <c>GetTree().Quit(0)</c> after saving.</param>
    /// <param name="thenDo">Optional action invoked after the capture is saved, before the optional quit.</param>
    /// <returns>A task that completes after the capture is saved and any post-capture actions are done.</returns>
    private async System.Threading.Tasks.Task CaptureAfterFrameAsync(
        string filename, bool quitAfter = false, Action? thenDo = null)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string dir = _captureDir ?? System.IO.Path.Combine(_dataDir, "captures");
        System.IO.Directory.CreateDirectory(dir);
        string path = System.IO.Path.Combine(dir, filename);
        var image = GetViewport().GetTexture().GetImage();
        Error saveErr = image.SavePng(path);
        GD.Print(saveErr == Error.Ok
            ? $"FIRST_LAUNCH_PROBE capture OK: {path}"
            : $"FIRST_LAUNCH_PROBE capture FAIL ({saveErr}): {path}");
        thenDo?.Invoke();
        if (quitAfter)
        {
            GetTree().Quit(0);
        }
    }
}
