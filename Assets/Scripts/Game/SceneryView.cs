using Swan.Core.Sim;
using Swan.Game.Rendering;
using UnityEngine;

namespace Swan.Game
{
    /// <summary>
    /// 树、灌木、石头这类跨格景物的表现层。它们不参与玩法，所以不进 World；
    /// 只在载入/换图时按地形和格坐标哈希铺一遍，保证同一张地图每次长得一样。
    /// </summary>
    public sealed class SceneryView : MonoBehaviour
    {
        ArtCatalog _art;
        World _world;

        public void Bind(World world, ArtCatalog art)
        {
            _world = world;
            _art = art;
            Rebuild();
        }

        public void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            if (_world == null || _art == null) return;

            int count = 0;
            for (int y = 0; y < _world.Height; y++)
            {
                for (int x = 0; x < _world.Width; x++)
                {
                    Cell cell = _world.GetCell(x, y);
                    if (cell.HasStructure || cell.HasPlant || cell.HasForage) continue;
                    if (!_art.TryGetProp(cell.TerrainId, x, y, out var prop)) continue;

                    var go = new GameObject("Prop");
                    go.transform.SetParent(transform, false);
                    go.transform.position = new Vector3(x + 0.5f + prop.OffsetX, y + prop.OffsetY, 0f);
                    var sprite = go.AddComponent<SpriteRenderer>();
                    sprite.sprite = _art.GetPropSprite(prop);
                    sprite.sortingOrder = 5;
                    count++;
                }
            }

            Debug.Log($"[SWAN] 景物 {count} 个");
        }
    }
}
