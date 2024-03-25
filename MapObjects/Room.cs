﻿using Cornifer.Json;
using Cornifer.Renderers;
using Cornifer.Structures;
using Cornifer.UI.Elements;
using Cornifer.UI.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Cornifer.MapObjects
{
    public class Room : MapObject
    {
        static Point[] Directions = new Point[] { new Point(0, -1), new Point(1, 0), new Point(0, 1), new Point(-1, 0) };
        public static HashSet<string> NonPickupObjectsWhitelist = new()
        {
            "GhostSpot", "BlueToken", "GoldToken", "PurpleToken",
            "RedToken", "WhiteToken", "DevToken", "GreenToken",
            "DataPearl", "UniqueDataPearl", "ScavengerOutpost",
            "HRGuard", "TempleGuard", "MoonCloak"
        };

        public bool IsGate;
        public bool IsShelter;
        public bool IsAncientShelter;
        public bool IsScavengerTrader;
        public bool IsScavengerOutpost;
        public bool IsScavengerTreasury;

        public Vector2? TreasuryPos;
        public Vector2? OutpostPos;

        public Point TileSize;
        public bool WaterInFrontOfTerrain;
        public Tile[,] Tiles = null!;

        public Point[] Exits = Array.Empty<Point>();
        public Shortcut[] Shortcuts = Array.Empty<Shortcut>();

        public int Layer;
        public ObjectProperty<int> WaterLevel = new("waterLevel", -1);
        public ObjectProperty<Subregion, string> Subregion = new("subregion", null!);
        public ObjectProperty<bool> Deathpit = new("deathpit", false);
        public ObjectProperty<bool> UseBetterTileCutout = new("betterTileCutout", true);
        public ObjectProperty<bool> CutoutAllSolidTiles = new("cutAllSolid", false);
        public ObjectProperty<bool> DrawInRoomShortcuts = new("inRoomShortcuts", false);
        public ObjectProperty<bool> AcidWater = new("acidWater", false);
        public ObjectProperty<ColorRef> AcidColor = new("acidWater", new(null, Color.Blue));

        public Effect[] Effects = Array.Empty<Effect>();
        public Connection?[] Connections = Array.Empty<Connection>();
        public HashSet<string> BrokenForSlugcats = new();

        public Texture2D? TileMap;
        public bool TileMapDirty = false;

        public bool Loaded = false;
        public bool Positioned = false;

        public string? DataString;
        public string? SettingsString;

        public readonly Region Region = null!;

        public override bool CanSetActive => true;

        protected override Layer DefaultLayer => Main.RoomsLayer;

        public override bool LoadCreationForbidden => true;
        public override int ShadeSize => InterfaceState.DisableRoomCropping.Value ? 0 : 5;
        public override int? ShadeCornerRadius => 6;
        public override bool ParentSelected => BoundRoom is not null && (BoundRoom.Selected || BoundRoom.ParentSelected) || base.ParentSelected;

        public override Vector2 ParentPosition
        {
            get => boundRoom is null ? Position : boundRoom.WorldPosition + Position;
            set
            {
                Position = value;
                if (boundRoom is not null)
                    Position -= boundRoom.WorldPosition;
            }
        }
        public override Vector2 Size => TileSize.ToVector2();

        public Room? BoundRoom
        {
            get => boundRoom;
            set
            {
                if (boundRoom is not null)
                    Position += boundRoom.WorldPosition;

                boundRoom = value;

                if (boundRoom is not null)
                    Position -= boundRoom.WorldPosition;
            }
        }

        public GateRoomData? GateData;
        public GateSymbols? GateSymbols;
        public MapText? GateRegionText;


        public bool CutOutsDirty = true;
        bool[,]? CutOutSolidTiles = null;

        Vector2 Position;
        private Room? boundRoom;

        public Room()
        {
            Subregion.SaveValue = v => v.Name;
            Subregion.LoadValue = s => Region.Subregions.First(r => r.Name == s);
        }

        public Room(Region region, string id) : this()
        {
            Region = region;
            Name = id;
        }
        public Point TraceShotrcut(Point pos, List<Point>? turns = null)
        {
            Point lastPos = pos;
            int? dir = null;
            bool foundDir = false;

            while (true)
            {
                if (dir is not null)
                {
                    Point dirVal = Directions[dir.Value];

                    Point testTilePos = pos + dirVal;

                    if (testTilePos.X >= 0 && testTilePos.Y >= 0 && testTilePos.X < TileSize.X && testTilePos.Y < TileSize.Y)
                    {
                        Tile tile = Tiles[testTilePos.X, testTilePos.Y];
                        if (tile.Shortcut == Tile.ShortcutType.Normal)
                        {
                            lastPos = pos;
                            pos = testTilePos;
                            continue;
                        }
                        else if (tile.Shortcut != Tile.ShortcutType.None)
                        {
                            pos = testTilePos;
                            break;
                        }
                    }
                }
                foundDir = false;
                for (int j = 0; j < 4; j++)
                {
                    Point dirVal = Directions[j];
                    Point testTilePos = pos + dirVal;

                    if (testTilePos == lastPos || testTilePos.X < 0 || testTilePos.Y < 0 || testTilePos.X >= TileSize.X || testTilePos.Y >= TileSize.Y)
                        continue;

                    Tile tile = Tiles[testTilePos.X, testTilePos.Y];
                    if (tile.Shortcut == Tile.ShortcutType.Normal)
                    {
                        if (dir is not null) // not the first iteration
                            turns?.Add(pos);

                        dir = j;
                        foundDir = true;
                        break;
                    }
                    else if (tile.Shortcut != Tile.ShortcutType.None)
                    {
                        pos = testTilePos;
                        foundDir = false;
                        break;
                    }
                }
                if (!foundDir)
                    break;
            }

            return pos;
        }

        public Tile GetTile(int x, int y)
        {
            x = Math.Clamp(x, 0, TileSize.X - 1);
            y = Math.Clamp(y, 0, TileSize.Y - 1);
            return Tiles[x, y];
        }

        public void Load(string data, string? settings)
        {
            SettingsString = settings;

            string[] lines = data.Split('\n');

            if (lines.TryGet(1, out string sizeWater))
            {
                string[] swArray = sizeWater.Split('|');
                if (swArray.TryGet(0, out string size))
                {
                    string[] sArray = size.Split('*');
                    if (sArray.TryGet(0, out string widthStr) && int.TryParse(widthStr, out int width))
                        TileSize.X = width;
                    if (sArray.TryGet(1, out string heightStr) && int.TryParse(heightStr, out int height))
                        TileSize.Y = height;
                }
                if (swArray.TryGet(1, out string waterLevelStr) && int.TryParse(waterLevelStr, out int waterLevel))
                {
                    WaterLevel.OriginalValue = waterLevel < 0 ? -1 : waterLevel + 1;
                }
                if (swArray.TryGet(2, out string waterInFrontStr))
                {
                    WaterInFrontOfTerrain = waterInFrontStr == "1";
                }
            }

            string? tilesLine = null;

            if (lines.TryGet(11, out string tiles))
                tilesLine = tiles;

            if (tilesLine is null)
            {
                int tileCount = TileSize.X * TileSize.Y;
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    int splitCount = lines[i].Count(c => c == '|');

                    if (splitCount >= tileCount - 1 && splitCount <= tileCount + 1)
                    {
                        tilesLine = lines[i];
                        break;
                    }
                }
            }

            Tiles = new Tile[TileSize.X, TileSize.Y];
            if (tilesLine is not null)
            {
                string[] tilesArray = tilesLine.Split('|');

                int x = 0, y = 0;
                for (int i = 0; i < tilesArray.Length; i++)
                {
                    if (tilesArray[i].Length == 0 || x < 0 || y < 0 || x >= Tiles.GetLength(0) || y >= Tiles.GetLength(1))
                        continue;

                    string[] tileArray = tilesArray[i].Split(',');
                    Tile tile = new();

                    for (int j = 0; j < tileArray.Length; j++)
                    {
                        if (j == 0)
                        {
                            if (!int.TryParse(tileArray[j], out int terrain))
                                continue;

                            tile.Terrain = (Tile.TerrainType)terrain;
                            continue;
                        }

                        switch (tileArray[j])
                        {
                            case "1": tile.Attributes |= Tile.TileAttributes.VerticalBeam; break;
                            case "2": tile.Attributes |= Tile.TileAttributes.HorizontalBeam; break;

                            case "3" when tile.Shortcut == Tile.ShortcutType.None:
                                tile.Shortcut = Tile.ShortcutType.Normal;
                                break;

                            case "4": tile.Shortcut = Tile.ShortcutType.RoomExit; break;
                            case "5": tile.Shortcut = Tile.ShortcutType.CreatureHole; break;
                            case "6": tile.Attributes |= Tile.TileAttributes.WallBehind; break;
                            case "7": tile.Attributes |= Tile.TileAttributes.Hive; break;
                            case "8": tile.Attributes |= Tile.TileAttributes.Waterfall; break;
                            case "9": tile.Shortcut = Tile.ShortcutType.NPCTransportation; break;
                            case "10": tile.Attributes |= Tile.TileAttributes.GarbageHole; break;
                            case "11": tile.Attributes |= Tile.TileAttributes.WormGrass; break;
                            case "12": tile.Shortcut = Tile.ShortcutType.RegionTransportation; break;
                        }
                    }

                    Tiles[x, y] = tile;

                    y++;
                    if (y >= TileSize.Y)
                    {
                        x++;
                        y = 0;
                    }
                }

                List<Point> exits = new();
                List<Point> shortcuts = new();

                for (int j = 0; j < TileSize.Y; j++)
                    for (int i = 0; i < TileSize.X; i++)
                    {
                        Tile tile = Tiles[i, j];

                        if (tile.Terrain == Tile.TerrainType.ShortcutEntrance)
                            shortcuts.Add(new Point(i, j));

                        if (tile.Shortcut == Tile.ShortcutType.RoomExit)
                            exits.Add(new Point(i, j));
                    }

                Point[] exitEntrances = new Point[exits.Count];

                for (int i = 0; i < exits.Count; i++)
                {
                    exitEntrances[i] = TraceShotrcut(exits[i]);
                }

                List<Shortcut> tracedShortcuts = new();

                foreach (Point shortcutIn in shortcuts)
                {
                    Point target = TraceShotrcut(shortcutIn);
                    Tile targetTile = GetTile(target.X, target.Y);

                    Tile.ShortcutType type = targetTile.Shortcut;
                    if (targetTile.Shortcut == Tile.ShortcutType.Normal && targetTile.Terrain != Tile.TerrainType.ShortcutEntrance)
                        type = Tile.ShortcutType.None;

                    tracedShortcuts.Add(new(shortcutIn, target, type));
                }

                Shortcuts = tracedShortcuts.ToArray();
                Exits = exitEntrances;
            }
            else
            {
                Main.LoadErrors.Add($"Could not find tile data for room {Name}");
            }

            if (lines.Length < 12)
            {
                int oldSize = lines.Length;
                Array.Resize(ref lines, 12);

                for (int i = oldSize - 1; i < lines.Length; i++)
                    lines[i] = "";
            }
            if (tilesLine is not null)
                lines[11] = tilesLine;

            for (int i = 0; i < lines.Length; i++)
            {
                if (i != 0 && i != 1 && i != 11)
                    lines[i] = "";
            }
            DataString = string.Join('\n', lines);

            if (settings is not null)
                foreach (string line in settings.Split('\n', StringSplitOptions.TrimEntries))
                {
                    string[] split = line.Split(':', 2, StringSplitOptions.TrimEntries);

                    if (split[0] == "PlacedObjects")
                    {
                        HashSet<PlacedObject> objects = new();
                        List<PlacedObject> filters = new();
                        string[] objectStrings = split[1].Split(',', StringSplitOptions.TrimEntries);
                        foreach (string str in objectStrings)
                        {
                            PlacedObject? obj = PlacedObject.Load(str);
                            if (obj is not null)
                            {
                                if (obj.Type == "Filter")
                                    filters.Add(obj);
                                else if (obj.Type == "ScavengerTreasury")
                                {
                                    IsScavengerTreasury = true;
                                    TreasuryPos = new(obj.RoomPos.X, TileSize.Y - obj.RoomPos.Y);
                                }
                                else
                                    objects.Add(obj);

                                if (obj.Type == "ScavengerOutpost")
                                    OutpostPos = new(obj.RoomPos.X, TileSize.Y - obj.RoomPos.Y);
                            }
                        }
                        List<PlacedObject> remove = new();

                        foreach (PlacedObject filter in filters)
                        {
                            Vector2 filterPos = filter.RoomPos;
                            float filterRad = filter.HandlePos.Length() / 20;

                            foreach (PlacedObject obj in objects)
                            {
                                Vector2 diff = obj.RoomPos - filterPos;
                                if (diff.Length() > filterRad)
                                    continue;

                                if (obj.SlugcatAvailability.Count == 0)
                                    obj.SlugcatAvailability.UnionWith(StaticData.Slugcats.Where(s => s.Playable).Select(s => s.Id));

                                obj.SlugcatAvailability.IntersectWith(filter.SlugcatAvailability);

                                if (obj.RemoveByAvailability && Main.SelectedSlugcat is not null && obj.SlugcatAvailability.Count > 0 && !obj.SlugcatAvailability.Contains(Main.SelectedSlugcat.Id))
                                    remove.Add(obj);
                            }
                        }

                        objects.ExceptWith(remove);

                        foreach (PlacedObject obj in objects)
                        {
                            obj.AddAvailabilityIcons();
                            Children.Add(obj);
                        }
                    }
                    else if (split[0] == "Effects")
                    {
                        List<Effect> effects = new();

                        foreach (string effectStr in split[1].Split(',', StringSplitOptions.TrimEntries))
                        {
                            string[] effectSplit = effectStr.Split('-');
                            if (effectSplit.Length == 4)
                            {
                                string name = effectSplit[0];
                                if (!float.TryParse(effectSplit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount))
                                    amount = 0;

                                effects.Add(new(name, amount));
                            }
                        }

                        Effects = effects.ToArray();
                    }
                }

            if (IsShelter && SpriteAtlases.Sprites.TryGetValue("ShelterMarker", out var shelterMarker))
                Children.Add(new SimpleIcon("ShelterMarker", shelterMarker));

            if (IsScavengerOutpost)
            {
                Vector2 align = OutpostPos.HasValue ? OutpostPos.Value / TileSize.ToVector2() : new Vector2(.5f);

                Children.Add(new MapText("TollText", Main.DefaultSmallMapFont, "Scavenger toll")
                {
                    ParentPosAlign = align,
                });
                if (SpriteAtlases.Sprites.TryGetValue("ChieftainA", out var tollIcon))
                    Children.Add(new SimpleIcon("TollIcon", tollIcon)
                    {
                        ParentPosAlign = align,
                    });
            }

            if (IsScavengerTrader)
            {
                Children.Add(new MapText("TraderText", Main.DefaultSmallMapFont, "Scavenger merchant"));
                if (SpriteAtlases.Sprites.TryGetValue("ChieftainA", out var tollIcon))
                    Children.Add(new SimpleIcon("TraderIcon", tollIcon));
            }

            if (IsScavengerTreasury)
            {
                Vector2 align = TreasuryPos.HasValue ? TreasuryPos.Value / TileSize.ToVector2() : new Vector2(.5f);

                Children.Add(new MapText("TreasuryText", Main.DefaultSmallMapFont, "Scavenger treasury")
                {
                    ParentPosAlign = align,
                });
                if (SpriteAtlases.Sprites.TryGetValue("ChieftainA", out var tollIcon))
                    Children.Add(new SimpleIcon("TreasuryIcon", tollIcon)
                    {
                        ParentPosAlign = align,
                    });
            }

            if (GateData is not null && IsGate)
            {
                Children.Add(GateSymbols = new GateSymbols(GateData.LeftKarma, GateData.RightKarma));
                Children.Add(GateRegionText = new MapText("TargetRegionText", Main.DefaultBigMapFont, $"Region Text"));

                GateRegionText.NoAlignOverride = true;
                GateRegionText.IconPosAlign = new(.5f);
                GateSymbols.Offset = new(0, MathF.Floor(-Size.Y / 2 - GateSymbols.Size.Y / 2 - 5));
                GateRegionText.Offset = new(0, MathF.Floor(-Size.Y / 2 - GateSymbols.Size.Y - 19 - Main.DefaultBigMapFont.LineSpacing / 2));
            }

            if (!Region.LegacyFormat && StaticData.VistaRooms.TryGetValue(Name!, out Vector2 vistaPoint))
            {
                Vector2 rel = vistaPoint / 20 / Size;

                rel.Y = 1 - rel.Y;

                Children.Add(new MapText("VistaMarker", Main.DefaultSmallMapFont, "Expedition\nvista\npoint")
                {
                    ParentPosAlign = rel,
                    RenderLayer = { OriginalValue = Main.VistaPointsLayer }
                });
            }

            if (BrokenForSlugcats.Count > 0)
            {
                string text = "Broken for " + string.Join(' ', StaticData.Slugcats.Where(s => BrokenForSlugcats.Contains(s.Id)).Select(s => $"[ic:Slugcat_{s.Id}]"));
                Children.Add(new MapText("BrokenShelterText", Main.DefaultSmallMapFont, text));
            }

            Deathpit.OriginalValue = !IsShelter && !IsGate && WaterLevel.Value < 0 && Enumerable.Range(0, TileSize.X).Any(x => Tiles[x, TileSize.Y - 1].Terrain == Tile.TerrainType.Air);

            if (Effects.Any(e => e.Name == "LethalWater"))
            {
                string key = $"reg_{Region.Id}_acid";
                if (ColorDatabase.Colors.TryGetValue(key, out ColorRef? cref))
                {
                    AcidWater.OriginalValue = true;
                    AcidColor.OriginalValue = cref;
                }
            }

            Loaded = true;
        }

        public Texture2D GetTileMap()
        {
            if (TileMap is null || TileMapDirty)
                UpdateTileMap();
            return TileMap!;
        }

        public void UpdateTileMap()
        {
            Subregion subregion = Subregion.Value;
            Color[] colors = ArrayPool<Color>.Shared.Rent(TileSize.X * TileSize.Y);
            Color waterColor = AcidWater.Value ? AcidColor.Value.Color : subregion.WaterColor.Color;
            try
            {
                bool invertedWater = Effects.Any(ef => ef.Name == "InvertedWater");

                int waterLevel = WaterLevel.Value;

                if (waterLevel < 0)
                {
                    Effect? waterFluxMin = Effects.FirstOrDefault(ef => ef.Name == "WaterFluxMinLevel");
                    Effect? waterFluxMax = Effects.FirstOrDefault(ef => ef.Name == "WaterFluxMaxLevel");

                    if (waterFluxMin is not null && waterFluxMax is not null)
                    {
                        float waterMid = 1 - (waterFluxMax.Amount + waterFluxMin.Amount) / 2 * (22f / 20f);
                        waterLevel = (int)(waterMid * TileSize.Y) + 2;
                    }
                }

                for (int j = 0; j < TileSize.Y; j++)
                    for (int i = 0; i < TileSize.X; i++)
                    {
                        if (!InterfaceState.DisableRoomCropping.Value && CutOutSolidTiles is not null && CutOutSolidTiles[i, j])
                        {
                            colors[i + j * TileSize.X] = Color.Transparent;
                            continue;
                        }

                        Tile tile = GetTile(i, j);

                        float gray = 1;

                        bool solid = tile.Terrain == Tile.TerrainType.Solid;

                        if (solid)
                            gray = 0;

                        else if (tile.Terrain == Tile.TerrainType.Floor)
                            gray = 0.35f;

                        else if (tile.Terrain == Tile.TerrainType.Slope)
                            gray = .4f;

                        else if (InterfaceState.DrawTileWalls.Value && tile.Attributes.HasFlag(Tile.TileAttributes.WallBehind))
                            gray = 0.75f;

                        if (InterfaceState.RegionBGShortcuts.Value && tile.Terrain == Tile.TerrainType.ShortcutEntrance)
                            gray = 1;

                        else if (tile.Attributes.HasFlag(Tile.TileAttributes.VerticalBeam) || tile.Attributes.HasFlag(Tile.TileAttributes.HorizontalBeam))
                            gray = 0.35f;

                        Color color = Color.Lerp(Color.Black, subregion.BackgroundColor.Color, gray);

                        if (!solid && (invertedWater ? j <= waterLevel : j >= TileSize.Y - waterLevel))
                        {
                            color = Color.Lerp(waterColor, color, InterfaceState.WaterTransparency.Value);
                        }

                        if (Deathpit.Value && j >= TileSize.Y - 5 && Tiles[i, TileSize.Y - 1].Terrain == Tile.TerrainType.Air)
                            color = Color.Lerp(Color.Black, color, (TileSize.Y - j - .5f) / 5f);

                        colors[i + j * TileSize.X] = color;
                    }

                if (InterfaceState.MarkShortcuts.Value)
                    foreach (Shortcut shortcut in Shortcuts)
                        if ((!InterfaceState.MarkExitsOnly.Value || shortcut.Type == Tile.ShortcutType.RoomExit) && shortcut.Type != Tile.ShortcutType.None)
                            colors[shortcut.Entrance.X + shortcut.Entrance.Y * TileSize.X] = new(255, 0, 0);


                TileMap ??= new(Main.Instance.GraphicsDevice, TileSize.X, TileSize.Y);
                TileMap.SetData(colors, 0, TileSize.X * TileSize.Y);
            }
            finally
            {
                ArrayPool<Color>.Shared.Return(colors);
            }
            TileMapDirty = false;
        }

        void ProcessCutouts()
        {
            CutOutSolidTiles = new bool[TileSize.X, TileSize.Y];

            if (CutoutAllSolidTiles.Value)
            {
                for (int j = 0; j < TileSize.Y; j++)
                    for (int i = 0; i < TileSize.X; i++)
                        if (Tiles[i, j].Terrain == Tile.TerrainType.Solid)
                            CutOutSolidTiles[i, j] = true;

                return;
            }

            Queue<Point> queue = new();
            bool[,] noCutTiles = new bool[TileSize.X, TileSize.Y];

            for (int i = 0; i < TileSize.X - 1; i++)
                queue.Enqueue(new(i, 0));

            for (int i = 1; i < TileSize.Y; i++)
                queue.Enqueue(new(0, i));

            for (int i = 0; i < TileSize.Y - 1; i++)
                queue.Enqueue(new(TileSize.X - 1, i));

            for (int i = 1; i < TileSize.X; i++)
                queue.Enqueue(new(i, TileSize.Y - 1));

            int GetTileNeighbors(int x, int y)
            {
                int neighbors = 0;

                if (x <= 0 || Tiles[x - 1, y].Terrain == Tile.TerrainType.Solid) neighbors += 1;
                if (x >= TileSize.X - 1 || Tiles[x + 1, y].Terrain == Tile.TerrainType.Solid) neighbors += 1;
                if (y <= 0 || Tiles[x, y - 1].Terrain == Tile.TerrainType.Solid) neighbors += 1;
                if (y >= TileSize.Y - 1 || Tiles[x, y + 1].Terrain == Tile.TerrainType.Solid) neighbors += 1;

                return neighbors;
            }

            bool IsTileOOB(int x, int y)
            {
                return x < 0 || y < 0 || x >= TileSize.X || y >= TileSize.Y;
            }

            bool Check2TileCutout(int x, int y)
            {
                bool cutout = !IsTileOOB(x, y) && Tiles[x, y].Terrain != Tile.TerrainType.Solid && GetTileNeighbors(x, y) == 3;
                if (cutout)
                    CutOutSolidTiles[x, y] = true;
                return cutout;
            }

            void ClearNoCutout(int x, int y)
            {
                for (int i = 0; i < TileSize.X; i++)
                    noCutTiles[i, y] = false;

                for (int i = 0; i < TileSize.Y; i++)
                    noCutTiles[x, i] = false;
            }

            bool CutTile(int x, int y)
            {
                if (IsTileOOB(x, y) || CutOutSolidTiles[x, y] || noCutTiles[x, y])
                    return false;

                if (UseBetterTileCutout.Value)
                {
                    int searchDist = 20;
                    int searchMaxDist = 30;

                    for (int i = x - 1; i >= Math.Max(x - searchMaxDist, 0); i--)
                    {
                        bool outOfRangeLeft = i < x - searchDist;

                        if (CutOutSolidTiles[i, y])
                            break;

                        if (Tiles[i, y].Terrain == Tile.TerrainType.Solid)
                            continue;

                        for (i = x + 1; i < Math.Min(x + searchMaxDist, TileSize.X); i++)
                        {
                            bool outOfRangeRight = i > x + searchDist;

                            if (CutOutSolidTiles[i, y])
                                break;

                            if (Tiles[i, y].Terrain == Tile.TerrainType.Solid)
                                continue;

                            if (outOfRangeLeft && outOfRangeRight)
                                break;

                            noCutTiles[x, y] = true;
                            return false;
                        }
                        break;
                    }

                    for (int i = y - 1; i >= Math.Max(y - searchMaxDist, 0); i--)
                    {
                        bool outOfRangeLeft = i < y - searchDist;

                        if (CutOutSolidTiles[x, i])
                            break;

                        if (Tiles[x, i].Terrain == Tile.TerrainType.Solid)
                            continue;

                        for (i = y + 1; i < Math.Min(y + searchMaxDist, TileSize.Y); i++)
                        {
                            bool outOfRangeRight = i > y + searchDist;

                            if (CutOutSolidTiles[x, i])
                                break;

                            if (Tiles[x, i].Terrain == Tile.TerrainType.Solid)
                                continue;

                            if (outOfRangeLeft && outOfRangeRight)
                                break;

                            noCutTiles[x, y] = true;
                            return false;
                        }
                        break;
                    }
                }
                CutOutSolidTiles[x, y] = true;
                ClearNoCutout(x, y);
                return true;
            }

            while (queue.TryDequeue(out Point point))
            {
                if (CutOutSolidTiles[point.X, point.Y] || noCutTiles[point.X, point.Y])
                    continue;

                if (Tiles[point.X, point.Y].Terrain != Tile.TerrainType.Solid)
                {
                    int neighbors = GetTileNeighbors(point.X, point.Y);

                    if (neighbors == 4)
                        CutOutSolidTiles[point.X, point.Y] = true;
                    if (neighbors == 3)
                    {
                        if (Check2TileCutout(point.X - 1, point.Y)
                         || Check2TileCutout(point.X + 1, point.Y)
                         || Check2TileCutout(point.X, point.Y - 1)
                         || Check2TileCutout(point.X, point.Y + 1))
                            CutOutSolidTiles[point.X, point.Y] = true;
                    }

                    continue;
                }

                if (!CutTile(point.X, point.Y))
                    continue;

                if (point.X > 0)
                    queue.Enqueue(new(point.X - 1, point.Y));

                if (point.X < TileSize.X - 1)
                    queue.Enqueue(new(point.X + 1, point.Y));

                if (point.Y > 0)
                    queue.Enqueue(new(point.X, point.Y - 1));

                if (point.Y < TileSize.Y - 1)
                    queue.Enqueue(new(point.X, point.Y + 1));
            }

            CutOutsDirty = false;
        }

        void FixGateData()
        {
            if (GateData is null)
                return;

            bool? leftConnection = null;

            foreach (Connection? connection in Connections)
            {
                if (connection is null || connection.Target.IsShelter)
                    continue;

                leftConnection = Exits[connection.Exit].X < TileSize.X / 2;
            }

            if (leftConnection is null)
                return;

            string? otherRegion = GateData.LeftRegionId == Region.Id ? GateData.RightRegionId : GateData.LeftRegionId;
            if (otherRegion is null)
                return;

            if (leftConnection.Value)
            {
                GateData.LeftRegionId = Region.Id;
                GateData.RightRegionId = otherRegion;
            }
            else
            {
                GateData.RightRegionId = Region.Id;
                GateData.LeftRegionId = otherRegion;
            }
        }

        public void PostRegionLoad()
        {
            if (IsShelter && Connections.Length == 1 && Connections[0] is not null)
                BoundRoom = Connections[0]!.Target;

            if (GateData is not null && IsGate)
            {
                ColorRef? leftColor = null;
                ColorRef? rightColor = null;
                ColorRef? regionColor = null;

                if (GateData.LeftRegionId is not null && GateData.RightRegionId is not null
                 && (GateData.LeftRegionId.Equals(Region.Id, StringComparison.InvariantCultureIgnoreCase)
                  || GateData.RightRegionId.Equals(Region.Id, StringComparison.InvariantCultureIgnoreCase)))
                    FixGateData();

                if (GateData.LeftRegionId is not null)
                    leftColor = ColorDatabase.GetRegionColor(GateData.LeftRegionId, null);

                if (GateData.RightRegionId is not null)
                    rightColor = ColorDatabase.GetRegionColor(GateData.RightRegionId, null);

                string? targetRegion = null;

                if (GateData.LeftRegionId is not null && !GateData.LeftRegionId.Equals(Region.Id, StringComparison.InvariantCultureIgnoreCase))
                    targetRegion = GateData.LeftRegionId;

                else if (GateData.RightRegionId is not null && !GateData.RightRegionId.Equals(Region.Id, StringComparison.InvariantCultureIgnoreCase))
                    targetRegion = GateData.RightRegionId;

                if (targetRegion is not null)
                    regionColor = ColorDatabase.GetRegionColor(targetRegion, null);

                leftColor ??= ColorRef.White;
                rightColor ??= ColorRef.White;
                regionColor ??= ColorRef.White;

                if (GateSymbols is not null)
                {
                    GateSymbols.LeftArrowColor.OriginalValue = leftColor;
                    GateSymbols.RightArrowColor.OriginalValue = rightColor;
                }

                if (GateRegionText is not null)
                {
                    if (GateData.TargetRegionName is null)
                    {
                        GateRegionText.Parent = null;
                    }
                    else
                    {
                        GateRegionText.Text.OriginalValue = $"To [c:{regionColor.GetKeyOrColorString()}]{GateData.TargetRegionName}[/c]";
                        GateRegionText.ParamsChanged();
                    }
                }
            }
        }

        protected override void DrawSelf(Renderer renderer)
        {
            if (!Loaded)
                return;

            if (CutOutsDirty)
                ProcessCutouts();

            renderer.DrawTexture(GetTileMap(), WorldPosition);

            if (base.Name is not null)
                Main.SpriteBatch.DrawStringAligned(Content.Consolas10, base.Name, renderer.TransformVector(WorldPosition + new Vector2(TileSize.X / 2, .5f)), Color.Yellow, new(.5f, 0), Color.Black);
        }
        protected override void BuildInnerConfig(UIList list)
        {
            if (Region is not null)
            {
                list.Elements.Add(new UIResizeablePanel
                {
                    Height = 100,

                    Padding = 4,

                    CanGrabTop = false,
                    CanGrabLeft = false,
                    CanGrabRight = false,
                    CanGrabBottom = true,

                    Elements =
                    {
                        new UILabel
                        {
                            Text = "Subregion",
                            Height = 15,
                            TextAlign = new(.5f)
                        },
                        new UIList
                        {
                            Top = 20,
                            Height = new(-20, 1),
                            ElementSpacing = 4
                        }.Assign(out UIList subregionList)
                    }
                });

                RadioButtonGroup group = new();

                for (int i = 0; i < Region.Subregions.Length; i++)
                {
                    Subregion subregion = Region.Subregions[i];
                    UIButton button = new()
                    {
                        Text = subregion.Name.Length == 0 ? "Main region" : subregion.Name,
                        Height = 20,
                        TextAlign = new(.5f),
                        RadioGroup = group,
                        Selectable = true,
                        Selected = subregion == Subregion.Value,
                        RadioTag = subregion,
                        SelectedTextColor = Color.Black,
                        SelectedBackColor = Color.White,
                    };

                    subregionList.Elements.Add(button);
                }

                group.ButtonClicked += (_, tag) =>
                {
                    if (tag is not Subregion subregion)
                        return;
                    Subregion.Value = subregion;
                    TileMapDirty = true;
                };
            }

            list.Elements.Add(new UIButton
            {
                Height = 20,

                Selectable = true,
                Selected = Deathpit.Value,
                Text = "Deathpit",

                SelectedBackColor = Color.White,
                SelectedTextColor = Color.Black,

                TextAlign = new(.5f)

            }.OnEvent(UIElement.ClickEvent, (btn, _) =>
            {
                Deathpit.Value = btn.Selected;
                TileMapDirty = true;
            }));

            list.Elements.Add(new UIButton
            {
                Height = 20,

                Selectable = true,
                Selected = UseBetterTileCutout.Value,
                Text = "Better tile cutouts",

                SelectedBackColor = Color.White,
                SelectedTextColor = Color.Black,

                TextAlign = new(.5f)

            }.OnEvent(UIElement.ClickEvent, (btn, _) =>
            {
                UseBetterTileCutout.Value = btn.Selected;

                CutOutsDirty = true;
                TileMapDirty = true;
                ShadeTextureDirty = true;
            }));

            list.Elements.Add(new UIButton
            {
                Height = 20,

                Selectable = true,
                Selected = CutoutAllSolidTiles.Value,
                Text = "Cut all solid tiles",

                SelectedBackColor = Color.White,
                SelectedTextColor = Color.Black,

                TextAlign = new(.5f)

            }.OnEvent(UIElement.ClickEvent, (btn, _) =>
            {
                CutoutAllSolidTiles.Value = btn.Selected;

                CutOutsDirty = true;
                TileMapDirty = true;
                ShadeTextureDirty = true;
            }));

            list.Elements.Add(new UIButton
            {
                Height = 20,

                Selectable = true,
                Selected = DrawInRoomShortcuts.Value,
                Text = "Draw in-room shortcuts",

                SelectedBackColor = Color.White,
                SelectedTextColor = Color.Black,

                TextAlign = new(.5f)

            }.OnEvent(UIElement.ClickEvent, (btn, _) => DrawInRoomShortcuts.Value = btn.Selected));

            list.Elements.Add(new UIButton
            {
                Height = 20,

                Selectable = true,
                Selected = AcidWater.Value,
                Text = "Acid water",

                SelectedBackColor = Color.White,
                SelectedTextColor = Color.Black,

                TextAlign = new(.5f)

            }.OnEvent(UIElement.ClickEvent, (btn, _) =>
            {
                AcidWater.Value = btn.Selected;
                TileMapDirty = true;
            }));

            list.Elements.Add(new UIButton
            {
                Height = 20,

                Text = "Set acid color",
                TextAlign = new(.5f)

            }.OnEvent(UIElement.ClickEvent, (btn, _) => Interface.ColorSelector.Show("Acid color", AcidColor.Value, (_, c) =>
            {
                AcidColor.Value = c;
                Region?.MarkRoomTilemapsDirty();
            })));

            list.Elements.Add(new UIPanel
            {
                Height = 27,
                Padding = 4,

                Elements =
                {
                    new UILabel
                    {
                        Top = 3,
                        Width = 90,
                        Height = 20,
                        Text = "Water level:",
                        WordWrap = false,
                    },
                    new UINumberInput
                    {
                        Width = new(-90, 1),
                        Left = new(0, 1, -1),
                        Value = WaterLevel.Value,
                        AllowDecimal = false,
                        AllowNegative = true,

                    }.OnEvent(UINumberInput.ValueChanged, (inp, _) => { WaterLevel.Value = (int)inp.Value; TileMapDirty = true; }),
                }
            });
        }

        protected override JsonNode? SaveInnerJson(bool forCopy)
        {
            JsonObject obj = new JsonObject()
            .SaveProperty(Deathpit)
            .SaveProperty(Subregion)
            .SaveProperty(UseBetterTileCutout)
            .SaveProperty(CutoutAllSolidTiles)
            .SaveProperty(WaterLevel)
            .SaveProperty(DrawInRoomShortcuts);

            if (IsGate && GateData is not null)
                obj["gateData"] = GateData.SaveJson();

            return obj;
        }
        protected override void LoadInnerJson(JsonNode node, bool shallow)
        {
            Subregion.LoadFromJson(node);
            Deathpit.LoadFromJson(node);
            UseBetterTileCutout.LoadFromJson(node);
            CutoutAllSolidTiles.LoadFromJson(node);
            WaterLevel.LoadFromJson(node);
            DrawInRoomShortcuts.LoadFromJson(node);

            if (!shallow && node.TryGet("gateData", out JsonObject? gateData))
            {
                GateData ??= new();
                GateData.LoadJson(gateData);

                // Set non-colored text to properly align it before loading new position
                if (GateData.TargetRegionName is not null && GateRegionText is not null)
                {
                    GateRegionText.Text.OriginalValue = $"To {GateData.TargetRegionName}";
                    GateRegionText.ParamsChanged();
                }
            }

            TileMapDirty = true;
        }

        public override string ToString()
        {
            return Name!;
        }

        public record class Connection(Room Target, int Exit, int TargetExit)
        {
            public override string ToString()
            {
                return $"{Exit} -> {Target.Name}[{TargetExit}]";
            }
        }
        public record class Shortcut(Point Entrance, Point Target, Tile.ShortcutType Type);
        public record class Effect(string Name, float Amount);

        public struct Tile
        {
            public TerrainType Terrain;
            public ShortcutType Shortcut;
            public TileAttributes Attributes;

            [Flags]
            public enum TileAttributes
            {
                None = 0,
                VerticalBeam = 1,
                HorizontalBeam = 2,
                WallBehind = 4,
                Hive = 8,
                Waterfall = 16,
                GarbageHole = 32,
                WormGrass = 64
            }

            public enum TerrainType
            {
                Air,
                Solid,
                Slope,
                Floor,
                ShortcutEntrance
            }

            public enum ShortcutType
            {
                None,
                Normal,
                RoomExit,
                CreatureHole,
                NPCTransportation,
                RegionTransportation,
            }
        }
    }
}