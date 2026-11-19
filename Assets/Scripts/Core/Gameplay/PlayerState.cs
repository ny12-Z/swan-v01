using System;
using Swan.Core.Sim;

namespace Swan.Core.Gameplay
{
    /// <summary>
    /// 主角状态（纯 C#，可单测）。坐标单位 = 格：X/Y 是脚底所在的连续坐标，
    /// 格 (x,y) 的范围是 [x,x+1) × [y,y+1)，所以站在格中心就是 (x+0.5, y+0.5)。
    /// 移动按 deltaTime 驱动，碰撞用四个角采样，撞墙时沿另一轴滑动。
    /// </summary>
    public sealed class PlayerState
    {
        public const int FacingDown = 0;
        public const int FacingUp = 1;
        public const int FacingLeft = 2;
        public const int FacingRight = 3;

        public float X;
        public float Y;
        public int Facing = FacingDown;
        public bool Moving;

        public int CellX => (int)Math.Floor(X);
        public int CellY => (int)Math.Floor(Y);

        public void Teleport(float x, float y)
        {
            X = x;
            Y = y;
            Moving = false;
        }

        /// <summary>出生点：优先 config 指定；否则从地图中心螺旋找一块能站的地。</summary>
        public void Spawn(World world, EngineConfig config)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (config == null) throw new ArgumentNullException(nameof(config));

            if (config.PlayerStartX >= 0 && config.PlayerStartY >= 0
                && config.PlayerStartX < world.Width && config.PlayerStartY < world.Height
                && CanStand(world, config.PlayerStartX + 0.5f, config.PlayerStartY + 0.5f, config))
            {
                Teleport(config.PlayerStartX + 0.5f, config.PlayerStartY + 0.5f);
                return;
            }

            int cx = world.Width / 2;
            int cy = world.Height / 2;
            int maxRadius = Math.Max(world.Width, world.Height);
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                        int x = cx + dx;
                        int y = cy + dy;
                        if (!world.Contains(x, y)) continue;
                        float px = x + 0.5f;
                        float py = y + 0.5f;
                        if (!CanStand(world, px, py, config)) continue;
                        var cell = world.GetCell(x, y);
                        if (cell.HasPlant || cell.HasHeatSource) continue;
                        Teleport(px, py);
                        return;
                    }
                }
            }

            Teleport(cx + 0.5f, cy + 0.5f);
        }

        /// <summary>WASD/方向键输入 → 本帧移动。dx/dy 可以是任意长度，内部会归一化。</summary>
        public void Move(World world, float dx, float dy, float deltaTime, EngineConfig config)
        {
            Moving = false;
            if (world == null || config == null || deltaTime <= 0f) return;
            if (Math.Abs(dx) < 1e-6f && Math.Abs(dy) < 1e-6f) return;

            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6f) return;
            dx /= length;
            dy /= length;

            if (Math.Abs(dx) > Math.Abs(dy)) Facing = dx > 0f ? FacingRight : FacingLeft;
            else if (Math.Abs(dy) > 1e-6f) Facing = dy > 0f ? FacingUp : FacingDown;

            float distance = config.PlayerMoveCellsPerSecond * deltaTime;
            float stepX = dx * distance;
            float stepY = dy * distance;

            if (TryMoveTo(world, X + stepX, Y, config)) Moving = true;
            if (TryMoveTo(world, X, Y + stepY, config)) Moving = true;
        }

        bool TryMoveTo(World world, float targetX, float targetY, EngineConfig config)
        {
            float radius = Math.Max(0.05f, config.PlayerCollisionRadius);
            if (targetX < radius || targetY < radius || targetX > world.Width - radius || targetY > world.Height - radius)
                return false;
            if (!CanStand(world, targetX, targetY, config)) return false;
            X = targetX;
            Y = targetY;
            return true;
        }

        /// <summary>角色脚底圆（用四个角近似）能否站在这里。</summary>
        public static bool CanStand(World world, float x, float y, EngineConfig config)
        {
            float radius = Math.Max(0.05f, config.PlayerCollisionRadius);
            if (!CanStandPoint(world, x - radius, y - radius, config)) return false;
            if (!CanStandPoint(world, x + radius, y - radius, config)) return false;
            if (!CanStandPoint(world, x - radius, y + radius, config)) return false;
            if (!CanStandPoint(world, x + radius, y + radius, config)) return false;
            return true;
        }

        static bool CanStandPoint(World world, float x, float y, EngineConfig config)
        {
            int cellX = (int)Math.Floor(x);
            int cellY = (int)Math.Floor(y);
            if (!world.Contains(cellX, cellY)) return false;
            var cell = world.GetCell(cellX, cellY);
            if (cell.Water > config.WalkableMaxWater) return false;
            if (cell.StructureId != 0)
            {
                if (!world.Data.Structures.TryGetRow(cell.StructureId, out var structure)) return false;
                if (structure.BlocksMovement) return false;
            }
            if (cell.HasPlant)
            {
                var plant = world.Data.Plants.RowByIndex(cell.PlantTypeId);
                if (!plant.Passable) return false;
            }
            return true;
        }

        /// <summary>主角身边 12×12（尺寸来自 config）是否包含这一格。矩形以主角所在格为中心。</summary>
        public bool InInteractionRange(int cellX, int cellY, EngineConfig config)
        {
            int width = Math.Max(1, config.InteractionWidth);
            int height = Math.Max(1, config.InteractionHeight);
            int left = CellX - width / 2;
            int bottom = CellY - height / 2;
            return cellX >= left && cellX < left + width
                   && cellY >= bottom && cellY < bottom + height;
        }
    }
}
