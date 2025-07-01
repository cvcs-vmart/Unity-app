using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
                    debugText.text =
                        $"Server running on Quest\nConnect to: http://{localIP}:{currentPort}/post_detections";

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
                                debugText.text = capturedRequestBody;

                                Debug.LogError("debug text " + capturedRequestBody);

                                // paintingPlacer.PlaceObjectFromJSON(capturedRequestBody);
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
            else
            {
                // Gestisci altre richieste o restituisci un errore 404
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
}