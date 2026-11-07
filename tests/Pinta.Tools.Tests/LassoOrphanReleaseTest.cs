using System.Reflection;
using NUnit.Framework;
using Pinta.Core;

namespace Pinta.Tools.Tests;

/// <summary>
/// A release whose press the lasso never took (ToolManager ends a drag early on a tool switch or
/// commit, then the physical release still arrives) used to apply the lasso's empty polygon,
/// replacing the user's selection with nothing.
/// </summary>
[TestFixture]
internal sealed class LassoOrphanReleaseTest : ToolsTestHarness
{
	private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

	[Test]
	public void AReleaseWithoutADragKeepsTheSelection ()
	{
		Document.Selection = SelectionOf (new RectangleI (4, 4, 8, 8));
		Document.Selection.Visible = true;

		LassoSelectTool t = new (PintaCore.Services);
		// Freeform is the mode whose release applies the drawn outline.
		((ToolBarDropDownButton) typeof (LassoSelectTool).GetProperty ("LassoModeButtom", NonPublicInstance)!.GetValue (t)!)
			.SelectedIndex = 0;
		typeof (BaseTool).GetMethod ("DoMouseUp", NonPublicInstance)!
			.Invoke (t, [Document, new ToolMouseEventArgs { PointDouble = new PointD (6, 6), MouseButton = MouseButton.Left }]);

		Assert.That (Document.Selection.GetBounds (), Is.EqualTo (new RectangleD (4, 4, 8, 8)));
	}
}
