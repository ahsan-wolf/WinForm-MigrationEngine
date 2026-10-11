using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Migrator.Engine.Assemblies;

/// <summary>
/// Reads the public API of a managed assembly with System.Reflection.Metadata. The file is parsed, never loaded into the process,
/// so none of its code runs. Method bodies are not read.
/// </summary>
public static partial class MetadataContractReader
{
    public static AssemblyContract Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a managed assembly (no CLR metadata).");

        var md = pe.GetMetadataReader();
        var names = new NameProvider(md);
        var assembly = md.GetAssemblyDefinition();
        var contract = new AssemblyContract
        {
            Name = md.GetString(assembly.Name),
            Version = assembly.Version.ToString(),
            References = md.AssemblyReferences.Select(h => md.GetAssemblyReference(h)).Select(r => $"{md.GetString(r.Name)} {r.Version}").OrderBy(s => s, StringComparer.Ordinal).ToList()
        };

        foreach (var handle in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(handle);
            if (!IsExported(md, td)) continue;
            var type = ReadType(md, names, handle, td);
            if (type.Name != "<Module>") contract.Types.Add(type);
        }

        contract.Types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return contract;
    }

    // ------------------------------------------------------------------------------------------ data access hints

    [GeneratedRegex(@"\b(?:FROM|JOIN|INTO|UPDATE)\s+(\[?[A-Za-z_][\w]*\]?(?:\.\[?[A-Za-z_][\w]*\]?)*)", RegexOptions.IgnoreCase)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"^\s*(?:SELECT|INSERT|UPDATE|DELETE|EXEC(?:UTE)?|WITH)\b|\bFROM\b.+\bWHERE\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SqlRegex();

    [GeneratedRegex(@"\b(?:password|pwd|uid|user\s*id|data\s*source|server|initial\s*catalog)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionRegex();

    [GeneratedRegex(@"^\s*EXEC(?:UTE)?\s+(\[?[\w\.]+\]?)", RegexOptions.IgnoreCase)]
    private static partial Regex ExecRegex();

    /// <summary>
    /// Scans the user-string heap (string literals in code) for SQL. Only table and procedure NAMES are kept. Connection-string-like
    /// literals are counted and discarded so credentials can never reach an output file.
    /// </summary>
    public static DataAccessHints ReadDataAccessHints(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var hints = new DataAccessHints();
        if (!pe.HasMetadata) return hints;

        var md = pe.GetMetadataReader();
        var procs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var h = MetadataTokens.UserStringHandle(1); !h.IsNil; h = md.GetNextHandle(h))
        {
            var text = md.GetUserString(h);
            if (text.Length < 8) continue;
            if (ConnectionRegex().IsMatch(text)) { hints.ConnectionStringLikeCount++; continue; }
            if (!SqlRegex().IsMatch(text)) continue;

            hints.SqlLiteralCount++;
            foreach (Match m in TableRegex().Matches(text))
            {
                var table = m.Groups[1].Value.Trim('[', ']');
                if (table.StartsWith("@") || table.Length < 3) continue;
                hints.Tables[table] = hints.Tables.GetValueOrDefault(table) + 1;
            }
            if (ExecRegex().Match(text) is { Success: true } e) procs.Add(e.Groups[1].Value.Trim('[', ']'));
        }
        hints.StoredProcedures = procs.ToList();
        hints.Tables = hints.Tables.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(kv => kv.Key, kv => kv.Value);
        return hints;
    }

    // ------------------------------------------------------------------------------------------------------ types

    private static bool IsExported(MetadataReader md, TypeDefinition td)
    {
        var vis = td.Attributes & TypeAttributes.VisibilityMask;
        if (vis == TypeAttributes.Public) return true;
        if (vis != TypeAttributes.NestedPublic) return false;
        var outer = td.GetDeclaringType();
        return !outer.IsNil && IsExported(md, md.GetTypeDefinition(outer));
    }

    private static TypeContract ReadType(MetadataReader md, NameProvider names, TypeDefinitionHandle handle, TypeDefinition td)
    {
        var baseName = td.BaseType.IsNil ? null : names.Describe(td.BaseType);
        var isInterface = (td.Attributes & TypeAttributes.Interface) != 0;
        var kind = isInterface ? "interface"
            : baseName == "System.Enum" ? "enum"
            : baseName == "System.ValueType" ? "struct"
            : baseName is "System.MulticastDelegate" or "System.Delegate" ? "delegate"
            : "class";

        var type = new TypeContract
        {
            Namespace = md.GetString(td.Namespace),
            Name = NameProvider.StripArity(md.GetString(td.Name)),
            FullName = names.FullName(handle),
            Kind = kind,
            BaseType = kind is "class" or "struct" && baseName is not (null or "System.Object" or "System.ValueType") ? baseName : null,
            IsAbstract = (td.Attributes & TypeAttributes.Abstract) != 0 && !isInterface,
            IsStatic = (td.Attributes & TypeAttributes.Abstract) != 0 && (td.Attributes & TypeAttributes.Sealed) != 0,
            GenericParameterCount = td.GetGenericParameters().Count
        };

        foreach (var ih in td.GetInterfaceImplementations())
            type.Interfaces.Add(names.Describe(md.GetInterfaceImplementation(ih).Interface));

        if (kind == "enum") ReadEnum(md, td, type);
        else if (kind != "delegate") ReadMembers(md, names, td, type);
        return type;
    }

    private static void ReadEnum(MetadataReader md, TypeDefinition td, TypeContract type)
    {
        foreach (var fh in td.GetFields())
        {
            var fd = md.GetFieldDefinition(fh);
            var name = md.GetString(fd.Name);
            if (name == "value__" || (fd.Attributes & FieldAttributes.Literal) == 0) continue;
            var constant = md.GetConstant(fd.GetDefaultValue());
            var value = md.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode);
            type.EnumMembers.Add(new EnumMemberContract { Name = name, Value = System.Convert.ToInt64(value) });
        }
    }

    private static void ReadMembers(MetadataReader md, NameProvider names, TypeDefinition td, TypeContract type)
    {
        foreach (var fh in td.GetFields())
        {
            var fd = md.GetFieldDefinition(fh);
            if ((fd.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public) continue;
            type.Fields.Add(new FieldContract
            {
                Name = md.GetString(fd.Name),
                Type = fd.DecodeSignature(names, null),
                IsStatic = (fd.Attributes & FieldAttributes.Static) != 0,
                IsConst = (fd.Attributes & FieldAttributes.Literal) != 0
            });
        }

        foreach (var ph in td.GetProperties())
        {
            var pd = md.GetPropertyDefinition(ph);
            var accessors = pd.GetAccessors();
            bool Public(MethodDefinitionHandle h) => !h.IsNil && (md.GetMethodDefinition(h).Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
            var canRead = Public(accessors.Getter);
            var canWrite = Public(accessors.Setter);
            if (!canRead && !canWrite) continue;

            var sig = pd.DecodeSignature(names, null);
            var anyAccessor = canRead ? accessors.Getter : accessors.Setter;
            type.Properties.Add(new PropertyContract
            {
                Name = md.GetString(pd.Name),
                Type = sig.ReturnType,
                CanRead = canRead,
                CanWrite = canWrite,
                IsStatic = (md.GetMethodDefinition(anyAccessor).Attributes & MethodAttributes.Static) != 0
            });
        }

        foreach (var mh in td.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            if ((m.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public) continue;
            var name = md.GetString(m.Name);
            var isCtor = name == ".ctor";
            if (!isCtor && (m.Attributes & MethodAttributes.SpecialName) != 0) continue; // get_/set_/add_/remove_/operators
            if (name == ".cctor") continue;

            var sig = m.DecodeSignature(names, null);
            var parameters = new List<ParameterContract>();
            var declared = m.GetParameters().Select(h => md.GetParameter(h)).Where(p => p.SequenceNumber > 0).ToDictionary(p => p.SequenceNumber, p => md.GetString(p.Name));
            for (var i = 0; i < sig.ParameterTypes.Length; i++)
                parameters.Add(new ParameterContract { Name = declared.GetValueOrDefault(i + 1) ?? $"arg{i}", Type = sig.ParameterTypes[i] });

            type.Methods.Add(new MethodContract
            {
                Name = isCtor ? type.Name : name,
                ReturnType = isCtor ? type.Name : sig.ReturnType,
                IsStatic = (m.Attributes & MethodAttributes.Static) != 0,
                IsConstructor = isCtor,
                Parameters = parameters
            });
        }

        foreach (var eh in td.GetEvents())
        {
            var ed = md.GetEventDefinition(eh);
            type.Events.Add(new EventContract { Name = md.GetString(ed.Name), Type = names.Describe(ed.Type) });
        }
    }

    // ------------------------------------------------------------------------------------------ type name decoding

    private sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _md;

        public NameProvider(MetadataReader md) => _md = md;

        public static string StripArity(string name)
        {
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name[..tick];
        }

        public string FullName(TypeDefinitionHandle handle)
        {
            var td = _md.GetTypeDefinition(handle);
            var name = StripArity(_md.GetString(td.Name));
            var outer = td.GetDeclaringType();
            if (!outer.IsNil) return FullName(outer) + "+" + name;
            var ns = _md.GetString(td.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        public string Describe(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => FullName((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeFromReference(_md, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(_md, null, (TypeSpecificationHandle)handle, 0),
            _ => "?"
        };

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool", PrimitiveTypeCode.Byte => "byte", PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Char => "char", PrimitiveTypeCode.Int16 => "short", PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.Int32 => "int", PrimitiveTypeCode.UInt32 => "uint", PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.UInt64 => "ulong", PrimitiveTypeCode.Single => "float", PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.String => "string", PrimitiveTypeCode.Object => "object", PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.IntPtr => "nint", PrimitiveTypeCode.UIntPtr => "nuint", PrimitiveTypeCode.TypedReference => "typedref",
            _ => typeCode.ToString()
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FullName(handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var tr = reader.GetTypeReference(handle);
            var name = StripArity(reader.GetString(tr.Name));
            if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
                return GetTypeFromReference(reader, (TypeReferenceHandle)tr.ResolutionScope, 0) + "+" + name;
            var ns = reader.GetString(tr.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(shape.Rank - 1, 0)) + "]";
        public string GetByReferenceType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(", ", typeArguments)}>";
        public string GetGenericMethodParameter(object? genericContext, int index) => "T" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "T" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    }
}
