using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AutoHPMA.Core.Services;

public sealed class ClubQuizQuestionBank
{
    private static readonly Regex AsciiAndWhitespaceRegex = new("[a-zA-Z\\s]+", RegexOptions.Compiled);
    private static readonly Regex CellColumnRegex = new("^[A-Z]+", RegexOptions.Compiled);

    private readonly IReadOnlyList<QuestionAnswer> _items;

    private ClubQuizQuestionBank(IReadOnlyList<QuestionAnswer> items)
    {
        _items = items;
    }

    public int Count => _items.Count;

    public static ClubQuizQuestionBank Load(string xlsxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xlsxPath);
        if (!File.Exists(xlsxPath))
        {
            throw new FileNotFoundException("社团问答题库文件不存在。", xlsxPath);
        }

        using var archive = ZipFile.OpenRead(xlsxPath);
        var sharedStrings = ReadSharedStrings(archive);
        var worksheetPath = ResolveFirstWorksheetPath(archive);
        var worksheetEntry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException($"题库工作表不存在：{worksheetPath}");

        using var stream = worksheetEntry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        var items = new List<QuestionAnswer>();
        foreach (var row in document.Descendants(ns + "row"))
        {
            var cells = row.Elements(ns + "c")
                .Select((cell, index) => new
                {
                    Column = GetCellColumn(cell, index),
                    Value = ReadCellValue(cell, sharedStrings, ns),
                })
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Value))
                .ToDictionary(cell => cell.Column, cell => cell.Value.Trim(), StringComparer.OrdinalIgnoreCase);

            if (!cells.TryGetValue("A", out var question) ||
                !cells.TryGetValue("B", out var answer) ||
                string.IsNullOrWhiteSpace(question) ||
                string.IsNullOrWhiteSpace(answer))
            {
                continue;
            }

            if (items.Count == 0 &&
                question.Trim().Equals("问题", StringComparison.OrdinalIgnoreCase) &&
                answer.Trim().Equals("答案", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalizedQuestion = NormalizeQuestion(question);
            if (string.IsNullOrWhiteSpace(normalizedQuestion) ||
                items.Any(item => item.NormalizedQuestion == normalizedQuestion))
            {
                continue;
            }

            items.Add(new QuestionAnswer(question.Trim(), normalizedQuestion, answer.Trim()));
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException("社团问答题库为空。");
        }

        return new ClubQuizQuestionBank(items);
    }

    public ClubQuizQuestionMatch FindBestMatch(string? question)
    {
        var normalizedQuestion = NormalizeQuestion(question);
        if (string.IsNullOrWhiteSpace(normalizedQuestion) || _items.Count == 0)
        {
            return new ClubQuizQuestionMatch(string.Empty, "未找到匹配项", 0);
        }

        QuestionAnswer? bestItem = null;
        var bestScore = 0d;
        foreach (var item in _items)
        {
            var score = CalculateSimilarity(normalizedQuestion, item.NormalizedQuestion);
            if (score > bestScore)
            {
                bestScore = score;
                bestItem = item;
            }
        }

        return bestItem is null
            ? new ClubQuizQuestionMatch(string.Empty, "未找到匹配项", 0)
            : new ClubQuizQuestionMatch(bestItem.Question, bestItem.Answer, bestScore);
    }

    public static char FindBestOption(string? answer, IReadOnlyDictionary<char, string?> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedAnswer = NormalizeAnswer(answer);
        var bestOption = 'A';
        var bestScore = double.MinValue;

        foreach (var (key, optionText) in options)
        {
            var score = CalculateSimilarity(normalizedAnswer, NormalizeAnswer(optionText));
            if (score > bestScore)
            {
                bestScore = score;
                bestOption = char.ToUpperInvariant(key);
            }
        }

        return bestOption;
    }

    public static string NormalizeQuestion(string? input) =>
        AsciiAndWhitespaceRegex.Replace(input ?? string.Empty, string.Empty).Trim();

    private static string NormalizeAnswer(string? input) =>
        Regex.Replace(input ?? string.Empty, "\\s+", string.Empty).Trim();

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var ns = document.Root?.Name.Namespace ?? XNamespace.None;

        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static string ResolveFirstWorksheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relationshipsEntry is null)
        {
            return "xl/worksheets/sheet1.xml";
        }

        using var workbookStream = workbookEntry.Open();
        using var relationshipsStream = relationshipsEntry.Open();
        var workbook = XDocument.Load(workbookStream);
        var relationships = XDocument.Load(relationshipsStream);

        var workbookNs = workbook.Root?.Name.Namespace ?? XNamespace.None;
        var relationshipNs = XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        var packageRelationshipNs = XNamespace.Get("http://schemas.openxmlformats.org/package/2006/relationships");

        var firstSheet = workbook.Descendants(workbookNs + "sheet").FirstOrDefault();
        var relationshipId = firstSheet?.Attribute(relationshipNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return "xl/worksheets/sheet1.xml";
        }

        var target = relationships
            .Descendants(packageRelationshipNs + "Relationship")
            .FirstOrDefault(relationship => relationship.Attribute("Id")?.Value == relationshipId)
            ?.Attribute("Target")
            ?.Value;

        if (string.IsNullOrWhiteSpace(target))
        {
            return "xl/worksheets/sheet1.xml";
        }

        return target.StartsWith("/", StringComparison.Ordinal)
            ? target.TrimStart('/')
            : $"xl/{target}".Replace('\\', '/');
    }

    private static string GetCellColumn(XElement cell, int fallbackIndex)
    {
        var reference = cell.Attribute("r")?.Value ?? string.Empty;
        var match = CellColumnRegex.Match(reference);
        return match.Success ? match.Value : ColumnNameFromIndex(fallbackIndex);
    }

    private static string ColumnNameFromIndex(int index)
    {
        var column = string.Empty;
        var value = index + 1;
        while (value > 0)
        {
            value--;
            column = (char)('A' + value % 26) + column;
            value /= 26;
        }

        return column;
    }

    private static string ReadCellValue(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        XNamespace worksheetNs)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(worksheetNs + "t").Select(text => text.Value));
        }

        var rawValue = cell.Element(worksheetNs + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(rawValue, out var sharedStringIndex) &&
            sharedStringIndex >= 0 &&
            sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return rawValue;
    }

    private static double CalculateSimilarity(string source, string target)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
        {
            return 0;
        }

        var maxLength = Math.Max(source.Length, target.Length);
        return 1d - LevenshteinDistance(source, target) / (double)maxLength;
    }

    private static int LevenshteinDistance(string source, string target)
    {
        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];

        for (var j = 0; j <= target.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length];
    }

    private sealed record QuestionAnswer(
        string Question,
        string NormalizedQuestion,
        string Answer);
}

public sealed record ClubQuizQuestionMatch(
    string Question,
    string Answer,
    double Score);
