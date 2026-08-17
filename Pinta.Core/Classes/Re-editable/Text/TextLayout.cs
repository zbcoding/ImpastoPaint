// 
// TextLayout.cs
//  
// Author:
//       Cameron White <cameronwhite91@gmail.com>
// 
// Copyright (c) 2015 Cameron White
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Immutable;
using System.Security;
using System.Text;


namespace Pinta.Core;

public sealed class TextLayout
{
	private TextEngine engine = null!; // NRT - Not sure how this is set, but all callers assume it is not-null

	public TextEngine Engine {
		get => engine;
		set {
			if (engine != null)
				engine.Modified -= OnEngineModified;
			engine = value;
			engine.Modified += OnEngineModified;
			OnEngineModified (this, EventArgs.Empty);
		}
	}

	public Pango.Layout Layout { get; }
	public int FontHeight => GetCursorLocation ().Height;

	public TextLayout (IChromeService chrome)
	{
		Layout = Pango.Layout.New (chrome.MainWindow.GetPangoContext ());
	}

	public ImmutableArray<RectangleI> GetSelectionRectangles ()
	{
		var regions = engine.SelectionRegions;
		var rects = ImmutableArray.CreateBuilder<RectangleI> (regions.Length);
		foreach (var region in regions) {
			PointI p1 = TextPositionToPoint (region.Key);
			PointI p2 = TextPositionToPoint (region.Value);
			rects.Add (new RectangleI (p1, new Size (p2.X - p1.X, FontHeight)));
		}
		return rects.MoveToImmutable ();
	}

	public RectangleI GetCursorLocation ()
	{
		int index = engine.PositionToUTF8Index (engine.CurrentPosition);
		Layout.GetCursorPos (index, out RectangleI strong, out _);

		int x = PangoExtensions.UnitsToPixels (strong.X) + engine.Origin.X;
		int y = PangoExtensions.UnitsToPixels (strong.Y) + engine.Origin.Y;
		int w = PangoExtensions.UnitsToPixels (strong.Width);
		int h = PangoExtensions.UnitsToPixels (strong.Height);

		return new (x, y, w, h);
	}

	public RectangleI GetLayoutBounds ()
	{
		Layout.GetPixelExtents (out RectangleI ink, out RectangleI logical);
		var cursor = GetCursorLocation ();

		// Area (flow) text: the box is a fixed width the user set, and its height comes
		// from Pango's wrapped logical extents so it grows with the visual (wrapped) line
		// count. This makes the interaction box track the flow box, not the ink.
		if (engine.WrapWidth > 0)
			return new (
				engine.Origin.X,
				engine.Origin.Y,
				engine.WrapWidth,
				Math.Max (cursor.Height, logical.Height));

		// GetPixelExtents() doesn't really return a very sensible height.
		// Instead of doing some hacky arithmetic to correct it, the height will just
		// be the cursor's height times the number of lines.
		return new (
			engine.Origin.X,
			engine.Origin.Y,
			ink.Width,
			cursor.Height * engine.LineCount);
	}

	public TextPosition PointToTextPosition (PointI point)
	{
		int x = PangoExtensions.UnitsFromPixels (point.X - engine.Origin.X);
		int y = PangoExtensions.UnitsFromPixels (point.Y - engine.Origin.Y);

		Layout.XyToIndex (x, y, out int index, out int trailing);

		return engine.UTF8IndexToPosition (index + trailing);
	}

	public PointI TextPositionToPoint (TextPosition p)
	{
		int index = engine.PositionToUTF8Index (p);

		Layout.IndexToPos (index, out RectangleI rect);

		int x = PangoExtensions.UnitsToPixels (rect.X) + engine.Origin.X;
		int y = PangoExtensions.UnitsToPixels (rect.Y) + engine.Origin.Y;

		return new PointI (x, y);
	}

	private void OnEngineModified (object? sender, EventArgs e)
	{
		string text = engine.ToString ();
		string? markup = SecurityElement.Escape (text);

		if (engine.Underline)
			markup = $"<u>{markup}</u>";

		switch (engine.Alignment) {
			case TextAlignment.Right:
				Layout.SetAlignment (Pango.Alignment.Right);
				break;
			case TextAlignment.Center:
				Layout.SetAlignment (Pango.Alignment.Center);
				break;
			case TextAlignment.Left:
			case TextAlignment.Justify:
				// Justified text left-aligns its base lines, with the extra inter-word
				// spacing that fills the box applied by Pango.
				Layout.SetAlignment (Pango.Alignment.Left);
				break;
		}

		Layout.SetJustify (false);
		Layout.SetJustifyLastLine (false);
		Layout.SetFontDescription (engine.Font);

		// Area (flow) text wraps to a fixed width; point text (WrapWidth 0) uses -1 to
		// grow to its natural width and only break on user-entered newlines.
		Layout.SetWidth (engine.WrapWidth > 0 ? PangoExtensions.UnitsFromPixels (engine.WrapWidth) : -1);
		Layout.SetMarkup (markup, -1);

		bool justify =
			engine.Alignment == TextAlignment.Justify
			&& engine.WrapWidth > 0
			&& IsFlowJustificationSpacingAcceptable (text);
		Layout.SetJustify (justify);
	}

	private bool IsFlowJustificationSpacingAcceptable (string text)
		=> IsFlowJustificationSpacingAcceptable (Layout, text, engine.WrapWidth);

	internal static bool IsFlowJustificationSpacingAcceptable (
		Pango.Layout layout,
		string text,
		int wrapWidth)
	{
		byte[] utf8Text = Encoding.UTF8.GetBytes (text);
		int lineCount = layout.GetLineCount ();
		int layoutWidth = PangoExtensions.UnitsFromPixels (wrapWidth);

		for (int lineIndex = 0; lineIndex < lineCount - 1; lineIndex++) {
			using Pango.LayoutLine line = layout.GetLineReadonly (lineIndex)!;
			using Pango.LayoutLine nextLine = layout.GetLineReadonly (lineIndex + 1)!;

			// Pango already leaves the final line in each paragraph unjustified.
			if (nextLine.GetIsParagraphStart ())
				continue;

			int lineStart = line.GetStartIndex ();
			int lineEnd = lineStart + line.GetLength ();
			line.GetExtents (out _, out Pango.Rectangle logical);

			int firstSpaceIndex = -1;
			int spaceCount = 0;
			for (int byteIndex = lineStart; byteIndex < lineEnd; byteIndex++) {
				// Pango's Latin justification stretches ordinary inter-word spaces.
				if (utf8Text[byteIndex] != (byte) ' ')
					continue;

				if (firstSpaceIndex < 0)
					firstSpaceIndex = byteIndex;
				spaceCount++;
			}

			long naturalSpaceWidth = 0;
			if (firstSpaceIndex >= 0) {
				layout.IndexToPos (firstSpaceIndex, out RectangleI space);
				naturalSpaceWidth = (long) Math.Abs (space.Width) * spaceCount;
			}

			// With no inter-word spaces, Pango has nothing on this Latin line to stretch.
			if (!IsJustificationSpacingAcceptable (layoutWidth, logical.Width, naturalSpaceWidth))
				return false;
		}

		return true;
	}

	// ponytail: Pango cannot cap one visual line. Falling back for the whole object
	// keeps rendering, cursor, and selection geometry together; a shared per-line
	// layout model is the upgrade path.
	internal static bool IsJustificationSpacingAcceptable (
		int layoutWidth,
		int lineWidth,
		long naturalSpaceWidth)
		=> naturalSpaceWidth == 0
			|| Math.Max (0L, (long) layoutWidth - lineWidth) <= naturalSpaceWidth;
}
