using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    /// <summary>放置物（结构）表的一行：栅栏、木箱、火把、小路。</summary>
    public sealed class StructureRow
    {
        public int StructureId;
        public string Name;
        public bool BlocksMovement;
        public bool BlocksPlanting;
        public string SpriteFile;
        public int SrcX;
        public int SrcY;
        public int SrcW;
        public int SrcH;
        public string TintHex;
        public string ItemId;
        public string Description;
        public int SourceLine;
        public string FileName;

        public override string ToString()
        {
            return $"{StructureId}:{Name}";
        }
    }

    public sealed class StructureTable
    {
        public const string FileName = "数值表(结构表)-机读.csv";

        readonly List<StructureRow> _rows;
        readonly Dictionary<int, StructureRow> _byId;

        public IReadOnlyList<StructureRow> Rows => _rows;

        StructureTable(List<StructureRow> rows)
        {
            _rows = rows;
            _byId = new Dictionary<int, StructureRow>();
            for (int i = 0; i < rows.Count; i++) _byId[rows[i].StructureId] = rows[i];
        }

        public bool TryGetRow(int structureId, out StructureRow row)
        {
            row = null;
            return structureId != 0 && _byId.TryGetValue(structureId, out row);
        }

        public StructureRow Get(int structureId)
        {
            if (TryGetRow(structureId, out var row)) return row;
            throw new DataLoadException(FileName, 0, "structureId", $"查不到结构 {structureId}");
        }

        public static StructureTable Load(CsvDocument doc, DataWarnings warnings)
        {
            var rows = new List<StructureRow>();
            var seen = new HashSet<int>();

            foreach (var row in doc.Rows)
            {
                int id = row.Int("structureId");
                if (id <= 0) throw row.Error("structureId", "必须 > 0（0 留给空地）");
                if (!seen.Add(id)) throw row.Error("structureId", $"主键重复：{id}");

                var structure = new StructureRow
                {
                    StructureId = id,
                    Name = row.Require("name"),
                    BlocksMovement = row.Bool("blocksMovement"),
                    BlocksPlanting = row.Bool("blocksPlanting"),
                    SpriteFile = row.Require("spriteFile"),
                    SrcX = row.Int("srcX"),
                    SrcY = row.Int("srcY"),
                    SrcW = row.Int("srcW"),
                    SrcH = row.Int("srcH"),
                    TintHex = row.Raw("tint", "#FFFFFF"),
                    ItemId = row.Raw("itemId", string.Empty),
                    Description = row.Raw("description", string.Empty),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                };

                if (structure.SrcW < 1 || structure.SrcH < 1) throw row.Error("srcW", "图片尺寸必须 ≥ 1");
                ColorHex.ParseRequired(structure.TintHex, doc.FileName, row.Line, "tint");
                rows.Add(structure);
            }

            if (rows.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "结构表是空的");
            return new StructureTable(rows);
        }

        /// <summary>等物品表加载完之后做双向外键校验（物品 → 结构、结构 → 物品）。</summary>
        public void ValidateItems(ItemTable items)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (string.IsNullOrEmpty(row.ItemId)) continue;
                if (!items.Contains(row.ItemId))
                {
                    throw new DataLoadException(FileName, row.SourceLine, "itemId",
                        $"外键失效：物品表里没有「{row.ItemId}」");
                }
            }
        }
    }
}
