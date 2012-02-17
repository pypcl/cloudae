﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using CloudAE.Core;

namespace CloudAE.App
{
	/// <summary>
	/// Interaction logic for Preview2D.xaml
	/// </summary>
	public sealed partial class Preview2D : UserControl, ITileSourceControl, IFactory
	{
		private const int MAX_BUFFER_SIZE_BYTES = (int)ByteSizesSmall.MB_128;

		private static readonly ColorRamp[] c_colorRamps;

		private PointCloudTileSource m_currentTileSource;

		private Dictionary<PointCloudTile, System.Windows.Shapes.Rectangle> m_loadedTiles;
		private Dictionary<PointCloudTile, byte[]> m_loadedTileBuffers;

		public string DisplayName
		{
			get { return "2D"; }
		}

		public int Index
		{
			get { return 0; }
		}

		public ImageSource Icon
		{
			get { return new BitmapImage(new Uri("pack://application:,,,/CloudAE.App;component/Icons/map.png")); }
		}

		public PointCloudTileSource CurrentTileSource
		{
			get
			{
				return m_currentTileSource;
			}
			set
			{
				previewImageGraphicsGrid.Children.Clear();
				m_loadedTiles.Clear();
				m_loadedTileBuffers.Clear();

				if (m_currentTileSource != null)
				{
					colorRampsCombo.SelectedItem = null;
				}

				m_currentTileSource = value;

				if (m_currentTileSource != null)
				{
					colorRampsCombo.SelectedItem = CurrentColorRamp;
					colorUseStdDev.IsChecked = CurrentColorUseStdDev;
				}
				else
				{
					previewImage.Source = null;
				}
			}
		}

		private ColorRamp CurrentColorRamp
		{
			get
			{
				ColorRamp ramp = ColorRamp.PredefinedColorRamps.Elevation1;
				if (CurrentTileSource.Preview != null)
					ramp = m_currentTileSource.Preview.ColorHandler as ColorRamp;

				return ramp;
			}
		}

		private bool CurrentColorUseStdDev
		{
			get
			{
				bool useStdDev = true;
				if (CurrentTileSource.Preview != null)
					useStdDev = CurrentTileSource.Preview.UseStdDevStretch;

				return useStdDev;
			}
		}

		static Preview2D()
		{
			List<ColorRamp> controls = RegisterColorHandlers();
			c_colorRamps = controls.ToArray();
		}
		
		public Preview2D()
		{
			InitializeComponent();

			m_loadedTiles = new Dictionary<PointCloudTile, System.Windows.Shapes.Rectangle>();
			m_loadedTileBuffers = new Dictionary<PointCloudTile, byte[]>();

			foreach (ColorRamp handler in c_colorRamps)
				colorRampsCombo.Items.Add(handler);
		}

		private static List<ColorRamp> RegisterColorHandlers()
		{
			List<ColorRamp> instances = new List<ColorRamp>();
			Type baseType = typeof(ColorRamp);

			Context.ProcessLoadedTypes(
				2,
				"ColorRamps",
				t => baseType.IsAssignableFrom(t),
				t => !t.IsAbstract,
				t => instances.Add(ColorRamp.LoadMap(t))
			);

			return instances;
		}

		private void OnColorHandlerSelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			ColorRamp ramp = (sender as ComboBox).SelectedItem as ColorRamp;
			if (ramp != null)
			{
				PointCloudTileSource source = CurrentTileSource;
				if (source != null)
					previewImage.Source = source.GeneratePreviewImage(ramp, CurrentColorUseStdDev);
			}
		}

		private void OnStdDevCheckedStateChanged(object sender, RoutedEventArgs e)
		{
			bool? useStdDev = (sender as ToggleButton).IsChecked;
			if (useStdDev.HasValue)
			{
				PointCloudTileSource source = CurrentTileSource;
				if (source != null)
					previewImage.Source = source.GeneratePreviewImage(CurrentColorRamp, useStdDev.Value);
			}
		}

		private void OnPreviewMouseMove(object sender, MouseEventArgs e)
		{
			FrameworkElement image = sender as FrameworkElement;
			PointCloudTile tile = GetTileFromMousePosition(image, e);

			UpdateCurrentTile(tile);
		}

		private PointCloudTile GetTileFromMousePosition(FrameworkElement container, MouseEventArgs e)
		{
			double containerWidth = container.ActualWidth;
			double containerHeight = container.ActualHeight;

			System.Windows.Point point = e.GetPosition(container);
			double imageRatioX = point.X / containerWidth;
			double imageRatioY = 1 - (point.Y / containerHeight);

			return CurrentTileSource.TileSet.GetTileByRatio(imageRatioX, imageRatioY);
		}

		private void UpdateCurrentTile(PointCloudTile tile)
		{
			foreach (System.Windows.Shapes.Rectangle rect in m_loadedTiles.Values)
			{
				rect.Stroke = System.Windows.Media.Brushes.DarkGray;
			}

			if (tile.PointCount == 0)
				return;

			List<PointCloudTile> tilesToLoad = new List<PointCloudTile>();
			int pointsToLoad = 0;

			int radius = 4;

			int xMin = Math.Max(0, tile.Col - radius);
			int xMax = Math.Min(tile.Col + radius + 1, CurrentTileSource.TileSet.Cols);
			int yMin = Math.Max(0, tile.Row - radius);
			int yMax = Math.Min(tile.Row + radius + 1, CurrentTileSource.TileSet.Rows);
			for (int x = xMin; x < xMax; x++)
			{
				for (int y = yMin; y < yMax; y++)
				{
					PointCloudTile currentTile = CurrentTileSource.TileSet[x, y];

					if (currentTile.PointCount > 0)
					{
						if (!m_loadedTiles.ContainsKey(currentTile))
						{
							tilesToLoad.Add(currentTile);
							pointsToLoad += currentTile.PointCount;

							System.Windows.Shapes.Rectangle rect = CreateTileRectangle(currentTile, previewImage, previewImageGrid);
							m_loadedTiles.Add(currentTile, rect);

							previewImageGraphicsGrid.Children.Add(rect);
						}
						else
						{
							System.Windows.Shapes.Rectangle rect = m_loadedTiles[currentTile];
							if (tile.Equals(currentTile))
								rect.Stroke = System.Windows.Media.Brushes.Red;
							else
								rect.Stroke = System.Windows.Media.Brushes.Blue;
						}
					}
				}
			}

			PointCloudTile[] loadedTiles = m_loadedTiles.Keys.ToArray();
			SortByDistanceFromTile(loadedTiles, tile);
			Array.Reverse(loadedTiles);

			// drop loaded tiles that are the farthest from the center
			int totalAllowedPoints = MAX_BUFFER_SIZE_BYTES / CurrentTileSource.PointSizeBytes;
			int loadedPoints = 0;
			for (int i = 0; i < loadedTiles.Length; i++)
				loadedPoints += loadedTiles[i].PointCount;

			int potentialTotalPoints = loadedPoints + pointsToLoad;

			if (potentialTotalPoints > totalAllowedPoints)
			{
				int pointsToDrop = potentialTotalPoints - totalAllowedPoints;
				int i = 0;
				while(pointsToDrop > 0)
				{
					PointCloudTile currentTile = loadedTiles[i];
					System.Windows.Shapes.Rectangle rect = m_loadedTiles[currentTile];
					m_loadedTiles.Remove(currentTile);
					m_loadedTileBuffers.Remove(currentTile);

					// I don't know why I need to do this, but I can occasionally get a duplicate add below if I don't
					tilesToLoad.Remove(currentTile);

					previewImageGraphicsGrid.Children.Remove(rect);
					pointsToDrop -= currentTile.PointCount;
					++i;
				}
			}

			PointCloudTile[] tilesToLoadArray = tilesToLoad.ToArray();
			SortByDistanceFromTile(tilesToLoadArray, tile);
			foreach (PointCloudTile currentTile in tilesToLoadArray)
			{
				// this will cause fragmentation problems, but it's just for demonstration
				byte[] inputBuffer = new byte[currentTile.PointCount * CurrentTileSource.PointSizeBytes];
				CurrentTileSource.LoadTile(currentTile, inputBuffer);
				m_loadedTileBuffers.Add(currentTile, inputBuffer);
			}
		}

		private void SortByDistanceFromTile(PointCloudTile[] tiles, PointCloudTile centerTile)
		{
			float[] distanceToCenter2 = new float[tiles.Length];
			for (int i = 0; i < tiles.Length; i++)
			{
				distanceToCenter2[i] = (float)(Math.Pow(tiles[i].Col - centerTile.Col, 2) + Math.Pow(tiles[i].Row - centerTile.Row, 2));
			}

			Array.Sort<float, PointCloudTile>(distanceToCenter2, tiles);
		}

		private System.Windows.Shapes.Rectangle CreateTileRectangle(PointCloudTile tile, FrameworkElement image, Panel grid)
		{
			double containerWidth = image.ActualWidth;
			double containerHeight = image.ActualHeight;

			double imageLocationX = (grid.ActualWidth - containerWidth) / 2;
			double imageLocationY = (grid.ActualHeight - containerHeight) / 2;

			double tileRatioX = (double)tile.Col / (CurrentTileSource.TileSet.Cols);
			double tileRatioY = 1 - ((double)(tile.Row + 1) / (CurrentTileSource.TileSet.Rows));

			double tileLocationX = imageLocationX + tileRatioX * containerWidth;
			double tileLocationY = imageLocationY + tileRatioY * containerHeight;

			double tileRatioWidth = 1.0 / (CurrentTileSource.TileSet.Cols);
			double tileRatioHeight = 1.0 / (CurrentTileSource.TileSet.Rows);

			System.Windows.Shapes.Rectangle rect = new System.Windows.Shapes.Rectangle();

			rect.StrokeThickness = 1;
			rect.Stroke = System.Windows.Media.Brushes.Green;

			rect.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
			rect.VerticalAlignment = System.Windows.VerticalAlignment.Top;

			rect.Width = tileRatioWidth * containerWidth;
			rect.Height = tileRatioHeight * containerHeight;

			rect.Margin = new Thickness(tileLocationX, tileLocationY, 0, 0);

			return rect;
		}
	}
}