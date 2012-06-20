﻿using System;
using System.Collections.Generic;
using System.Linq;

using CloudAE.Core.Geometry;

namespace CloudAE.Core
{
	public class PointCloudBinarySource : PointCloudSource, IPointCloudBinarySourceEnumerable
	{
		public const string FILE_EXTENSION = "bin";

		private readonly long m_count;
		private readonly Quantization3D m_quantization;
		private readonly short m_pointSizeBytes;
		private readonly int m_pointsPerBuffer;
		private readonly int m_usableBytesPerBuffer;

		private long m_pointDataOffset;
		private Extent3D m_extent;

		#region Properties

		public long Count
		{
			get { return m_count; }
		}

		public Quantization3D Quantization
		{
			get { return m_quantization; }
		}

		public short PointSizeBytes
		{
			get { return m_pointSizeBytes; }
		}

		public int UsableBytesPerBuffer
		{
			get { return m_usableBytesPerBuffer; }
		}

		public int PointsPerBuffer
		{
			get { return m_pointsPerBuffer; }
		}

		public long PointDataOffset
		{
			get { return m_pointDataOffset; }
			protected set { m_pointDataOffset = value; }
		}

		public Extent3D Extent
		{
			get { return m_extent; }
			protected set { m_extent = value; }
		}

		#endregion

		public PointCloudBinarySource(string file, long count, Extent3D extent, Quantization3D quantization, long dataOffset, short pointSizeBytes)
			: base(file)
		{
			m_count = count;
			Extent = extent;
			m_quantization = quantization;
			PointDataOffset = dataOffset;
			m_pointSizeBytes = pointSizeBytes;
			m_pointsPerBuffer = BufferManager.BUFFER_SIZE_BYTES / pointSizeBytes;
			m_usableBytesPerBuffer = m_pointsPerBuffer * pointSizeBytes;
		}

		public PointCloudBinarySourceEnumerator GetBlockEnumerator(byte[] buffer)
		{
			return new PointCloudBinarySourceEnumerator(this, buffer);
		}
	}
}
