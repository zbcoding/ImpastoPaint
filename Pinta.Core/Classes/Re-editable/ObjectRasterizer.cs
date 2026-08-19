// ObjectRasterizer.cs
//
// Bakes a subset of a layer's live editable objects (shapes + text) into its base raster as one
// undoable step, keeping the rest editable. Used by:
//   - the Edit > Cut/Erase path, which rasterizes only the objects the selection overlaps (after
//     prompting the user), and
//   - the layers dock "Rasterize All Objects" menu item, which rasterizes every object on a layer.
//
// The object surfaces already equal the render of the object lists (the object-layer invariant), so
// baking is: render the chosen objects onto the base raster, drop them from the object lists, then
// re-render the object surfaces from what remains.

using System.Collections.Generic;
using System.Linq;
using Cairo;
using ClipperLib;

namespace Pinta.Core;

public static class ObjectRasterizer
{
	/// <summary>
	/// Returns the indices of the shapes/text objects on <paramref name="layer"/> whose bounds overlap
	/// <paramref name="region"/> (a selection's bounding box). Used to rasterize only what an op touches.
	/// </summary>
	public static void FindIntersecting (
		UserLayer layer,
		RectangleD region,
		out List<int> shapeIndices,
		out List<int> textIndices)
	{
		shapeIndices = [];
		textIndices = [];

		for (int i = 0; i < layer.ShapeObjects.Count; ++i)
			if (Overlaps (region, layer.ShapeObjects[i].GetApproximateBounds ()))
				shapeIndices.Add (i);

		for (int i = 0; i < layer.TextObjects.Count; ++i) {
			RectangleI b = layer.TextObjects[i].TextBounds;
			if (Overlaps (region, new (b.X, b.Y, b.Width, b.Height)))
				textIndices.Add (i);
		}
	}

	// RectangleD.Right and .Bottom are the last pixel, not one past it, so two regions that share only
	// their edge pixel still overlap. Testing with < instead missed exactly that pixel, which meant a
	// selection abutting an object's edge skipped the bake and let the following raster op write over
	// pixels that were still being drawn from an object.
	private static bool Overlaps (in RectangleD a, in RectangleD b)
		=> a.Left <= b.Right && a.Right >= b.Left && a.Top <= b.Bottom && a.Bottom >= b.Top;

	/// <summary>
	/// Indices of the Object-mode shapes on <paramref name="layer"/> that a deselect would fuse into
	/// its base raster: shapes drawn with a frozen selection clip that lies partly outside that clip
	/// (i.e. genuinely clipped). Shapes fully inside their clip stay editable (deselect just drops the
	/// clip); shapes with no clip are unaffected. Shared by the deselect confirmation prompt (Core) and
	/// the actual fuse step on selection-clear (Pinta.Tools) so they always agree.
	/// </summary>
	public static List<int> FindClippedShapesOnDeselect (UserLayer layer)
	{
		List<int> result = [];
		for (int i = 0; i < layer.ShapeObjects.Count; ++i) {
			ShapeObject s = layer.ShapeObjects[i];
			if (s.Clip is not null && !ClipContainsShape (s))
				result.Add (i);
		}
		return result;
	}

	// True when the shape lies entirely within its frozen clip, so clipping has no visible effect.
	// Uses the clip's actual region (not its bounding box): the shape's bounding box is subtracted
	// from the selection polygons, and an empty difference means every pixel of the box — and so the
	// shape — falls inside the region. This is correct for non-rectangular (lasso) clips, where a
	// bbox-vs-bbox test would wrongly report containment when the region cuts through the shape.
	// The box over-approximates the shape, so the answer is conservative: a "not contained" verdict
	// may bake a shape that was in fact fully inside, which only costs editability, never reveals
	// hidden pixels.
	public static bool ClipContainsShape (ShapeObject s)
	{
		RectangleD b = s.GetApproximateBounds ();

		List<IntPoint> box = [
			new ((long) b.X, (long) b.Y),
			new ((long) b.Right, (long) b.Y),
			new ((long) b.Right, (long) b.Bottom),
			new ((long) b.X, (long) b.Bottom),
		];

		Clipper clipper = new ();
		clipper.AddPath (box, PolyType.ptSubject, true);
		clipper.AddPaths (s.Clip!.SelectionPolygons, PolyType.ptClip, true);

		List<List<IntPoint>> difference = [];
		clipper.Execute (ClipType.ctDifference, difference);

		return difference.Count == 0;
	}

	/// <summary>Display labels for the given objects, mirroring the layers dock naming.</summary>
	public static IEnumerable<string> Describe (
		UserLayer layer,
		IReadOnlyList<int> shapeIndices,
		IReadOnlyList<int> textIndices)
	{
		foreach (int i in shapeIndices)
			yield return ShapeLabel (layer.ShapeObjects[i]);
		foreach (int _ in textIndices)
			yield return Translations.GetString ("Text");
	}

	private static string ShapeLabel (ShapeObject s)
		=> string.IsNullOrEmpty (s.Name) ? ShapeTypeName (s.ShapeType) : s.Name;

	private static string ShapeTypeName (ShapeObjectType type) => type switch {
		ShapeObjectType.Ellipse => Translations.GetString ("Ellipse"),
		ShapeObjectType.RoundedLineSeries => Translations.GetString ("Rounded Rectangle"),
		ShapeObjectType.Triangle => Translations.GetString ("Triangle"),
		ShapeObjectType.OpenLineCurveSeries => Translations.GetString ("Line/Curve"),
		_ => Translations.GetString ("Shape"),
	};

	/// <summary>
	/// Prompts the user to confirm rasterizing the listed objects. Returns true if they accept. Runs a
	/// nested loop (blocking) so it fits the synchronous action handlers that call it.
	/// </summary>
	public static bool Confirm (IChromeService chrome, IReadOnlyList<string> labels)
	{
		// Opt-out: users who know the operations rasterize objects can silence the prompt (Settings → UI).
		if (PintaCore.Settings.GetSetting (SettingNames.SKIP_RASTERIZE_OBJECTS_DIALOG, false))
			return true;

		const int max_listed = 12;
		string list = string.Join ("\n", labels.Take (max_listed).Select (l => "• " + l));
		if (labels.Count > max_listed)
			list += "\n" + Translations.GetString ("…and {0} more", labels.Count - max_listed);

		string body = Translations.GetString ("To perform this action, these objects must be rasterized (baked into the layer's pixels and no longer editable):")
			+ "\n\n" + list;

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (
			chrome.MainWindow,
			Translations.GetString ("Rasterize Objects?"),
			body);

		const string cancel_response = "cancel";
		const string rasterize_response = "rasterize";
		dialog.AddResponse (cancel_response, Translations.GetString ("_Cancel"));
		dialog.AddResponse (rasterize_response, Translations.GetString ("_Rasterize"));
		dialog.SetResponseAppearance (rasterize_response, Adw.ResponseAppearance.Destructive);
		dialog.CloseResponse = cancel_response;
		dialog.DefaultResponse = rasterize_response;

		return dialog.RunBlocking () == rasterize_response;
	}

	/// <summary>
	/// Bakes the given shapes/text objects into <paramref name="layer"/>'s base raster as one undoable
	/// step, leaving the rest editable. Returns true if anything was baked.
	/// </summary>
	public static bool RasterizeSubset (
		Document doc,
		IWorkspaceService workspace,
		IChromeService chrome,
		UserLayer layer,
		IReadOnlyList<int> shapeIndices,
		IReadOnlyList<int> textIndices,
		DocumentSelection? textClip = null,
		CompoundHistoryItem? historyGroup = null)
	{
		if (shapeIndices.Count == 0 && textIndices.Count == 0)
			return false;

		// Snapshot the full pre-bake state for undo (base + the object surface + the object list).
		ImageSurface baseBefore = layer.Surface.Clone ();
		ImageSurface objectBefore = layer.ObjectLayer.Layer.Surface.Clone ();
		var objectsBefore = ObjectOpacity.CloneAll (layer.Objects);

		// Bake the chosen objects onto the base raster BEFORE removing them from the list — the
		// renderers read by index.
		//
		// The subset is composited as a *group*: rendered in unified z-order into one scratch surface
		// with each object's own blend mode applied (exactly as RenderLayerObjects does), then painted
		// onto the base raster with Normal. Baking each object straight onto the base would either
		// re-blend it against the image beneath (wrong colour) or drop the blend entirely (a Multiply
		// text over a shape would bake as its raw colour) — both change how the pixels look the moment
		// a selection cut rasterizes a text and the shape it overlaps.
		// textClip is set by the text tool's Raster-mode commit so the baked pixels match the clipped
		// preview; the generic Cut/Erase and "Rasterize All" callers pass null (bake the full object).
		ImageSurface baked = CairoExtensions.CreateImageSurface (Format.Argb32, layer.Surface.Width, layer.Surface.Height);
		int shapeSeen = 0;
		int textSeen = 0;
		foreach (ILayerObject o in layer.Objects) {
			if (o is ShapeObject shape) {
				if (shapeIndices.Contains (shapeSeen))
					LayerObjectSelection.RenderShape (baked, layer, shape);
				shapeSeen++;
			} else if (o is TextObject text) {
				if (textIndices.Contains (textSeen))
					TextObjectRenderer.Render (baked, text, chrome, antialias: true, clip: textClip);
				textSeen++;
			}
		}

		using (Context g = new (layer.Surface)) {
			g.SetSourceSurface (baked, 0, 0);
			g.Paint ();
		}

		// Drop the baked objects (descending index so earlier removals don't shift later ones).
		foreach (int i in shapeIndices.OrderByDescending (i => i))
			layer.RemoveObjectAtKind (isText: false, i);
		foreach (int i in textIndices.OrderByDescending (i => i))
			layer.RemoveObjectAtKind (isText: true, i);

		// Re-render the object surface from what remains so the baked pixels aren't composited twice.
		// Shapes: the Tools seam rebuilds the live editing engines. Only when shapes were actually
		// baked — an empty subset (e.g. a text-only rasterize) must not trigger a reload, which
		// re-composites the remaining editable shapes UNCLIPPED and would redraw any drawn-inside-a-
		// selection shape in full. The object surface is rebuilt from the remaining unified list.
		if (shapeIndices.Count > 0)
			LayerObjectSelection.RequestShapeReload (layer);
		ObjectOpacity.RefreshLayerNoInvalidate (chrome, layer);

		RasterizeObjectsHistoryItem item = new (
			workspace,
			Resources.Icons.ImageFlatten,
			Translations.GetString ("Rasterize Objects"),
			baseBefore, objectBefore,
			objectsBefore, layer);

		// When part of a larger action (e.g. a resize), the bake is recorded into that action's
		// compound item so the whole thing undoes in one step; otherwise it's its own history step.
		if (historyGroup is not null)
			historyGroup.Push (item);
		else
			doc.History.PushNewItem (item);

		// The baked objects' on-canvas editing chrome (the text tool's dashed re-edit rectangles,
		// handle dots and "Obj." badges) lives on the tool layer, which nothing else here touches —
		// clear it so it doesn't hover over pixels that are no longer objects. The active tool
		// redraws its own overlay on the next edit. ponytail: clear from Core rather than adding a
		// per-tool seam; the tool layer is transient chrome by definition.
		doc.Layers.ToolLayer.Clear ();
		LayerObjectSelection.RaiseObjectsChanged ();

		workspace.Invalidate ();
		return true;
	}

	/// <summary>
	/// Bakes every layer's live objects into its base raster before a coordinate-changing op (crop /
	/// canvas resize / image resize). Those ops only transform raster pixels, but an object's geometry
	/// is vector control points the resize never touches — so an un-baked object would snap back to its
	/// old placement on the next redraw (and a partial-clip shape would keep an invisible clip boundary).
	/// Each layer's bake is its own undoable step, pushed before the resize's own history item, matching
	/// the design's "resize that must bake auto-rasterizes first." No-op for layers without objects.
	/// Prompts the user to confirm (these ops make editable objects permanent pixels); returns false if
	/// they cancel, in which case nothing is baked and the caller must abort its op. Returns true when
	/// there was nothing to bake (no prompt shown).
	/// </summary>
	public static bool RasterizeAllLayersForResize (
		Document doc,
		IWorkspaceService workspace,
		IChromeService chrome,
		CompoundHistoryItem? historyGroup = null)
	{
		List<string> labels = [.. doc.Layers.UserLayers.SelectMany (DescribeAll)];

		if (labels.Count > 0 && !Confirm (chrome, labels))
			return false;

		foreach (UserLayer layer in doc.Layers.UserLayers)
			RasterizeAllObjects (doc, workspace, chrome, layer, confirm: false, historyGroup: historyGroup);

		return true;
	}

	/// <summary>
	/// Makes <paramref name="layer"/> safe for a destructive raster operation confined to
	/// <paramref name="selection"/> — cut, erase, or lifting the pixels out to move them. Every such
	/// operation reads and writes the base raster, but what the user sees is the layer's composite, so
	/// anything living outside the base raster has to be baked into it first or the operation acts on
	/// pixels nobody is looking at.
	/// <para>
	/// Modifier nodes force a whole-stack bake: the accumulator has already folded the objects beneath
	/// them into the same pixels, so there is no per-region subset to take. Shapes and text alone stay
	/// separable, and only the ones the selection overlaps are baked. Prompts before either, listing
	/// what stops being editable; returns false only when the user cancels, in which case nothing was
	/// baked and the caller must abort.
	/// </para>
	/// </summary>
	public static bool PrepareForSelectionRasterOp (
		Document doc,
		IWorkspaceService workspace,
		IChromeService chrome,
		UserLayer layer,
		DocumentSelection selection,
		CompoundHistoryItem? historyGroup = null)
	{
		if (!layer.HasAnyObjects)
			return true;

		RectangleD region = selection.GetBounds ();
		if (layer.HasModifiers && SelectionReachesAnyModifier (layer, selection, region)) {
			List<string> labels = DescribeAll (layer).ToList ();
			if (labels.Count > 0 && !Confirm (chrome, labels))
				return false;

			RasterizeModifierStack (doc, workspace, chrome, layer, historyGroup);
			return true;
		}

		FindIntersecting (layer, region, out List<int> shapeIndices, out List<int> textIndices);

		if (shapeIndices.Count == 0 && textIndices.Count == 0)
			return true; // the selection misses every object; nothing to bake.

		List<string> objectLabels = Describe (layer, shapeIndices, textIndices).ToList ();
		if (!Confirm (chrome, objectLabels))
			return false;

		RasterizeSubset (doc, workspace, chrome, layer, shapeIndices, textIndices, historyGroup: historyGroup);
		return true;
	}

	// A node with no clip modifies the whole layer, so any selection reaches it. For a clipped node,
	// reject disjoint bounds cheaply, then intersect the actual selection polygons. Bounds alone are
	// insufficient for ellipses and lassos: their empty corners can overlap without sharing pixels.
	public static bool SelectionReachesAnyModifier (UserLayer layer, DocumentSelection selection)
		=> SelectionReachesAnyModifier (layer, selection, selection.GetBounds ());

	private static bool SelectionReachesAnyModifier (
		UserLayer layer,
		DocumentSelection selection,
		in RectangleD selectionBounds)
	{
		Clipper clipper = new ();
		List<List<IntPoint>> intersection = [];

		foreach (ILayerModifierNode node in layer.ModifierNodes) {
			if (node.Clip is null)
				return true;

			if (!Overlaps (selectionBounds, node.Clip.GetBounds ()))
				continue;

			clipper.Clear ();
			intersection.Clear ();
			clipper.AddPaths (selection.SelectionPolygons, PolyType.ptSubject, true);
			clipper.AddPaths (node.Clip.SelectionPolygons, PolyType.ptClip, true);
			clipper.Execute (ClipType.ctIntersection, intersection);

			if (intersection.Count > 0)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Bakes every live object on <paramref name="layer"/>, prompting first unless the caller already
	/// confirmed (<paramref name="confirm"/> false). Returns false only if the user cancels, in which
	/// case nothing was baked; true when there was nothing to bake or the bake happened.
	/// </summary>
	public static bool RasterizeAllObjects (
		Document doc,
		IWorkspaceService workspace,
		IChromeService chrome,
		UserLayer layer,
		bool confirm = true,
		CompoundHistoryItem? historyGroup = null)
	{
		if (!layer.HasAnyObjects)
			return true;

		// A layer with modifier nodes renders from its accumulated composite, which already folds in
		// every shape and text beneath them. Its pixels are not separable into individual objects, so
		// rasterizing "everything" on such a layer means baking the whole stack at once — the same
		// step the dock's per-node Rasterize performs.
		if (layer.HasModifiers) {
			List<string> labels = BakeLabels (layer);
			if (confirm && labels.Count > 0 && !Confirm (chrome, labels))
				return false;
			return RasterizeModifierStack (doc, workspace, chrome, layer, historyGroup);
		}

		RasterizeSubset (
			doc, workspace, chrome, layer,
			[.. Enumerable.Range (0, layer.ShapeObjects.Count)],
			[.. Enumerable.Range (0, layer.TextObjects.Count)],
			historyGroup: historyGroup);

		return true;
	}

	/// <summary>
	/// Prompts before letting a paint stroke bake a transformed layer's stack. The transform node
	/// makes the layer render from its composite, so a freehand stroke drawn in local raster space
	/// cannot sit correctly under the transform; the only way to paint is to rasterize it first.
	/// </summary>
	public static bool ConfirmRasterizeToPaint (IChromeService chrome, UserLayer layer)
	{
		// Deliberately NOT gated behind SKIP_RASTERIZE_OBJECTS_DIALOG: that opt-out is for the routine
		// geometry ops, where a bake is expected. Here one stray click would silently destroy a
		// non-destructive transform, which is destructive enough to always ask about.
		const int max_listed = 12;
		List<string> labels = BakeLabels (layer);
		string list = string.Join ("\n", labels.Take (max_listed).Select (l => "• " + l));
		if (labels.Count > max_listed)
			list += "\n" + Translations.GetString ("…and {0} more", labels.Count - max_listed);

		string body = Translations.GetString ("Painting on a transformed layer is blocked while the transform is active. Rasterize the transform to paint here? The transform and everything beneath it become part of the layer's pixels.")
			+ "\n\n" + list;

		using Adw.MessageDialog dialog = Adw.MessageDialog.New (
			chrome.MainWindow,
			Translations.GetString ("Rasterize to Paint?"),
			body);

		const string cancel_response = "cancel";
		const string rasterize_response = "rasterize";
		dialog.AddResponse (cancel_response, Translations.GetString ("_Cancel"));
		dialog.AddResponse (rasterize_response, Translations.GetString ("_Rasterize"));
		dialog.SetResponseAppearance (rasterize_response, Adw.ResponseAppearance.Destructive);
		dialog.CloseResponse = cancel_response;
		dialog.DefaultResponse = rasterize_response;

		return dialog.RunBlocking () == rasterize_response;
	}

	/// <summary>
	/// Confirms once for everything <paramref name="layers"/> would bake, then bakes each affected
	/// layer without prompting again, all into <paramref name="historyGroup"/>. Confirming before any
	/// mutation means a cancel cannot leave a half-baked layer with no undo entry.
	/// </summary>
	public static bool RasterizeLayers (
		Document doc,
		IWorkspaceService workspace,
		IChromeService chrome,
		IReadOnlyList<UserLayer> layers,
		CompoundHistoryItem historyGroup)
	{
		List<UserLayer> affected = layers.Where (l => l.HasAnyObjects).ToList ();

		List<string> labels = [];
		foreach (UserLayer layer in affected) {
			if (layer.HasModifiers)
				labels.AddRange (BakeLabels (layer));
			else
				labels.AddRange (DescribeAll (layer));
		}

		if (labels.Count > 0 && !Confirm (chrome, labels))
			return false;

		foreach (UserLayer layer in affected)
			RasterizeAllObjects (doc, workspace, chrome, layer, confirm: false, historyGroup: historyGroup);

		return true;
	}

	/// <summary>
	/// Bakes a modifier-carrying layer's whole stack into its base raster and drops every child, as
	/// one undoable step. Used by the geometry operations (crop / resize / rotate / flatten) and by
	/// the dock's per-node Rasterize, which all funnel through <see cref="RasterizeAllObjects"/>.
	/// </summary>
	public static bool RasterizeModifierStack (
		Document doc,
		IWorkspaceService workspace,
		IChromeService chrome,
		UserLayer layer,
		CompoundHistoryItem? historyGroup = null)
	{
		ImageSurface baseBefore = layer.Surface.Clone ();
		ImageSurface objectBefore = layer.ObjectLayer.Layer.Surface.Clone ();
		List<ILayerObject> objectsBefore = ObjectOpacity.CloneAll (layer.Objects);

		// Ensure the composite reflects any raster edit made since the last render, then bake it.
		ObjectOpacity.RefreshLayerNoInvalidate (chrome, layer);
		if (!layer.RasterizeModifierStack ())
			return false;

		ObjectOpacity.RefreshLayer (workspace, chrome, layer);

		RasterizeObjectsHistoryItem item = new (
			workspace,
			Resources.Icons.ImageFlatten,
			Translations.GetString ("Rasterize Layer Effects"),
			baseBefore, objectBefore,
			objectsBefore, layer);

		if (historyGroup is not null)
			historyGroup.Push (item);
		else
			doc.History.PushNewItem (item);

		doc.Layers.ToolLayer.Clear ();
		LayerObjectSelection.RaiseObjectsChanged ();
		workspace.Invalidate ();
		return true;
	}

	/// <summary>
	/// What baking a modifier-carrying layer's stack would make permanent: its modifier nodes, plus
	/// its mask when the mask is applying (the bake folds the mask into the pixels and drops it, so
	/// a prompt that named only the nodes understated what the user was about to lose).
	/// </summary>
	private static List<string> BakeLabels (UserLayer layer)
	{
		List<string> labels = [.. layer.ModifierNodes.Select (node => node.DisplayName)];
		if (layer.Mask is { Hidden: false })
			labels.Add (Translations.GetString ("Layer Mask"));
		return labels;
	}

	// Everything on the layer a bake would make permanent, modifier nodes included. Leaving the
	// nodes out made a layer whose only children were effects or transforms describe itself as
	// empty, so crop / resize / flatten baked them with no rasterize notice at all.
	private static IEnumerable<string> DescribeAll (UserLayer layer)
		=> Describe (
			layer,
			[.. Enumerable.Range (0, layer.ShapeObjects.Count)],
			[.. Enumerable.Range (0, layer.TextObjects.Count)])
		.Concat (layer.ModifierNodes.Select (node => node.DisplayName));
}
