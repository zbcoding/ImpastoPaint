using System;
using Pinta.Core;

namespace Pinta.Tools;

/// <summary>
/// Walks a layer's unified object list in z-order, dispatching each shape/text object to the
/// caller's own renderer. Every path that redraws the shared ObjectLayer surface (the text tool's
/// per-keystroke redraw, its cross-layer commit variant, and the shape edit engine's live-geometry
/// redraw) has to walk this same order: drawing all shapes first and all text after (or vice versa)
/// pins one kind permanently above the other regardless of how the user actually stacked them, and
/// silently flips how a blended object composites against whatever sits beneath it. The three
/// callers still differ in what "render" means for each object (skip-if-empty, live-engine vs.
/// stored-object shape geometry, dirty-rect tracking) - only the walk itself is shared.
/// </summary>
internal static class ObjectLayerRenderWalk
{
	public static void Walk (
		UserLayer layer,
		Action<ShapeObject> renderShape,
		Action<TextObject> renderText)
	{
		foreach (ILayerObject o in layer.Objects) {
			switch (o) {
				case ShapeObject shape:
					renderShape (shape);
					break;
				case TextObject text:
					renderText (text);
					break;
			}
		}
	}
}
