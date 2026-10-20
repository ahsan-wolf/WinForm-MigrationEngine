using Migrator.Engine.Projections;
using WebFormsMigrator.IR.Contracts;
using WebFormsMigrator.IR.Models;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>
/// Facet "controlTree": populates <see cref="PageIr.Name"/> and <see cref="PageIr.RootControl"/> from the Designer.cs
/// (<c>MarkupContent</c>) and code-behind (<c>CodeBehindContent</c>). Only real controls become nodes (no anonymous noise).
/// </summary>
public sealed class WinFormsControlTreeExtractor : IIrExtractor
{
    private readonly IReadOnlyDictionary<string, WinFormsSourceText> _userControls;
    private readonly int _maxRecursionDepth;
    private readonly WinFormsTypeResolver? _resolver;

    /// <param name="userControls">
    /// Simple class name -> source of every UserControl in the project. A field whose type matches is inlined as real
    /// child nodes instead of an opaque leaf.
    /// </param>
    /// <param name="maxRecursionDepth">Guards against UserControls that (incorrectly) reference each other cyclically.</param>
    public WinFormsControlTreeExtractor(IReadOnlyDictionary<string, WinFormsSourceText>? userControls = null, int maxRecursionDepth = 4, WinFormsTypeResolver? resolver = null)
    {
        _resolver = resolver;
        _userControls = userControls ?? new Dictionary<string, WinFormsSourceText>();
        _maxRecursionDepth = maxRecursionDepth;
    }

    public string Facet => "controlTree";

    public void Extract(IrExtractionContext context, PageIr target)
    {
        var designer = WinFormsDesignerParser.Parse(context.MarkupContent);
        target.Name = designer.ClassName;
        target.MasterPageName = null; // no WinForms equivalent of a Web Forms master page
        target.RootControl = BuildRoot(designer, context.CodeBehindContent ?? "", ResxReader.ReadSibling(context.FilePath), _maxRecursionDepth);
    }

    /// <summary>Records the standard framework type behind a custom control (attribute <c>ResolvedType</c>); standard types need nothing.</summary>
    private void Resolve(ControlNodeIr node)
    {
        if (_resolver is null || node.TagName.StartsWith("System.Windows.Forms.", StringComparison.Ordinal)) return;
        if (_resolver.Resolve(node.TagName) is { } resolved) node.Attributes[ControlTypes.ResolvedTypeKey] = resolved;
    }

    private ControlNodeIr BuildRoot(DesignerParseResult designer, string codeBehind, string? resourcesXml, int remainingDepth)
    {
        // Designer.cs partials rarely repeat the base-type clause; the code-behind usually does, so it wins.
        var baseType = CodeBehind.FindClass(codeBehind, designer.ClassName)?.BaseList?.Types.FirstOrDefault()?.Type.ToString()
                       ?? designer.BaseTypeName
                       ?? "Form";

        var root = new ControlNodeIr
        {
            Id = designer.ClassName,
            TagName = CodeBehind.NormalizeBaseType(baseType),
            Attributes = new Dictionary<string, string>(designer.RootAttributes)
        };
        foreach (var (eventName, handler) in designer.RootEventHandlers)
            root.Attributes[$"On{eventName}"] = handler;
        Resolve(root);

        var resources = ResxReader.Parse(resourcesXml);
        var images = ResxReader.ParseImages(resourcesXml);
        var imageStreams = ResxReader.ParseImageStreams(resourcesXml);
        if (designer.ResourceKeys.TryGetValue("", out var rootKey)) MergeResources(root.Attributes, resources, rootKey);
        MergeImages(root.Attributes, images, designer.ResourceKeys.GetValueOrDefault(""), "Icon");

        var nodesById = new Dictionary<string, ControlNodeIr>();
        foreach (var id in designer.ControlDeclarationOrder.Concat(designer.DeclaredControlTypes.Keys).Distinct())
        {
            if (!designer.DeclaredControlTypes.TryGetValue(id, out var tagName)) continue;

            var attributes = designer.ControlAttributes.TryGetValue(id, out var a)
                ? new Dictionary<string, string>(a)
                : new Dictionary<string, string>();
            if (designer.ControlEventHandlers.TryGetValue(id, out var handlers))
                foreach (var (eventName, handler) in handlers)
                    attributes[$"On{eventName}"] = handler;

            if (designer.ResourceKeys.TryGetValue(id, out var key)) MergeResources(attributes, resources, key);
            MergeImages(attributes, images, designer.ResourceKeys.GetValueOrDefault(id), "Image");
            MergeImageList(attributes, imageStreams, designer.ResourceKeys.GetValueOrDefault(id));

            var node = new ControlNodeIr { Id = id, TagName = tagName, Attributes = attributes };
            Resolve(node);

            if (remainingDepth > 0)
            {
                var simpleType = tagName[(tagName.LastIndexOf('.') + 1)..];
                if (_userControls.TryGetValue(simpleType, out var source))
                {
                    var inner = WinFormsDesignerParser.Parse(source.DesignerSource);
                    node.Children.AddRange(BuildRoot(inner, source.CodeBehindSource, source.ResourcesSource, remainingDepth - 1).Children);
                }
            }

            nodesById[id] = node;
        }

        foreach (var (childId, parentId) in designer.ParentOf)
        {
            if (!nodesById.TryGetValue(childId, out var child)) continue;

            if (parentId.Length > 0 && nodesById.TryGetValue(parentId, out var parent))
                parent.Children.Add(child);
            else
                root.Children.Add(child); // parent is the form itself, or declared elsewhere (another partial)
        }

        // Non-visual components never added to a container (Timer, ToolTip, BindingSource, ...) still surface at the root.
        foreach (var (id, node) in nodesById)
            if (!designer.ParentOf.ContainsKey(id))
                root.Children.Add(node);

        return root;
    }

    /// <summary>
    /// The image a control's Image (or the form's Icon) points at, when it is a readable byte-array resource: kept as base64 under the pseudo-attributes
    /// <c>{property}.Data</c> and <c>{property}.Format</c> (png, gif, jpg, bmp, ico, svg), so generators can write it as a web asset.
    /// </summary>
    private static void MergeImages(Dictionary<string, string> attributes, Dictionary<string, byte[]> images, string? resourceKey, string property)
    {
        if (images.Count == 0) return;
        var name = ResxReader.ImageResourceName(attributes, resourceKey, property);
        if (name is null || !images.TryGetValue(name, out var bytes)) return;
        attributes[$"{property}.Data"] = Convert.ToBase64String(bytes);
        attributes[$"{property}.Format"] = ResxReader.Sniff(bytes);
    }

    /// <summary>
    /// An ImageList's images: the `ImageStream` resource decoded into one 32-bit BMP per image (base64 JSON array under the pseudo-attribute <c>Images.Data</c>)
    /// plus <c>Images.Size</c> ("16x16"); the keys come from the designer's SetKeyName calls (<c>Images.Keys</c>). Streams that cannot be decoded are left out.
    /// </summary>
    private static void MergeImageList(Dictionary<string, string> attributes, Dictionary<string, byte[]> streams, string? resourceKey)
    {
        if (streams.Count == 0 || !attributes.ContainsKey("ImageStream")) return;
        var name = ResxReader.ImageResourceName(attributes, resourceKey, "ImageStream");
        if (name is null || !streams.TryGetValue(name, out var bytes) || ImageListDecoder.Decode(bytes) is not { } list) return;
        attributes["Images.Data"] = System.Text.Json.JsonSerializer.Serialize(list.Images.Select(Convert.ToBase64String));
        attributes["Images.Size"] = $"{list.Width}x{list.Height}";
    }

    /// <summary>Localizable forms keep Text/ToolTip in the .resx; values set in code win over the resource.</summary>
    private static void MergeResources(Dictionary<string, string> attributes, Dictionary<string, string> resources, string key)
    {
        foreach (var property in ResxReader.MergedProperties)
            if (!attributes.ContainsKey(property) && resources.TryGetValue($"{key}.{property}", out var value))
                attributes[property] = value;
    }
}
