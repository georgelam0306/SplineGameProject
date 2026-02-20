namespace Derp.Doc.Model;

public sealed class DocBlockTableVariableOverride
{
    public string VariableId { get; set; } = "";
    public string Expression { get; set; } = "";

    public DocBlockTableVariableOverride Clone()
    {
        return new DocBlockTableVariableOverride
        {
            VariableId = VariableId,
            Expression = Expression,
        };
    }
}
