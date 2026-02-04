namespace redisqa.Models;

public class FKLinkModel
{
    public TableModel SourceTable { get; set; } = null!;
    public AttributeModel SourceAttribute { get; set; } = null!;
    public TableModel TargetTable { get; set; } = null!;
    public AttributeModel TargetAttribute { get; set; } = null!;
}
