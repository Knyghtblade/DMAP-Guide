using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DmapShared;

/// <summary>Reads the words out of a PDF (typed text only; a scanned page has none to read).</summary>
public static class PdfText
{
    public static string Read(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var text = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            // Keeps the words in reading order, line by line, as they sit on the page.
            string words = ContentOrderTextExtractor.GetText(page).Trim();
            if (words.Length == 0) continue;
            if (text.Length > 0) text.Append("\n\n");
            text.Append(words.Replace("\r\n", "\n"));
        }
        return text.ToString();
    }
}
