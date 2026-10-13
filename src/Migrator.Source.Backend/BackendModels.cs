namespace Migrator.Source.Backend;

/// <summary>Rules for the backend analyzer (data file `rules/backend-patterns.json`; defaults work without it).</summary>
public sealed class BackendPatterns
{
    public List<string> WindowsNamespaces { get; set; } = new();
    public List<string> WindowsNames { get; set; } = new();
    public List<string> WindowsAttributes { get; set; } = new();
    public List<string> ConfigAccess { get; set; } = new();
    public List<string> HostContext { get; set; } = new();
    public List<string> DataAccessTypes { get; set; } = new();
    public List<string> ProxyBaseTypes { get; set; } = new();
    public List<string> ServiceBaseTypes { get; set; } = new();
    public List<string> ServiceAttributes { get; set; } = new();
    public List<string> MethodAttributes { get; set; } = new();
}

public sealed record SourceSummary(string Root, int CsFiles, int Classes, int Asmx, int ConfigFiles);

/// <summary>One occurrence of a pattern. Never carries the source text (it may contain secrets), only where and what kind.</summary>
public sealed record Finding(string Category, string Pattern, string File, string Class, string Member, int Line);

public sealed record OperationInfo(string Name, string ReturnType, IReadOnlyList<string> Parameters, int Statements, IReadOnlyList<string> CallsClasses, bool DataAccess, bool Windows, bool HostContext, bool Config);

public sealed record ServiceInfo(string Name, string File, string BaseType, IReadOnlyList<OperationInfo> Operations);

public sealed record ProxyInfo(string Name, string File, string BaseType, IReadOnlyList<string> Operations, int UrlAssignments);

public sealed record StaticStateInfo(string Class, string Name, string Type, string Kind, string File, int Line);

public sealed record FacadeInfo(string Name, string File, bool IsStatic, int Methods, int Thin, int Logic, int DataAccess, int Windows, IReadOnlyList<string> Proxies);

public sealed record ConfigKeys(string File, IReadOnlyList<string> AppSettings, IReadOnlyList<string> ConnectionStrings);

public sealed record WsdlInfo(string File, IReadOnlyList<string> Services, IReadOnlyList<string> Endpoints, IReadOnlyList<string> Operations);

public sealed class BackendAnalysis
{
    public List<SourceSummary> Sources { get; } = new();
    public List<ServiceInfo> Services { get; } = new();
    public List<ProxyInfo> Proxies { get; } = new();
    public List<FacadeInfo> Facades { get; } = new();
    public List<StaticStateInfo> StaticState { get; } = new();
    public List<Finding> Findings { get; } = new();
    public List<ConfigKeys> Config { get; } = new();
    public List<WsdlInfo> Wsdls { get; } = new();
    public List<string> Warnings { get; } = new();
}
