namespace Migrator.Engine.Hosting;

public enum DiagnosticSeverity { Info, Warning, Error }

public sealed record EngineDiagnostic(DiagnosticSeverity Severity, string Message);
