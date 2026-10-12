namespace Migrator.Engine.Assemblies;

// Public surface of a managed assembly, read from metadata only (the assembly is never loaded or executed).

public sealed class ParameterContract
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

public sealed class MethodContract
{
    public string Name { get; set; } = "";
    public string ReturnType { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool IsConstructor { get; set; }
    public List<ParameterContract> Parameters { get; set; } = new();
}

public sealed class PropertyContract
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
    public bool IsStatic { get; set; }
}

public sealed class FieldContract
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsStatic { get; set; }
    public bool IsConst { get; set; }
}

public sealed class EnumMemberContract
{
    public string Name { get; set; } = "";
    public long Value { get; set; }
}

public sealed class EventContract
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}

public sealed class TypeContract
{
    public string Namespace { get; set; } = "";
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";

    /// <summary>class | struct | enum | interface | delegate</summary>
    public string Kind { get; set; } = "class";

    public string? BaseType { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public int GenericParameterCount { get; set; }
    public List<FieldContract> Fields { get; set; } = new();
    public List<PropertyContract> Properties { get; set; } = new();
    public List<MethodContract> Methods { get; set; } = new();
    public List<EventContract> Events { get; set; } = new();
    public List<EnumMemberContract> EnumMembers { get; set; } = new();
}

public sealed class AssemblyContract
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public List<string> References { get; set; } = new();
    public List<TypeContract> Types { get; set; } = new();
}

/// <summary>Table names and counts found in SQL-looking string literals. Connection-string-like literals are counted, never stored.</summary>
public sealed class DataAccessHints
{
    public int SqlLiteralCount { get; set; }
    public int ConnectionStringLikeCount { get; set; }
    public Dictionary<string, int> Tables { get; set; } = new();
    public List<string> StoredProcedures { get; set; } = new();
}
