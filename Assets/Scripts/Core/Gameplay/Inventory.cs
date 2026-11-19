using System;
using System.Collections.Generic;
using Swan.Core.Data;

namespace Swan.Core.Gameplay
{
    /// <summary>背包（纯 C#）。物品 id 对应物品表；数量只在这里改，World 不知道背包存在。</summary>
    public sealed class Inventory
    {
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Count(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return 0;
            return _counts.TryGetValue(itemId, out var count) ? count : 0;
        }

        public bool Has(string itemId, int amount = 1) => amount <= 0 || Count(itemId) >= amount;

        public void Add(string itemId, int amount)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0) return;
            _counts[itemId] = Count(itemId) + amount;
        }

        public bool Remove(string itemId, int amount = 1)
        {
            if (string.IsNullOrEmpty(itemId) || amount <= 0) return true;
            int have = Count(itemId);
            if (have < amount) return false;
            if (have == amount) _counts.Remove(itemId);
            else _counts[itemId] = have - amount;
            return true;
        }

        public void Clear() => _counts.Clear();

        /// <summary>按物品表顺序返回非零条目，HUD 显示用。</summary>
        public List<KeyValuePair<string, int>> Snapshot(ItemTable items)
        {
            var list = new List<KeyValuePair<string, int>>();
            if (items == null)
            {
                foreach (var pair in _counts) list.Add(pair);
                return list;
            }

            for (int i = 0; i < items.Rows.Count; i++)
            {
                var row = items.Rows[i];
                int count = Count(row.ItemId);
                if (count > 0) list.Add(new KeyValuePair<string, int>(row.ItemId, count));
            }
            return list;
        }

        public void GrantStartingItems(ItemTable items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Rows.Count; i++)
            {
                var row = items.Rows[i];
                if (row.StartingCount > 0) Add(row.ItemId, row.StartingCount);
            }
        }
    }
}
