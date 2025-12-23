using UnityEngine;

public class VATTester : MonoBehaviour
{
    public GameObject vatPrefab;
    public GameObject animatorPrefab;

    public Transform vatParent;
    public Transform animatorParent;
    
    public int rowCount;
    public int count;

    private void Awake()
    {
        for (int i = 0; i < count; i++)
        {
            GameObject vatInstanceObject = Instantiate(vatPrefab);
            vatInstanceObject.transform.position = new Vector3(i % rowCount, 0, i / rowCount);
            vatInstanceObject.transform.parent = vatParent;
            
            GameObject animatorInstanceObject = Instantiate(animatorPrefab);
            animatorInstanceObject.transform.position = new Vector3(i % rowCount, 0, i / rowCount);
            animatorInstanceObject.transform.parent = animatorParent;
        }
        
        vatParent.gameObject.SetActive(false);
    }
    
    public void OnChangedVatToggle(bool isOn)
    {
        vatParent.gameObject.SetActive(isOn);
        animatorParent.gameObject.SetActive(!isOn);
    }
}
