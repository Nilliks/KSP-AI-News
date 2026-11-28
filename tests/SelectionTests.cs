using System.Collections.Generic;
using KerbalNews.Web;

/// <summary>Copying selected page text: how paragraphs, rows and table cells are joined.</summary>
public static partial class WebTests
{
    static SelectionText.Piece P(string t, float x, float y, float w, float h) { return new SelectionText.Piece { Text = t, X = x, Y = y, W = w, H = h, FontSize = 16 }; }

    public static void TestSelectionTextJoin()
    {
        // Two paragraphs a margin apart, then a line right under the second.
        var s = SelectionText.Join(new List<SelectionText.Piece> { P("First para.", 0, 0, 300, 40), P("Second para.", 0, 56, 300, 20), P("By Bob Kerman", 0, 78, 200, 20) });
        Eq("First para.\n\nSecond para.\nBy Bob Kerman", s, "paragraph breaks");
        // A label and its value side by side, and a table row.
        Eq("Mass\t12.4 t", SelectionText.Join(new List<SelectionText.Piece> { P("Mass", 0, 0, 40, 20), P("12.4 t", 200, 0, 50, 20) }), "tab between columns");
        Eq("Posted by Jeb", SelectionText.Join(new List<SelectionText.Piece> { P("Posted by", 0, 0, 70, 20), P("Jeb", 72, 0, 30, 20) }), "space between close pieces");
        // Odd spaces cleaned, empty pieces skipped, trailing spaces before a break dropped.
        Eq("a b\nc", SelectionText.Join(new List<SelectionText.Piece> { P("a b  \nc­", 0, 0, 100, 40), P("   ", 0, 60, 10, 20) }), "cleaned");
    }
}
