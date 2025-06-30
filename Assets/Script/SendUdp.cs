using System;
using System.Collections;
using System.Net.Sockets;
using System.Threading.Tasks;
using PassthroughCameraSamples;
using TMPro;
using UnityEngine;

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
    public OVRCameraRig ovrCameraRig; // Assign this in the Inspector

    public PaintingPlacer paintingPlacer; //test

    private IEnumerator Start()
    {
        udpClient = new UdpClient();
        udpClient.Client.SendBufferSize = 65507;

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

    public void toggleSendStream()
    {
        Vector3 cameraPosition = ovrCameraRig.leftEyeAnchor.position;
        Quaternion cameraRotation = ovrCameraRig.leftEyeAnchor.rotation;


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
            coruSend = StartCoroutine(CaptureFrames());
        else
            StopCoroutine(coruSend);

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
                // Leggi i pixel e aggiorna la texture
                pixelBuffer = webcam.GetPixels32();
                reusableTexture.SetPixels32(pixelBuffer);
                reusableTexture.Apply();

                byte[] jpg = reusableTexture.EncodeToJPG(80); // qualità JPEG bassa = meno lag

                // Invio in background per non bloccare il main thread
                Task.Run(() => SendFrame(jpg));
            }
        }
    }

    private void SendFrame(byte[] frameData)
    {
        try
        {
            int maxChunkSize = 65007;
            int totalChunks = (int)Math.Ceiling((double)frameData.Length / maxChunkSize);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * maxChunkSize;
                int chunkSize = Math.Min(maxChunkSize, frameData.Length - offset);
                byte[] chunk = new byte[chunkSize + 5];

                chunk[0] = (byte)totalChunks;
                Buffer.BlockCopy(BitConverter.GetBytes(i), 0, chunk, 1, 4);
                Buffer.BlockCopy(frameData, offset, chunk, 5, chunkSize);

                udpClient.Send(chunk, chunk.Length, ip, port);
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