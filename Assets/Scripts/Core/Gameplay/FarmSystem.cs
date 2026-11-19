using System;
using System.Collections.Generic;
using Swan.Core.Data;
using Swan.Core.Sim;

namespace Swan.Core.Gameplay
{
    /// <summary>一次交互的结果。表现层只读这个结构，不直接改世界。</summary>
    public struct InteractionResult
    {
        public bool Success;
        public string ActionId;
        public string Message;
        public string ItemId;
        public int ItemCount;
        public int X;
        public int Y;

        public static InteractionResult Fail(string actionId, int x, int y, string message)
        {
            return new InteractionResult
            {
                Success = false,
                ActionId = actionId,
                Message = message,
                X = x,
                Y = y
            };
        }

        public static InteractionResult Ok(string actionId, int x, int y, string message, string itemId, int itemCount)
        {
            return new InteractionResult
            {
                Success = true,
                ActionId = actionId,
                Message = message,
                ItemId = itemId,
                ItemCount = itemCount,
                X = x,
                Y = y
            };
        }
    }

    /// <summary>
    /// 动作系统：把「玩家 + 12×12 范围 + 背包 + 世界」接在一起。
    /// 纯 C#，不依赖引擎；动作数值来自动作表，物品来自物品表。
    /// </summary>
    public sealed class FarmSystem
    {
        readonly World _world;
        readonly DataBundle _data;
        readonly Inventory _inventory;
        readonly PlayerState _player;
        readonly EngineConfig _config;

        ActionRow _currentAction;
        string _selectedItemId;

        public FarmSystem(World world, DataBundle data, Inventory inventory, PlayerState player)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _config = data.Config;
            SelectAction(0);
        }

        public World World => _world;
        public DataBundle Data => _data;
        public Inventory Inventory => _inventory;
        public PlayerState Player => _player;
        public EngineConfig Config => _config;

        public IReadOnlyList<ActionRow> Actions => _data.Actions.Rows;
        public ActionRow CurrentAction => _currentAction;
        public string CurrentActionId => _currentAction?.ActionId ?? string.Empty;
        public int CurrentActionIndex => _currentAction == null ? -1 : _data.Actions.IndexOf(_currentAction.ActionId);
        public string SelectedItemId => _selectedItemId;

        public ItemRow SelectedItem
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedItemId)) return null;
                return _data.Items.TryGet(_selectedItemId, out var row) ? row : null;
            }
        }

        public int SelectedItemCount => _inventory.Count(_selectedItemId);

        public bool InRange(int x, int y) => _player.InInteractionRange(x, y, _config);

        public void SelectAction(int index)
        {
            if (_data.Actions.Rows.Count == 0)
            {
                _currentAction = null;
                _selectedItemId = null;
                return;
            }

            if (index < 0) index = _data.Actions.Rows.Count - 1;
            if (index >= _data.Actions.Rows.Count) index = 0;
            _currentAction = _data.Actions.Rows[index];

            if (_currentAction.SelectsItem)
            {
                var item = PickFirstItem(_currentAction.SelectsCategory);
                _selectedItemId = item?.ItemId;
            }
            else
            {
                _selectedItemId = null;
            }
        }

        public void CycleAction(int delta)
        {
            int count = _data.Actions.Rows.Count;
            if (count == 0) return;
            SelectAction((CurrentActionIndex + delta + count) % count);
        }

        public bool CycleItem(int delta)
        {
            if (_currentAction == null || !_currentAction.SelectsItem) return false;
            var list = ItemsForCategory(_currentAction.SelectsCategory);
            if (list.Count == 0)
            {
                _selectedItemId = null;
                return false;
            }

            int index = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].ItemId == _selectedItemId)
                {
                    index = i;
                    break;
                }
            }
            index = (index + delta + list.Count) % list.Count;
            _selectedItemId = list[index].ItemId;
            return true;
        }

        public void SelectItem(string itemId)
        {
            if (_data.Items.Contains(itemId)) _selectedItemId = itemId;
        }

        public bool TrySelectActionByHotkey(string hotkey, out ActionRow action)
        {
            action = null;
            if (string.IsNullOrEmpty(hotkey)) return false;
            for (int i = 0; i < _data.Actions.Rows.Count; i++)
            {
                if (_data.Actions.Rows[i].Hotkey != hotkey) continue;
                SelectAction(i);
                action = _data.Actions.Rows[i];
                return true;
            }
            return false;
        }

        public InteractionResult Interact(int x, int y)
        {
            string actionId = CurrentActionId;
            if (_currentAction == null) return InteractionResult.Fail(actionId, x, y, "没有可用动作，检查动作表");

            if (!_world.Contains(x, y)) return InteractionResult.Fail(actionId, x, y, "点击到地图外了");
            if (!InRange(x, y))
            {
                return InteractionResult.Fail(actionId, x, y,
                    $"超出 {_config.InteractionWidth}×{_config.InteractionHeight} 交互范围，走近一点再点");
            }

            switch (_currentAction.ActionId)
            {
                case "plant": return DoPlant(x, y);
                case "harvest": return DoHarvest(x, y);
                case "dig": return DoDig(x, y);
                case "place": return DoPlace(x, y);
                case "collect": return DoCollect(x, y);
                case "water": return DoWater(x, y);
                case "fertilize": return DoFertilize(x, y);
                case "heat": return DoHeat(x, y);
                default:
                    return InteractionResult.Fail(actionId, x, y, $"动作「{_currentAction.ActionId}」还没有实现");
            }
        }

        // ————————————————————————————— 具体动作 —————————————————————————————

        InteractionResult DoPlant(int x, int y)
        {
            var item = SelectedItem;
            if (item == null || !item.IsSeed)
                return Fail(x, y, "请先选择一种种子（Q/E 切换）");
            if (!_inventory.Has(item.ItemId))
                return Fail(x, y, $"背包里没有{item.Name}了");

            if (!_world.TryPlant(x, y, item.SeedPlantId, out var message)) return Fail(x, y, message);

            _inventory.Remove(item.ItemId, 1);
            return InteractionResult.Ok("plant", x, y, $"种下{item.Name}（剩 {_inventory.Count(item.ItemId)}）", item.ItemId, 1);
        }

        InteractionResult DoHarvest(int x, int y)
        {
            var before = _world.GetCell(x, y);
            if (!before.HasPlant) return Fail(x, y, "这一格没有作物");
            var type = _data.Plants.RowByIndex(before.PlantTypeId);

            if (!_world.TryHarvest(x, y, out float yield, out var message)) return Fail(x, y, message);

            int amount = Math.Max(1, (int)Math.Round(yield));
            string itemId = type.YieldItem;
            if (_data.Items.TryResolve(itemId, out var produce)) itemId = produce.ItemId;
            if (!string.IsNullOrEmpty(itemId)) _inventory.Add(itemId, amount);

            int seeds = 0;
            if (_config.SeedReturnOnHarvest > 0)
            {
                var seedItem = _data.Items.SeedForPlant(type.TypeId);
                if (seedItem != null)
                {
                    seeds = _config.SeedReturnOnHarvest;
                    _inventory.Add(seedItem.ItemId, seeds);
                }
            }

            string gain = string.IsNullOrEmpty(itemId) ? "空气" : _data.Items.Get(itemId).Name;
            string seedText = seeds > 0 ? $" + {seeds} 种子" : string.Empty;
            return InteractionResult.Ok("harvest", x, y,
                $"收获 {gain}×{amount}{seedText}（{message}）", itemId, amount);
        }

        InteractionResult DoDig(int x, int y)
        {
            var cell = _world.GetCell(x, y);
            if (cell.StructureId != 0) return Fail(x, y, "先把放置物收回来再挖");
            if (cell.HasPlant) return Fail(x, y, "先把作物收掉再挖");
            if (!string.IsNullOrEmpty(cell.ForageItemId)) return Fail(x, y, "先把地上的野物采集掉再挖");
            if (cell.Water > _config.DigMaxWater)
                return Fail(x, y, $"这里水太多了（{cell.Water:0.#} > {_config.DigMaxWater:0.#}），挖不动");

            if (!_world.TryApplySoil(x, y, -_currentAction.Power, -_currentAction.Power2, out var message))
                return Fail(x, y, message);

            if (!string.IsNullOrEmpty(_currentAction.GainItem) && _currentAction.Power3 > 0)
            {
                _inventory.Add(_currentAction.GainItem, _currentAction.Power3);
            }
            return InteractionResult.Ok("dig", x, y, message, _currentAction.GainItem, _currentAction.Power3);
        }

        InteractionResult DoPlace(int x, int y)
        {
            var item = SelectedItem;
            if (item == null || !item.IsPlaceable)
                return Fail(x, y, "请先选择一种放置物（Q/E 切换）");
            if (!_inventory.Has(item.ItemId))
                return Fail(x, y, $"背包里没有{item.Name}了");

            int structureId = item.PlaceStructureId;
            if (!_world.TrySetStructure(x, y, structureId, out var message)) return Fail(x, y, message);

            _inventory.Remove(item.ItemId, 1);
            return InteractionResult.Ok("place", x, y, message, item.ItemId, 1);
        }

        InteractionResult DoCollect(int x, int y)
        {
            var cell = _world.GetCell(x, y);
            if (!string.IsNullOrEmpty(cell.ForageItemId))
            {
                if (!_world.TryCollectForage(x, y, out var itemId, out int count, out var message))
                    return Fail(x, y, message);
                _inventory.Add(itemId, count);
                return InteractionResult.Ok("collect", x, y, message, itemId, count);
            }

            if (cell.StructureId != 0)
            {
                int structureId = cell.StructureId;
                if (!_world.TryRemoveStructure(x, y, out var message)) return Fail(x, y, message);
                var structure = _data.Structures.Get(structureId);
                if (!string.IsNullOrEmpty(structure.ItemId))
                {
                    _inventory.Add(structure.ItemId, 1);
                    string itemName = _data.Items.TryGet(structure.ItemId, out var itemRow)
                        ? itemRow.Name
                        : structure.ItemId;
                    string text = structureId == _config.RockStructureId
                        ? $"搬走{structure.Name}，获得 {itemName} ×1"
                        : $"收回{structure.Name}，已放回背包";
                    return InteractionResult.Ok("collect", x, y, text, structure.ItemId, 1);
                }
                return InteractionResult.Ok("collect", x, y, message, null, 0);
            }

            return Fail(x, y, "这里没有可采集的野物，也没有可收回的放置物");
        }

        InteractionResult DoWater(int x, int y)
        {
            var cell = _world.GetCell(x, y);
            if (cell.Water >= _config.WaterMax - 0.01f)
                return Fail(x, y, "这一格的水已经满了");

            if (!_world.TryApplySoil(x, y, _currentAction.Power, 0f, out var message))
                return Fail(x, y, message);
            return InteractionResult.Ok("water", x, y, message, null, 0);
        }

        InteractionResult DoFertilize(int x, int y)
        {
            var cost = _currentAction.CostItem;
            if (!string.IsNullOrEmpty(cost) && !_inventory.Has(cost))
            {
                string name = _data.Items.TryGet(cost, out var row) ? row.Name : cost;
                return Fail(x, y, $"没有{name}了");
            }

            var cell = _world.GetCell(x, y);
            if (cell.Nutrient >= _config.NutrientMax - 0.01f)
                return Fail(x, y, "这一格的养分已经满了");

            if (!_world.TryApplySoil(x, y, 0f, _currentAction.Power, out var message))
                return Fail(x, y, message);

            if (!string.IsNullOrEmpty(cost)) _inventory.Remove(cost, 1);
            return InteractionResult.Ok("fertilize", x, y, message, cost, 1);
        }

        InteractionResult DoHeat(int x, int y)
        {
            var cell = _world.GetCell(x, y);
            if (cell.HasHeatSource)
            {
                _world.SetHeatSource(x, y, false);
                return InteractionResult.Ok("heat", x, y, "已关掉这一格的热源", null, 0);
            }

            var cost = _currentAction.CostItem;
            if (!string.IsNullOrEmpty(cost) && !_inventory.Has(cost))
            {
                string name = _data.Items.TryGet(cost, out var row) ? row.Name : cost;
                return Fail(x, y, $"没有{name}了，先去采集一些");
            }

            if (!_world.SetHeatSource(x, y, true)) return Fail(x, y, "这里放不了热源");
            if (!string.IsNullOrEmpty(cost)) _inventory.Remove(cost, 1);
            return InteractionResult.Ok("heat", x, y, "已点上热源，周围温度会升高", cost, 1);
        }

        // ————————————————————————————— 工具 —————————————————————————————

        InteractionResult Fail(int x, int y, string message)
        {
            return InteractionResult.Fail(CurrentActionId, x, y, message);
        }

        ItemRow PickFirstItem(string category)
        {
            var list = ItemsForCategory(category);
            return list.Count > 0 ? list[0] : null;
        }

        List<ItemRow> ItemsForCategory(string category)
        {
            var list = new List<ItemRow>();
            for (int i = 0; i < _data.Items.Rows.Count; i++)
            {
                var row = _data.Items.Rows[i];
                if (category == "seed" && row.IsSeed) list.Add(row);
                else if (category == "placeable" && row.IsPlaceable) list.Add(row);
            }
            return list;
        }
    }
}
