namespace ShotX.Services;

/// <summary>Tiny key-based localization catalog (Spanish/English). Not a full resx setup —
/// ShotX only needs two languages and a few dozen short strings.</summary>
public static class Loc
{
    public static event Action? LanguageChanged;

    public static string CurrentLanguage => SettingsService.Current.Language == "en" ? "en" : "es";

    public static void SetLanguage(string language)
    {
        SettingsService.Current.Language = language == "en" ? "en" : "es";
        SettingsService.Save();
        LanguageChanged?.Invoke();
    }

    public static string T(string key)
    {
        if (Catalog.TryGetValue(key, out var pair))
        {
            return CurrentLanguage == "en" ? pair.En : pair.Es;
        }
        return key;
    }

    private static readonly Dictionary<string, (string Es, string En)> Catalog = new()
    {
        // Main window
        ["main.whatToDo"] = ("¿Qué quieres hacer?", "What do you want to do?"),
        ["main.region"] = ("Región", "Region"),
        ["main.fullScreen"] = ("Pantalla completa", "Full screen"),
        ["main.activeWindow"] = ("Ventana activa", "Active window"),
        ["main.openImage"] = ("Abrir imagen...", "Open image..."),
        ["main.openFolder"] = ("Abrir carpeta", "Open folder"),
        ["main.settings"] = ("Configuración", "Settings"),

        // Tray menu
        ["tray.captureRegion"] = ("Capturar región", "Capture region"),
        ["tray.captureFullScreen"] = ("Capturar pantalla completa", "Capture full screen"),
        ["tray.captureWindow"] = ("Capturar ventana activa", "Capture active window"),
        ["tray.openEditor"] = ("Abrir editor", "Open editor"),
        ["tray.openFolder"] = ("Abrir carpeta de capturas", "Open screenshots folder"),
        ["tray.settings"] = ("Configuración...", "Settings..."),
        ["tray.exit"] = ("Salir", "Exit"),

        // Editor toolbar tooltips
        ["tool.select"] = ("Seleccionar / mover", "Select / move"),
        ["tool.rectangle"] = ("Rectángulo", "Rectangle"),
        ["tool.ellipse"] = ("Elipse", "Ellipse"),
        ["tool.line"] = ("Línea", "Line"),
        ["tool.arrow"] = ("Flecha", "Arrow"),
        ["tool.pen"] = ("Lápiz", "Pen"),
        ["tool.highlighter"] = ("Resaltador", "Highlighter"),
        ["tool.text"] = ("Texto (aparece un cuadro para escribir, luego se puede mover/redimensionar)",
            "Text (a box appears to type in, then move/resize it)"),
        ["tool.stepNumber"] = ("Número de paso", "Step number"),
        ["tool.blur"] = ("Difuminar", "Blur"),
        ["tool.crop"] = ("Recortar", "Crop"),
        ["tool.undo"] = ("Deshacer (Ctrl+Z)", "Undo (Ctrl+Z)"),
        ["tool.redo"] = ("Rehacer (Ctrl+Y)", "Redo (Ctrl+Y)"),
        ["tool.save"] = ("Guardar (Ctrl+S)", "Save (Ctrl+S)"),
        ["tool.copy"] = ("Copiar al portapapeles (Ctrl+C)", "Copy to clipboard (Ctrl+C)"),
        ["tool.importImage"] = ("Importar imagen", "Import image"),
        ["tool.image"] = ("Imagen", "Image"),

        ["editor.size"] = ("Tamaño (grosor de línea o tamaño de texto, según lo seleccionado)",
            "Size (stroke width or text size, depending on what's selected)"),
        ["editor.sizeDecrease"] = ("Disminuir tamaño", "Decrease size"),
        ["editor.sizeIncrease"] = ("Aumentar tamaño", "Increase size"),
        ["editor.background"] = ("Relleno (texto, rectángulo o elipse)", "Fill (text, rectangle or ellipse)"),
        ["editor.noBackground"] = ("Sin relleno", "No fill"),
        ["editor.opacity"] = ("Opacidad", "Opacity"),
        ["editor.align"] = ("Alinear texto (texto, rectángulo o elipse)", "Align text (text, rectangle or ellipse)"),
        ["editor.alignHorizontal"] = ("Horizontal", "Horizontal"),
        ["editor.alignVertical"] = ("Vertical", "Vertical"),
        ["editor.alignLeft"] = ("Izquierda", "Left"),
        ["editor.alignCenterH"] = ("Centro", "Center"),
        ["editor.alignRight"] = ("Derecha", "Right"),
        ["editor.alignTop"] = ("Arriba", "Top"),
        ["editor.alignCenterV"] = ("Centro", "Center"),
        ["editor.alignBottom"] = ("Abajo", "Bottom"),
        ["window.minimize"] = ("Minimizar", "Minimize"),
        ["window.maximize"] = ("Maximizar", "Maximize"),
        ["window.restore"] = ("Restaurar", "Restore"),
        ["window.close"] = ("Cerrar", "Close"),

        // Editor bottom bar
        ["editor.copy"] = ("Copiar", "Copy"),
        ["editor.saveAs"] = ("Guardar como...", "Save as..."),
        ["editor.save"] = ("Guardar", "Save"),
        ["editor.copiedToClipboard"] = ("Copiado al portapapeles", "Copied to clipboard"),
        ["editor.savedAt"] = ("Guardado en {0}", "Saved to {0}"),
        ["editor.saveFailed"] = ("No se pudo guardar la imagen", "Could not save the image"),
        ["editor.unsavedChanges"] = ("Hay cambios sin guardar. ¿Cerrar de todos modos?",
            "There are unsaved changes. Close anyway?"),
        ["editor.unsaved"] = ("sin guardar", "unsaved"),
        ["editor.applyCrop"] = ("Aplicar recorte", "Apply crop"),
        ["editor.cancel"] = ("Cancelar", "Cancel"),

        // Layers panel
        ["layers.title"] = ("Capas", "Layers"),
        ["layers.empty"] = ("Sin elementos todavía", "Nothing here yet"),
        ["layers.lock"] = ("Bloquear", "Lock"),
        ["layers.unlock"] = ("Desbloquear", "Unlock"),
        ["layers.delete"] = ("Eliminar", "Delete"),
        ["layers.moveUp"] = ("Subir", "Move up"),
        ["layers.moveDown"] = ("Bajar", "Move down"),
        ["layers.type.rectangle"] = ("Rectángulo", "Rectangle"),
        ["layers.type.ellipse"] = ("Elipse", "Ellipse"),
        ["layers.type.line"] = ("Línea", "Line"),
        ["layers.type.arrow"] = ("Flecha", "Arrow"),
        ["layers.type.pen"] = ("Lápiz", "Pen"),
        ["layers.type.highlighter"] = ("Resaltador", "Highlighter"),
        ["layers.type.text"] = ("Texto", "Text"),
        ["layers.type.stepNumber"] = ("Paso", "Step"),
        ["layers.type.blur"] = ("Difuminado", "Blur"),
        ["layers.type.image"] = ("Imagen", "Image"),

        // Capture overlay hint
        ["overlay.hint"] = ("Arrastra para seleccionar una zona · clic sobre una ventana para capturarla · Esc para cancelar",
            "Drag to select an area · click a window to capture it · Esc to cancel"),

        // Settings window
        ["settings.title"] = ("Configuración", "Settings"),
        ["settings.appearance"] = ("Apariencia", "Appearance"),
        ["settings.language"] = ("Idioma", "Language"),
        ["settings.theme"] = ("Tema", "Theme"),
        ["settings.themeSystem"] = ("Sistema", "System"),
        ["settings.themeLight"] = ("Claro", "Light"),
        ["settings.themeDark"] = ("Oscuro", "Dark"),
        ["settings.capture"] = ("Captura", "Capture"),
        ["settings.autoCopy"] = ("Copiar al portapapeles automáticamente", "Copy to clipboard automatically"),
        ["settings.autoOpenEditor"] = ("Abrir editor al capturar", "Open editor when capturing"),
        ["settings.playSound"] = ("Sonido de obturador", "Shutter sound"),
        ["settings.hotkeys"] = ("Atajos de teclado", "Keyboard shortcuts"),
        ["settings.hotkeyRegion"] = ("Capturar región", "Capture region"),
        ["settings.hotkeyFullScreen"] = ("Capturar pantalla completa", "Capture full screen"),
        ["settings.hotkeyWindow"] = ("Capturar ventana activa", "Capture active window"),
        ["settings.pressKeys"] = ("Presiona una tecla...", "Press a key..."),
        ["settings.pressKeysHint"] = ("Presiona la combinación que quieras usar, o Esc para cancelar.",
            "Press the key combination you want to use, or Esc to cancel."),
        ["settings.hotkeyDuplicate"] = ("Esa combinación ya la usa otra acción.", "That combination is already used by another action."),
        ["settings.saving"] = ("Guardado", "Saving"),
        ["settings.saveFolder"] = ("Carpeta de capturas", "Screenshots folder"),
        ["settings.chooseFolder"] = ("Elegir...", "Choose..."),
        ["settings.chooseFolderDialogTitle"] = ("Elegir carpeta para guardar capturas", "Choose folder to save screenshots"),
        ["settings.startup"] = ("Inicio", "Startup"),
        ["settings.startWithWindows"] = ("Iniciar ShotX con Windows", "Start ShotX with Windows"),
        ["settings.updates"] = ("Actualizaciones", "Updates"),
        ["settings.autoUpdate"] = ("Instalar actualizaciones automáticamente", "Install updates automatically"),
        ["settings.checkUpdates"] = ("Buscar ahora", "Check now"),
        ["settings.installUpdate"] = ("Instalar {0}", "Install {0}"),
        ["settings.about"] = ("Acerca de", "About"),
        ["settings.mcp"] = ("Acceso de IA (servidor MCP)", "AI access (MCP server)"),
        ["settings.mcpEnabled"] = ("Activar servidor MCP", "Enable MCP server"),
        ["settings.mcpRunning"] = ("Corriendo en {0}", "Running at {0}"),
        ["settings.mcpNotRunning"] = ("No está corriendo: {0}", "Not running: {0}"),
        ["settings.mcpDisabled"] = ("Desactivado", "Disabled"),
        ["settings.connectClaude"] = ("Conectar Claude", "Connect Claude"),
        ["settings.claudeCodeHint"] = ("O conéctalo desde Claude Code con:", "Or connect it from Claude Code with:"),
    };
}
