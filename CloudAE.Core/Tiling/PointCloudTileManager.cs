﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Jacere.Core;
using Jacere.Core.Geometry;
using Jacere.Core.Windows;
using Jacere.Data.PointCloud;
using ProcessPrivileges;

namespace CloudAE.Core
{
	public class PointCloudTileManager : IPropertyContainer
	{
		public static readonly IPropertyState<int> PROPERTY_DESIRED_TILE_COUNT;
		private static readonly IPropertyState<int> PROPERTY_MAX_TILES_FOR_ESTIMATION;
		private static readonly IPropertyState<int> PROPERTY_MAX_LOWRES_POINTS;

		private readonly Identity m_id;
		private readonly IPointCloudBinarySource m_source;
        
		static PointCloudTileManager()
		{
			PROPERTY_DESIRED_TILE_COUNT = Context.RegisterOption(Context.OptionCategory.Tiling, "DesiredTilePoints", 40000);
			PROPERTY_MAX_TILES_FOR_ESTIMATION = Context.RegisterOption(Context.OptionCategory.Tiling, "EstimationTilesMax", 10000000);
			PROPERTY_MAX_LOWRES_POINTS = Context.RegisterOption(Context.OptionCategory.Tiling, "LowResPointsMax", 1000000);
		}

		public PointCloudTileManager(IPointCloudBinarySource source)
		{
			m_id = IdentityManager.AcquireIdentity(GetType().Name);
			m_source = source;
		}

		#region Indexed Tiling Methods

		public PointCloudTileSource TilePointFileIndex(LASFile tiledFile, BufferInstance segmentBuffer, ProgressManager progressManager)
		{
			var stopwatch = new Stopwatch();
			stopwatch.Start();

			var analysis = AnalyzePointFile(segmentBuffer.Length, progressManager);
			var quantizedExtent = m_source.QuantizedExtent;
			var tileCounts = analysis.Density.GetTileCountsForInitialization();

			var fileSize = tiledFile.PointDataOffset + (m_source.PointSizeBytes * m_source.Count);

			AttemptFastAllocate(tiledFile.FilePath, fileSize);

			var lowResPointCountMax = PROPERTY_MAX_LOWRES_POINTS.Value;
			var lowResBuffer = BufferManager.AcquireBuffer(m_id, lowResPointCountMax * m_source.PointSizeBytes);
			var lowResWrapper = new PointBufferWrapper(lowResBuffer, m_source.PointSizeBytes, lowResPointCountMax);

			using (var outputStream = StreamManager.OpenWriteStream(tiledFile.FilePath, fileSize, tiledFile.PointDataOffset))
			{
				var i = 0;
				foreach (var segment in analysis.GridIndex)
				{
					progressManager.Log("~ Processing Index Segment {0}/{1}", ++i, analysis.GridIndex.Count);

					var sparseSegment = m_source.CreateSparseSegment(segment);
					var sparseSegmentWrapper = new PointBufferWrapper(segmentBuffer, sparseSegment);

					var tileRegionFilter = new TileRegionFilter(tileCounts, quantizedExtent, segment.GridRange);

					// this call will fill the buffer with points, add the counts, and sort
					QuantTilePointsIndexed(sparseSegment, sparseSegmentWrapper, tileRegionFilter, tileCounts, lowResWrapper, progressManager);
					var segmentFilteredPointCount = tileRegionFilter.GetCellOrdering().Sum(t => tileCounts.Data[t.Row, t.Col]);
					var segmentFilteredBytes = segmentFilteredPointCount * sparseSegmentWrapper.PointSizeBytes;

					// write out the buffer
					using (var process = progressManager.StartProcess("WriteIndexSegment"))
					{
						var segmentBufferIndex = 0;
						foreach (var tileCount in segment.GridRange.GetCellOrdering().Select(c => tileCounts.Data[c.Row, c.Col]).Where(count => count > 0))
						{
							var tileSize = tileCount * sparseSegmentWrapper.PointSizeBytes;
							outputStream.Write(sparseSegmentWrapper.Data, segmentBufferIndex, tileSize);
							segmentBufferIndex += tileSize;

							if (!process.Update((float)segmentBufferIndex / segmentFilteredBytes))
								break;
						}
					}

					if (progressManager.IsCanceled())
						break;
				}
			}

			// todo: get lowres counts
			var lowResCounts = tileCounts.Copy<int>();

			var actualDensity = new PointCloudTileDensity(tileCounts, m_source.Extent);
			var tileSet = new PointCloudTileSet(m_source, actualDensity, tileCounts, lowResCounts);
			var tileSource = new PointCloudTileSource(tiledFile, tileSet, analysis.Statistics);

			if (!progressManager.IsCanceled())
				tileSource.IsDirty = false;

			tileSource.WriteHeader();

			return tileSource;
		}

		private static void AttemptFastAllocate(string path, long fileSize)
		{
			var currentProcess = Process.GetCurrentProcess();
			using (new PrivilegeEnabler(currentProcess, Privilege.ManageVolume))
			{
				using (var outputStream = new FileStream(path, FileMode.OpenOrCreate))
				{
					outputStream.SetLength(fileSize);
					// (requires SE_MANAGE_VOLUME_NAME privilege, which is only given to admin by default)
					if (!NativeMethods.SetFileValidData(outputStream.SafeFileHandle.DangerousGetHandle(), fileSize))
					{
						Context.WriteLine("SetFileValidData() failed. Win32 error: {0}", Marshal.GetLastWin32Error());
					}
				}
			}
		}

		public PointCloudAnalysisResult AnalyzePointFile(int maxSegmentLength, ProgressManager progressManager)
		{
			var stopwatch = new Stopwatch();
			stopwatch.Start();

			var tileCounts = CreateTileCountsForEstimation(m_source);
			var analysis = QuantEstimateDensity(m_source, maxSegmentLength, tileCounts, progressManager);

			progressManager.Log(stopwatch, "Computed Density ({0})", analysis.Density);

			return analysis;
		}

		private static unsafe void QuantTilePointsIndexed(IPointCloudBinarySource source, PointBufferWrapper segmentBuffer, TileRegionFilter tileFilter, SQuantizedExtentGrid<int> tileCounts, PointBufferWrapper lowResBuffer, ProgressManager progressManager)
		{
			var quantizedExtent = source.QuantizedExtent;

			// generate counts and add points to buffer
			using (var process = progressManager.StartProcess("QuantTilePointsIndexedFilter"))
			{
				var group = new ChunkProcessSet(tileFilter, segmentBuffer);
				group.Process(source.GetBlockEnumerator(process));
			}

			// sort points in buffer
			using (var process = progressManager.StartProcess("QuantTilePointsIndexedSort"))
			{
				var tilePositions = tileFilter.CreatePositionGrid(segmentBuffer, source.PointSizeBytes);

				var sortedCount = 0;
				foreach (var tile in tileFilter.GetCellOrdering())
				{
					var currentPosition = tilePositions[tile.Row, tile.Col];
					if (currentPosition.IsIncomplete)
					{
						while (currentPosition.IsIncomplete)
						{
							var p = (SQuantizedPoint3D*)currentPosition.DataPtr;

							var targetPosition = tilePositions[
								(((*p).Y - quantizedExtent.MinY) / tileCounts.CellSizeY),
								(((*p).X - quantizedExtent.MinX) / tileCounts.CellSizeX)
								];

							if (targetPosition.DataPtr != currentPosition.DataPtr)
							{
								// the point tile is not the current traversal tile,
								// so swap the points and resume on the swapped point
								targetPosition.Swap(currentPosition.DataPtr);
							}
							else
							{
								// this point is in the correct tile, move on
								currentPosition.Increment();
							}

							++sortedCount;
						}

						if (!process.Update((float)sortedCount / segmentBuffer.PointCount))
							break;
					}
				}
			}

			// determine representative low-res points for each tile and swap them to a new buffer
			using (var process = progressManager.StartProcess("QuantTilePointsIndexedExtractLowRes"))
			{
				// todo: tiles need to be square

				var index = 0;
				foreach (var tile in tileFilter.GetCellOrdering())
				{
					int count = tileCounts.Data[tile.Row, tile.Col];
					byte *dataPtr = segmentBuffer.PointDataPtr + (index * source.PointSizeBytes);
					byte* dataEndPtr = dataPtr + (count * source.PointSizeBytes);

					//m_pixelsOverRangeX = (double)m_grid.SizeX / m_source.QuantizedExtent.RangeX;

					byte* pb = dataPtr;
					while (pb < dataEndPtr)
					{
						var p = (SQuantizedPoint3D*)pb;

						//int pixelX = (int)(((*p).X - m_minX) * m_pixelsOverRangeX);
						//int pixelY = (int)(((*p).Y - m_minY) * m_pixelsOverRangeY);

						//if ((*p).Z > m_gridQuantized.Data[pixelY, pixelX])
						//	m_gridQuantized.Data[pixelY, pixelX] = (*p).Z;

						pb += source.PointSizeBytes;
					}
				}

				//var group = new ChunkProcessSet(tileFilter, segmentBuffer);
				//group.Process(source.GetBlockEnumerator(process));
			}
		}

		private static PointCloudAnalysisResult QuantEstimateDensity(IPointCloudBinarySource source, int maxSegmentLength, SQuantizedExtentGrid<int> tileCounts, ProgressManager progressManager)
		{
			Statistics stats = null;
			List<PointCloudBinarySourceEnumeratorSparseGridRegion> gridIndexSegments = null;

			var extent = source.Extent;
			var quantizedExtent = source.QuantizedExtent;

            var statsMapping = new ScaledStatisticsMapping(quantizedExtent.MinZ, quantizedExtent.RangeZ, 1024);
			var gridCounter = new GridCounter(source, tileCounts);

			using (var process = progressManager.StartProcess("QuantEstimateDensity"))
			{
				var group = new ChunkProcessSet(gridCounter, statsMapping);
				group.Process(source.GetBlockEnumerator(process));
			}

			stats = statsMapping.ComputeStatistics(extent.MinZ, extent.RangeZ);
			var density = new PointCloudTileDensity(tileCounts, extent);
			gridIndexSegments = gridCounter.GetGridIndex(density, maxSegmentLength);

			var result = new PointCloudAnalysisResult(density, stats, source.Quantization, gridIndexSegments);

			return result;
		}

		#endregion

		#region Helpers

		private static SQuantizedExtentGrid<int> CreateTileCountsForEstimation(IPointCloudBinarySource source)
		{
			var count = source.Count;
			var extent = source.Extent;

			var tileCountForUniformData = (int)(count / PROPERTY_DESIRED_TILE_COUNT.Value);
			//int tileCount = Math.Min(tileCountForUniformData, PROPERTY_MAX_TILES_FOR_ESTIMATION.Value);
			var tileCount = tileCountForUniformData;

			// for estimation, use a reduced tile size to minimize indexing overlap
			tileCount *= 16;

			tileCount = Math.Min(tileCount, PROPERTY_MAX_TILES_FOR_ESTIMATION.Value);

			var tileArea = extent.Area / tileCount;
			var tileSize = Math.Sqrt(tileArea);

			return source.QuantizedExtent.CreateGridFromCellSize<int>(tileSize, source.Quantization, true);
		}

		#endregion
	}
}
