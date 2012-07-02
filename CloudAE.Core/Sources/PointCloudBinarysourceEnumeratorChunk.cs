﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace CloudAE.Core
{
	public unsafe class PointCloudBinarySourceEnumeratorChunk : IProgress
	{
		public readonly uint Index;
		public readonly int BytesRead;
		public readonly int PointsRead;
		public readonly byte[] Data;
		public readonly byte* DataPtr;
		public readonly byte* DataEndPtr;
		public readonly int Length;

		private readonly float m_progress;

		public float Progress
		{
			get { return m_progress; }
		}

		public PointCloudBinarySourceEnumeratorChunk(uint index, BufferInstance buffer, int bytesRead, int pointsRead, float progress)
		{
			Index = index;
			BytesRead = bytesRead;
			PointsRead = pointsRead;
			Data = buffer.Data;
			DataPtr = buffer.DataPtr;
			DataEndPtr = DataPtr + BytesRead;
			Length = (int)(DataEndPtr - DataPtr);

			m_progress = progress;
		}
	}
}
