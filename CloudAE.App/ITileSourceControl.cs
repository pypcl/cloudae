﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using CloudAE.Core;

namespace CloudAE.App
{
	interface ITileSourceControl
	{
		string DisplayName
		{
			get;
		}

		PointCloudTileSource CurrentTileSource
		{
			get;
			set;
		}
	}
}
