using UnityEngine;

namespace AnimatedDrawingsWorld.World
{
    public class WorldSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject[] characterPrefabs;
        [SerializeField] private int count = 3;
        [SerializeField] private Vector2 areaMin = new(-4f, -2f);
        [SerializeField] private Vector2 areaMax = new(4f, 2f);

        private void Start()
        {
            if (characterPrefabs == null || characterPrefabs.Length == 0)
            {
                Debug.LogError("WorldSpawner: 'Character Prefabs' is empty — assign at least one character prefab in the Inspector.", this);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                var prefab = characterPrefabs[Random.Range(0, characterPrefabs.Length)];
                var position = new Vector3(
                    Random.Range(areaMin.x, areaMax.x),
                    Random.Range(areaMin.y, areaMax.y),
                    0f);

                Instantiate(prefab, position, Quaternion.identity, transform);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            var center = (Vector3)(areaMin + areaMax) / 2f;
            var size = new Vector3(areaMax.x - areaMin.x, areaMax.y - areaMin.y, 0f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
