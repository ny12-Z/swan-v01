using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    /// <summary>
    /// 物品表的一行。物品是背包里能堆叠的东西；种子和放置物通过外键指向植物/结构表。
    /// 所有数值都来自 数值表(物品表)-机读.csv，代码里不写死任何物品数量。
    /// </summary>
    public sealed class ItemRow
    {
        public string ItemId;
        public string Name;
        public string Category;
        public string IconFile;
        public int SrcX;
        public int SrcY;
        public int SrcW;
        public int SrcH;
        public int StackMax;
        public int PlaceStructureId;
        public string SeedPlantId;
        public int StartingCount;
        public string Description;
        public int SourceLine;
        public string FileName;

        public bool IsSeed => !string.IsNullOrEmpty(SeedPlantId);
        public bool IsPlaceable => PlaceStructureId != 0;

        public override string ToString()
        {
            return $"{ItemId}:{Name}";
        }
    }

    /// <summary>
    /// 物品表。背包初始物品由 startingCount 决定；种子/放置物通过外键连到植物/结构表。
    /// </summary>
    public sealed class ItemTable
    {
        public const string FileName = "数值表(物品表)-机读.csv";

        readonly List<ItemRow> _rows;
        readonly Dictionary<string, ItemRow> _byId;

        public IReadOnlyList<ItemRow> Rows => _rows;

        ItemTable(List<ItemRow> rows)
        {
            _rows = rows;
            _byId = new Dictionary<string, ItemRow>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++) _byId[rows[i].ItemId] = rows[i];
        }

        public bool TryGet(string itemId, out ItemRow row)
        {
            row = null;
            return !string.IsNullOrEmpty(itemId) && _byId.TryGetValue(itemId, out row);
        }

        /// <summary>
        /// 按 itemId 或显示名解析。数值表里有些产量列写的是中文名（例：植物表的 yieldItem=食物纤维），
        /// 这里统一翻译成物品主键，避免「表里写着名字、背包里查不到」。
        /// </summary>
        public bool TryResolve(string itemIdOrName, out ItemRow row)
        {
            if (TryGet(itemIdOrName, out row)) return true;
            if (string.IsNullOrEmpty(itemIdOrName)) return false;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Name, itemIdOrName, StringComparison.Ordinal))
                {
                    row = _rows[i];
                    return true;
                }
            }
            row = null;
            return false;
        }

        public ItemRow Get(string itemId)
        {
            if (TryGet(itemId, out var row)) return row;
            throw new DataLoadException(FileName, 0, "itemId", $"查不到物品「{itemId}」");
        }

        public bool Contains(string itemId) => !string.IsNullOrEmpty(itemId) && _byId.ContainsKey(itemId);

        public List<ItemRow> OfCategory(string category)
        {
            var list = new List<ItemRow>();
            for (int i = 0; i < _rows.Count; i++)
            {
                if (string.Equals(_rows[i].Category, category, StringComparison.Ordinal)) list.Add(_rows[i]);
            }
            return list;
        }

        public ItemRow FirstSeed()
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].IsSeed) return _rows[i];
            return null;
        }

        public ItemRow FirstPlaceable()
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].IsPlaceable) return _rows[i];
            return null;
        }

        public ItemRow SeedForPlant(string plantTypeId)
        {
            if (string.IsNullOrEmpty(plantTypeId)) return null;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].IsSeed && _rows[i].SeedPlantId == plantTypeId) return _rows[i];
            }
            return null;
        }

        public static ItemTable Load(CsvDocument doc, StructureTable structures, PlantTypeTable plants, DataWarnings warnings)
        {
            var rows = new List<ItemRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in doc.Rows)
            {
                var itemId = row.Require("itemId");
                if (!seen.Add(itemId)) throw row.Error("itemId", $"主键重复：{itemId}");

                var item = new ItemRow
                {
                    ItemId = itemId,
                    Name = row.Require("name"),
                    Category = row.Require("category"),
                    IconFile = row.Require("iconFile"),
                    SrcX = row.Int("srcX"),
                    SrcY = row.Int("srcY"),
                    SrcW = row.Int("srcW"),
                    SrcH = row.Int("srcH"),
                    StackMax = row.Int("stackMax"),
                    PlaceStructureId = row.Int("placeStructureId"),
                    SeedPlantId = row.Raw("seedPlantId", string.Empty),
                    StartingCount = row.Int("startingCount"),
                    Description = row.Raw("description", string.Empty),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                };

                if (item.StackMax < 1) throw row.Error("stackMax", "至少是 1");
                if (item.StartingCount < 0) throw row.Error("startingCount", "不能是负数");
                if (item.SrcW < 1 || item.SrcH < 1) throw row.Error("srcW", "图标尺寸必须 ≥ 1");
                if (item.PlaceStructureId != 0 && !structures.TryGetRow(item.PlaceStructureId, out _))
                {
                    throw row.Error("placeStructureId", $"外键失效：结构表里没有 {item.PlaceStructureId}");
                }
                if (!string.IsNullOrEmpty(item.SeedPlantId) && !plants.TryGet(item.SeedPlantId, out _))
                {
                    throw row.Error("seedPlantId", $"外键失效：植物表里没有「{item.SeedPlantId}」");
                }
                if (item.IsSeed && !item.IsPlaceable)
                {
                    // 种子必须能指向一种可种植的植物
                    var plant = plants.Get(item.SeedPlantId);
                    if (!plant.PlantableInV01 && warnings != null)
                    {
                        warnings.Add($"{doc.FileName}:{row.Line} 种子 {itemId} 指向的 {plant.TypeId} 在 v0.1 不开放种植");
                    }
                }

                rows.Add(item);
            }

            if (rows.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "物品表是空的");
            return new ItemTable(rows);
        }
    }
}
