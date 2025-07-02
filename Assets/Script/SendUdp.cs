using System;
using System.Collections;
using System.Net.Sockets;
using System.Threading.Tasks;
using Meta.Net.NativeWebSocket;
using PassthroughCameraSamples;
using TMPro;
using UnityEngine;

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

    private IEnumerator Start()
    {
        // Aspetta che la webcam sia pronta
        while (manager.WebCamTexture == null || !manager.WebCamTexture.isPlaying)
        {
            yield return null;
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
                Vector3 camPosition = ovrCameraRig.transform.position;
                Quaternion camRotation = ovrCameraRig.transform.rotation;

                // Leggi i pixel e aggiorna la texture
                pixelBuffer = webcam.GetPixels32();
                reusableTexture.SetPixels32(pixelBuffer);
                reusableTexture.Apply();

                byte[] jpgBytes = reusableTexture.EncodeToJPG(80); // qualità JPEG bassa = meno lag


                FrameData dataToSend = new FrameData
                {
                    frame = Convert.ToBase64String(jpgBytes),
                    camera_pose = new CameraPoseData
                    {
                        position = new PositionData { x = camPosition.x, y = camPosition.y, z = camPosition.z },
                        rotation = new RotationData
                        {
                            x = camRotation.eulerAngles.x, y = camRotation.eulerAngles.y, z = camRotation.eulerAngles.z
                        }
                    }
                };


                // Invio in background per non bloccare il main thread
                Task.Run(() => SendFrame(dataToSend));
            }
        }
    }

    async private void SendFrame(FrameData frameData)
    {
        try
        {
            // Serializza in JSON e invia
            string json = JsonUtility.ToJson(frameData);
            if (websocket.State == WebSocketState.Open)
            {
                await websocket.SendText(json);
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
}