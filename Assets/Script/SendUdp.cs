using System;
using System.Collections;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Meta.Net.NativeWebSocket;
using Meta.XR;
using PassthroughCameraSamples;
using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;

[Serializable]
public class FrameData
{
    public string frame; // Frame codificato in Base64
    public CameraPoseData camera_pose;
}


public class SendUdp : MonoBehaviour
{
    [SerializeField] private WebCamTextureManager manager;

    private UdpClient udpClient;
    private bool isSending = false;
    private Coroutine coruSend;

    private string ip = "192.168.204.71";
    private int port = 12345;

    private Texture2D reusableTexture;
    private Color32[] pixelBuffer;
    private WaitForSeconds frameDelay = new WaitForSeconds(0.1f); // 10 FPS
    public TextMeshProUGUI debugText;
    public Camera ovrCameraRig; // Assign this in the Inspector

    public PaintingPlacer paintingPlacer; //test

    private WebSocket websocket;

    const float originalScreenWidth = 1280.0f;
    const float originalScreenHeight = 960.0f;

    private EnvironmentRaycastManager environmentRaycastManager;

    private IEnumerator Start()
    {
        // Aspetta che la webcam sia pronta
        while (manager.WebCamTexture == null || !manager.WebCamTexture.isPlaying)
        {
            yield return null;
        }

        environmentRaycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
        if (environmentRaycastManager == null)
        {
            Debug.LogError(
                "environmentRaycastManager non trovato.");
        }

        int width = manager.WebCamTexture.width;
        int height = manager.WebCamTexture.height;

        reusableTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        pixelBuffer = new Color32[width * height];
    }

    async public void toggleSendStream()
    {
        Vector3 cameraPosition = ovrCameraRig.transform.position;
        Quaternion cameraRotation = ovrCameraRig.transform.rotation;

        Debug.Log($"Passthrough Camera Position: {cameraPosition}");
        Debug.Log($"Passthrough Camera Rotation: {cameraRotation.eulerAngles}");

        debugText.text = $"Visore Position: {cameraPosition}, Rotation: {cameraRotation.eulerAngles}";


        /*   DetectedQuadroData q = new DetectedQuadroData
           {
               id = "quadro_1",
               nx = 0.55f,
               ny = 0.40f,
               nwidth = 0.0005f,
               nheight = 0.0005f,
               confidence = 0.92f
           };
           DetectedQuadroData[] p = new DetectedQuadroData[1];


           PositionData sc = new PositionData
           {
               x = cameraPosition.x,
               y = cameraPosition.y,
               z = cameraPosition.z
           };

           RotationData r = new RotationData
           {
               x = cameraRotation.eulerAngles.x,
               y = cameraRotation.eulerAngles.y,
               z = cameraRotation.eulerAngles.z
           };

           CameraPoseData cameraPose = new CameraPoseData
           {
               position = sc,
               rotation = r
           };


           p[0] = q;
           var s = new DetectionInput
           {
               detected_quadri = p,
               camera_pose = cameraPose
           };
           // paintingPlacer.placePaint(s);*/


        if (!isSending)
        {
            websocket = new WebSocket("ws://" + ip + ":" + port);

            websocket.OnOpen += () =>
            {
                Debug.Log("Connessione WebSocket aperta!");
                isSending = true;
                coruSend = StartCoroutine(CaptureFrames());
            };

            websocket.OnError += (e) =>
            {
                Debug.LogError("Errore WebSocket " + "ws://" + ip + ":" + port + ": " + e);
                isSending = false;
            };

            websocket.OnClose += (e) =>
            {
                Debug.Log("Connessione WebSocket chiusa!");
                isSending = false;
            };

            await websocket.Connect();
        }
        else
        {
            websocket.Close();
            StopCoroutine(coruSend);
        }

        isSending = !isSending;
    }

    public void setIP(string ip) => this.ip = ip;

    public void setport(string port)
    {
        if (int.TryParse(port, out int p))
            this.port = p;
    }

    IEnumerator CaptureFrames()
    {
        while (true)
        {
            yield return frameDelay;
            //yield return new WaitForEndOfFrame();

            var webcam = manager.WebCamTexture;

            if (webcam != null && webcam.didUpdateThisFrame)
            {
                var r = PassthroughCameraUtils.GetCameraPoseInWorld(PassthroughCameraEye.Left);
                Vector3 camPosition = r.position;
                Quaternion camRotation = r.rotation;

                // Leggi i pixel e aggiorna la texture
                pixelBuffer = webcam.GetPixels32();
                reusableTexture.SetPixels32(pixelBuffer);
                reusableTexture.Apply();

                byte[] jpgBytes = reusableTexture.EncodeToJPG(80);

                // Invio in background per non bloccare il main thread
                Task.Run(() => SendFrame(camPosition, camRotation, jpgBytes));
            }
        }
    }

    async private void SendFrame(Vector3 camPosition, Quaternion camRotation, byte[] jpgBytes)
    {
        FrameData dataToSend = new FrameData
        {
            camera_pose = new CameraPoseData
            {
                position = new PositionData { x = camPosition.x, y = camPosition.y, z = camPosition.z },
                rotation = new RotationData
                {
                    x = camRotation.eulerAngles.x, y = camRotation.eulerAngles.y, z = camRotation.eulerAngles.z
                }
            }
        };

        string json = JsonUtility.ToJson(dataToSend);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] jsonLengthBytes = BitConverter.GetBytes(jsonBytes.Length);

        byte[] messageBytes = new byte[jsonLengthBytes.Length + jsonBytes.Length + jpgBytes.Length];
        jsonLengthBytes.CopyTo(messageBytes, 0);
        jsonBytes.CopyTo(messageBytes, jsonLengthBytes.Length);
        jpgBytes.CopyTo(messageBytes, jsonLengthBytes.Length + jsonBytes.Length);


        try
        {
            if (websocket.State == WebSocketState.Open)
            {
                await websocket.Send(messageBytes);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Errore invio frame: " + e.Message);
        }
    }

    private void OnDestroy()
    {
        udpClient?.Close();
        if (reusableTexture != null)
            Destroy(reusableTexture);
    }

    /*
    test of raycast grid more points
    public void RaycastGridOnScreen(int gridSize)
    {
        if (environmentRaycastManager == null)
        {
            Debug.LogError(" environmentRaycastManager non assegnati.");
            return;
        }


        float stepX = originalScreenWidth / gridSize;
        float stepY = originalScreenHeight / gridSize;

        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                int x = (int)((i + 0.5f) * stepX);
                int y = (int)((j + 0.5f) * stepY);
                Ray ray = PassthroughCameraUtils.ScreenPointToRayInWorld(PassthroughCameraEye.Left,
                    new Vector2Int(x, y));
                if (environmentRaycastManager.Raycast(ray, out EnvironmentRaycastHit hit, 300))
                {
                }
            }
        }
    }

    public TimeSpan MeasureRaycastGridTime(int gridSize)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        RaycastGridOnScreen(gridSize);
        stopwatch.Stop();
        debugText.text +=
            $"\n Tempo impiegato per il raycast su una griglia {gridSize}x{gridSize}: {stopwatch.ElapsedMilliseconds} ms";
        return stopwatch.Elapsed;
    }*/
}