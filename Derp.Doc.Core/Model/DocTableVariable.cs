using Derp.Doc.Plugins;

namespace Derp.Doc.Model;

public sealed class DocTableVariable
{
    private DocColumnKind _kind = DocColumnKind.Text;
    private string _columnTypeId = DocColumnTypeIds.Text;

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "variable";
    public DocColumnKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            if (DocColumnTypeIdMapper.ShouldSyncWithKind(_columnTypeId))
            {
                _columnTypeId = DocColumnTypeIdMapper.FromKind(value);
            }
        }
    }

    public string ColumnTypeId
    {
        get => _columnTypeId;
        set => _columnTypeId = DocColumnTypeIdMapper.Resolve(value, _kind);
    }

    public string Expression { get; set; } = "";

    public DocTableVariable Clone()
    {
        return new DocTableVariable
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            ColumnTypeId = ColumnTypeId,
            Expression = Expression,
        };
    }
}
