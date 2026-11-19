using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    /// <summary>某个地形上会生成什么野物、生成概率多大。</summary>
    public sealed class ForageRow
    {
        public int TerrainId;
        public string ItemId;
        public float Chance;
        public int SourceLine;
        public string FileName;

        public override string ToString()
        {
            return $"terrain {TerrainId} -> {ItemId} ({Chance:0.###})";
        }
    }

    public sealed class ForageTable
    {
        public const string FileName = "数值表(采集物表)-机读.csv";

        readonly List<ForageRow> _rows;
        readonly Dictionary<int, ForageRow> _byTerrain;

        public IReadOnlyList<ForageRow> Rows => _rows;

        ForageTable(List<ForageRow> rows)
        {
            _rows = rows;
            _byTerrain = new Dictionary<int, ForageRow>();
            for (int i = 0; i < rows.Count; i++) _byTerrain[rows[i].TerrainId] = rows[i];
        }

        public bool TryGetForTerrain(int terrainId, out ForageRow row)
        {
            row = null;
            return _byTerrain.TryGetValue(terrainId, out row);
        }

        public static ForageTable Load(CsvDocument doc, TerrainTable terrain, ItemTable items, DataWarnings warnings)
        {
            var rows = new List<ForageRow>();
            var seen = new HashSet<int>();

            foreach (var row in doc.Rows)
            {
                int terrainId = row.Int("terrainId");
                if (!seen.Add(terrainId)) throw row.Error("terrainId", $"同一地形只能配一种采集物，重复：{terrainId}");
                if (!terrain.TryGetById(terrainId, out _))
                {
                    throw row.Error("terrainId", $"外键失效：地形表里没有 {terrainId}");
                }

                var itemId = row.Require("itemId");
                if (!items.Contains(itemId)) throw row.Error("itemId", $"外键失效：物品表里没有「{itemId}」");

                float chance = row.Float("chance");
                if (chance < 0f || chance > 1f) throw row.Error("chance", "必须在 0..1 之间");

                rows.Add(new ForageRow
                {
                    TerrainId = terrainId,
                    ItemId = itemId,
                    Chance = chance,
                    SourceLine = row.Line,
                    FileName = doc.FileName
                });
            }

            if (rows.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "采集物表是空的");
            return new ForageTable(rows);
        }
    }
}
