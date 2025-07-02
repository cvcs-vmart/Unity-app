using System;
using Meta.XR;
using TMPro;
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
    public float centerX;
    public float centerY;
    public float nWidth;
    public float nHeight;
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
    public TextMeshProUGUI debugText;

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

        debugText.text += "\n " + mainCamera.pixelWidth + "x" + mainCamera.pixelHeight + "\n\n";

        // Dimensioni dello schermo su cui è stata fatta la rilevazione
        const float originalScreenWidth = 1280.0f;
        const float originalScreenHeight = 960.0f;

        // Calcola i fattori di scala
        float scaleX = mainCamera.pixelWidth / originalScreenWidth;
        float scaleY = mainCamera.pixelHeight / originalScreenHeight;

        foreach (var quadro in data.detected_quadri)
        {
            // Applica la proporzione alle coordinate e dimensioni del quadro
            float scaledCenterX = quadro.centerX * scaleX;
            float scaledCenterY = quadro.centerY * scaleY;
            float scaledNWidth = quadro.nWidth * scaleX * 0.9f;
            float scaledNHeight = quadro.nHeight * scaleY * 0.9f;

            Debug.Log(
                $"Tentativo di posizionare l'oggetto per il quadro:");

            // 1. Crea il raggio dal centro del rilevamento (usando le coordinate scalate)
            Ray centerRay = CreateRayFromHistoricalPose(data.camera_pose, scaledCenterX, scaledCenterY);

            // 2. Esegui il Raycast per trovare il punto centrale sulla superficie
            if (environmentRaycastManager.Raycast(centerRay, out EnvironmentRaycastHit centerHit, maxPlacementDistance))
            {
                // --- POSIZIONAMENTO E ROTAZIONE ---
                Vector3 position = centerHit.point;
                Quaternion rotation = Quaternion.LookRotation(centerHit.normal);
                GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);

                Debug.Log($"Prefab '{objectToPlace.name}' istanziato a: {position} sulla superficie reale.");

                // --- INIZIO LOGICA DI SCALATURA ---

                // 3. Calcola i punti per i bordi destro e superiore usando le dimensioni scalate
                // NOTA: Calcoliamo la mezza larghezza e mezza altezza per essere più robusti a distorsioni prospettiche.
                float halfWidthNx = scaledCenterX + (scaledNWidth / 2.0f);
                float halfHeightNy = scaledCenterY + (scaledNHeight / 2.0f);

                Ray rightRay = CreateRayFromHistoricalPose(data.camera_pose, halfWidthNx, scaledCenterY);
                Ray topRay = CreateRayFromHistoricalPose(data.camera_pose, scaledCenterX, halfHeightNy);

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


    private Ray CreateRayFromHistoricalPose(CameraPoseData poseData, float pixelX, float pixelY)
    {
        // a. Ricostruisci la posizione e la rotazione storiche
        Vector3 historicalPosition = new Vector3(poseData.position.x, poseData.position.y, poseData.position.z);
        Quaternion historicalRotation = Quaternion.Euler(poseData.rotation.x, poseData.rotation.y, poseData.rotation.z);

        pixelY = mainCamera.pixelHeight - pixelY;

        // c. Usa ScreenPointToRay della telecamera di riferimento per ottenere una direzione
        Ray referenceRay = mainCamera.ScreenPointToRay(new Vector2(pixelX, pixelY));

        // d. Trasforma la direzione del raggio per allinearla alla rotazione storica
        Vector3 localDirection = mainCamera.transform.InverseTransformDirection(referenceRay.direction);
        Vector3 finalDirection = historicalRotation * localDirection;

        // e. Crea e restituisci il raggio finale con l'origine e la direzione corrette.
        return new Ray(historicalPosition, finalDirection.normalized);
    }
}