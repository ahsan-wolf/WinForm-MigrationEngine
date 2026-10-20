using System.Text.RegularExpressions;
using Migrator.Engine.Assemblies;

namespace Migrator.Target.React.Contracts;

/// <summary>
/// Secrets must never reach browser code. Anything whose name or type looks like a credential, key, license or connection string is
/// omitted from generated types and services and listed in the report as server-only. Deliberately over-inclusive: a false positive
/// costs one line in the report, a false negative leaks a secret.
/// </summary>
public static partial class SecretFilter
{
    [GeneratedRegex(@"password|passwd|pwd|secret|encryptionkey|licen[sc]ecode|connectionstring|credential|apikey|accesskey|privatekey|token", RegexOptions.IgnoreCase)]
    private static partial Regex DenyRegex();

    public static bool IsSecretName(string name) => DenyRegex().IsMatch(name);

    public static bool IsSecretProperty(PropertyContract p) => IsSecretName(p.Name) || IsSecretName(p.Type);

    private static readonly HashSet<string> TextOrPrimitive = new(StringComparer.Ordinal)
    {
        "string", "System.String", "char", "byte", "byte[]", "short", "int", "long", "bool", "float", "double", "System.Decimal"
    };

    /// <summary>
    /// Omit the method entirely when its name or return type is secret-like (GetEncryptionKey, ...) or it takes a secret as plain data
    /// (a string/primitive parameter named password, token, ...). A secret-looking parameter that is an OBJECT (a credentials holder)
    /// is handled by <see cref="IsCredentialObject"/> instead: the operation stays, the parameter goes.
    /// </summary>
    public static bool IsSecretMethod(MethodContract m) =>
        IsSecretName(m.Name) || IsSecretName(m.ReturnType)
        || m.Parameters.Any(p => IsSecretName(p.Name) && TextOrPrimitive.Contains(p.Type));

    /// <summary>A non-primitive parameter whose type or name marks it as a credentials holder (NetworkCredential, ServiceCredentialsInfo, ...).
    /// Browser code never holds credentials, so the generated signature drops it and the server supplies it.</summary>
    public static bool IsCredentialObject(ParameterContract p) =>
        !TextOrPrimitive.Contains(p.Type) && (IsSecretName(p.Type) || IsSecretName(p.Name));
}
