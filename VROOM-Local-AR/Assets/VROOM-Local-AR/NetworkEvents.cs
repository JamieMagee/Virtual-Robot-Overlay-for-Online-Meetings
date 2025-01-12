﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading;
//using System.Threading.Tasks;

[Serializable]
public class NetworkEvent
{
    public string EventName;
    public string EventData;

    //public NetworkEvent(string eventName, string eventData)
    //{
    //    this.EventName = eventName;
    //    this.EventData = eventData;
    //}

    //public string GetJson()
    //{
    //    return "{ \"EventName\": \"" + ((this.EventName != null && this.EventName != "") ? this.EventName : "0") +
    //        "\", \"EventData\": \"" + ((this.EventData != null && this.EventData != "") ? this.EventData : "0") + "\" }";
    //}
}

public class NetworkEvents : MonoBehaviour
{
    public bool AutoLogErrors = true;
    public string LocalPeerId;
    public string RemotePeerId;
    public string HttpServerAddress = "http://127.0.0.1:3000/";
    public float PollTimeMs = 500f;
    //private float timeSincePollMs = 0f;
    //private bool lastGetComplete = true;
    //private ConcurrentQueue<Action> _mainThreadWorkQueue = new ConcurrentQueue<Action>();

    public delegate void NetworkEventHandler(string data);

    private Dictionary<string, NetworkEventHandler> networkEventHandlers = new Dictionary<string, NetworkEventHandler>();
    private Dictionary<string, float> timeSincePollMs = new Dictionary<string, float>();
    private Dictionary<string, bool> lastGetComplete = new Dictionary<string, bool>();

    private void Start()
    {
        if (string.IsNullOrEmpty(HttpServerAddress))
        {
            throw new ArgumentNullException("HttpServerAddress");
        }
        if (!HttpServerAddress.EndsWith("/"))
        {
            HttpServerAddress += "/";
        }

        // If not explicitly set, default local ID to some unique ID generated by Unity
        if (string.IsNullOrEmpty(LocalPeerId))
        {
            LocalPeerId = SystemInfo.deviceUniqueIdentifier;
        }
    }

    // Update is called once per frame
    private void Update()
    {
        //// Execute any pending work enqueued by background tasks
        //while (_mainThreadWorkQueue.TryDequeue(out Action workload))
        //{
        //    workload();
        //}

        foreach (string eventName in networkEventHandlers.Keys)
        {
            // if we have not reached our PollTimeMs value...
            if (timeSincePollMs[eventName] <= PollTimeMs)
            {
                // we keep incrementing our local counter until we do.
                timeSincePollMs[eventName] += Time.deltaTime * 1000.0f;
                continue;
            }

            // if we have a pending request still going, don't queue another yet.
            if (!lastGetComplete[eventName])
            {
                continue;
            }

            // when we have reached our PollTimeMs value...
            timeSincePollMs[eventName] = 0f;

            // begin the poll and process.
            lastGetComplete[eventName] = false;
            StartCoroutine(CO_GetAndProcessFromServer(eventName));
        }
    }

    public bool AddHandler(string eventName, NetworkEventHandler eventHandler)
    {
        if (networkEventHandlers.ContainsKey(eventName))
        {
            return false;
        }
        else
        {
            networkEventHandlers.Add(eventName, eventHandler);
            timeSincePollMs.Add(eventName, float.MaxValue);
            lastGetComplete.Add(eventName, true);

            return true;
        }
    }

    public bool RemoveHandler(string eventName)
    {
        if (networkEventHandlers.ContainsKey(eventName))
        {
            networkEventHandlers.Remove(eventName);
            timeSincePollMs.Remove(eventName);
            lastGetComplete.Remove(eventName);

            return true;
        }
        else
        {
            return false;
        }
    }

    public void PostEventMessage(NetworkEvent networkEvent)
    {
        StartCoroutine(postEvtMssg(networkEvent));
    }

    private IEnumerator postEvtMssg(NetworkEvent networkEvent)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(networkEvent));
        var www = new UnityWebRequest(HttpServerAddress + "event/" + RemotePeerId, UnityWebRequest.kHttpVerbPOST);
        www.uploadHandler = new UploadHandlerRaw(data);

        yield return www.Send();

        if (AutoLogErrors && (www.isError))
        {
            Debug.Log("Failure posting event: " + www.error);
        }
    }

    private IEnumerator CO_GetAndProcessFromServer(string eventName)
    {
        var www = UnityWebRequest.Get(HttpServerAddress + "event/" + LocalPeerId + "/" + eventName);
        yield return www.Send();

        if (!www.isError)
        {
            var json = www.downloadHandler.text;

            try
            {
                NetworkEvent networkEvent = JsonUtility.FromJson<NetworkEvent>(json);

                // if the message is good
                if (networkEvent != null)
                {
                    NetworkEventHandler eventHandler;
                    if (networkEventHandlers.TryGetValue(networkEvent.EventName, out eventHandler))
                    {
                        eventHandler.Invoke(networkEvent.EventData);
                    }
                }
                else if (AutoLogErrors)
                {
                    Debug.LogError("Failed to deserialize JSON message : " + json);
                }
            }
            catch (System.Exception exception)
            {
                Debug.Log("Error: " + exception);
                lastGetComplete[eventName] = true;
            }
        }
        else if (AutoLogErrors && www.isError)
        {
            Debug.LogError("Error trying to get data from " + HttpServerAddress + " : " + www.error);
        }
        else
        {
            // This is very spammy because the node-dss protocol uses 404 as regular "no data yet" message, which is an HTTP error
            //Debug.LogError("HTTP error: " + www.error);
        }

        lastGetComplete[eventName] = true;
    }
}
