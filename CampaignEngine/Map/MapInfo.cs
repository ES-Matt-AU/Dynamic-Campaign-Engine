using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CampaignEngine.Core;

namespace CampaignEngine.Map
{
    public class MapAirbase
    {
        /// <summary>The static prefab id in the .vtm file. Scenario files refer to bases by this.</summary>
        public int PrefabId;
        public string Prefab;
        public Vec2 Position;
        public double Elevation;
        public double Heading;
    }

    /// <summary>The parts of a VTOL VR custom map (.vtm) the campaign needs.</summary>
    public class MapInfo
    {
        /// <summary>VTOL VR terrain chunk size in metres. The .vtm "grid" values are position / 3072.</summary>
        public const double ChunkSizeMetres = 3072;

        public string MapId;
        public string FilePath;
        public int SizeChunks;
        public List<MapAirbase> Airbases = new List<MapAirbase>();

        public double SizeMetres() => SizeChunks * ChunkSizeMetres;

        public static MapInfo Load(string vtmPath)
        {
            var root = ConfigNode.Parse(File.ReadAllText(vtmPath));
            var mapNode = root.GetNode("VTMapCustom") ?? throw new InvalidDataException("Not a VTOL VR custom map file (no VTMapCustom node).");

            var map = new MapInfo
            {
                FilePath = vtmPath,
                MapId = mapNode.GetValue("mapID") ?? Path.GetFileNameWithoutExtension(vtmPath),
                SizeChunks = int.Parse(mapNode.GetValue("mapSize") ?? "0", CultureInfo.InvariantCulture)
            };

            var prefabs = mapNode.GetNode("StaticPrefabs");
            if (prefabs != null)
            {
                foreach (var prefab in prefabs.GetNodes("StaticPrefab"))
                {
                    string name = prefab.GetValue("prefab") ?? "";
                    if (!name.StartsWith("airbase", StringComparison.OrdinalIgnoreCase))
                        continue;

                    double[] pos = ParseVector(prefab.GetValue("globalPos"));
                    double[] rot = ParseVector(prefab.GetValue("rotation"));
                    map.Airbases.Add(new MapAirbase
                    {
                        PrefabId = int.Parse(prefab.GetValue("id") ?? "-1", CultureInfo.InvariantCulture),
                        Prefab = name,
                        Position = new Vec2(pos[0], pos[2]),
                        Elevation = pos[1],
                        Heading = rot[1]
                    });
                }
            }

            if (map.SizeChunks <= 0)
                throw new InvalidDataException("Map file has no valid mapSize.");

            return map;
        }

        /// <summary>Parses "(x, y, z)".</summary>
        private static double[] ParseVector(string text)
        {
            var result = new double[3];
            if (string.IsNullOrEmpty(text))
                return result;

            string[] parts = text.Trim('(', ')', ' ').Split(',');
            for (int i = 0; i < parts.Length && i < 3; i++)
                result[i] = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
            return result;
        }
    }
}
