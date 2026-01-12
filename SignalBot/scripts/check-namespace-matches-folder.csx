#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Args:
// 0: rootDir (default "src")
// 1: baseNamespace (default "MyCompany.MyProduct")
var rootDir = Args.Length > 0 ? Args[0] : "src";
var baseNs  = Args.Length > 1 ? Args[1] : "MyCompany.MyProduct";

if (!Directory.Exists(rootDir))
{
    Console.WriteLine($"⚠️ Directory not found: {rootDir}. Nothing to check.");
    return;
}

string NormalizePath(string p) => p.Replace('\\', '/').Trim('/');

string ExpectedNamespaceForFile(string filePath)
{
    // filePath относителен rootDir
    var rel = Path.GetRelativePath(rootDir, filePath);
    var dir = Path.GetDirectoryName(rel) ?? "";
    dir = NormalizePath(dir);

    if (string.IsNullOrWhiteSpace(dir))
        return baseNs;

    var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);

    // Если хочешь PascalCase нормализацию, тут можно добавить преобразование.
    var suffix = string.Join(".", parts);
    return $"{baseNs}.{suffix}";
}

string? GetDeclaredNamespace(SyntaxNode root)
{
    // Поддержка file-scoped namespace
    var fileScoped = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
    if (fileScoped != null) return fileScoped.Name.ToString();

    // Поддержка обычного namespace (берем первый верхний)
    var normal = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
    if (normal != null) return normal.Name.ToString();

    // Нет namespace (например, top-level statements) — считаем нарушением для обычных проектов
    return null;
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
    if (string.IsNullOrWhiteSpace(text)) continue;

    var tree = CSharpSyntaxTree.ParseText(text);
    var root = tree.GetRoot();

    // Если файл вообще не содержит типов, можно не проверять namespace
    var hasTypes =
        root.DescendantNodes().Any(n =>
            n is ClassDeclarationSyntax ||
            n is InterfaceDeclarationSyntax ||
            n is StructDeclarationSyntax ||
            n is RecordDeclarationSyntax ||
            n is EnumDeclarationSyntax);

    if (!hasTypes) continue;

    var declared = GetDeclaredNamespace(root);
    var expected = ExpectedNamespaceForFile(file);

    if (declared == null)
    {
        violations.Add($"{file}: namespace не указан, ожидается '{expected}'");
        continue;
    }

    if (!string.Equals(declared, expected, StringComparison.Ordinal))
    {
        violations.Add($"{file}: namespace '{declared}', ожидается '{expected}'");
    }
}

if (violations.Any())
{
    Console.WriteLine("❌ Правило нарушено: namespace должен соответствовать папке.");
    Console.WriteLine($"Root: {rootDir}, BaseNamespace: {baseNs}");
    Console.WriteLine();
    foreach (var v in violations) Console.WriteLine(v);
    Environment.Exit(1);
}

Console.WriteLine("✅ OK: namespace соответствует папке.");
