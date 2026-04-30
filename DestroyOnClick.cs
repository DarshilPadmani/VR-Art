using UnityEngine;

public class DestroyOnClick : MonoBehaviour
{
    // This method must be public to show up in the Button settings
    public void DestroyThisObject()
    {
        // Destroys the GameObject this script is attached to
        Destroy(gameObject);
    }
}