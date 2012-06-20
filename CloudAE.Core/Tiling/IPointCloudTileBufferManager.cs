﻿
namespace CloudAE.Core
{
	public unsafe interface IPointCloudTileBufferManager
	{
		/// <summary>
		/// Gets the tile source.
		/// </summary>
		/// <value>The tile source.</value>
		PointCloudTileSource TileSource
		{
			get;
		}

		/// <summary>
		/// Adds the point to the tiling, but provides no guarantee
		/// about when the point will be written to disk.
		/// </summary>
		/// <param name="p">The point.</param>
		/// <param name="tileX">The tile X.</param>
		/// <param name="tileY">The tile Y.</param>
		void AddPoint(byte* p, int tileX, int tileY);
		
		/// <summary>
		/// Finalizes the tiles.
		/// The progress manager is provided in case the operation hits the disk.
		/// </summary>
		/// <param name="progressManager">The progress manager.</param>
		void FinalizeTiles(ProgressManager progressManager);
	}
}
