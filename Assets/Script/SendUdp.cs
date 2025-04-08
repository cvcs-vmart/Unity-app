using System;
using System.Collections;
using System.Net.Sockets;
using PassthroughCameraSamples;
using TMPro;
using UnityEngine;

public class SendUdp : MonoBehaviour
{
    [SerializeField] private WebCamTextureManager manager;

    private UdpClient udpClient;

    private bool isSending = false;
    private Coroutine coruSend;

    private string ip = "192.168.210.18";
    private int port = 12345;

    private IEnumerator Start()
    {
        udpClient = new UdpClient();
        udpClient.Client.SendBufferSize = 65507; // Max dimensione UDP
        while (manager.WebCamTexture == null)
        {
            yield return null;
        }
        
    }

    public void toggleSendStream()
    {
        if (!isSending)
        {
            coruSend = StartCoroutine(CaptureFrames());
        }
        else
        {
            StopCoroutine(coruSend);
        }

        isSending = !isSending;
    }

    public void setIP(string ip)
    {
        Debug.Log("passato: " +ip);
        this.ip = ip;
    }

    public void setport(string port)
    {
        if (int.TryParse(port, out int risultato))
        {
            this.port = risultato;
        }
    }

    IEnumerator CaptureFrames()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();

            var screen = manager.WebCamTexture;

            if (screen != null && screen.didUpdateThisFrame)
            {
                // Crea una Texture2D per catturare il frame
                Texture2D tex = new Texture2D(screen.width, screen.height, TextureFormat.RGB24, false);
                tex.SetPixels(screen.GetPixels());
                tex.Apply();

                // Codifica la texture in formato JPEG (modifica la qualità se necessario)
                byte[] imageBytes = tex.EncodeToJPG();

                // Invia i dati via UDP
                SendFrame(imageBytes);

                // Pulisci la texture per evitare memory leak
                Destroy(tex);
            }
        }
    }


    private void SendFrame(byte[] frameData)
    {
        try
        {
            // Suddividi in chunk (necessario per UDP)
            int maxChunkSize = 65007; // Max per pacchetto UDP
            int totalChunks = (int)Math.Ceiling((double)frameData.Length / maxChunkSize);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * maxChunkSize;
                int chunkSize = Math.Min(maxChunkSize, frameData.Length - offset);
                byte[] chunk = new byte[chunkSize + 5];

                // Header: [T]otal chunks + [C]hunk index
                chunk[0] = (byte)totalChunks;
                Buffer.BlockCopy(BitConverter.GetBytes(i), 0, chunk, 1, 4);
                Buffer.BlockCopy(frameData, offset, chunk, 5, chunkSize);

                udpClient.Send(chunk, chunk.Length, ip, port);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Errore invio frame: {e.Message}");
        }
    }

    void OnDestroy()
    {
        udpClient?.Close();
    }
}