﻿using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using WCell.Terrain.GUI.Util;
using Color = Microsoft.Xna.Framework.Graphics.Color;
using Vector3 = Microsoft.Xna.Framework.Vector3;

namespace WCell.Terrain.GUI.Renderers
{
	class TileRenderer : RendererBase
	{
		private const bool DrawLiquids = false;
		private static Color TerrainColor
		{
			get { return Color.DarkSlateGray; }
			//get { return Color.Green; }
		}

		private static Color WaterColor
		{
			//get { return Color.DarkSlateGray; }
			get { return Color.Blue; }
		}


		public TileRenderer(Game game)
			: base(game)
		{
		}

		public TerrainTile Tile
		{
			get;
			private set;
		}

		#region Build polygons
		protected override void BuildVerticiesAndIndicies()
		{
			// Cycle through each ADT
			var viewer = (TerrainViewer) Game;
			Tile = viewer.Tile;

			var tempVertices = new List<VertexPositionNormalColored>();
			var tempIndicies = new List<int>();
			var offset = 0;

			// Handle the ADTs
			for (var v = 0; v < Tile.TerrainVertices.Length; v++)
			{
				var vertex1 = Tile.TerrainVertices[v];
				XNAUtil.TransformWoWCoordsToXNACoords(ref vertex1);
				var vertexPosNmlCol1 = new VertexPositionNormalColored(vertex1.ToXna(),
																		TerrainColor,
																		Vector3.Up);
				tempVertices.Add(vertexPosNmlCol1);
			}

			for (var i = 0; i < Tile.TerrainIndices.Length; i += 3)
			{
				var index1 = Tile.TerrainIndices[i];
				var index2 = Tile.TerrainIndices[i+1];
				var index3 = Tile.TerrainIndices[i+2];
				tempIndicies.Add(index1);
				tempIndicies.Add(index2);
				tempIndicies.Add(index3);
			}
			offset = tempVertices.Count;

			//if (!DrawLiquids) continue;
			if (Tile.LiquidVertices != null)
			{
				for (var v = 0; v < Tile.LiquidVertices.Count; v++)
				{
					var vertex = Tile.LiquidVertices[v];
					var vertexPosNmlCol = new VertexPositionNormalColored(vertex.ToXna(),
					                                                      WaterColor,
					                                                      Vector3.Down);
					tempVertices.Add(vertexPosNmlCol);
				}
				for (var i = 0; i < Tile.LiquidIndices.Count; i++)
				{
					tempIndicies.Add(Tile.LiquidIndices[i] + offset);
				}
			}
			offset = tempVertices.Count;

			_cachedIndices = tempIndicies.ToArray();
			_cachedVertices = tempVertices.ToArray();

			_renderCached = true;
		}
		#endregion
	}
}
