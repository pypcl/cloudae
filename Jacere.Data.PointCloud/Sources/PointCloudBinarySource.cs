﻿using System;
using System.Collections.Generic;
using System.Linq;
using Jacere.Core;
using Jacere.Core.Geometry;

namespace Jacere.Data.PointCloud
{
	public class PointCloudBinarySource : PointCloudSource, IPointCloudBinarySource
	{
		public const string FILE_EXTENSION = "bin";

		private readonly long m_count;
		private readonly SQuantization3D m_quantization;
		private readonly short m_pointSizeBytes;

		private long m_pointDataOffset;
		private Extent3D m_extent;
		private SQuantizedExtent3D m_quantizedExtent;

		#region Properties

		public long Count
		{
			get { return m_count; }
		}

		public SQuantization3D Quantization
		{
			get { return m_quantization; }
		}

		public short PointSizeBytes
		{
			get { return m_pointSizeBytes; }
		}

		public long PointDataOffset
		{
			get { return m_pointDataOffset; }
		}

		public Extent3D Extent
		{
			get { return m_extent; }
		}

		public SQuantizedExtent3D QuantizedExtent
		{
			get { return m_quantizedExtent; }
		}

		public IEnumerable<string> SourcePaths
		{
			get { yield return FilePath; }
		}

		#endregion

		public PointCloudBinarySource(FileHandlerBase file, long count, Extent3D extent, SQuantization3D quantization, long dataOffset, short pointSizeBytes)
			: base(file)
		{
			m_count = count;
			m_extent = extent;
			m_quantization = quantization;
			m_pointDataOffset = dataOffset;
			m_pointSizeBytes = pointSizeBytes;
			m_quantizedExtent = quantization.Convert(m_extent);
		}

		public virtual IStreamReader GetStreamReader()
		{
			return StreamManager.OpenReadStream(FilePath);
		}

		public IPointCloudBinarySourceEnumerator GetBlockEnumerator(ProgressManagerProcess process)
		{
			return new PointCloudBinarySourceEnumerator(this, process);
		}

		public IPointCloudBinarySourceEnumerator GetBlockEnumerator(BufferInstance buffer)
		{
			return new PointCloudBinarySourceEnumerator(this, buffer);
		}

		public virtual IPointCloudBinarySource CreateSegment(long pointIndex, long pointCount)
		{
			long offset = PointDataOffset + pointIndex * PointSizeBytes;
			var segment = new PointCloudBinarySource(FileHandler, pointCount, Extent, Quantization, offset, PointSizeBytes);
			return segment;
		}

		public IPointCloudBinarySource CreateSparseSegment(PointCloudBinarySourceEnumeratorSparseRegion regions)
		{
			var regionSegments = new List<IPointCloudBinarySource>();
			foreach (var region in regions)
			{
				long pointIndex = (long)regions.PointsPerChunk * region.ChunkStart;
				long pointCount = (long)regions.PointsPerChunk * region.ChunkCount;

				if (pointIndex + pointCount > Count)
				{
					long diff = pointIndex + pointCount - Count;
					if (diff < regions.PointsPerChunk)
						pointCount -= diff;
					else
						throw new Exception("right off the end");
				}

				var regionSegment = CreateSegment(pointIndex, pointCount);
				regionSegments.Add(regionSegment);
			}

			var sparseComposite = new PointCloudBinarySourceComposite(FileHandler, Extent, regionSegments.ToArray());

			return sparseComposite;
		}
	}
}
