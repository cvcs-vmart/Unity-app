using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Meta.XR;
using PassthroughCameraSamples;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

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
    public int id;
}

[Serializable]
public class RetrievalRequestData
{
    public int id;
}

public class PaintingPlacer : MonoBehaviour
{
    [Tooltip("Il prefab da istanziare nella scena. Assicurati che sia un piano 1x1 con pivot al centro.")]
    public GameObject objectToPlace;

    public GameObject panel;

    [Tooltip("La distanza massima a cui il raggio cercherà una superficie reale.")]
    public float maxPlacementDistance = 2000f;

    [Tooltip("La telecamera principale della scena, usata come riferimento per le proprietà ottiche.")]
    public Camera mainCamera;

    private EnvironmentRaycastManager environmentRaycastManager;
    public TextMeshProUGUI debugText;

    public LayerMask sceneMeshLayer; // the mesh of the scene where the object will be placed
    public LayerMask paintingLayer; // layer of the paintings

    private MeshFilter map;

    public Transform controllerRightHand;

    static public Dictionary<int, GameObject> paintingPannels = new Dictionary<int, GameObject>();

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

        environmentRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
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

        //placePaint_PC(data);
        //placePaint(data);
        placePaint_Room(data);
    }

    //metodo chiamato quando il RoomMesh viene caricato. 
    public void onRoomMeshLoad(MeshFilter map)
    {
        //Impostare il layer all'ogetto in modo che il raycast possa colpirlo
        this.map = map;
        map.gameObject.layer = LayerMask.NameToLayer("SceneMeshes");

        // Disabilita il renderer del RoomMesh per evitare che sia visibile nella scena
        var renderer = map.GetComponent<Renderer>();
        if (renderer != null) renderer.enabled = false;
    }

    public void toggleMapVisibility()
    {
        if (map == null) return;

        var renderer = map.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.enabled = !renderer.enabled;
        }
    }

    public void remap()
    {
        OVRScene.RequestSpaceSetup();
    }


    // terzo metodo, fai il raycast sulle pareti della stanza
    public void placePaint_Room(DetectionInput data)
    {
        if (data == null || data.detected_quadri == null || data.detected_quadri.Length == 0)
        {
            Debug.LogWarning("JSON non valido o nessun 'quadro' rilevato.");
            return;
        }

        foreach (var quadro in data.detected_quadri)
        {
            var r = ScreenPointToRayInWorldOnHistoricalPos(PassthroughCameraEye.Left,
                new Vector2Int((int)quadro.centerX, 960 - (int)quadro.centerY), // la coordinate Y sono invertite
                data.camera_pose);

            if (Physics.Raycast(r, out var centerHit, maxPlacementDistance, sceneMeshLayer))
            {
                Vector3 position = centerHit.point;
                Quaternion rotation = Quaternion.LookRotation(centerHit.normal);
                GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);
                debugText.text += "\n posizionato: " + position + " con rotazione: " + rotation.eulerAngles;


                Ray rightRay = ScreenPointToRayInWorldOnHistoricalPos(PassthroughCameraEye.Left,
                    new Vector2Int((int)(quadro.centerX + quadro.nWidth / 2),
                        960 - (int)quadro.centerY), // la coordinate Y sono invertite
                    data.camera_pose);
                Ray topRay = ScreenPointToRayInWorldOnHistoricalPos(PassthroughCameraEye.Left,
                    new Vector2Int((int)quadro.centerX,
                        960 - (int)(quadro.centerY + quadro.nHeight / 2)), // la coordinate Y sono invertite
                    data.camera_pose);

                Vector3 rightPoint;
                Vector3 topPoint;

                // Lancia il raggio per il bordo destro. Se fallisce, proietta il raggio sul piano del muro.
                if (Physics.Raycast(rightRay, out var rightHit, maxPlacementDistance, sceneMeshLayer))
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
                if (Physics.Raycast(topRay, out var topHit, maxPlacementDistance, sceneMeshLayer))
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


                Vector3 offsetDirection = centerHit.normal.normalized;
// Sposta leggermente l'oggetto indietro rispetto al muro (es. 0.01 unità)
                float offsetDistance = 0.01f;
                instantiatedObject.transform.position += offsetDirection * offsetDistance;

                // Applica la scala. Assumendo che l'oggetto sia 1x1, la scala è direttamente la dimensione calcolata.
                // Manteniamo la scala Z originale del prefab per evitare di dargli uno spessore strano.
                instantiatedObject.transform.localScale =
                    new Vector3(worldWidth, worldHeight, instantiatedObject.transform.localScale.z);


                instantiatedObject.layer = LayerMask.NameToLayer("Paintings");


                instantiatedObject.name = $"Painting:{data.id}";
                debugText.text += "\n posizionato: " + position + " con rotazione: " + rotation.eulerAngles +
                                  "\n larghezza: " + worldWidth + " altezza: " + worldHeight + " id: " + data.id;
            }
            else
            {
                debugText.text += "\n Non ho preso nulla";
            }
        }
    }


    //second approach, using the PassthroughCameraUtil to place objects in the scene, funziona benissimo ma ha un problema: realtime, the raycast can hit only on the FOV
    public void placePaint_PC(DetectionInput data)
    {
        if (data == null || data.detected_quadri == null || data.detected_quadri.Length == 0)
        {
            Debug.LogWarning("JSON non valido o nessun 'quadro' rilevato.");
            return;
        }

        debugText.text += "\n " + mainCamera.pixelWidth + "x" + mainCamera.pixelHeight + "\n\n";

        foreach (var quadro in data.detected_quadri)
        {
            var r = ScreenPointToRayInWorldOnHistoricalPos(PassthroughCameraEye.Left,
                new Vector2Int((int)quadro.centerX, 960 - (int)quadro.centerY), // la coordinate Y sono invertite
                data.camera_pose);

            if (environmentRaycastManager.Raycast(r, out EnvironmentRaycastHit centerHit, maxPlacementDistance))
            {
                Vector3 position = centerHit.point;
                Quaternion rotation = Quaternion.LookRotation(centerHit.normal);
                GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);
                debugText.text += "\n posizionato: " + position + " con rotazione: " + rotation.eulerAngles;


                Ray rightRay = ScreenPointToRayInWorldOnHistoricalPos(PassthroughCameraEye.Left,
                    new Vector2Int((int)(quadro.centerX + quadro.nWidth / 2),
                        960 - (int)quadro.centerY), // la coordinate Y sono invertite
                    data.camera_pose);
                Ray topRay = ScreenPointToRayInWorldOnHistoricalPos(PassthroughCameraEye.Left,
                    new Vector2Int((int)quadro.centerX,
                        960 - (int)(quadro.centerY + quadro.nHeight / 2)), // la coordinate Y sono invertite
                    data.camera_pose);

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
            }
            else
            {
                debugText.text += "\n Non ho preso nulla";
            }
        }
    }

    // Method taken from PassthroughCameraUtils.cs, modified to place the object in the right position while moving
    public static Ray ScreenPointToRayInWorldOnHistoricalPos(PassthroughCameraEye cameraEye, Vector2Int screenPoint,
        CameraPoseData cameraPose)
    {
        var rayInCamera = PassthroughCameraUtils.ScreenPointToRayInCamera(cameraEye, screenPoint);
        var cameraPoseInWorld =
            new Pose(new Vector3(cameraPose.position.x, cameraPose.position.y, cameraPose.position.z),
                Quaternion.Euler(cameraPose.rotation.x, cameraPose.rotation.y, cameraPose.rotation.z));
        var rayDirectionInWorld = cameraPoseInWorld.rotation * rayInCamera.direction;
        return new Ray(cameraPoseInWorld.position, rayDirectionInWorld);
    }


// ---------------------- finish second approach ----------------------


    // First approach, not working properly becouse it not taking into account all the camera parameters
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

        /*placeObjectInPose(data.camera_pose, 0, 0);
        placeObjectInPose(data.camera_pose, 100, 100);
        placeObjectInPose(data.camera_pose, 0, mainCamera.pixelHeight);
        placeObjectInPose(data.camera_pose, mainCamera.pixelWidth, 0);
        placeObjectInPose(data.camera_pose, mainCamera.pixelWidth, mainCamera.pixelHeight);*/

        foreach (var quadro in data.detected_quadri)
        {
            // Applica la proporzione alle coordinate e dimensioni del quadro
            float scaledCenterX = quadro.centerX * scaleX;
            float scaledCenterY = quadro.centerY * scaleY;
            float scaledNWidth = quadro.nWidth * scaleX;
            float scaledNHeight = quadro.nHeight * scaleY;

            Debug.Log(
                $"Tentativo di posizionare l'oggetto per il quadro:");

            // 1. Crea il raggio dal centro del rilevamento (usando le coordinate scalate)
            Ray centerRay = CreateRayFromHistoricalPose(data.camera_pose, scaledCenterX, scaledCenterY);
            /*placeObjectInPose(data.camera_pose, scaledCenterX - scaledNWidth / 2, scaledCenterY - scaledNHeight / 2);
            placeObjectInPose(data.camera_pose, scaledCenterX + scaledNWidth / 2, scaledCenterY - scaledNHeight / 2);
            placeObjectInPose(data.camera_pose, scaledCenterX - scaledNWidth / 2, scaledCenterY + scaledNHeight / 2);
            placeObjectInPose(data.camera_pose, scaledCenterX + scaledNWidth / 2, scaledCenterY + scaledNHeight / 2);*/


            // 2. Esegui il Raycast per trovare il punto centrale sulla superficie
            if (environmentRaycastManager.Raycast(centerRay, out EnvironmentRaycastHit centerHit, maxPlacementDistance))
            {
                // --- POSIZIONAMENTO E ROTAZIONE ---
                Vector3 position = centerHit.point;
                Quaternion rotation = Quaternion.LookRotation(centerHit.normal);
                GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);


                /*GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);

                marker.transform.position = position;
                marker.transform.localScale = Vector3.one * 0.05f;


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

                Debug.LogWarning("pos: " + position);
                Debug.LogWarning("right " + rightPoint);
                Debug.LogWarning("left " + topPoint);

                // Applica la scala. Assumendo che l'oggetto sia 1x1, la scala è direttamente la dimensione calcolata.
                // Manteniamo la scala Z originale del prefab per evitare di dargli uno spessore strano.

                // instantiatedObject.transform.localScale =
                //  new Vector3(worldWidth, worldHeight, instantiatedObject.transform.localScale.z);
*/
                debugText.text += "\nOggetto posizionato";
            }
            else
            {
                debugText.text += "\nnulla";
                Debug.LogWarning(
                    $"Il raggio simulato non ha colpito nessuna superficie reale entro {maxPlacementDistance} metri. L'oggetto non può essere posizionato. Origine: {centerRay.origin}, Direzione: {centerRay.direction}");
            }
        }
    }


    private void placeObjectInPose(CameraPoseData poseData, float pixelX, float pixelY)
    {
        Ray ray = CreateRayFromHistoricalPose(poseData, pixelX, pixelY);

        if (environmentRaycastManager.Raycast(ray, out EnvironmentRaycastHit centerHit, maxPlacementDistance))
        {
            Vector3 position = centerHit.point;
            Quaternion rotation = Quaternion.LookRotation(centerHit.normal);
            GameObject instantiatedObject = Instantiate(objectToPlace, position, rotation);
        }
    }

    private Ray CreateRayFromHistoricalPose(CameraPoseData poseData, float pixelX, float pixelY)
    {
        // a. Ricostruisci la posizione e la rotazione storiche
        Vector3 historicalPosition = new Vector3(poseData.position.x, poseData.position.y, poseData.position.z);
        Quaternion historicalRotation = Quaternion.Euler(poseData.rotation.x, poseData.rotation.y, poseData.rotation.z);

        pixelY = mainCamera.pixelHeight - pixelY;
        //pixelX = mainCamera.pixelWidth - pixelX;

        // c. Usa ScreenPointToRay della telecamera di riferimento per ottenere una direzione
        Ray referenceRay = mainCamera.ScreenPointToRay(new Vector2(pixelX, pixelY));

        // d. Trasforma la direzione del raggio per allinearla alla rotazione storica
        Vector3 localDirection = mainCamera.transform.InverseTransformDirection(referenceRay.direction);
        Vector3 finalDirection = historicalRotation * localDirection;

        // e. Crea e restituisci il raggio finale con l'origine e la direzione corrette.
        return new Ray(historicalPosition, finalDirection.normalized);
    }

// ---------------------- end first approach ----------------------


    void Update()
    {
        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            var ray = new Ray(controllerRightHand.position, controllerRightHand.forward);

            if (Physics.Raycast(ray, out var hit, 100f, paintingLayer))
            {
                debugText.text +=
                    $"\nHai colpito: {hit.collider.gameObject.name} (Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}) ";
                var id_str = hit.collider.gameObject.name.Split(":")[1];
                var id = int.Parse(id_str);

                if (!paintingPannels.ContainsKey(id))
                {
                    var tr = PassthroughCameraUtils.GetCameraPoseInWorld(PassthroughCameraEye.Left);

                    Vector3 panelPosition = tr.position + tr.forward * 0.5f;
                    Quaternion panelRotation = Quaternion.Euler(tr.rotation.eulerAngles.x + 20f,
                        mainCamera.transform.eulerAngles.y, mainCamera.transform.eulerAngles.z);
                    GameObject instantiatedObject = Instantiate(panel, panelPosition, panelRotation);
                    instantiatedObject.name = $"panel:{id}";
                    
                    paintingPannels.Add(id, instantiatedObject);
                    
                    Debug.LogWarning("ok ho piazzato: ");
                    getRetrival(id);
                }
            }
        }
    }

    public void getRetrival(int id)
    {
        Debug.LogWarning("faccio partire la richiesta POST a: ");
        var ip = "192.168.186.71";
        var port = 3333;
        var endpoint = "/for_resnet_and_dino_ret";
        var url = $"http://{ip}:{port}{endpoint}";

        // Crea il payload JSON con l'id
        RetrievalRequestData requestData = new RetrievalRequestData { id = id };
        var payload = JsonUtility.ToJson(requestData);
            
       
        // Avvia una coroutine per la richiesta HTTP POST
        StartCoroutine(PostRequest(url, payload));
    }

    private IEnumerator PostRequest(string url, string json)
    {
        Debug.LogWarning("sono nella corunitne ");
        using (var www = new UnityWebRequest(url, "POST"))
        {
            Debug.LogWarning("sono nella using ");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");
            Debug.LogWarning("prima di invio ");
            yield return www.SendWebRequest();
            Debug.LogWarning("dopo di invio ");

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Risposta server: " + www.downloadHandler.text);
            }
            else
            {
                Debug.LogError("Errore richiesta: " + www.error);
            }
        }
    }
}

