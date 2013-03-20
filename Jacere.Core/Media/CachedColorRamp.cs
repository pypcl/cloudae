﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Jacere.Core
{
	/// <summary>
	/// Pre-calculated color ramp using the 2^n mapping mechanism.
	/// Stretched for a specified size and input range (including std dev stretch).
	/// </summary>
	public class CachedColorRamp
	{
		private const bool SCALE_DESIRED_BINS_TO_SOURCE_RANGE = false;

		private readonly ColorRamp m_ramp;

		private readonly int m_realMin;
		private readonly int m_realMax;

		private readonly int m_sourceMin;
		private readonly int m_sourceMax;
		private readonly uint m_sourceRange;
		private readonly int m_sourceRangeExtendedPow;
		private readonly uint m_sourceRangeExtended;

		private readonly int m_binCountDesired;
		private readonly int m_binCountPow;
		private readonly int m_binCount;

		private readonly int m_realMinShifted;
		private readonly int m_realMaxShifted;

		private readonly int m_sourceMinShifted;
		private readonly int m_sourceMaxShifted;

		private readonly int m_sourceRightShift;

		private readonly int[] m_bins;

		public int GetColor(int z)
		{
			return m_bins[z >> m_sourceRightShift];
		}

		public CachedColorRamp(ColorRamp ramp, int min, int max, QuantizedStatistics stats, bool useStdDevStretch, int desiredDestinationBins)
		{
            if (useStdDevStretch && stats == null)
				throw new ArgumentException("There must be a stats argument if stretching is enabled.");

			m_ramp = ramp;
			m_binCountDesired = desiredDestinationBins;

            m_realMin = min;
            m_realMax = max;

			if (useStdDevStretch)
			{
				uint stdDevMultiple = 2 * stats.StdDev;
				m_sourceMin = (int)Math.Max(m_realMin, (long)stats.m_mean - stdDevMultiple);
				m_sourceMax = (int)Math.Min(m_realMax, (long)stats.m_mean + stdDevMultiple);
			}
			else
			{
				m_sourceMin = m_realMin;
				m_sourceMax = m_realMax;
			}

			m_sourceRange = (uint)((long)m_sourceMax - m_sourceMin);

			// stretch desired destination bins over adjusted range,
			// and determine how many total bins are required at that scale
			double totalBinsEstimate = desiredDestinationBins;
			if (SCALE_DESIRED_BINS_TO_SOURCE_RANGE)
				totalBinsEstimate = totalBinsEstimate * (m_realMax + 1) / m_sourceRange;

			m_binCountPow = (int)Math.Ceiling(Math.Log(totalBinsEstimate, 2));
			m_binCount = (int)Math.Pow(2, m_binCountPow);

			// extend range for source
			m_sourceRangeExtendedPow = (int)Math.Ceiling(Math.Log(m_realMax + 1, 2));
			m_sourceRangeExtended = (uint)Math.Pow(2, m_sourceRangeExtendedPow);
			if (m_sourceRangeExtendedPow == 32)
				m_sourceRangeExtended = uint.MaxValue;
			else if (m_sourceRangeExtendedPow > 32)
				throw new Exception("how is this possible");

			// assume only right shifts will be required
			if (m_binCountPow > m_sourceRangeExtendedPow)
				throw new Exception("I did not expect this");

			m_sourceRightShift = m_sourceRangeExtendedPow - m_binCountPow;

			m_realMinShifted = m_realMin >> m_sourceRightShift;
			m_realMaxShifted = m_realMax >> m_sourceRightShift;

			m_sourceMinShifted = m_sourceMin >> m_sourceRightShift;
			m_sourceMaxShifted = m_sourceMax >> m_sourceRightShift;

			m_bins = new int[m_binCount + 1];

			for (int i = m_realMinShifted; i < m_sourceMinShifted; i++)
				m_bins[i] = ramp.GetColor(0.0).ToArgb();
			for (int i = m_sourceMaxShifted + 1; i <= m_realMaxShifted + 1; i++)
				m_bins[i] = ramp.GetColor(1.0).ToArgb();

			uint destinationRange = (uint)((long)m_sourceMaxShifted - m_sourceMinShifted + 1);

			for (int i = m_sourceMinShifted; i <= m_sourceMaxShifted; i++)
			{
				//double ratio = (i - m_sourceMinShifted + 0.5) / m_sourceRange;
				var iMinusMinShifted = i - m_sourceMinShifted;
				double ratio = (double)iMinusMinShifted / destinationRange;
				m_bins[iMinusMinShifted] = ramp.GetColor(ratio).ToArgb();
			}
		}
	}
}
