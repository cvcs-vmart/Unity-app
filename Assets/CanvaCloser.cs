using UnityEngine;

public class CanvaCloser : MonoBehaviour
{
    public void CloseCanvas()
    {
        // Questa funzione sarà chiamata dal bottone
        // Rimuove l'oggetto GameObject a cui è attaccato questo script
        // che si presume sia il Canvas stesso.
        Destroy(gameObject);

        var id = gameObject.name.Split(":")[1];
        PaintingPlacer.paintingPannels.Remove(id);
    }
}