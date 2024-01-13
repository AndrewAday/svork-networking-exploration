using System.Collections;
using System.Collections.Generic;
using extOSC.Core;
using UnityEngine;

public class SvorkPlayerController : SvorkNetworkBehavior
{
    public float speed = .2f;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (IsOwner) {
            HandleInput();
            // relay transform data
            SvorkNetworkManager.instance.SendTransform(this);
        }
    }

    private void HandleInput()
    {
        if (Input.GetKey(KeyCode.W)) {
            transform.position += Time.deltaTime * speed * Vector3.forward;
        }
        if (Input.GetKey(KeyCode.S)) {
            transform.position += Time.deltaTime * speed * Vector3.back;
        }
        if (Input.GetKey(KeyCode.A)) {
            transform.position += Time.deltaTime * speed * Vector3.left;
        }
        if (Input.GetKey(KeyCode.D)) {
            transform.position += Time.deltaTime * speed * Vector3.right;
        }
    }
}
