using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WebFormsMigrator.WinFormsAnalyzers;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- <Form.Designer.cs> <Form.cs> [output.ir.json] [customControlsDir]");
            Console.WriteLine();
            Console.WriteLine("  customControlsDir: a folder containing *.Designer.cs / *.cs pairs for any");
            Console.WriteLine("  custom UserControls referenced by the form, so they're inlined as real");
            Console.WriteLine("  child nodes instead of opaque leaves. See Sample/ for the expected layout.");
            return;
        }

        var designerPath = args[0];
        var codeBehindPath = args[1];
        var outputPath = args.Length > 2
            ? args[2]
            : Path.ChangeExtension(Path.GetFileName(codeBehindPath), null) + ".ir.json";

        var mainUnit = new WinFormsSourceUnit
        {
            DesignerSource = File.ReadAllText(designerPath),
            CodeBehindSource = File.ReadAllText(codeBehindPath)
        };

        Dictionary<string, WinFormsSourceUnit>? customControls = null;
        if (args.Length > 3 && Directory.Exists(args[3]))
        {
            customControls = new Dictionary<string, WinFormsSourceUnit>();
            foreach (var designerFile in Directory.GetFiles(args[3], "*.Designer.cs"))
            {
                var codeFile = designerFile.Substring(0, designerFile.Length - ".Designer.cs".Length) + ".cs";
                if (!File.Exists(codeFile)) continue;

                var typeName = Path.GetFileNameWithoutExtension(codeFile);
                customControls[typeName] = new WinFormsSourceUnit
                {
                    DesignerSource = File.ReadAllText(designerFile),
                    CodeBehindSource = File.ReadAllText(codeFile)
                };
            }
        }

        var ir = WinFormsIrExtractor.Extract(mainUnit, customControls);

        var json = JsonSerializer.Serialize(ir, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = null // keep PascalCase to match the existing *.ir.json samples
        });

        File.WriteAllText(outputPath, json);
        Console.WriteLine($"Wrote {outputPath}");
    }
}
