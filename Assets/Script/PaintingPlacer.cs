using System;
using Meta.XR;
using UnityEngine;

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
    public float maxPlacementDistance = 2000f;

    [Tooltip("La telecamera principale della scena, usata come riferimento per le proprietà ottiche.")]
    public Camera mainCamera;

    private EnvironmentRaycastManager environmentRaycastManager;


    void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("Nessuna telecamera principale trovata. Assegnala nell'Inspector.");
                return;
            }
        }

        environmentRaycastManager = FindObjectOfType<EnvironmentRaycastManager>();
        if (environmentRaycastManager == null)
        {
            Debug.LogError(
                "environmentRaycastManager non trovato.");
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

        // 1. Deserializza il JSON
        DetectionInput data = JsonUtility.FromJson<DetectionInput>(jsonString);

        placePaint(data);
    }


    public void placePaint(DetectionInput data)
    {
        if (data == null || data.detected_quadri == null || data.detected_quadri.Length == 0)
        {
            Debug.LogWarning("JSON non valido o nessun 'quadro' rilevato.");
            return;
        }

        DetectedQuadroData quadro = data.detected_quadri[0];
        Debug.Log($"Tentativo di posizionare l'oggetto per il quadro: {quadro.id} con confidenza: {quadro.confidence}");

        // 2. *** MODIFICA CHIAVE: Crea il raggio dalla posa storica ***
        // Invece di usare la telecamera attuale, costruiamo un raggio che simula
        // la posizione e l'orientamento della telecamera al momento del rilevamento.
        Ray ray = CreateRayFromHistoricalPose(data.camera_pose, quadro);

        // 3. Esegui il Raycast sull'ambiente fisico
        // NOTA: Il raycasting sull'ambiente potrebbe richiedere un approccio diverso
        // a seconda della versione dell'SDK. Physics.Raycast funziona solo sui collider di Unity.
        // Per l'ambiente reale, devi usare le API di Meta.
        // Qui usiamo Physics.Raycast assumendo che tu abbia dei collider di scena generati
        // dalla Room Setup. Se non li hai, questa parte va adattata.
        if (environmentRaycastManager.Raycast(ray, out EnvironmentRaycastHit hitInfo, maxPlacementDistance))
        {
            // La posizione è il punto di impatto del raggio.
            Vector3 position = hitInfo.point;

            // La rotazione fa in modo che l'oggetto sia "piatto" contro il muro,
            // con la sua parte frontale (-transform.forward) che punta verso l'esterno.
            Quaternion rotation = Quaternion.LookRotation(hitInfo.normal);

            // Istanziamo il nostro prefab
            GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);

            // Opzionale: Se il tuo modello ha il lato "frontale" lungo l'asse Z positivo,
            // potresti dover ruotare l'oggetto per farlo puntare verso l'utente.
            // In tal caso, usa: Quaternion.LookRotation(-hitInfo.normal);
            // instantiatedObject.transform.rotation = Quaternion.LookRotation(-hitInfo.normal);

            Debug.Log($"Prefab '{objectToPlace.name}' istanziato a: {position} sulla superficie reale.");
        }
        else
        {
            Debug.LogWarning(
                $"Il raggio simulato non ha colpito nessuna superficie reale entro {maxPlacementDistance} metri. L'oggetto non può essere posizionato. Origine: {ray.origin}, Direzione: {ray.direction}");
        }
    }

    /// <summary>
    /// Crea un raggio (Ray) partendo da una posa storica (posizione/rotazione) e
    /// da coordinate normalizzate sullo schermo.
    /// </summary>
    /// <param name="poseData">I dati di posizione e rotazione della telecamera al momento dello scatto.</param>
    /// <param name="quadroData">I dati del quadro rilevato, incluse le coordinate normalizzate.</param>
    /// <returns>Un oggetto Ray da usare per il raycasting.</returns>
    private Ray CreateRayFromHistoricalPose(CameraPoseData poseData, DetectedQuadroData quadroData)
    {
        // a. Ricostruisci la posizione e la rotazione storiche
        Vector3 historicalPosition = new Vector3(poseData.position.x, poseData.position.y, poseData.position.z);
        Quaternion historicalRotation = Quaternion.Euler(poseData.rotation.x, poseData.rotation.y, poseData.rotation.z);

        // b. Converti le coordinate normalizzate (0-1) in coordinate di viewport (-1 a 1 per il centro) o di schermo (pixel)
        // Usiamo la telecamera principale come riferimento per ottenere le sue proprietà (FOV, aspect ratio)
        // che influenzano la proiezione del raggio.

        // Calcoliamo le coordinate in pixel. Usiamo float per non perdere precisione.
        float pixelX = quadroData.nx * mainCamera.pixelWidth;
        float pixelY = quadroData.ny * mainCamera.pixelHeight;

        // c. Usa la funzione ScreenPointToRay della telecamera di riferimento.
        // Questa funzione crea un raggio che parte dalla posizione della telecamera e passa
        // attraverso il punto specificato sul suo piano di proiezione.
        Ray referenceRay = mainCamera.ScreenPointToRay(new Vector2(pixelX, pixelY));

        // d. Ora abbiamo un raggio con l'origine e la direzione SBAGLIATE (quelle della telecamera live).
        // Però, possiamo estrarre la direzione del raggio e "ri-orientarla" secondo la nostra rotazione storica.

        // Trasformiamo la direzione del raggio (che è in coordinate globali) nello spazio locale della telecamera di riferimento.
        Vector3 localDirection = mainCamera.transform.InverseTransformDirection(referenceRay.direction);

        // Ora trasformiamo questa direzione locale nello spazio globale usando la nostra rotazione storica.
        // Questo ci dà la direzione corretta come se il raggio fosse stato emesso dalla telecamera con la posa storica.
        Vector3 finalDirection = historicalRotation * localDirection;

        // e. Crea e restituisci il raggio finale con l'origine e la direzione corrette.
        return new Ray(historicalPosition, finalDirection.normalized);
    }
}