namespace Derp.UI;

internal enum EditorCommandId : byte
{
    FileNew = 0,
    FileOpen = 1,
    FileSave = 2,
    FileSaveAs = 3,
    FileExit = 4,

    EditUndo = 5,
    EditRedo = 6,
    EditCut = 7,
    EditCopy = 8,
    EditPaste = 9,
    EditDuplicate = 10,
    EditDelete = 11,

    WindowLayers = 12,
    WindowCanvas = 13,
    WindowInspector = 14,
    WindowVariables = 15,
    WindowAnimations = 16,
    WindowTools = 17,

    HelpAbout = 18,

    FileExportBdui = 19,
}
