// MoveLayerHistoryItem.cs
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
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

namespace Pinta.Core;

// Reorders a layer from one index to another (e.g. via drag and drop in the
// layers pad). Unlike SwapLayersHistoryItem this shifts intervening layers
// rather than swapping two layers.
public sealed class MoveLayerHistoryItem : BaseHistoryItem
{
	private readonly int from_index;
	private readonly int to_index;

	public MoveLayerHistoryItem (string icon, string text, int fromIndex, int toIndex) : base (icon, text)
	{
		from_index = fromIndex;
		to_index = toIndex;
	}

	public override void Redo () => Move (from_index, to_index);

	public override void Undo () => Move (to_index, from_index);

	private void Move (int from, int to)
	{
		var doc = this.Document!;

		UserLayer layer = doc.Layers[from];
		doc.Layers.DeleteLayer (from);
		doc.Layers.Insert (layer, to);
		doc.Layers.SetCurrentUserLayer (to);
	}
}
