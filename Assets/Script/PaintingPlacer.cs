using System;
using UnityEngine;

[Serializable]
public class DetectedQuadro
{
    public string id;
    public float nx;
    public float ny;
    public float nwidth;
    public float nheight;
    public float confidence;
}

[Serializable]
public class Vector3Serializable
{
    public float x;
    public float y;
    public float z;

    // Metodo per convertire facilmente in un Vector3 di Unity
    public Vector3 ToUnityVector3()
    {
        return new Vector3(x, y, z);
    }
}

[Serializable]
public class QuaternionSerializable
{
    public float x;
    public float y;
    public float z;
    public float w;

    // Metodo per convertire facilmente in un Quaternion di Unity
    public Quaternion ToUnityQuaternion()
    {
        
        return new Quaternion(x, y, z, w);
    }
}

[Serializable]
public class EulerAnglesSerializable
{
    public float x; // pitch
    public float y; // yaw
    public float z; // roll

    // Metodo per convertire facilmente in un Quaternion di Unity
    public Quaternion ToUnityQuaternion()
    {
        return Quaternion.Euler(x, y, z);
    }
}

[Serializable]
public class CameraPose
{
    public Vector3Serializable position;
    public EulerAnglesSerializable rotation; // Modificato da QuaternionSerializable a EulerAnglesSerializable
}

[Serializable]
public class FrameData
{
    public DetectedQuadro[] detected_quadri;
    public double frame_timestamp;
    public CameraPose camera_pose;
}

public class PaintingPlacer : MonoBehaviour
{
    [Tooltip("Incolla qui il JSON ricevuto dal server Python.")] [TextArea(15, 20)]
    public string serverJson;

    [Tooltip("Il prefab da usare per visualizzare il quadro. Dovrebbe essere un Quad di default.")]
    public GameObject paintingPrefab;

    [Tooltip(
        "La distanza presunta a cui posizionare i quadri dalla telecamera virtuale. Questo valore è cruciale e potrebbe richiedere aggiustamenti.")]
    public float distanceFromCamera = 3.0f;
    
    public Camera camera;

    // Aggiunge un pulsante nell'Inspector per avviare la procedura
    [ContextMenu("Instanzia Quadri dal JSON")]
    public void PlacePaintingsFromJSON()
    {
        if (string.IsNullOrEmpty(serverJson))
        {
            Debug.LogError("Il campo JSON è vuoto. Incolla il JSON ricevuto dal server.");
            return;
        }

        if (paintingPrefab == null)
        {
            Debug.LogError("Prefab del quadro non assegnato. Assegna un prefab (es. un Quad) all'apposito campo.");
            return;
        }

        // Deserializza il JSON nelle nostre classi C#
        FrameData frameData;
        try
        {
            frameData = JsonUtility.FromJson<FrameData>(serverJson);
        }
        catch (Exception e)
        {
            Debug.LogError($"Errore nel parsing del JSON: {e.Message}");
            return;
        }

        // Ricrea la posizione e la rotazione della telecamera al momento della cattura
        Vector3 historicalCamPosition = frameData.camera_pose.position.ToUnityVector3();
        Quaternion historicalCamRotation = frameData.camera_pose.rotation.ToUnityQuaternion();

        // Per la proiezione, usiamo la telecamera principale della scena,
        // ma la spostiamo temporaneamente nella posa storica.
        // Questo è più efficiente che creare una nuova telecamera.
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError(
                "Nessuna telecamera principale trovata nella scena. Assicurati che la tua telecamera abbia il tag 'MainCamera'.");
            return;
        }

        // Salva la posa attuale della telecamera per ripristinarla dopo
        Vector3 originalCamPos = mainCamera.transform.position;
        Quaternion originalCamRot = mainCamera.transform.rotation;

        // "Mettiti nella stessa posizione" del visore
        mainCamera.transform.position = historicalCamPosition;
        mainCamera.transform.rotation = historicalCamRotation;

        Debug.Log($"Numero di quadri da istanziare: {frameData.detected_quadri.Length}");

        // Itera su ogni quadro rilevato
        foreach (var quadro in frameData.detected_quadri)
        {
            // --- Calcolo della Posizione nel Mondo 3D ---
            // Le coordinate (nx, ny) sono "Viewport Coordinates".
            // Il punto (0,0) è l'angolo in basso a sinistra dello schermo, (1,1) è in alto a destra.
            // Usiamo Camera.ViewportToWorldPoint per proiettare questo punto 2D nello spazio 3D.
            // Richiede una coordinata Z, che è la distanza dalla telecamera.
            Vector3 centerPosition =
                mainCamera.ViewportToWorldPoint(new Vector3(quadro.nx, quadro.ny, distanceFromCamera));

            // --- Calcolo delle Dimensioni nel Mondo 3D ---
            // Per trovare la larghezza e l'altezza in unità di mondo, proiettiamo altri due punti
            // e calcoliamo la distanza.
            Vector3 rightEdgePoint =
                mainCamera.ViewportToWorldPoint(new Vector3(quadro.nx + quadro.nwidth / 2, quadro.ny,
                    distanceFromCamera));
            Vector3 topEdgePoint =
                mainCamera.ViewportToWorldPoint(new Vector3(quadro.nx, quadro.ny + quadro.nheight / 2,
                    distanceFromCamera));

            float worldWidth = Vector3.Distance(centerPosition, rightEdgePoint) * 2;
            float worldHeight = Vector3.Distance(centerPosition, topEdgePoint) * 2;

            // --- Istanziazione e Configurazione dell'Oggetto ---
            GameObject newPainting = Instantiate(paintingPrefab, centerPosition, Quaternion.identity);
            newPainting.name = quadro.id;

            // Scala il quad (che è 1x1 di default) per avere le dimensioni corrette
            newPainting.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);

            // Orienta il quadro in modo che sia rivolto verso la posizione da cui è stato "visto"
            newPainting.transform.LookAt(historicalCamPosition);

            Debug.Log(
                $"Creato quadro '{quadro.id}' a posizione {centerPosition} con scala ({worldWidth}, {worldHeight})");
        }

        // Ripristina la telecamera alla sua posizione e rotazione originali
        mainCamera.transform.position = originalCamPos;
        mainCamera.transform.rotation = originalCamRot;
    }

    public void PlacePaintingsFromJsonString(string json)
    {
        serverJson = json;
        PlacePaintingsFromJSON();
    }
}