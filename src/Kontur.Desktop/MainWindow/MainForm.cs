using Kontur.Core;

namespace Kontur.Desktop;

/// <summary>Connects parameter controls, the drawing and application commands.</summary>
internal sealed partial class MainForm : Form
{
    private readonly Dictionary<string, TextBox> inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ErrorProvider errors = new()
    {
        BlinkStyle = ErrorBlinkStyle.NeverBlink
    };
    private readonly ToolTip tips = new()
    {
        AutoPopDelay = 14000,
        InitialDelay = 400
    };
    private readonly SketchCanvas sketch = new()
    {
        Dock = DockStyle.Fill,
        ShowTolerances = true
    };
    private readonly Button exportButton = Theme.Button("Сформировать документы", true);
    private readonly ComboBox controllerInput = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private ResultInspectionView? inspection;
    private readonly WorkspaceTabs workspace = new();
    private readonly TabPage geometryPage = new("Геометрия") { BackColor = Theme.Surface, Padding = Padding.Empty };
    private TabPage? inspectionPage;
    private TabPage? processingPage;
    private ProcessingView? processing;
    internal ProcessingView? Processing => processing;
    private readonly ToolStripMenuItem toleranceView = new("Показывать допуски")
    {
        CheckOnClick = true,
        Checked = true
    };
    private readonly ColumnStyle geometryColumn = new(SizeType.Absolute, 440);
    private readonly Panel geometryHost = new()
    {
        Dock = DockStyle.Fill,
        Margin = Padding.Empty
    };
    private readonly Panel dimensionFlyout = new()
    {
        Visible = false,
        BackColor = Theme.Border,
        Padding = new Padding(1),
        Name = "DimensionFlyout"
    };
    private Control geometryInputs = null!;
    private DimensionEditor? dimensionEditor;
    internal DimensionEditor? ActiveDimensionEditor => dimensionEditor;

    private TableLayoutPanel? dimensionTable;
    private TableLayoutPanel? geometrySections;
    private bool fittingGeometry;
    private readonly Dictionary<string, DeviationReadout> deviationReadouts = [];
    internal DeviationReadout DeviationView(string key) => deviationReadouts[key];
    internal bool ToleranceViewVisible => toleranceView.Checked;

    private readonly ActionableStatusLabel status = new();
    internal ActionableStatusLabel Diagnostic => status;
    internal TabControl Workspace => workspace;
    private readonly System.Windows.Forms.Timer previewTimer = new()
    {
        Interval = 180
    };
    private Parameters working = Engine.Defaults;
    private Project? project;
    private bool loading;
    private bool stale = true;
    private readonly List<Font> smokeFonts = [];
    internal Project? CurrentProject => project;
    internal bool IsStale => stale;

    internal MainForm()
    {
        Text = "ParamForge " + Engine.Version;
        Name = "ParamForgeMainForm";
        Font = Theme.Body;
        ForeColor = Theme.Ink;
        BackColor = Theme.Surface;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 850);
        // Keep the drawing's minimum working area after adding the page strip.
        MinimumSize = new Size(1188, 794);
        StartPosition = FormStartPosition.CenterScreen;
        errors.ContainerControl = this;
        InitializeLayout();
        workspace.CloseRequested += page =>
        {
            if (page == inspectionPage)
            {
                CloseInspection();
            }
            else if (page == processingPage)
            {
                CloseProcessing();
            }
        };
        status.Bind(DiagnosticTarget, NavigateDiagnostic);
        Resize += (_, _) => PositionDimensionFlyout();
        geometryHost.SizeChanged += (_, _) => PositionDimensionFlyout();
        geometryHost.LocationChanged += (_, _) => PositionDimensionFlyout();
        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            UpdatePreview();
        };
        sketch.NominalRequested += FocusNominal;
        sketch.NominalSubmitted += CommitNominal;
        sketch.DimensionDragStarted += BeginDimensionDrag;
        sketch.DimensionDragPreview += PreviewDimensionDrag;
        sketch.DimensionDragFinished += FinishDimensionDrag;
        SetParameters(Engine.Defaults with
        {
            AutoStock = true
        });
        GenerateNow();
    }
    internal SketchCanvas Sketch => sketch;
    internal ResultInspectionView? Inspection => inspection;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            errors.Dispose();
            tips.Dispose();
            previewTimer.Dispose();
            foreach (var font in smokeFonts)
            {
                font.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
