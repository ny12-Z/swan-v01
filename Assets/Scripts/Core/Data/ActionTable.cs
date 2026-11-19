using System;
using System.Collections.Generic;

namespace Swan.Core.Data
{
    /// <summary>
    /// 动作表的一行。power 的语义由动作 id 决定（挖掘=扣除量，浇水=增加量），
    /// 具体数值全部在表里，代码只负责执行。
    /// </summary>
    public sealed class ActionRow
    {
        public string ActionId;
        public string Name;
        public string Hotkey;
        public string SelectsCategory;
        public string CostItem;
        public string GainItem;
        public float Power;
        public float Power2;
        public int Power3;
        public string Description;
        public int SourceLine;
        public string FileName;

        public bool SelectsItem => !string.IsNullOrEmpty(SelectsCategory);

        public override string ToString()
        {
            return $"{ActionId}:{Name}";
        }
    }

    public sealed class ActionTable
    {
        public const string FileName = "数值表(动作表)-机读.csv";

        readonly List<ActionRow> _rows;
        readonly Dictionary<string, ActionRow> _byId;

        public IReadOnlyList<ActionRow> Rows => _rows;

        ActionTable(List<ActionRow> rows)
        {
            _rows = rows;
            _byId = new Dictionary<string, ActionRow>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++) _byId[rows[i].ActionId] = rows[i];
        }

        public int IndexOf(string actionId)
        {
            for (int i = 0; i < _rows.Count; i++) if (_rows[i].ActionId == actionId) return i;
            return -1;
        }

        public bool TryGet(string actionId, out ActionRow row)
        {
            row = null;
            return !string.IsNullOrEmpty(actionId) && _byId.TryGetValue(actionId, out row);
        }

        public ActionRow Get(string actionId)
        {
            if (TryGet(actionId, out var row)) return row;
            throw new DataLoadException(FileName, 0, "actionId", $"查不到动作「{actionId}」");
        }

        public static ActionTable Load(CsvDocument doc, ItemTable items, DataWarnings warnings)
        {
            var rows = new List<ActionRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in doc.Rows)
            {
                var actionId = row.Require("actionId");
                if (!seen.Add(actionId)) throw row.Error("actionId", $"主键重复：{actionId}");

                var action = new ActionRow
                {
                    ActionId = actionId,
                    Name = row.Require("name"),
                    Hotkey = row.Require("hotkey"),
                    SelectsCategory = row.Raw("selectsCategory", string.Empty),
                    CostItem = row.Raw("costItem", string.Empty),
                    GainItem = row.Raw("gainItem", string.Empty),
                    Power = row.Float("power"),
                    Power2 = row.Float("power2"),
                    Power3 = row.Int("power3"),
                    Description = row.Raw("description", string.Empty),
                    SourceLine = row.Line,
                    FileName = doc.FileName
                };

                if (!string.IsNullOrEmpty(action.CostItem) && !items.Contains(action.CostItem))
                {
                    throw row.Error("costItem", $"外键失效：物品表里没有「{action.CostItem}」");
                }
                if (!string.IsNullOrEmpty(action.GainItem) && !items.Contains(action.GainItem))
                {
                    throw row.Error("gainItem", $"外键失效：物品表里没有「{action.GainItem}」");
                }
                if (action.SelectsItem && action.SelectsCategory != "seed" && action.SelectsCategory != "placeable")
                {
                    throw row.Error("selectsCategory", "只能是 seed 或 placeable（留空表示不需要选物品）");
                }
                if (action.Power < 0f || action.Power2 < 0f)
                {
                    throw row.Error("power", "必须是正数（挖掘的扣除方向由动作 id 决定）");
                }
                if (action.Power3 < 0) throw row.Error("power3", "不能是负数");
                rows.Add(action);
            }

            if (rows.Count == 0) throw new DataLoadException(doc.FileName, 1, null, "动作表是空的");
            return new ActionTable(rows);
        }
    }
}
