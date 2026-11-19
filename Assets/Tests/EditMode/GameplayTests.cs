using System;
using System.IO;
using NUnit.Framework;
using Swan.Core;
using Swan.Core.Data;
using Swan.Core.Gameplay;
using Swan.Core.Sim;
using Swan.Game;
using Swan.Game.Rendering;
using UnityEngine;

namespace Swan.Tests
{
    /// <summary>
    /// 主角/动作/背包的测试：全部走真实的 StreamingAssets 数据，
    /// 但不碰场景——玩法逻辑必须能脱离 Unity 场景独立验证。
    /// </summary>
    [TestFixture]
    public sealed class GameplayTests
    {
        DataBundle _data;
        World _world;
        Inventory _inventory;
        PlayerState _player;
        FarmSystem _farm;

        [SetUp]
        public void SetUp()
        {
            _data = TestData.LoadReal();
            _world = new World(_data, 20, 20);
            _inventory = new Inventory();
            _inventory.GrantStartingItems(_data.Items);
            _player = new PlayerState();
            _player.Teleport(5.5f, 5.5f);
            _farm = new FarmSystem(_world, _data, _inventory, _player);

            // 测试区清干净：野物/结构会挡住种植和放置，也会让「点击哪一格」变得不可控
            for (int y = 0; y < 12; y++)
            {
                for (int x = 0; x < 12; x++)
                {
                    _world.TryCollectForage(x, y, out _, out _, out _);
                    _world.TryRemoveStructure(x, y, out _);
                    _world.SetCellValues(x, y, 30f, 30f);
                }
            }
        }

        [Test]
        public void Move_IsDeltaTimeDriven_NotPerFrame()
        {
            // 同样的 1 秒，拆成 10 帧和 100 帧应该走一样远；帧率高不该跑得快
            _player.Teleport(3.5f, 3.5f);
            float startX = _player.X;
            for (int i = 0; i < 10; i++) _player.Move(_world, 1f, 0f, 0.1f, _data.Config);
            float tenFrames = _player.X - startX;

            _player.Teleport(3.5f, 3.5f);
            startX = _player.X;
            for (int i = 0; i < 100; i++) _player.Move(_world, 1f, 0f, 0.01f, _data.Config);
            float hundredFrames = _player.X - startX;

            Assert.That(tenFrames, Is.EqualTo(_data.Config.PlayerMoveCellsPerSecond).Within(0.05f));
            Assert.That(hundredFrames, Is.EqualTo(tenFrames).Within(0.05f));
            Assert.That(_player.Facing, Is.EqualTo(PlayerState.FacingRight));
        }

        [Test]
        public void Move_StopsAtBlockingStructure()
        {
            Assert.That(_world.TrySetStructure(7, 5, 1, out _), Is.True);
            _player.Teleport(5.5f, 5.5f);
            for (int i = 0; i < 100; i++) _player.Move(_world, 1f, 0f, 0.02f, _data.Config);

            float radius = _data.Config.PlayerCollisionRadius;
            Assert.That(_player.X, Is.LessThan(7f - radius + 0.01f), "主角应该被栅栏挡住，不能穿过去");
            Assert.That(_player.CellX, Is.EqualTo(5).Or.EqualTo(6));
        }

        [Test]
        public void InteractionRange_IsTwelveByTwelveAroundPlayerCell()
        {
            _player.Teleport(10.5f, 10.5f);
            Assert.That(_farm.InRange(10, 10), Is.True);
            Assert.That(_farm.InRange(4, 4), Is.True);
            Assert.That(_farm.InRange(15, 15), Is.True);
            Assert.That(_farm.InRange(3, 10), Is.False);
            Assert.That(_farm.InRange(10, 16), Is.False);
        }

        [Test]
        public void Plant_ConsumesSeed_AndHarvestReturnsProduceAndSeed()
        {
            _farm.TrySelectActionByHotkey("1", out _);
            int seedsBefore = _inventory.Count("seed_p01");

            var planted = _farm.Interact(5, 5);
            Assert.That(planted.Success, Is.True, planted.Message);
            Assert.That(_world.GetCell(5, 5).HasPlant, Is.True);
            Assert.That(_inventory.Count("seed_p01"), Is.EqualTo(seedsBefore - 1));

            // 直接把成长推到成熟（环境步是唯一权威路径，这里把它跑够）
            for (int i = 0; i < 80; i++)
            {
                _world.SetCellValues(5, 5, 100f, 100f);
                _world.SetTemperature(5, 5, 20f);
                _world.StepEnvironment();
            }
            Assert.That(_world.GetCell(5, 5).Harvestable, Is.True, "作物应该已经成熟");

            _farm.TrySelectActionByHotkey("2", out _);
            int produceBefore = _inventory.Count("produce_p01");
            var harvested = _farm.Interact(5, 5);

            Assert.That(harvested.Success, Is.True, harvested.Message);
            Assert.That(_world.GetCell(5, 5).HasPlant, Is.False);
            Assert.That(_inventory.Count("produce_p01"), Is.GreaterThan(produceBefore));
            Assert.That(_inventory.Count("seed_p01"), Is.EqualTo(seedsBefore), "收获会返还种子");
        }

        [Test]
        public void Dig_LowersSoil_AndGivesSoilItem()
        {
            _farm.TrySelectActionByHotkey("3", out _);
            _world.SetCellValues(6, 5, 30f, 30f);
            int soilBefore = _inventory.Count("soil");

            var result = _farm.Interact(6, 5);
            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(_world.GetCell(6, 5).Water, Is.EqualTo(22f).Within(0.01f));
            Assert.That(_world.GetCell(6, 5).Nutrient, Is.EqualTo(26f).Within(0.01f));
            Assert.That(_inventory.Count("soil"), Is.GreaterThan(soilBefore));
        }

        [Test]
        public void Place_ThenCollect_ReturnsItemToInventory()
        {
            _farm.TrySelectActionByHotkey("4", out _);
            _farm.SelectItem("fence_item");
            int before = _inventory.Count("fence_item");
            Assert.That(before, Is.GreaterThan(0));

            var placed = _farm.Interact(6, 5);
            Assert.That(placed.Success, Is.True, placed.Message);
            Assert.That(_world.GetCell(6, 5).StructureId, Is.EqualTo(1));
            Assert.That(_inventory.Count("fence_item"), Is.EqualTo(before - 1));

            _farm.TrySelectActionByHotkey("5", out _);
            var collected = _farm.Interact(6, 5);
            Assert.That(collected.Success, Is.True, collected.Message);
            Assert.That(_world.GetCell(6, 5).StructureId, Is.EqualTo(0));
            Assert.That(_inventory.Count("fence_item"), Is.EqualTo(before));
        }

        [Test]
        public void WaterAndFertilize_ApplyTablePowers()
        {
            _world.SetCellValues(6, 5, 30f, 30f);
            _farm.TrySelectActionByHotkey("6", out _);
            Assert.That(_farm.Interact(6, 5).Success, Is.True);
            Assert.That(_world.GetCell(6, 5).Water, Is.EqualTo(42f).Within(0.01f));

            int compost = _inventory.Count("compost");
            _farm.TrySelectActionByHotkey("7", out _);
            Assert.That(_farm.Interact(6, 5).Success, Is.True);
            Assert.That(_world.GetCell(6, 5).Nutrient, Is.EqualTo(40f).Within(0.01f));
            Assert.That(_inventory.Count("compost"), Is.EqualTo(compost - 1));
        }

        [Test]
        public void Heat_TogglesWithTorchCost()
        {
            _farm.TrySelectActionByHotkey("8", out _);
            int torches = _inventory.Count("torch_item");
            Assert.That(torches, Is.GreaterThan(0));

            Assert.That(_farm.Interact(6, 5).Success, Is.True);
            Assert.That(_world.GetCell(6, 5).HasHeatSource, Is.True);
            Assert.That(_inventory.Count("torch_item"), Is.EqualTo(torches - 1));

            Assert.That(_farm.Interact(6, 5).Success, Is.True);
            Assert.That(_world.GetCell(6, 5).HasHeatSource, Is.False);
        }

        [Test]
        public void Interact_RejectsOutOfRange()
        {
            _player.Teleport(10.5f, 10.5f);
            _farm.TrySelectActionByHotkey("3", out _);
            var result = _farm.Interact(0, 0);
            Assert.That(result.Success, Is.False);
            StringAssert.Contains("交互范围", result.Message);
        }

        [Test]
        public void Forage_CanBeCollected_AndWorldCountDrops()
        {
            World world = new World(_data, 40, 40);
            Assert.That(world.ForageCount, Is.GreaterThan(0), "40×40 地图应该铺出野物");

            int x = -1, y = -1;
            for (int i = 0; i < world.CellCount && x < 0; i++)
            {
                int cx = i % world.Width;
                int cy = i / world.Width;
                if (world.GetCell(cx, cy).HasForage)
                {
                    x = cx;
                    y = cy;
                }
            }
            Assert.That(x, Is.GreaterThanOrEqualTo(0), "找不到任何野物格");

            var p = new PlayerState();
            p.Teleport(x + 0.5f, y + 0.5f);
            var farm = new FarmSystem(world, _data, new Inventory(), p);
            farm.TrySelectActionByHotkey("5", out _);
            int before = world.ForageCount;
            var result = farm.Interact(x, y);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(result.ItemCount, Is.GreaterThan(0));
            Assert.That(world.ForageCount, Is.EqualTo(before - 1));
        }

        [Test]
        public void Config_SaveAndRestoreDefault_RoundTrips()
        {
            string directory = Path.Combine(Path.GetTempPath(), "swan-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.Copy(Path.Combine(TestData.Folder, "config.json"), ConfigIO.ConfigPath(directory));
                File.Copy(Path.Combine(TestData.Folder, "config.default.json"), ConfigIO.DefaultPath(directory));

                var config = _data.Config.Clone();
                config.PlayerMoveCellsPerSecond = 9.25f;
                config.InteractionWidth = 16;

                Assert.That(ConfigIO.Save(config, directory, out var saveMessage), Is.True, saveMessage);
                var saved = ConfigLoader.LoadText(File.ReadAllText(ConfigIO.ConfigPath(directory)));
                Assert.That(saved.PlayerMoveCellsPerSecond, Is.EqualTo(9.25f).Within(0.001f));
                Assert.That(saved.InteractionWidth, Is.EqualTo(16));

                Assert.That(ConfigIO.RestoreDefault(directory, out var restoreMessage), Is.True, restoreMessage);
                var restored = ConfigLoader.LoadText(File.ReadAllText(ConfigIO.ConfigPath(directory)));
                Assert.That(restored.PlayerMoveCellsPerSecond, Is.EqualTo(_data.Config.PlayerMoveCellsPerSecond).Within(0.001f));
                Assert.That(restored.InteractionWidth, Is.EqualTo(_data.Config.InteractionWidth));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void Rocks_AreSeeded_BlockMovement_AndCanBeCollected()
        {
            int rockX = -1, rockY = -1;
            for (int y = 1; y < _world.Height - 1 && rockX < 0; y++)
            {
                for (int x = 1; x < _world.Width - 1; x++)
                {
                    if (_world.GetCell(x, y).StructureId == _data.Config.RockStructureId)
                    {
                        rockX = x;
                        rockY = y;
                        break;
                    }
                }
            }

            Assert.That(rockX, Is.GreaterThanOrEqualTo(0), "default config should seed rocks");
            Assert.That(_data.Structures.Get(_data.Config.RockStructureId).BlocksMovement, Is.True);
            Assert.That(PlayerState.CanStand(_world, rockX + 0.5f, rockY + 0.5f, _data.Config), Is.False,
                "a rock cell must not be walkable");

            _player.Teleport(rockX + 1.5f, rockY + 0.5f);
            _farm.TrySelectActionByHotkey("5", out _);
            int stoneBefore = _inventory.Count("stone");
            var result = _farm.Interact(rockX, rockY);

            Assert.That(result.Success, Is.True, result.Message);
            Assert.That(_world.GetCell(rockX, rockY).StructureId, Is.EqualTo(0));
            Assert.That(_inventory.Count("stone"), Is.EqualTo(stoneBefore + 1));
        }

        [Test]
        public void ArtCatalog_LoadsGroundAndPlayerFrames()
        {
            var art = ArtCatalog.Load(DataLocator.ArtDirectory);
            Assert.That(art.HasArt, Is.True);
            Assert.That(art.TryGetGround(7, out _), Is.True, "地形 7 应该有地面图块");

            Sprite sprite = art.GetPlayerSprite(PlayerState.FacingDown, 0);
            Assert.That(sprite, Is.Not.Null);
            Assert.That(sprite.texture.width, Is.EqualTo(16));
            Assert.That(sprite.texture.height, Is.EqualTo(16));
            UnityEngine.Object.DestroyImmediate(sprite.texture);
            UnityEngine.Object.DestroyImmediate(sprite);
        }
    }
}
