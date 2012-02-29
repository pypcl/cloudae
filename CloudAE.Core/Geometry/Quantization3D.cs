﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CloudAE.Core.Geometry
{
	public abstract class Quantization3D : IQuantization3D, ISerializeBinary
	{
		private const int LOG_ROUNDING_PRECISION = 12;

		public readonly double ScaleFactorX;
		public readonly double ScaleFactorY;
		public readonly double ScaleFactorZ;
		public readonly double OffsetX;
		public readonly double OffsetY;
		public readonly double OffsetZ;

		public Quantization3D(double sfX, double sfY, double sfZ, double oX, double oY, double oZ)
		{
			ScaleFactorX = sfX;
			ScaleFactorY = sfY;
			ScaleFactorZ = sfZ;
			OffsetX = oX;
			OffsetY = oY;
			OffsetZ = oZ;
		}

		public Quantization3D(BinaryReader reader)
		{
			ScaleFactorX = reader.ReadDouble();
			ScaleFactorY = reader.ReadDouble();
			ScaleFactorZ = reader.ReadDouble();
			OffsetX = reader.ReadDouble();
			OffsetY = reader.ReadDouble();
			OffsetZ = reader.ReadDouble();
		}

		public void Serialize(BinaryWriter writer)
		{
			writer.Write(ScaleFactorX);
			writer.Write(ScaleFactorY);
			writer.Write(ScaleFactorZ);
			writer.Write(OffsetX);
			writer.Write(OffsetY);
			writer.Write(OffsetZ);
		}

		public static Quantization3D Create(Extent3D extent, bool unsigned)
		{
			double qOffsetX = extent.MidpointX;
			double qOffsetY = extent.MidpointY;
			double qOffsetZ = extent.MidpointZ;

			if (unsigned)
			{
				qOffsetX = extent.MinX;
				qOffsetY = extent.MinY;
				qOffsetZ = extent.MinZ;
			}

			// this is a stupid way to do this
			// I need to get the precision evaluation working

			double pow2to32 = Math.Pow(2, 32);
			double logBase = 10; // this value effects debugging and compressibility

			int precisionMaxX = (int)Math.Floor(Math.Log(pow2to32 / (extent.RangeX), logBase));
			int precisionMaxY = (int)Math.Floor(Math.Log(pow2to32 / (extent.RangeY), logBase));
			int precisionMaxZ = (int)Math.Floor(Math.Log(pow2to32 / (extent.RangeZ), logBase));

			double qScaleFactorX = Math.Pow(logBase, -precisionMaxX);
			double qScaleFactorY = Math.Pow(logBase, -precisionMaxY);
			double qScaleFactorZ = Math.Pow(logBase, -precisionMaxZ);

			if(unsigned)
				return new UQuantization3D(qScaleFactorX, qScaleFactorY, qScaleFactorZ, qOffsetX, qOffsetY, qOffsetZ);
			else
				return new SQuantization3D(qScaleFactorX, qScaleFactorY, qScaleFactorZ, qOffsetX, qOffsetY, qOffsetZ);
		}

		public static UQuantization3D Create(Extent3D extent, SQuantization3D inputQuantization, int[][] testValues)
		{
			// determine best scale factors
			int pointsToTest = testValues[0].Length;
			double[] scaleFactors = new double[] { inputQuantization.ScaleFactorX, inputQuantization.ScaleFactorY, inputQuantization.ScaleFactorZ };
			for (int i = 0; i < 3; i++)
			{
				int[] values = testValues[i];
				Array.Sort<int>(values);
				int min = values[0];
				int max = values[pointsToTest - 1];
				int range = max - min;

				// determine the base of the scale factor
				int scaleInverse = (int)Math.Ceiling(1 / scaleFactors[i]);
				int scaleBase = FindBase(scaleInverse);
				int scalePow = (int)Math.Round(Math.Log(scaleInverse, scaleBase), LOG_ROUNDING_PRECISION);

				// count differences
				SortedList<uint, int> diffCounts = new SortedList<uint, int>();

				for (int p = 1; p < pointsToTest; p++)
				{
					uint diff = (uint)(values[p] - values[p - 1]);
					if (diffCounts.ContainsKey(diff))
						++diffCounts[diff];
					else
						diffCounts.Add(diff, 1);
				}

				int differenceCount = diffCounts.Count;
				double[] diffPow = new double[differenceCount];
				for (int d = 1; d < differenceCount; d++)
					diffPow[d] = Math.Log(diffCounts.Keys[d], scaleBase);

				int nonZeroDiffPointCount = diffCounts.SkipWhile(kvp => kvp.Key == 0).Sum(kvp => kvp.Value);
				double[] diffPowComponentRatio = new double[differenceCount];
				for (int d = 1; d < differenceCount; d++)
					diffPowComponentRatio[d] = diffPow[d] * diffCounts.Values[d] / nonZeroDiffPointCount;

				// this rounding is a WAG
				double componentSum = Math.Round(diffPowComponentRatio.Sum(), 4);
				int compontentSumPow = (int)componentSum;
				if (compontentSumPow > 1)
					scaleFactors[i] = Math.Pow(scaleBase, compontentSumPow - scalePow);
			}
			return new UQuantization3D(scaleFactors[0], scaleFactors[1], scaleFactors[2], Math.Floor(extent.MinX), Math.Floor(extent.MinY), Math.Floor(extent.MinZ));
		}

		public static UQuantization3D Create(Extent3D extent, double[][] testValues)
		{
			// determine best scale factors
			int pointsToTest = testValues[0].Length;
			double[] scaleFactors = new double[3];
			for (int i = 0; i < 3; i++)
			{
				double[] values = testValues[i];
				Array.Sort<double>(values);
				double min = values[0];
				double max = values[pointsToTest - 1];
				double range = max - min;

				int scaleBase = 10;

				// count differences
				SortedList<double, int> diffCounts = new SortedList<double, int>();

				for (int p = 1; p < pointsToTest; p++)
				{
					double diff = values[p] - values[p - 1];
					if (diffCounts.ContainsKey(diff))
						++diffCounts[diff];
					else
						diffCounts.Add(diff, 1);
				}

				int differenceCount = diffCounts.Count;
				double[] diffPow = new double[differenceCount];
				for (int d = 1; d < differenceCount; d++)
					diffPow[d] = Math.Log(diffCounts.Keys[d], scaleBase);

				int nonZeroDiffPointCount = diffCounts.SkipWhile(kvp => kvp.Key == 0).Sum(kvp => kvp.Value);
				double[] diffPowComponentRatio = new double[differenceCount];
				for (int d = 1; d < differenceCount; d++)
					diffPowComponentRatio[d] = diffPow[d] * diffCounts.Values[d] / nonZeroDiffPointCount;

				// this rounding is a WAG
				double componentSum = Math.Round(diffPowComponentRatio.Sum(), 4);
				int compontentSumPow = (int)componentSum;
				scaleFactors[i] = Math.Pow(scaleBase, compontentSumPow);
			}
			return new UQuantization3D(scaleFactors[0], scaleFactors[1], scaleFactors[2], Math.Floor(extent.MinX), Math.Floor(extent.MinY), Math.Floor(extent.MinZ));
		}

		private static int FindBase(int inverseScale)
		{
			// find factors
			Dictionary<int, int> factors = new Dictionary<int, int>();

			int currentFactorValue = 2;

			int remainder = inverseScale;
			while (remainder > 1)
			{
				if (remainder % currentFactorValue == 0)
				{
					remainder = remainder / currentFactorValue;
					if (factors.ContainsKey(currentFactorValue))
						++factors[currentFactorValue];
					else
						factors.Add(currentFactorValue, 1);
				}
				else
				{
					++currentFactorValue;
				}
			}

			int smallestCount = factors.Values.Min();

			int scaleBase = 1;
			foreach (int factor in factors.Keys)
			{
				scaleBase *= (factor * (factors[factor] / smallestCount));
			}

			return scaleBase;
		}

		public abstract IQuantizedPoint3D Convert(Point3D point);
		public abstract Point3D Convert(IQuantizedPoint3D point);
		public abstract IQuantizedExtent3D Convert(Extent3D extent);
		public abstract Extent3D Convert(IQuantizedExtent3D extent);
	}
}
