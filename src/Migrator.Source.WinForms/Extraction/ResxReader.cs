using System.Xml.Linq;

namespace Migrator.Source.WinForms.Extraction;

/// <summary>Reads the plain-string entries of a Form/UserControl .resx (e.g. "btnSave.Text" -> "Save").</summary>
internal static class ResxReader
{
    /// <summary>Only string-valued properties that carry user-visible text are merged; geometry and binary resources are not.</summary>
    public static readonly string[] MergedProperties = { "Text", "ToolTip", "ToolTipText", "HeaderText", "PlaceholderText" };

    public static Dictionary<string, string> Parse(string? resxXml)
    {
        var entries = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(resxXml)) return entries;

        try
        {
            foreach (var data in XDocument.Parse(resxXml).Root?.Elements("data") ?? Enumerable.Empty<XElement>())
            {
                var name = (string?)data.Attribute("name");
                // Skip designer metadata (">>btn.Name") and typed/binary entries (images, icons).
                if (string.IsNullOrEmpty(name) || name.StartsWith(">>") || data.Attribute("type") != null || data.Attribute("mimetype") != null)
                    continue;
                entries[name] = (string?)data.Element("value") ?? "";
            }
        }
        catch (System.Xml.XmlException)
        {
            // A malformed .resx must not fail the form: values simply are not merged.
        }
        return entries;
    }

    public static string? ReadSibling(string codeBehindPath)
    {
        var path = Path.ChangeExtension(codeBehindPath, ".resx");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
