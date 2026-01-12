#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var rootDir = Args.Length > 0 ? Args[0] : "src";

if (!Directory.Exists(rootDir))
{
    Console.WriteLine($"⚠️ Directory not found: {rootDir}. Nothing to check.");
    return;
}

var csFiles = Directory.GetFiles(rootDir, "*.cs", SearchOption.AllDirectories)
    .Where(f =>
        !f.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) &&
        !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) &&
        !f.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
    .ToList();

var violations = new List<string>();

foreach (var file in csFiles)
{
    var text = File.ReadAllText(file);

    // Пропускаем пустые/только комментарии файлы
    if (string.IsNullOrWhiteSpace(text)) continue;

    var tree = CSharpSyntaxTree.ParseText(text);
    var root = tree.GetRoot();

    // Считаем только TOP-LEVEL типы (не вложенные).
    // То есть те, у которых родитель - namespace (обычный или file-scoped) или compilation unit.
    bool IsTopLevel(SyntaxNode n) =>
        n.Parent is CompilationUnitSyntax ||
        n.Parent is NamespaceDeclarationSyntax ||
        n.Parent is FileScopedNamespaceDeclarationSyntax;

    var topLevelTypes =
        root.DescendantNodes()
            .Where(n => IsTopLevel(n))
            .Where(n =>
                n is ClassDeclarationSyntax ||
                n is InterfaceDeclarationSyntax ||
                n is StructDeclarationSyntax ||
                n is RecordDeclarationSyntax ||
                n is EnumDeclarationSyntax)
            .ToList();

    if (topLevelTypes.Count > 1)
    {
        var names = topLevelTypes.Select(n => n switch
        {
            ClassDeclarationSyntax x => $"class {x.Identifier.Text}",
            InterfaceDeclarationSyntax x => $"interface {x.Identifier.Text}",
            StructDeclarationSyntax x => $"struct {x.Identifier.Text}",
            RecordDeclarationSyntax x => $"record {x.Identifier.Text}",
            EnumDeclarationSyntax x => $"enum {x.Identifier.Text}",
            _ => "type"
        });

        violations.Add($"{file}: найдено {topLevelTypes.Count} типов: {string.Join(", ", names)}");
    }
}

if (violations.Any())
{
    Console.WriteLine("❌ Правило нарушено: один top-level тип на файл.");
    Console.WriteLine("Исправь: вынеси каждый тип в отдельный .cs файл.");
    Console.WriteLine();
    foreach (var v in violations) Console.WriteLine(v);
    Environment.Exit(1);
}

Console.WriteLine("✅ OK: один top-level тип на файл.");
