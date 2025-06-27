using System;
using Meta.XR;
using PassthroughCameraSamples;
using UnityEngine;

// Contiene EnvironmentRaycastManager e RaycastHit

[Serializable]
public class PositionData
{
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class RotationData
{
    public float x;
    public float y;
    public float z;
}

[Serializable]
public class CameraPoseData
{
    public PositionData position;
    public RotationData rotation;
}

[Serializable]
public class DetectedQuadroData
{
    public string id;
    public float nx;
    public float ny;
    public float nwidth;
    public float nheight;
    public float confidence;
}

[Serializable]
public class DetectionInput
{
    public DetectedQuadroData[] detected_quadri;
    public CameraPoseData camera_pose;
}

public class PaintingPlacer : MonoBehaviour
{
    [Tooltip("Il prefab da istanziare nella scena.")]
    public GameObject objectToPlace;

    [Tooltip("La distanza massima a cui il raggio cercherà una superficie reale.")]
    public float maxPlacementDistance = 20f;

    private Camera mainCamera; // Potrebbe non servire più direttamente per il raycast del passthrough
    private EnvironmentRaycastManager environmentRaycastManager;

    void Start()
    {
        mainCamera = Camera.main; // Manteniamo per ogni evenienza, ma PassthroughCameraUtils è preferito
        if (mainCamera == null)
        {
            Debug.LogWarning(
                "Nessuna telecamera principale trovata. PassthroughCameraUtils sarà utilizzato per il raycast.");
        }

        environmentRaycastManager = FindObjectOfType<EnvironmentRaycastManager>();
        if (environmentRaycastManager == null)
        {
            Debug.LogError(
                "EnvironmentRaycastManager non trovato nella scena. Assicurati di averlo aggiunto a un GameObject (es. OVRCameraRig). Impossibile eseguire il raycast nell'ambiente reale.");
        }
    }

    /// <summary>
    /// Funzione pubblica che riceve il JSON e avvia il processo di posizionamento.
    /// </summary>
    /// <param name="jsonString">La stringa JSON con i dati di rilevamento.</param>
    public void PlaceObjectFromJSON(string jsonString)
    {
        if (objectToPlace == null)
        {
            Debug.LogError("Il prefab 'objectToPlace' non è stato assegnato nell'Inspector.");
            return;
        }

        if (environmentRaycastManager == null)
        {
            Debug.LogError("EnvironmentRaycastManager non valido. Impossibile posizionare l'oggetto.");
            return;
        }

        // 1. Deserializza il JSON nella nostra struttura dati
        DetectionInput data = JsonUtility.FromJson<DetectionInput>(jsonString);

        if (data == null || data.detected_quadri == null || data.detected_quadri.Length == 0)
        {
            Debug.LogWarning("JSON non valido o nessun 'quadro' rilevato.");
            return;
        }

        // Prendiamo il primo quadro rilevato per questo esempio
        DetectedQuadroData quadro = data.detected_quadri[0];
        Debug.Log($"Tentativo di posizionare l'oggetto per il quadro: {quadro.id} con confidenza: {quadro.confidence}");

        // 2. Prepara il punto dello schermo per il raycast
        // Le coordinate (nx, ny) sono normalizzate (0 a 1).
        // PassthroughCameraUtils.ScreenPointToRayInWorld si aspetta pixel screen coordinates (width, height),
        // quindi dobbiamo convertirle dalla scala normalizzata (0-1) alla scala pixel.
        Vector2Int cameraScreenPoint = new Vector2Int(
            (int)quadro.nx * Screen.width, // Normalizzata X * Larghezza dello schermo in pixel
            (int)quadro.ny * Screen.height // Normalizzata Y * Altezza dello schermo in pixel
        );

        // 3. Crea un raggio dalla telecamera passthrough attraverso il punto dello schermo
        // Usiamo PassthroughCameraEye.Left per esempio, puoi scegliere Right o Center se disponibile e preferibile.
        var ray = PassthroughCameraUtils.ScreenPointToRayInWorld(PassthroughCameraEye.Left, cameraScreenPoint);

        // Visualizza il raggio nell'editor di Unity per il debug
        Debug.DrawRay(ray.origin, ray.direction * maxPlacementDistance, Color.cyan, 10.0f);

        // 4. Esegui il Raycast usando EnvironmentRaycastManager
        // Especifichiamo esplicitamente il tipo della hit per evitare ambiguità
        if (environmentRaycastManager.Raycast(ray, out EnvironmentRaycastHit hitInfo,
                maxPlacementDistance))
        {
            // La posizione è il punto di impatto del raggio.
            Vector3 position = hitInfo.point;

            // La rotazione fa in modo che l'oggetto sia "piatto" contro il muro.
            // Quaternion.LookRotation guarda nella direzione della normale della superficie per farla "guardare via"
            // Se vuoi che il quadro sia "appoggiato" sul muro, dovrebbe guardare nella direzione opposta alla normale.
            // L'esempio fornito usa hitInfo.normal, che significa che l'oggetto "guarda fuori" dal muro.
            // Se vuoi che il quadro sia rivolto verso l'utente quando è sul muro, dovresti usare -hitInfo.normal.
            // Per un quadro, è più intuitivo che guardi verso chi lo posiziona.
            Quaternion rotation = Quaternion.LookRotation(-hitInfo.normal, Vector3.up);

            // Istanziamo il nostro prefab
            GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);

            Debug.Log($"Prefab '{objectToPlace.name}' istanziato a: {position} nell'ambiente reale.");

            // OPZIONALE: Per rendere il quadro persistente nell'ambiente reale tra sessioni.
            // Questo richiede l'aggiunta del componente OVRSpatialAnchor al prefab o runtime.
            // OVRSpatialAnchor anchor = instantiatedObject.AddComponent<OVRSpatialAnchor>();
            // anchor.StartSavingAnchor();
            // Debug.Log($"Tentativo di salvare OVRSpatialAnchor per l'oggetto: {quadro.id}");
        }
        else
        {
            Debug.LogWarning(
                "Il raggio non ha colpito nessuna superficie nell'ambiente reale entro la distanza massima. Impossibile posizionare l'oggetto.");
        }
    }
}