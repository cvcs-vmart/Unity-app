using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using PassthroughCameraSamples;
using TMPro;
using UnityEngine;

// Per List
// 192.168.204.246

// Ancora necessario per il parsing JSON

public class UnityHTTPServer : MonoBehaviour
{
    // Usare '*' è corretto, ma aggiungeremo anche l'IP specifico per maggiore affidabilità.
    public string serverPort = "8080";
    private HttpListener listener;
    private Thread listenerThread;
    private bool isListening = true;
    public TextMeshProUGUI debugText;
    public TextMeshProUGUI port;
    public TextMeshProUGUI ip;
    private int currentPort;
    private bool serverStarted = false;

    // Riferimento al DetectionManager per aggiornare gli indicatori
    // public DetectionManager detectionManager; // Assegna questo nel Inspector

    // Riferimento a PaintingPlacer per piazzare i quadri
    public PaintingPlacer paintingPlacer;

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Controlla i permessi per Android (Meta Quest)
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission("android.permission.INTERNET"))
        {
            UnityEngine.Android.Permission.RequestUserPermission("android.permission.INTERNET");
        }
#endif

        // Verifica che il dispatcher sia inizializzato
        if (UnityMainThreadDispatcher.Instance() == null)
        {
            Debug.LogError(
                "UnityMainThreadDispatcher non è stato inizializzato correttamente. Le operazioni sul thread principale potrebbero fallire.");
        }

        // Prova ad avviare il server dopo un breve ritardo per permettere l'inizializzazione completa
        Invoke("InitializeHttpServer", 0.5f);
    }

    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }

        throw new Exception("No network adapters with an IPv4 address in the system!");
    }

    // Controlla se una porta è disponibile
    private bool IsPortAvailable(int port)
    {
        try
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    void InitializeHttpServer()
    {
        // Se il server è già stato avviato, non procedere
        if (serverStarted) return;

        // Prima chiudi qualsiasi listener precedente, per sicurezza
        StopHttpServer();

        // Prova le porte: la porta principale, poi +1, +2, ecc.
        int basePort = int.Parse(serverPort);
        int maxAttempts = 10; // Prova fino a 10 porte diverse

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            int portToTry = basePort + attempt;

            // Verifica se la porta è disponibile
            if (!IsPortAvailable(portToTry))
            {
                Debug.LogWarning($"Port {portToTry} is not available, trying next port...");
                continue;
            }

            currentPort = portToTry;

            try
            {
                listener = new HttpListener();

                string localIP = "127.0.0.1";
                try
                {
                    localIP = GetLocalIPAddress();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Could not get local IP address: {ex.Message}");
                }

                // Usa solo un prefisso generico per evitare conflitti
                string prefix = $"http://*:{currentPort}/";
                listener.Prefixes.Add(prefix);
                Debug.Log($"Trying to start server on: {prefix}");

                listener.Start();

                Debug.Log($"Unity HTTP Server started successfully on port {currentPort}");
                if (debugText != null)
                {
                    port.text = currentPort + "";
                    ip.text = localIP;
                }

                serverStarted = true;
                isListening = true;

                listenerThread = new Thread(new ThreadStart(ListenForRequests));
                listenerThread.IsBackground = true;
                listenerThread.Start();

                // Se arriviamo qui, il server è stato avviato con successo
                return;
            }
            catch (HttpListenerException ex)
            {
                Debug.LogError($"Failed to start HTTP server on port {currentPort}: {ex.Message}");

                // Chiudi il listener corrente prima di riprovare
                if (listener != null)
                {
                    try
                    {
                        listener.Close();
                    }
                    catch
                    {
                    }

                    listener = null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unexpected error starting HTTP server: {ex.Message}");

                // Chiudi il listener corrente prima di riprovare
                if (listener != null)
                {
                    try
                    {
                        listener.Close();
                    }
                    catch
                    {
                    }

                    listener = null;
                }
            }
        }

        // Se arriviamo qui, non siamo riusciti a trovare una porta disponibile
        Debug.LogError($"Failed to start HTTP server after {maxAttempts} attempts.");
        if (debugText != null)
            debugText.text = $"Server failed to start.\nCould not find available port.";
    }

    void ListenForRequests()
    {
        while (isListening && listener != null && listener.IsListening)
        {
            try
            {
                HttpListenerContext context = listener.GetContext(); // Blocca finché non arriva una richiesta
                ThreadPool.QueueUserWorkItem(o => ProcessRequest(context));
            }
            catch (HttpListenerException)
            {
                // Listener è stato interrotto (es. stop chiamato)
                if (isListening) Debug.LogError("HttpListenerException, but server is supposed to be listening.");
                break;
            }
            catch (ObjectDisposedException)
            {
                // Il listener è stato chiuso/abortito, è normale quando si chiude l'applicazione
                Debug.Log("Listener closed or aborted.");
                break;
            }
            catch (Exception ex)
            {
                Debug.LogError($"HTTP Listener Error: {ex.ToString()}");
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        try
        {
            Debug.Log("Received request from: " + request.RemoteEndPoint.ToString());

            // Aggiungi header CORS per consentire richieste da qualsiasi origine
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");

            // Gestione richiesta OPTIONS (pre-flight CORS)
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.Close();
                return;
            }

            if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/post_detections")
            {
                string requestBody;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = reader.ReadToEnd();
                }

                Debug.Log($"Received POST to /post_detections: {requestBody}");

                // Deserializza il JSON sul thread principale di Unity con gestione dell'assenza del dispatcher
                var dispatcher = UnityMainThreadDispatcher.Instance();
                if (dispatcher != null)
                {
                    string capturedRequestBody = requestBody;
                    dispatcher.Enqueue(() =>
                    {
                        try
                        {
                            if (debugText != null) debugText.text += "\n" + capturedRequestBody;
                            // Chiamata diretta al piazzamento quadri
                            if (paintingPlacer != null)
                            {
                                //debugText.text = capturedRequestBody;

                                Debug.LogError("debug text " + capturedRequestBody);

                                paintingPlacer.PlaceObjectFromJSON(capturedRequestBody);
                            }
                            else
                            {
                                Debug.LogError("paintingPlacer non assegnato su UnityHTTPServer!");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Error processing received data: {e.Message}\nJSON: {capturedRequestBody}");
                        }
                    });
                }
                else
                {
                    Debug.LogError(
                        "UnityMainThreadDispatcher non disponibile. Impossibile elaborare i dati ricevuti sul thread principale di Unity.");
                }

                // Invia una risposta di successo
                RespondWithJson(response, "{\"status\": \"success\", \"message\": \"Detections received by Unity\"}",
                    (int)HttpStatusCode.OK);
            }
            else if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/post_retrieval")
            {
                Debug.Log("Received POST to /post_retrieval");
                // Estrai il boundary dal Content-Type header
                string boundary = GetBoundary(request.ContentType);
                if (string.IsNullOrEmpty(boundary))
                {
                    RespondWithJson(response,
                        "{\"status\": \"error\", \"message\": \"Invalid Content-Type for multipart request\"}",
                        (int)HttpStatusCode.BadRequest);
                    return;
                }

                // Leggi il corpo della richiesta
                using (var memoryStream = new MemoryStream())
                {
                    request.InputStream.CopyTo(memoryStream);
                    byte[] requestBodyBytes = memoryStream.ToArray();

                    // Esegui il parsing del corpo multipart
                    ParsedMultipartData parsedData = ParseMultipartData(requestBodyBytes, boundary);
                    Debug.Log($"Parsed {parsedData.ImageDatas.Count} images and ID: '{parsedData.Id}' from the request.");

                    // Invia i dati delle immagini al thread principale di Unity per creare le Texture2D
                    var dispatcher = UnityMainThreadDispatcher.Instance();
                    if (dispatcher != null)
                    {
                        dispatcher.Enqueue(() =>
                        {
                            try
                            {
                                if (paintingPlacer != null)
                                {
                                    
                                    List<Texture2D> receivedPaintings = new List<Texture2D>();
                                    foreach (var imageData in parsedData.ImageDatas)
                                    {
                                        // Crea una nuova texture. Le dimensioni non sono importanti, LoadImage le adatterà.
                                        Texture2D tex = new Texture2D(2, 2);
                                        // Carica i dati dell'immagine (es. JPG o PNG) nella texture
                                        if (tex.LoadImage(imageData))
                                        {
                                            receivedPaintings.Add(tex);
                                        }
                                        else
                                        {
                                            Debug.LogError(
                                                "Impossibile caricare i dati di un'immagine in una Texture2D.");
                                        }
                                    }
                                    
                                    debugText.text += $"\nReceived {receivedPaintings.Count} paintings for ID: {parsedData.Id}.";
                                }
                                else
                                {
                                    Debug.LogError("paintingPlacer non assegnato su UnityHTTPServer!");
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.LogError($"Error processing received image data: {e.Message}");
                            }
                        });
                        RespondWithJson(response,
                            "{\"status\": \"success\", \"message\": \"Images and ID received by Unity\"}",
                            (int)HttpStatusCode.OK);
                    }
                    else
                    {
                        Debug.LogError("UnityMainThreadDispatcher non disponibile.");
                        RespondWithJson(response,
                            "{\"status\": \"error\", \"message\": \"Internal server error (dispatcher not found)\"}",
                            (int)HttpStatusCode.InternalServerError);
                    }
                }
            }
            else
            {
                RespondWithJson(response,
                    "{\"status\": \"error\", \"message\": \"Endpoint not found or method not allowed\"}",
                    (int)HttpStatusCode.NotFound);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error processing request: {ex.ToString()}");
            if (response.OutputStream.CanWrite)
            {
                RespondWithJson(response,
                    "{\"status\": \"error\", \"message\": \"An internal server error occurred.\"}",
                    (int)HttpStatusCode.InternalServerError);
            }
        }
        finally
        {
            response.Close();
        }
    }

    private void RespondWithJson(HttpListenerResponse response, string jsonString, int statusCode)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        byte[] buffer = Encoding.UTF8.GetBytes(jsonString);
        response.ContentLength64 = buffer.Length;
        try
        {
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Could not write response: {ex.Message}");
        }
    }

    // Funzione di utilità per trovare un array di byte in un altro array di byte
    private int IndexOf(byte[] searchIn, byte[] searchBytes, int start)
    {
        for (int i = start; i <= searchIn.Length - searchBytes.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < searchBytes.Length; j++)
            {
                if (searchIn[i + j] != searchBytes[j])
                {
                    found = false;
                    break;
                }
            }

            if (found) return i;
        }

        return -1;
    }

    void OnApplicationQuit()
    {
        StopHttpServer();
    }

    void OnDestroy()
    {
        StopHttpServer();
    }

    void OnApplicationPause(bool pause)
    {
        // Quando l'app va in pausa, assicurati di rilasciare le risorse di rete
        if (pause)
        {
            StopHttpServer();
        }
        else if (!serverStarted)
        {
            // Se l'app torna in primo piano e il server non è attivo, riavvia il server
            Invoke("InitializeHttpServer", 0.5f);
        }
    }

    void StopHttpServer()
    {
        if (!isListening) return;

        isListening = false;
        serverStarted = false;

        if (listener != null)
        {
            try
            {
                // Chiamare Abort invece di Stop/Close per sbloccare immediatamente GetContext()
                listener.Abort();
                listener.Close();
                Debug.Log("Unity HTTP Server stopped.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error stopping HTTP server: {ex.Message}");
            }
            finally
            {
                listener = null;
            }
        }

        if (listenerThread != null && listenerThread.IsAlive)
        {
            try
            {
                listenerThread.Join(1000); // Aspetta che il thread finisca, con un timeout
            }
            catch
            {
            }

            listenerThread = null;
        }
    }

    private string GetBoundary(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        string[] parts = contentType.Split(';');
        foreach (string part in parts)
        {
            string trimmedPart = part.Trim();
            if (trimmedPart.StartsWith("boundary="))
            {
                return trimmedPart.Substring("boundary=".Length);
            }
        }

        return null;
    }

    private class ParsedMultipartData
    {
        public List<byte[]> ImageDatas { get; } = new List<byte[]>();
        public string Id { get; set; }
    }
    
    private ParsedMultipartData ParseMultipartData(byte[] requestBody, string boundary)
    {
        var parsedData = new ParsedMultipartData();
        byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
    
        List<byte[]> parts = SplitByteArray(requestBody, boundaryBytes);
    
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
    
            int headerEndIndex = FindHeaderEnd(part);
            if (headerEndIndex == -1) continue;
    
            // Estrai gli header come stringa per analizzarli
            string headersString = Encoding.UTF8.GetString(part, 0, headerEndIndex);
            string contentDisposition = null;
            using (var reader = new StringReader(headersString))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase))
                    {
                        contentDisposition = line;
                        break;
                    }
                }
            }
    
            if (contentDisposition == null) continue;
    
            string name = null;
            string[] dispositionParts = contentDisposition.Split(';');
            foreach (string dispositionPart in dispositionParts)
            {
                string trimmedPart = dispositionPart.Trim();
                if (trimmedPart.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                {
                    name = trimmedPart.Substring("name=".Length).Trim('"');
                    break;
                }
            }
    
            if (string.IsNullOrEmpty(name)) continue;
    
            // Estrai il contenuto dopo gli header
            int contentStartIndex = headerEndIndex + 4; // Salta \r\n\r\n
            if (contentStartIndex >= part.Length) continue;
            
            // Rimuovi l'ultimo \r\n che precede il boundary successivo
            int contentLength = part.Length - contentStartIndex - 2;
            if (contentLength <= 0) continue;
    
            byte[] contentBytes = new byte[contentLength];
            Array.Copy(part, contentStartIndex, contentBytes, 0, contentLength);
    
            if (name.Equals("images", StringComparison.OrdinalIgnoreCase))
            {
                parsedData.ImageDatas.Add(contentBytes);
            }
            else if (name.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                parsedData.Id = Encoding.UTF8.GetString(contentBytes);
            }
        }
    
        return parsedData;
    }

    private int FindHeaderEnd(byte[] data)
    {
        byte[] sequence = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' }; // CRLF CRLF
        for (int i = 0; i < data.Length - sequence.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < sequence.Length; j++)
            {
                if (data[i + j] != sequence[j])
                {
                    found = false;
                    break;
                }
            }

            if (found) return i;
        }

        return -1;
    }

    private List<byte[]> SplitByteArray(byte[] source, byte[] separator)
    {
        var parts = new List<byte[]>();
        int lastIndex = 0;
        int currentIndex;
        while ((currentIndex = IndexOf(source, separator, lastIndex)) != -1)
        {
            int length = currentIndex - lastIndex;
            if (length > 0)
            {
                byte[] part = new byte[length];
                Array.Copy(source, lastIndex, part, 0, length);
                parts.Add(part);
            }

            lastIndex = currentIndex + separator.Length;
        }

        // Aggiungi l'ultima parte se esiste
        if (lastIndex < source.Length)
        {
            int length = source.Length - lastIndex;
            byte[] part = new byte[length];
            Array.Copy(source, lastIndex, part, 0, length);
            parts.Add(part);
        }

        return parts;
    }
}

