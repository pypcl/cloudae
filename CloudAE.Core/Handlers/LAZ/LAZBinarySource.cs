﻿using System;
using System.Collections.Generic;
using System.Linq;
using CloudAE.Core.Geometry;

namespace CloudAE.Core
{
	public class LAZBinarySource : PointCloudBinarySource
	{
		private readonly LAZFile m_handler;

		public LAZBinarySource(FileHandlerBase file, long count, Extent3D extent, Quantization3D quantization, long dataOffset, short pointSizeBytes)
			: base(file.FilePath, count, extent, quantization, dataOffset, pointSizeBytes)
		{
			m_handler = (LAZFile)file;
		}

		public override IStreamReader GetStreamReader()
		{
			return new LAZStreamReader(FilePath, m_handler.Header, m_handler.EncodedVLR);
		}

		public override IPointCloudBinarySource CreateSegment(long pointIndex, long pointCount)
		{
			long offset = PointDataOffset + pointIndex * PointSizeBytes;
			var segment = new LAZBinarySource(m_handler, pointCount, Extent, Quantization, offset, PointSizeBytes);
			return segment;
		}
	}
}
