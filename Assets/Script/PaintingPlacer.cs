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
    [Tooltip("Il prefab da istanziare nella scena. Assicurati che sia un piano 1x1 con pivot al centro.")]
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

        foreach (var quadro in data.detected_quadri)
        {
            Debug.Log(
                $"Tentativo di posizionare l'oggetto per il quadro: {quadro.id} con confidenza: {quadro.confidence}");

            // 1. Crea il raggio dal centro del rilevamento
            Ray centerRay = CreateRayFromHistoricalPose(data.camera_pose, quadro.nx, quadro.ny);

            // 2. Esegui il Raycast per trovare il punto centrale sulla superficie
            if (environmentRaycastManager.Raycast(centerRay, out EnvironmentRaycastHit centerHit, maxPlacementDistance))
            {
                // --- POSIZIONAMENTO E ROTAZIONE ---
                Vector3 position = centerHit.point;
                Quaternion rotation = Quaternion.LookRotation(centerHit.normal);
                GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);

                Debug.Log($"Prefab '{objectToPlace.name}' istanziato a: {position} sulla superficie reale.");

                // --- INIZIO LOGICA DI SCALATURA ---

                // 3. Calcola i punti per i bordi destro e superiore
                // NOTA: Calcoliamo la mezza larghezza e mezza altezza per essere più robusti a distorsioni prospettiche.
                float halfWidthNx = quadro.nx + (quadro.nwidth / 2.0f);
                float halfHeightNy = quadro.ny + (quadro.nheight / 2.0f);

                Ray rightRay = CreateRayFromHistoricalPose(data.camera_pose, halfWidthNx, quadro.ny);
                Ray topRay = CreateRayFromHistoricalPose(data.camera_pose, quadro.nx, halfHeightNy);

                Vector3 rightPoint;
                Vector3 topPoint;

                // Lancia il raggio per il bordo destro. Se fallisce, proietta il raggio sul piano del muro.
                if (environmentRaycastManager.Raycast(rightRay, out EnvironmentRaycastHit rightHit,
                        maxPlacementDistance))
                {
                    rightPoint = rightHit.point;
                }
                else
                {
                    // Fallback: proietta il raggio sul piano del muro trovato con il raggio centrale
                    Plane wallPlane = new Plane(centerHit.normal, centerHit.point);
                    wallPlane.Raycast(rightRay, out float enter);
                    rightPoint = rightRay.GetPoint(enter);
                    Debug.LogWarning("Raycast per il bordo destro fallito. Usato fallback di proiezione su piano.");
                }

                // Lancia il raggio per il bordo superiore. Se fallisce, usa lo stesso fallback.
                if (environmentRaycastManager.Raycast(topRay, out EnvironmentRaycastHit topHit, maxPlacementDistance))
                {
                    topPoint = topHit.point;
                }
                else
                {
                    Plane wallPlane = new Plane(centerHit.normal, centerHit.point);
                    wallPlane.Raycast(topRay, out float enter);
                    topPoint = topRay.GetPoint(enter);
                    Debug.LogWarning("Raycast per il bordo superiore fallito. Usato fallback di proiezione su piano.");
                }

                // 4. Calcola le dimensioni nel mondo reale e applica la scala
                // Misuriamo la distanza dal centro al bordo e la raddoppiamo.
                float worldWidth = Vector3.Distance(position, rightPoint) * 2.0f;
                float worldHeight = Vector3.Distance(position, topPoint) * 2.0f;

                // Applica la scala. Assumendo che l'oggetto sia 1x1, la scala è direttamente la dimensione calcolata.
                // Manteniamo la scala Z originale del prefab per evitare di dargli uno spessore strano.
                instantiatedObject.transform.localScale =
                    new Vector3(worldWidth, worldHeight, instantiatedObject.transform.localScale.z);

                Debug.Log($"Oggetto scalato a (Larghezza x Altezza): {worldWidth} x {worldHeight}");
            }
            else
            {
                Debug.LogWarning(
                    $"Il raggio simulato non ha colpito nessuna superficie reale entro {maxPlacementDistance} metri. L'oggetto non può essere posizionato. Origine: {centerRay.origin}, Direzione: {centerRay.direction}");
            }
        }
    }

    /// <summary>
    /// Crea un raggio (Ray) partendo da una posa storica e da coordinate normalizzate.
    /// Ho refattorizzato questo metodo per accettare nx/ny direttamente e renderlo più riutilizzabile.
    /// </summary>
    private Ray CreateRayFromHistoricalPose(CameraPoseData poseData, float nx, float ny)
    {
        // a. Ricostruisci la posizione e la rotazione storiche
        Vector3 historicalPosition = new Vector3(poseData.position.x, poseData.position.y, poseData.position.z);
        Quaternion historicalRotation = Quaternion.Euler(poseData.rotation.x, poseData.rotation.y, poseData.rotation.z);

        // b. Converti le coordinate normalizzate (0-1) in coordinate di schermo (pixel)
        float pixelX = nx;
        float pixelY = ny;

        // c. Usa ScreenPointToRay della telecamera di riferimento per ottenere una direzione
        Ray referenceRay = mainCamera.ScreenPointToRay(new Vector2(pixelX, pixelY));

        // d. Trasforma la direzione del raggio per allinearla alla rotazione storica
        Vector3 localDirection = mainCamera.transform.InverseTransformDirection(referenceRay.direction);
        Vector3 finalDirection = historicalRotation * localDirection;

        // e. Crea e restituisci il raggio finale con l'origine e la direzione corrette.
        return new Ray(historicalPosition, finalDirection.normalized);
    }
}