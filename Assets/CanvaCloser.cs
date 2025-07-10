using UnityEngine;

public class CanvaCloser : MonoBehaviour
{
    public void CloseCanvas()
    {
        // Questa funzione sarà chiamata dal bottone
        // Rimuove l'oggetto GameObject a cui è attaccato questo script
        // che si presume sia il Canvas stesso.
        Destroy(gameObject);

        var ids = gameObject.name.Split(":")[1];
        int id = -1;
        int.TryParse(ids, out id);
        if (id != -1)
            PaintingPlacer.paintingPannels.Remove(id);
    }
}