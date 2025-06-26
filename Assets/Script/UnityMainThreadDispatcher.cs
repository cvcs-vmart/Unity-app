using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A thread-safe class which holds a queue of actions to be executed on the main thread. It can be used to make calls to the Unity API from threads other than the main thread.
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher _instance;
    private static bool _initialized = false;
    private static readonly object _lockObject = new object();

    /// <summary>
    /// Inizializzare il dispatcher all'avvio dell'applicazione
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (!_initialized)
        {
            GameObject obj = new GameObject("UnityMainThreadDispatcher");
            _instance = obj.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(obj);
            _initialized = true;
            Debug.Log("UnityMainThreadDispatcher inizializzato sul thread principale");
        }
    }

    public static UnityMainThreadDispatcher Instance()
    {
        // Non creare istanze dal thread secondario
        return _instance;
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _initialized = true;
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    public void Enqueue(Action action)
    {
        if (action == null) return;
        
        lock (_lockObject)
        {
            _executionQueue.Enqueue(action);
        }
    }

    void Update()
    {
        Action[] actionsToExecute = null;
        
        lock (_lockObject)
        {
            if (_executionQueue.Count > 0)
            {
                actionsToExecute = new Action[_executionQueue.Count];
                _executionQueue.CopyTo(actionsToExecute, 0);
                _executionQueue.Clear();
            }
        }

        if (actionsToExecute != null)
        {
            foreach (var action in actionsToExecute)
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Errore nell'esecuzione dell'azione: {e.Message}");
                }
            }
        }
    }
}
