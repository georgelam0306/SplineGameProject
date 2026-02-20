namespace Derp.Doc.Model;

[Flags]
public enum DocFormulaEvalScope
{
    None = 0,
    Interactive = 1 << 0,
    Commit = 1 << 1,
    Export = 1 << 2,
}
