/////////////////////////////////////////////////////////////////////////////////
// Derived from the MIT-licensed Paint.NET 3.x source release, via Pinta.      //
// Copyright (C) Rick Brewster, Tom Jackson, and past contributors.            //
// Portions Copyright (C) Microsoft Corporation. All Rights Reserved.          //
// See THIRD-PARTY-NOTICES.md for full licensing and attribution details.      //
/////////////////////////////////////////////////////////////////////////////////

using System;

namespace Pinta.Core;

public sealed class IndexEventArgs : EventArgs
{

	public int Index { get; }

	public IndexEventArgs (int i)
	{
		Index = i;
	}
}

