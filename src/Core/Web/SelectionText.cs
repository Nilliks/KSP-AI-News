using System.Collections.Generic;
using System.Text;

namespace KerbalNews.Web
{
    /// <summary>
    /// Turns the pieces of a page selection (one per paragraph, in page order, with where each sits) into plain
    /// text for the clipboard: a line break between paragraphs, a blank line where they are clearly apart, and a
    /// tab between pieces side by side, so a pasted table keeps its columns.
    /// </summary>
    public static class SelectionText
    {
        public class Piece
        {
            public string Text = "";
            public float X, Y, W, H, FontSize = 16;
        }

        public static string Join(List<Piece> pieces)
        {
            var sb = new StringBuilder();
            Piece prev = null;
            foreach (var p in pieces)
            {
                string text = Clean(p.Text);
                if (text.Trim() == "") continue;
                if (prev != null)
                {
                    float gap = p.Y - (prev.Y + prev.H);
                    bool sameRow = p.Y < prev.Y + prev.H - 2 && p.X >= prev.X + prev.W - 2;
                    if (sameRow) sb.Append(p.X - (prev.X + prev.W) > p.FontSize * 0.6f ? "\t" : " ");
                    else sb.Append(gap > p.FontSize * 0.6f ? "\n\n" : "\n");
                }
                sb.Append(text);
                prev = p;
            }
            return sb.ToString().Trim();
        }

        /// <summary>No-break spaces become spaces and soft hyphens and zero-width marks go; spaces before line
        /// breaks are dropped.</summary>
        static string Clean(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == '­' || c == '​' || c == '‌' || c == '‍' || c == '﻿' || c == '\r') continue;
                if (c == '\n') { while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--; sb.Append('\n'); continue; }
                sb.Append(c == ' ' || c == ' ' || c == ' ' ? ' ' : c);
            }
            return sb.ToString();
        }
    }
}
