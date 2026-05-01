#if TOOLS
using Godot;
using System.Collections.Generic;
using System.IO;

namespace PS1Godot.UI;

// Lowers the barrier-of-entry for new authors: pick a template,
// pick a save path, click Create. The plugin then duplicates the
// chosen template under res://, opens the new scene, and the
// author's working from a known-good starting point instead of
// hand-assembling PS1Scene + PS1Player + Camera3D + nav region.
//
// Templates source: addons/ps1godot/templates/*.tscn (5 hand-authored
// scenes covering empty / demo / menu / gameplay / intro splash).
// Templates README has per-template fill-in guidance — exposed via
// the description panel below the picker.
[Tool]
public partial class PS1NewSceneWizard : AcceptDialog
{
    private sealed record Template(string FileName, string DisplayName, string Description);

    private static readonly List<Template> Templates = new()
    {
        new("empty_template.tscn", "Empty",
            "Smallest valid scene — just PS1Scene + PS1Player + Camera3D. Boots black; build outward from here."),
        new("demo_template.tscn", "Demo (floor + cubes + HUD)",
            "Empty + a floor + 2 colored cubes + a HUD label. Smallest scene that's actually visually interesting on F5."),
        new("menu_template.tscn", "Title menu (UI only)",
            "Title screen / main menu — pure UI, no 3D gameplay. Title text + 'PRESS START' prompt; advance via Scene.Load(1)."),
        new("gameplay_template.tscn", "Gameplay level",
            "Level scaffolding — PS1Player + floor + an ExampleTrigger box. Add walls, props, more triggers as needed."),
        new("intro_splash_template.tscn", "Intro splash (boot logo)",
            "Boot-logo splash — 3D spinning logo + studio name + 'Licensed by …' text + chime + auto-transition to game."),
    };

    private const string TemplateDir = "res://addons/ps1godot/templates/";

    private OptionButton _picker = null!;
    private Label        _desc   = null!;
    private LineEdit     _pathEdit = null!;
    private FileDialog?  _fileDialog;

    public PS1NewSceneWizard()
    {
        Title = "PS1Godot: New Scene from Template";
        OkButtonText = "Create";
        DialogCloseOnEscape = true;
        Size = new Vector2I(560, 280);

        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 8);
        AddChild(vbox);

        // Template picker
        var pickerRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pickerRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(pickerRow);
        pickerRow.AddChild(new Label { Text = "Template:", CustomMinimumSize = new Vector2(80, 0) });
        _picker = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        for (int i = 0; i < Templates.Count; i++)
            _picker.AddItem(Templates[i].DisplayName, i);
        _picker.ItemSelected += _ => RefreshDescription();
        pickerRow.AddChild(_picker);

        _desc = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.Word,
            CustomMinimumSize = new Vector2(0, 60),
        };
        _desc.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        vbox.AddChild(_desc);

        // Save path
        var pathRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        pathRow.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(pathRow);
        pathRow.AddChild(new Label { Text = "Save to:", CustomMinimumSize = new Vector2(80, 0) });
        _pathEdit = new LineEdit
        {
            Text = "res://my_scene.tscn",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = "res://path/to/scene.tscn",
        };
        pathRow.AddChild(_pathEdit);
        var browse = new Button { Text = "Browse…" };
        browse.Pressed += OnBrowse;
        pathRow.AddChild(browse);

        var hint = new Label
        {
            Text = "After Create: the duplicate is opened in the editor. The original template is never modified.",
        };
        hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
        hint.AddThemeFontSizeOverride("font_size", 11);
        vbox.AddChild(hint);

        Confirmed += OnConfirmed;

        _picker.Selected = 0;
        RefreshDescription();
    }

    private void RefreshDescription()
    {
        int idx = _picker.Selected;
        if (idx < 0 || idx >= Templates.Count) { _desc.Text = ""; return; }
        _desc.Text = Templates[idx].Description;
    }

    private void OnBrowse()
    {
        if (_fileDialog == null)
        {
            _fileDialog = new FileDialog
            {
                FileMode      = FileDialog.FileModeEnum.SaveFile,
                Access        = FileDialog.AccessEnum.Resources,
                Title         = "Save new PS1 scene as…",
                CurrentDir    = "res://",
                Size          = new Vector2I(700, 480),
            };
            _fileDialog.AddFilter("*.tscn", "Godot scenes (*.tscn)");
            _fileDialog.FileSelected += path =>
            {
                _pathEdit.Text = path;
            };
            AddChild(_fileDialog);
        }
        _fileDialog.PopupCentered();
    }

    private void OnConfirmed()
    {
        int idx = _picker.Selected;
        if (idx < 0 || idx >= Templates.Count) return;

        string templatePath = TemplateDir + Templates[idx].FileName;
        string savePath = _pathEdit.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(savePath))
        {
            GD.PushError("[PS1Godot] New scene wizard: save path is empty.");
            ShowError("Save path is required.");
            return;
        }
        if (!savePath.StartsWith("res://"))
        {
            ShowError("Save path must start with res://. Use Browse… to pick a project-relative location.");
            return;
        }
        if (!savePath.EndsWith(".tscn", System.StringComparison.OrdinalIgnoreCase))
            savePath += ".tscn";

        // Read the template + write the duplicate. Use FileAccess so
        // we go through Godot's res:// resolver (handles export-pack
        // and PCK overlays correctly even though we're editor-side).
        string? templateText = ReadResText(templatePath);
        if (templateText == null)
        {
            ShowError($"Couldn't read template: {templatePath}");
            return;
        }

        string absSavePath = ProjectSettings.GlobalizePath(savePath);
        if (File.Exists(absSavePath))
        {
            ShowError($"File already exists: {savePath}\n\nPick a different path or delete the existing scene first.");
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absSavePath) ?? "");
            File.WriteAllText(absSavePath, templateText);
        }
        catch (System.Exception e)
        {
            ShowError($"Write failed: {e.Message}");
            return;
        }

        // Re-scan res:// so EditorInterface picks up the new file
        // before we try to open it. ScanSources is the cheap path.
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        EditorInterface.Singleton.OpenSceneFromPath(savePath);
        GD.Print($"[PS1Godot] Created scene from {Templates[idx].DisplayName}: {savePath}");
    }

    private static string? ReadResText(string resPath)
    {
        using var fa = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        if (fa == null) return null;
        return fa.GetAsText();
    }

    private void ShowError(string message)
    {
        var err = new AcceptDialog
        {
            Title = "PS1Godot: New Scene wizard",
            DialogText = message,
        };
        AddChild(err);
        err.PopupCentered();
    }
}
#endif
