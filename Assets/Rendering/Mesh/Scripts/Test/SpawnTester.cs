using UnityEngine;

public class SpawnTester : MonoBehaviour
{
    public GameObject spawnObject;
    public int count;

    public Vector2 space;
    public int countPerRow;

    public void Awake()
    {
        for(int i = 0; i < count; i++)
        {
            int row = i / countPerRow;
            int col = i % countPerRow;

            Instantiate(spawnObject, new Vector3(row * space.x, 0, col * space.y), Quaternion.identity);
        }
    }
}
