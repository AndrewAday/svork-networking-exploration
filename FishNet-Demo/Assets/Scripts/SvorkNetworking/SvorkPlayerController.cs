using System;
using System.Collections;
using System.Collections.Generic;
using extOSC.Core;
using Unity.VisualScripting;
using UnityEngine;

public class SvorkPlayerController : SvorkNetworkBehavior
{
    public float speed = .2f;

    private ChuckSubInstance cs;
    // Start is called before the first frame update

#region chunity globals
    private string _u2cPlayEvent ;

#endregion
    void Start()
    {  
        cs = GetComponent<ChuckSubInstance>();
        _u2cPlayEvent = cs.GetUniqueVariableName();
        // TODO: either figure out multiline string syntax highlighting
        // OR write scanner/parser to read in a .ck file and interpolate with c# variables
        cs.RunCode($@"
            SndBuf buf => dac;
            ""special:dope"" => buf.read;
            buf.samples() => buf.pos;

            {(nobID % 2 == 0 ? .5 : 1.5)} => buf.rate;

            global Event {_u2cPlayEvent};

            // local listener (listens for events specific to a single networked object)
            ""/svork/client/playSound/"" + {nobID} => string clientOscAddress;
            fun void playSoundListener() {{
                OscIn oin;
                OscMsg msg;

                // Note: must be different port than Unity extOSC Receiver
                {SvorkNetworkManager.instance.Port + 1000} => oin.port;

                <<< ""chuck listening on"", clientOscAddress >>>;

                oin.addAddress(clientOscAddress);

                while (oin => now) {{
                    while (oin.recv(msg)) {{

                        <<< 
                            "" playing sndbuf on client port: "" + oin.port() +
                            ""nobID: "" + {nobID}
                        >>>;

                        0 => buf.pos;

                    }}
                }}
            }}
            spork ~ playSoundListener();

            fun void chunityEventListener() {{
                OscOut xmit;
                xmit.dest( ""{SvorkNetworkManager.instance.ServerIP()}"", {SvorkNetworkManager.instance.ServerPort()} );

                while (true) {{
                    {_u2cPlayEvent} => now;
                    // broadcast play event to server
                    xmit.start(""/svork/relay/chuck"");  // TODO: /relay/unity vs /relay/chuck
                    xmit.add(0);  // TARGET_ALL
                    xmit.add(clientOscAddress);
                    xmit.add(""{SvorkNetworkManager.instance.MyNetworkName}"");
                    xmit.send();  // note a safe send
                    <<< ""chuck relaying event to"", clientOscAddress >>>;
                }}
            }}
            spork ~ chunityEventListener();

            while (1::eon => now) {{}}
        ");
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

        if (Input.GetKeyDown(KeyCode.Space))
            cs.BroadcastEvent(_u2cPlayEvent);
    }
}
