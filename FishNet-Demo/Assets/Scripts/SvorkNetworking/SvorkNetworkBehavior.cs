using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SvorkNetworkBehavior : MonoBehaviour
{
    #region Public

    // all of these properties are synchronized across the network

    // which machine spawned this object
    public string SpawnerID = "";

    // which machine owns this object
    public string OwnerID = "";

    // network id of this object
    public int nobID = -1;

    public bool IsOwner => OwnerID == SvorkNetworkManager.instance.MyNetworkName;

    #endregion

    // after being instantiated on client, spawned on all clients

    // on awake
    // protected virtual void Awake() {
        // need to register this object with the network manager
        //

        // broadcast spawn to all clients
        
    // }

}
