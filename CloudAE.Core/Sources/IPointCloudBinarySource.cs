﻿using System;
using CloudAE.Core.Geometry;

namespace CloudAE.Core
{
	public interface IPointCloudBinarySource : IPointCloudBinarySourceEnumerable, IPointCloudBinarySourceSequentialEnumerable
	{
		Extent3D Extent { get; }
		Quantization3D Quantization { get; }

		IPointCloudBinarySource CreateSegment(long pointIndex, long pointCount);
	}
}
