using FishNet;
using FishNet.Transporting;
using FishNet.Object;
using FishNet.Object.Prediction;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerInputDriver : NetworkBehaviour
{
    private CharacterController _characterController;
    private Vector2 _moveInput;
    private Vector3 _moveDirection;
    private bool _jump;
    [SerializeField] public float jumpSpeed = 6f;
    [SerializeField] public float speed = 8f;
    [SerializeField] public float gravity = -9.8f;


    #region Types.

    public struct MoveInputData : IReplicateData
    {
        public Vector2 moveVector;
        public bool jump;
        public bool grounded;

        /* Everything below this is required for
        * the interface. You do not need to implement
        * Dispose, it is there if you want to clean up anything
        * that may allocate when this structure is discarded. */
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    public struct ReconcileData : IReconcileData
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public ReconcileData(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
            _tick = 0;
        }

        /* Everything below this is required for
        * the interface. You do not need to implement
        * Dispose, it is there if you want to clean up anything
        * that may allocate when this structure is discarded. */
        private uint _tick;
        public void Dispose() { }
        public uint GetTick() => _tick;
        public void SetTick(uint value) => _tick = value;
    }

    #endregion

    private void Start()
    {
        _characterController = GetComponent(typeof(CharacterController)) as CharacterController;
        _jump = false;

        // subscribe to TimeManager callback
        InstanceFinder.TimeManager.OnTick += TimeManager_OnTick;
    }

    private void OnDestroy()
    {
        if (InstanceFinder.TimeManager != null)
            InstanceFinder.TimeManager.OnTick -= TimeManager_OnTick;
    }

    // Used in client-authoritative movement
    // private void Update()
    // {
    //     if (!base.IsOwner) return;  // only control my own player

    //     if (_characterController.isGrounded)
    //     {
    //         _moveDirection = new Vector3(_moveInput.x, 0.0f, _moveInput.y);
    //         _moveDirection *= speed;

    //         if (_jump)
    //         {
    //             _moveDirection.y = jumpSpeed;
    //             _jump = false;
    //         }
    //     }
    //     _moveDirection.y += gravity * Time.deltaTime;
    //     _characterController.Move(_moveDirection * Time.deltaTime);
    // }

    #region UnityEventCallbacks
    public void OnMovement(InputAction.CallbackContext context)
    {
        if (!base.IsOwner) return;
        _moveInput = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!base.IsOwner) return;
        if (context.started || context.performed)
        {
            _jump = true;
        }
        else if (context.canceled)
        {
            _jump = false;
        }
    }
    #endregion

    #region Prediction Methods

    [Replicate]
    private void Move(MoveInputData md, bool asServer, Channel channel = Channel.Unreliable, bool replaying = false)
    {
        Vector3 move = new Vector3();
        if (md.grounded)
        {
            move.x = md.moveVector.x;
            move.y = gravity;
            move.z = md.moveVector.y;
            if (md.jump)
            {
                move.y = jumpSpeed;
            }
        }
        else
        {
            move.x = md.moveVector.x;
            move.z = md.moveVector.y;
        }
        move.y += gravity * (float)base.TimeManager.TickDelta; // gravity is negative...
        _characterController.Move(move * speed * (float)base.TimeManager.TickDelta);
    }

    [Reconcile]
    private void Reconciliation(ReconcileData rd, bool asServer, Channel channel = Channel.Unreliable)
    {
        transform.position = rd.Position;
        transform.rotation = rd.Rotation;
    }

    #endregion

    #region Movement Processing

        private void GetInputData(out MoveInputData moveData)
        {
            moveData = new MoveInputData
            {
                jump = _jump, 
                grounded = _characterController.isGrounded, 
                moveVector = _moveInput
            };
        }
        private void TimeManager_OnTick()
        {
            if (base.IsOwner)
            {
                Reconciliation(default, false);
                GetInputData(out MoveInputData md);
                Move(md, false);
            }

            if (base.IsServer)
            {
                Move(default, true);
                ReconcileData rd = new ReconcileData(transform.position, transform.rotation);
                Reconciliation(rd, true);
            }
        }

    #endregion
}