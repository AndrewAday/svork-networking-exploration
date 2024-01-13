using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using extOSC;
using UnityEngine.Events;
using System.Diagnostics;
using UnityEditor;
using UnityEngine.Assertions;


/*
Svork custom Network Manager Singleton
*/

public enum SvorkRole {
    Performer = 0,
    Audience = 1
}

public class SvorkNetworkManager : MonoBehaviour
{
    public static SvorkNetworkManager instance;

    public int Port = 6449;

    public SvorkRole Role = SvorkRole.Performer;

    public GameObject PlayerPrefab;

    private string LocalIP = "";

    public string MyNetworkName = "";

#region OSC Components 
    private OSCTransmitter Transmitter;
    private OSCReceiver Receiver;
#endregion

    Dictionary<int, GameObject> NetworkObjects = new Dictionary<int, GameObject>();

    void Awake()
    {
        if (instance != null) {
            UnityEngine.Debug.Log("SvorkNetworkManager already exists, destroying duplicate");
            Destroy(this);
            return;
        }

        instance = this;
        DontDestroyOnLoad(this);  // Keep this object alive between scenes

        // change the port for Parrel Sync Clones (to prevent OSC collisions)
        #if UNITY_EDITOR
        if (ParrelSync.ClonesManager.IsClone()) {
            // convert arg to index
            string cloneArg = ParrelSync.ClonesManager.GetArgument();
            int cloneIndex = int.Parse(cloneArg);

            Port = Port + cloneIndex + 1;

            UnityEngine.Debug.Log($"ParrelSync Clone detected, changing port to {Port}");
        }
        #endif

        // Get Local IP
        IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (IPAddress ip in host.AddressList) {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
                UnityEngine.Debug.Log($"Local IP: {ip.ToString()} | Port: {Port}");
                LocalIP = ip.ToString();
                break;
            }
        }

        // Create OSC Transmitter  (note address and port are set in the editor, must match Svork Network Server)
        Transmitter = gameObject.GetComponent<OSCTransmitter>() ?? gameObject.AddComponent<OSCTransmitter>();

        Receiver = gameObject.GetComponent<OSCReceiver>() ?? gameObject.AddComponent<OSCReceiver>();
        Receiver.LocalPort = Port;

        // set up spawn listener
        string spawnAddress = "/svork/client/spawn";
        Receiver.Bind(spawnAddress, (OSCMessage message) => {
            int nobID = message.Values[1].IntValue;

            // if nobID already registered, ignore. Avoids duplicate spawns
            if (NetworkObjects.ContainsKey(nobID)) return;

            string senderNetworkName = message.Values[0].StringValue;
            string prefabName = message.Values[2].StringValue;

            // Create the prefab
            GameObject prefab = Resources.Load<GameObject>(prefabName);
            Assert.IsNotNull(prefab, $"Prefab {prefabName} not found in Resources");

            GameObject go = Instantiate(prefab);
            SvorkNetworkBehavior snb = go.GetComponent<SvorkNetworkBehavior>();
            Assert.IsNotNull(snb, $"Prefab {prefabName} must have a SvorkNetworkBehavior component");

            // set network properties
            snb.SpawnerID = senderNetworkName;
            snb.OwnerID = senderNetworkName;
            snb.nobID = nobID;

            // add to map
            NetworkObjects.Add(nobID, go);

            // notify server that we spawned
            // doesn't need to be safe, as server is the one retrying (not us the clients)
            Send(spawnAddress + "/received", OSCValue.String(MyNetworkName), OSCValue.Int(nobID));
        });

        // transform receiver
        string transformAddress = "/svork/client/transform";
        Receiver.Bind(transformAddress, (OSCMessage message) => {
            string senderNetworkName = message.Values[0].StringValue;
            int nobID = message.Values[1].IntValue;

            // if nobID not registered, ignore.
            if (!NetworkObjects.ContainsKey(nobID)) return;

            // update transform
            GameObject go = NetworkObjects[nobID];
            go.transform.position = new Vector3(
                message.Values[2].FloatValue,
                message.Values[3].FloatValue,
                message.Values[4].FloatValue
            );
            // go.transform.rotation = new Quaternion(
            //     message.Values[5].FloatValue,
            //     message.Values[6].FloatValue,
            //     message.Values[7].FloatValue,
            //     message.Values[8].FloatValue
            // );
        });


        // set network ID as IP:port
        MyNetworkName = $"{LocalIP}:{Port}";
    }


    // Start is called before the first frame update
    void Start()
    {   
        // NOTE: OSC transmission must happen AFTER Awake()
        // NOTE: can enable auto-pack bundling on Transmitter to send all OSC
        // messages within a frame as a single bundle
        //     Don't know if Chuck supports parsing bundles

        // Register client with Svork Network Server
        // var message = new OSCMessage("/svork/connect");
        // message.AddValue(OSCValue.String(LocalIP));
        // message.AddValue(OSCValue.Int(Port));
        // Transmitter.Send(message);  // TODO: this should be a SafeSend, i.e. 2-way verify

        // TODO: register as performer or audience
        SafeSend(
            "/svork/safe/connect", 
            (OSCMessage message) => {
                // TODO: save my ID?
                UnityEngine.Debug.Log($@"
                    Svork Network Server received my connection
                    Client UID: {message.Values[0].Value}
                ");

                // spawn player
                Spawn(PlayerPrefab);
            },
            OSCValue.String(LocalIP), 
            OSCValue.Int(Port)
        );

    }

#region NetworkUtils

    public enum SvorkNetworkTarget {
        ALL = 0,  // can multicast
        PERFORMERS = 1,  // all performers
        AUDIENCE = 2,  // all audience
        ALL_BUT_ME = 3,
    }

    public GameObject Spawn(GameObject prefab) {
        // first instantiate the prefab locally (client authoritative)
        GameObject go = Instantiate(prefab);

        // check for SvorkNetworkBehavior
        SvorkNetworkBehavior snb = go.GetComponent<SvorkNetworkBehavior>();
        Assert.IsNotNull(snb, $"Prefab {prefab.name} must have a SvorkNetworkBehavior component");

        // set network properties
        snb.SpawnerID = MyNetworkName;
        snb.OwnerID = MyNetworkName;
        snb.nobID = -1;  // set to -1 to indicate not yet registered

        // request server for new nobID
        SafeSend(
            "/svork/safe/nextNobID",
            (OSCMessage message) => {
                int newNobID = message.Values[0].IntValue;
                snb.nobID = newNobID;
                // add to map
                NetworkObjects.Add(newNobID, go);

                UnityEngine.Debug.Log($"Assigned nobID {newNobID} to {go.name}");

                // broadcast spawn to all other clients
                Send(  // TODO: change to safe
                    "/svork/relay",  // TODO: change to safe eventually 
                    OSCValue.Int( (int) SvorkNetworkTarget.ALL_BUT_ME ),  // Spawner ID
                    OSCValue.String("/svork/client/spawn"),  // Owner ID
                    OSCValue.String(MyNetworkName),
                    OSCValue.Int(newNobID),
                    OSCValue.String(prefab.name)
                    // TODO: parent nob ID
                );
            },
            OSCValue.String(MyNetworkName)
        );


        // SafeSend("/svork/safe/spawn", 
        //     (OSCMessage message) => {
                // actually we instantiate on RECEIVE

                // // instantiate the guy
                // GameObject go = Instantiate(prefab);
                // // check for SvorkNetworkBehavior
                // SvorkNetworkBehavior snb = go.GetComponent<SvorkNetworkBehavior>();
                // if (snb) {
                //     snb.SpawnerID = message.Values[0].StringValue;
                //     snb.OwnerID = message.Values[1].StringValue;
                //     snb.nobID = message.Values[2].IntValue;

                //     UnityEngine.Debug.Log($"Spawned {go.name} with ID {snb.nobID} by {snb.SpawnerID} for {snb.OwnerID}");
                // }
            // },
            // OSCValue.String(MyNetworkName),  // Spawner ID
            // OSCValue.String(prefab.name)
        // );

        return go;
    }

    public void SendTransform(SvorkNetworkBehavior snb) {
        // send transform data along with nobID
        Send(
            "/svork/relay",
            OSCValue.Int( (int) SvorkNetworkTarget.ALL_BUT_ME ),  // client authoritative
            OSCValue.String("/svork/client/transform"),  // Owner ID
            OSCValue.String(MyNetworkName),
            OSCValue.Int(snb.nobID),
            OSCValue.Float(snb.transform.position.x),
            OSCValue.Float(snb.transform.position.y),
            OSCValue.Float(snb.transform.position.z)
            // OSCValue.Float(go.transform.rotation.x),
            // OSCValue.Float(go.transform.rotation.y),
            // OSCValue.Float(go.transform.rotation.z),
            // OSCValue.Float(go.transform.rotation.w)
        );
    }

    // not safe! no confirmation of receipt
    public void Send(string oscAddress, params OSCValue[] values) {
        var message = new OSCMessage(oscAddress, values);
        Transmitter.Send(message);
    }

    // generic method for sending OSC messages and receiving a confirmation
    public void SafeSend(
        string oscAddress,
        UnityAction<OSCMessage> receiptCallback,
        params OSCValue[] values
    ) {
        var message = new OSCMessage(oscAddress, values);
        StartCoroutine(
            _SafeSendImpl( message, oscAddress + "/received", receiptCallback)
        );
    }

    // coroutine for safe sending OSC messages
    IEnumerator _SafeSendImpl(
        OSCMessage message,
        string receiptAddress,
        UnityAction<OSCMessage> receiptCallback,
        float retryInterval = .1f,  // 100ms default idk
        int maxAttempts = int.MaxValue
        ) {
        bool received = false;
        // instantiate receiver to confirm message receipt
        var bind = new OSCBind(receiptAddress, (OSCMessage message) => {
            if (received) return;  // ignore duplicate receipts

            // mark message as received
            received = true;  // use dat closure

            // execute callback
            receiptCallback(message);
            
            UnityEngine.Debug.Log("Message received");
        });
        Receiver.Bind(bind);

        for (int i = 0; i < maxAttempts; i++) {
            Transmitter.Send(message);
            // TODO: maybe wait for frame boundary instead of fixed time
            yield return new WaitForSeconds(retryInterval);
            if (received) {
                Receiver.Unbind(bind);
                break;
            }
        }
    }



#endregion

}
