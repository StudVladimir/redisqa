using System.Collections.Generic;

namespace redisqa.Models;

public class AttributeModel
{
    public string Name { get; set; } = string.Empty;
    public bool IsPrimaryKey { get; set; }
    public bool IsIndex { get; set; }
    public bool IsForeignKey { get; set; }
    
    // Foreign key references
    public List<ForeignKeyReference>? ForeignKeyReferences { get; set; }
}

public class ForeignKeyReference
{
    public string Condition { get; set; } = "from";
    public string ReferenceTable { get; set; } = string.Empty;
    public string ReferenceAttribute { get; set; } = string.Empty;
}
