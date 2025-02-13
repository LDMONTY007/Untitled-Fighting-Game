using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SocialPlatforms;
using UnityEngine.UIElements;

public class Player : MonoBehaviour
{

#if UNITY_EDITOR
    //if we are in the editor, not in the built game.
    public bool debugMode = false;
    private TextMeshPro debugStateText;
    private GameObject debugTextObj;
    public Vector3 debugTextPosition = Vector3.zero;
#endif
    [HideInInspector]
    public int stock = 0;

    public List<AudioClip> hitSounds = new List<AudioClip>();
    public List<AudioClip> deathSounds = new List<AudioClip>();

    public enum Direction
    {
        None,
        Up,
        Down,
        Left,
        Right
    }

    private float _damagePercent = 0f;

    //The gravity we return to 
    //after modifying gravity.
    float baseGravity = 9.81f;
    float gravity = 9.81f;
    float fallGravity = 9.81f;

    public float damagePercent { get { return _damagePercent; } set { float clamped = Mathf.Clamp(value, 0f, 999.0f); _damagePercent = clamped; } }

    [HideInInspector]
    public CharacterIcon characterIcon;

    private Icon icon;

    [Header("Misc References")]
    public Hurtbox hurtbox;

    public ParticleSystem launchParticles;
    
    public GameObject spriteParent;

    public Animator animator;
    
    private bool isFacingLeft;

    //the index of this character in the GameManager.
    [HideInInspector]
    public int characterIndex;

    /// <summary>
    /// Used to set the angle, damage, and knockback of attacks.
    /// </summary>
    public Moveset moveset;

    //Hitstun should probably be a part of the playerstate
    //or some sort of substate so that these are 2 layers 
    //compunded.

    int hitStunFrames = 0;
    //If hitStunFrames is not 0 we are hitstunned.
    bool isHitStunned => hitStunFrames > 0;

    public enum PlayerState
    {
        None,       //Base state, no additional effects are applied.
        attacking,  //Induced when attacking. Just allows us to make sure we don't start another attack when already attacking. Might need to delete this.
        dashing,    //Used when the player is dashing, do not set X velocity after dashing.
        launched,   //Induced when launched. This just lets us know to stop the old launch coroutine and start a new one. Disables some physics.
        helpless,   //Induced after running out of jumps while in the air. Sometimes called "Freefall" https://www.ssbwiki.com/Helpless
        intangible, //Induced by dodging. Cannot be hit or pushed by other players. https://www.ssbwiki.com/Intangibility
        shielding,  //Induced by shielding, Cannot be damaged but doing any other action will exit this state.
        grabbing,   //Induced by grabbing another character.
        grabbed,    //Induced when being grabbed.
    }


    [Header("Player State")]
    public PlayerState state = PlayerState.None;



    [Header("Movement Parameters")] //Explain that this will show a header in the inspector to categorize variables
    [Range(1, 5)] public float helplessSpeed = 1.5f;
    [Range(1, 10)] public float walkSpeed = 5f;
    [Range(1, 20)] public float runSpeed = 12f;
    [Range(1, 20)] public float maxSpeed = 14f;

    [Header("Dash Parameters")]
    [Range(1, 30)] public float dashDist = 5f;
    public int dashFrames = 10;
    public float dashModifier = 1f;
    //This var stores the current dashCoroutine 
    //and allows us to check if the dashCoroutine 
    //is running. It is null otherwise.
    private Coroutine dashCoroutine;
    //This tells us the direction the player input in order
    //to begin dashing
    private Direction dashDirection = Direction.None;

    private Coroutine jumpCoroutine;

    private bool dodging;
    [Header("Dodge Parameters")]
    public int dodgeFrames = 14;
    public int intangibilityFrames = 21;
    [Range(1, 30)] public float dodgeDist = 5f;
    public float dodgeModifier = 1f;

    [Header("Shield Parameters")]
    public float totalShield = 50f;
    private float shieldHealth = 0f;
    public Transform shieldTransform;
    private Vector3 ogShieldScale = Vector3.one;

    [HideInInspector]
    public float xAxis;
    [HideInInspector]
    public float yAxis;

    private Vector2 lastDirectionInput;
    private float lastXinput;
    private Vector2 moveInput;
    private Vector2 camRelInput; 
    private Vector2 moveDirection;

    private Direction curDirection;
    private Direction lastDirection;

    #region movement bools
    //we walk if the x input is less than or equal to 0.5f
    private bool isWalking => Mathf.Abs(xAxis) <= 0.5f ? true : false;
    //set movespeed based on if we are walking or helpless.
    private float moveSpeed => state == PlayerState.helpless ? helplessSpeed : isWalking ? walkSpeed : runSpeed;

    private bool shouldDash;
    #endregion

    private Transform camTransform;

    private Rigidbody2D rb;


    private PlayerInput playerInput;

    //Input actions
    private InputAction moveAction;
    private InputAction dirAction;
    private InputAction jumpAction;
    private InputAction attackAction;
    private InputAction specialAction;
    //Attack inputs
    private InputAction upTiltAction;
    private InputAction downTiltAction;
    private InputAction rightTiltAction;
    private InputAction leftTiltAction;
    //Smash inputs
    private InputAction upSmashAction;
    private InputAction downSmashAction;
    private InputAction rightSmashAction;
    private InputAction leftSmashAction;

    //Shield input
    private InputAction shieldAction;

    //Grab input
    private InputAction grabAction;

    //Pause input
    private InputAction pauseAction;

    #region input bools

    bool shouldAttack;
    bool shouldAttackContinuous;

    bool shouldSpecial;
    bool shouldSpecialContinuous;

    bool shouldSmash;
    bool shouldSmashContinuous;

    //did the player tap this frame?
    bool didTap;
    float tapStopWindow = 0.2f;
    float tapStopTime;

    public float attackStopWindow = 0.2f;
    float attackStopTime;
    bool shouldWaitToAttack;
    bool doDelayedAttack;

    bool shouldDodge;
    #endregion



    #region Jumping

    //Changing isGrounded is what caused a logic error
    //with the launching code. 
    //public bool isGrounded => Physics2D.BoxCast(transform.position, this.GetComponent<BoxCollider2D>().bounds.extents, 0f, -transform.up, this.GetComponent<BoxCollider2D>().bounds.extents.y + groundCheckDist, rCasting);
    public bool isGrounded => Physics2D.BoxCast(transform.position, this.GetComponent<BoxCollider2D>().size, 0f, -transform.up, groundCheckDist, rCasting);
    public bool inAir => !jumping && !isGrounded;
    [HideInInspector]
    public bool doJump;





    private LayerMask rCasting;

    [Header("Jumping Parameters")]
    public float groundCheckDist = 0.1f;
    private int jumpCount = 1; //What we modify and check when jumping.
    public int jumpTotal = 1; //Total jumps, so for instance if you wanted 3 jumps set this to 3.
    private bool jumpCanceled;
    private bool jumping;
    //private bool falling => inAir && transform.InverseTransformDirection(rb.velocity).y < 0;
    private bool falling;
    public double jumpHeight = 5f; //Our jump height, set this to a specific value and our player will reach that height with a maximum deviation of 0.1
    //time to reach the apex of the jump.
    //0.01f looks just like smash ultimate jumping.
    public float timeToApex = 0.01f;
    public float timeToFall = 0.5f;
    [Tooltip("Desired Height / jump height reached. Do not modify if you have not modified the time values.")]
    public float jumpHeightModifier = 1.2f;
    private float buttonTime;
    private float jumpTime;

    //LD should encapsulate this
    //and the other code that calculates
    //the jump distance
    //in a #IF UNITY_EDITOR statement bc it would otherwise be included in builds and is
    //calculated every jump.
    public double jumpDist; //used to see the measured distance of the jump.
    public Vector2 ogJump; //Not included just like what I said above.
    public float fallMultiplier = 9f; //When you reach the peak of the expected arc this is the force applied to make falling more fluid.
    public float lowJumpMultiplier = 15f; //When you stop holding jump to do a low jump this is the force applied to make the jump stop short.
    private Coroutine rotateCoroutine;

    #endregion

    

    private void Awake()
    {
        //get the player input and assign the actions.
        playerInput = GetComponent<PlayerInput>();
        moveAction = playerInput.actions["Move"];
        dirAction = playerInput.actions["Direction"];
        jumpAction = playerInput.actions["Jump"];
        attackAction = playerInput.actions["Attack"];
        specialAction = playerInput.actions["Special"];


        //Smash Attacks
        upSmashAction = playerInput.actions["UpSmash"];
        downSmashAction = playerInput.actions["DownSmash"];
        rightSmashAction = playerInput.actions["RightSmash"];
        leftSmashAction = playerInput.actions["LeftSmash"];

        /*        upSmashAction.performed += UpSmash;
                downSmashAction.performed += DownSmash;
                rightSmashAction.performed += RightSmash;
                leftSmashAction.performed += LeftSmash;*/

        shieldAction = playerInput.actions["Shield"];
        grabAction = playerInput.actions["Grab"];

        pauseAction = playerInput.actions["Pause"];

        
    }

    private void OnEnable()
    {
        upSmashAction.Enable();
        downSmashAction.Enable();
        rightSmashAction.Enable();
        leftSmashAction.Enable();
        //call the pause method when the pause action is performed.
        //pauseAction.performed += GameManager.instance.gameMenu.Pause;
    }

    private void OnDisable()
    {
        upSmashAction.Disable();
        downSmashAction.Disable();
        rightSmashAction.Disable();
        leftSmashAction.Disable();
        //Remove the subscription when this is disabled.
        //pauseAction.performed -= GameManager.instance.gameMenu.Pause;
    }


    

    // Start is called before the first frame update
    void Start()
    {
        
        //set the shield health.
        shieldHealth = totalShield;
        //set the OG scale of this shield for use later.
        ogShieldScale = shieldTransform.localScale;

        rCasting = LayerMask.GetMask("Player", "Ignore Raycast"); //Assign our layer mask to player
        rCasting = ~rCasting; //Invert the layermask value so instead of being just the player it becomes every layer but the mask

        //get the main camera's transform.
        //WE SHOULD NEVER HAVE MORE THAN 1 CAMERA.
        camTransform = Camera.main.transform;

        //get rigidbody.
        rb = GetComponent<Rigidbody2D>();

        //get animator if it isn't manually assigned.
        if (animator == null)
            animator = GetComponent<Animator>();

        //DISABLE GRAVITY SO WE CAN USE OUR OWN.
        rb.gravityScale = 0;

        //call the pause method when the pause action is performed.
        if (GameManager.instance.gameMenu != null)
        {
            pauseAction.performed += context => GameManager.instance.gameMenu.Pause(context);
        }

        //I should probably check if this is a respawn before I play this bc otherwise all the characters will play a respawn audio at the beginning.
        AudioManager.instance.globalSource.PlayOneShot(GameManager.instance.gameMode.respawnSounds[UnityEngine.Random.Range(0, GameManager.instance.gameMode.respawnSounds.Count)]);

    }

    // Update is called once per frame
    void Update()
    {

        //If the game is paused make this function 
        //stop here until unpaused.
        if (GameManager.instance.isPaused)
        {
            return;
        }

        //Debug.Log(GameManager.instance.isPaused);

        #region input bools

        //only true during the frame the button is pressed.
        shouldAttack = attackAction.WasPressedThisFrame();
        

        //While button is held down this is true.
        shouldAttackContinuous = attackAction.IsPressed();

        //only true during the frame the button is pressed.
        shouldSpecial = specialAction.WasPressedThisFrame();
        //While button is held down this is true.
        shouldSpecialContinuous = attackAction.IsPressed();

        //this deterimines if the player tapped an input direction this frame.
        Vector2 curDir = dirAction.ReadValue<Vector2>();

        //set the curDirection.
        curDirection = GetDirection(curDir);

        //Debug.Log(curDirection.ToString().Color("cyan"));

        //Detect if the player tapped.
        //the first 2 parts of teh conditional
        //insure that we aren't detecting the joystick 
        //flinging back to the center after the player lets
        //go of the joystick.

        //but we are just checking if the speed of the joystick
        //input is fast enough and if it is we detect a tap.
        if (curDir.magnitude > 0.09 && curDir.magnitude > lastDirectionInput.magnitude && ((curDir - lastDirectionInput).magnitude / Time.deltaTime) >= 20f)
        {
            didTap = true;
            //Debug.Log("TAP! ".Color("red") + ((curDir - lastDirectionInput).magnitude / Time.deltaTime));
        }


        //window until we stop telling 
        //the code that we tapped. 
        if (didTap /*&& !startedTapInput*/)
        {
            if (tapStopTime >= tapStopWindow)
            {
                tapStopTime = 0f;
                didTap = false;
            }
            else
                tapStopTime += Time.deltaTime;
        }


        //Check if we should dash. 
        if (didTap && (curDirection == Direction.Right || curDirection == Direction.Left) && state != PlayerState.dashing)
        {

            //set should dash to true so we dash on the next fixedUpdate.
            //Even if we dash, we are still going to be able to input 
            //a smash attack.

            //We should only start dashing if shouldAttack is false
            //this frame, otherwise we do a smash attack and don't start dashing.

            //we should enter "Dashing" and only then should we do a "Dash Attack"


            //set dashDirection so the coroutine knows which way we dashed.
            dashDirection = curDirection;
            shouldDash = true;
        }


        //we should never to this delaying
        //of an attack while in the air.
        //I should probably change the code around
        //so that we just check if the player does an input
        //before starting a jump 
        if (shouldAttack && !inAir || shouldWaitToAttack && !inAir)
        {
            //remove the !shouldWaitToAttack later if need be,
            //but it makes it so that we attack after the delay
            //and if we press attack again it DOESN'T reset the 
            //attack delay thus canceling the attack and extending
            //the timer. 
            if (attackAction.WasPressedThisFrame() && !shouldWaitToAttack)
            {
                attackStopTime = 0f;
                Debug.Log("Attack hit before tapping".Color("red"));
            }

            if (attackStopTime >= attackStopWindow)
            {
                //attackStopTime = 0f;
                shouldWaitToAttack = false;
                doDelayedAttack = true;
            }
            else
            {
                attackStopTime += Time.deltaTime;
                shouldWaitToAttack = true;
            }
        }



        #endregion

        #region jump bool control
        doJump |= (jumpAction.WasPressedThisFrame() && jumpCount > 0 && !jumping); //We use |= because we want doJump to be set from true to false
        //This ^ Operator is the or equals operator, it's kind of hard to explain so hopefully I explain this correctly,
        //Basically its saying this : doJump = doJump || (jumpAction.GetButtonDown() && jumpCount > 0 && !jumping)
        //Which is too say, if doJump is already true we return true otherwise we check (jumpAction.GetButtonDown() && jumpCount > 0 && !jumping)
        //The reason we do this is because the only time we want doJump = false is when we directly set it later in the code after we
        //call the jump function. So unless we are setting doJump to false it will be able to return true and only check our conditional
        //again once it is set to false.
        //in the event that doJump is already false and our conditional returns true;
        if (isGrounded)
        {
            if (!jumping)
            {
                jumpCount = jumpTotal; //Reset jump count when we land.
                jumpCanceled = false;
                //set gravity back to base.
                gravity = baseGravity;
                //animator.SetTrigger("landing");
            }
        }

        if (jumping) //Track the time in air when jumping
        {
            jumpTime += Time.deltaTime;
        }

        if (jumping && !jumpCanceled)
        {

            if (!jumpAction.IsPressed()) //If we stop giving input for jump cancel jump so we can have a variable jump.
            {
                jumpCanceled = true;
            }

            if (jumpTime < buttonTime)
            {
                jumpDist = transform.position.y - ogJump.y;
            }

            if (jumpTime >= buttonTime) //When we reach our projected time stop jumping and begin falling.
            {
                Debug.Log("JUMP CANCELED BY BUTTON TIME".Color("Green"));
                //pause the editor
                //Debug.Break();
                jumpCanceled = true;

                //set gravity back to fall gravity
                gravity = fallGravity;
                //gravity = baseGravity;


                //jumpDist = Vector2.Distance(transform.position, ogJump); //Not needed, just calculates distance from where we started jumping to our highest point in the jump.
                jumpDist = transform.position.y - ogJump.y;
            }
        }

        if (jumpCanceled) //Cancel the jump.
        {
            jumping = false;
        }

        #endregion

        #region moveInput

        moveInput = moveAction.ReadValue<Vector2>();
        moveDirection = ((moveInput.normalized.x * camTransform.right.normalized) + (moveInput.normalized.y * camTransform.up.normalized)) * moveSpeed;

        camRelInput = ((moveInput.normalized.x * camTransform.right.normalized) + (moveInput.normalized.y * camTransform.up.normalized));

        //xAxis = moveInput.x != 0 ? moveInput.x > 0 ? 1 : -1 : 0;
        //A or D is pressed return -1 or 1, relative to which
        //if tilted less than or equal to .75f then walk.
        //otherwise return zero.
        if (moveInput.x != 0 && Mathf.Abs(moveInput.x) > 0.01 && (Mathf.Abs(moveInput.x) > Mathf.Abs(moveInput.y)))
        {
            if (moveInput.x > 0)
            {
                xAxis = moveInput.x <= 0.75f ? 0.5f : 1f;
            }
            else if (moveInput.x < 0)
            {
                xAxis = moveInput.x <= -0.75f ? -1 : -0.5f;
            }
        }
        else
        {
            xAxis = 0;
        }


        if (moveInput.y != 0 && Mathf.Abs(moveInput.y) > 0.01)
        {
            if (moveInput.y > 0)
            {
                yAxis = moveInput.y <= 0.75f ? 0.5f : 1f;
            }
            else if (moveInput.y < 0)
            {
                yAxis = moveInput.y <= -0.75f ? -1 : -0.5f;
            }
        }
        else
        {
            yAxis = 0;
        }


        #endregion


        #region Shield Input
        //if the user inputs shield
        //during this frame reset their shield's
        //health
        if (shieldAction.WasPressedThisFrame())
        {
            Debug.Log("Should Shield".Color("red"));
            

            //TODO:
            //start the shield shrinking timer here.
        }
        else if (shieldAction.IsPressed())
        {
            //for now, if it's greater than zero we ignore damage.
            if (shieldHealth > 0)
            {
                if (isGrounded && state != PlayerState.helpless && state != PlayerState.launched && state != PlayerState.dashing)
                {

                    //TODO:
                    //implement code for enabling/disabling the shield of the character
                    //relative to the health.

                    //TODO:
                    //implement Shielding state so that you can't move while shielding. 

                    state = PlayerState.shielding;
                }
            }
        }
        else
        {   //if we stop shielding, turn off the shield.
            if (state == PlayerState.shielding)
            {
                state = PlayerState.None;
            }
            if (shieldTransform.gameObject.activeSelf)
            {
                
                shieldTransform.gameObject.SetActive(false);
            }
        }
        #endregion

        if (isGrounded && state != PlayerState.shielding)
        {

            if (grabAction.WasPressedThisFrame())
            {
                HandleGrab();
            }    
            else if (!isHitStunned && state != PlayerState.helpless && state != PlayerState.dashing)
            {
                //only rotate fast when tapping
                //otherwise do rotation coroutine.
                if (didTap)
                {
                    Debug.Log("THIS");
                    HandleInstantaneousRotation();
                }
                else
                {
                    HandleRotation();
                }
                HandleAttack();
                HandleSpecial();
            }
            else if (!isHitStunned && state == PlayerState.dashing)
            {
                HandleDashAttack();
            }
        }
        else if (inAir && state != PlayerState.shielding)
        {
            if (!isHitStunned && state != PlayerState.helpless)
            {
                HandleAerial();
                HandleSpecial();
                
                if (grabAction.WasPressedThisFrame() || shieldAction.WasPressedThisFrame())
                {
                    //set "shouldDodge" to true.
                    //actually just call the dodge coroutine.
                    
                    StartCoroutine(DodgeCoroutine());
                }
            }
        }


        lastXinput = moveInput.x;

        lastDirectionInput = dirAction.ReadValue<Vector2>();
        lastDirection = curDirection;


        //We are able to 
        //apply forces to the rigidbody during
        //update because we set the rigidbody
        //to interpolate. Normally, this 
        //wouldn't work.
        //ApplyFinalMovements();

        HandlePassiveAnimation();

        HandleUI();

        //we check for the state after
        //everything else because it would
        //be annoying to input an attack 
        //and see it start just for the 
        //forces from the attack to not
        //be applied.
        //Also I don't have a good explanation
        //for it.
        HandleState();

#if UNITY_EDITOR
        //preprocessor to check for debug stuff.
        if (debugMode)
        {
            HandleDebug();
        }
#endif
    }

    private void HandleGrab()
    {
        RaycastHit2D hit = Physics2D.BoxCast(transform.position + new Vector3(0f, this.GetComponent<BoxCollider2D>().bounds.extents.x), this.GetComponent<BoxCollider2D>().size, 0f, -spriteParent.transform.right, groundCheckDist);
        if (hit)
        {
            if (hit.collider.gameObject.CompareTag("Player"))
            {
                GameObject enemy = hit.collider.gameObject;
                if (enemy != null)
                {
                    if (enemy.GetComponent<Joint2D>() == null)
                    {
                        //TODO:
                        //tell the other player to set their state to "Grabbed"
                        //Set our state to "grabbing"
                        //so that we can't move but they can try to break the joint
                        //by moving left and right.
                        //also set the strength of the joint to a lower value.
                        FixedJoint2D joint = enemy.AddComponent<FixedJoint2D>();
                        joint.connectedBody = rb;
                        joint.enableCollision = false;
                    }
                }
            }
        }
    }

    private void HandleDashAttack()
    {
        //if we should do a dash attack, call it.
        if (shouldAttack && state == PlayerState.dashing)
        {
            DashAttack();
        }
    }

    private void FixedUpdate()
    {
        //currently
        //you can jump while doing an attack,
        //I think this is how smash works.

        #region dashing
        if (shouldDash && isGrounded && state != PlayerState.shielding)
        {
            if (dashCoroutine == null)
            {
                dashCoroutine = StartCoroutine(DashCoroutine());
            }
            else
            {
                //Debug.Log("Stopping old dash Coroutine".Color("orange"));
                StopCoroutine(dashCoroutine);
                dashCoroutine = null;
                dashCoroutine = StartCoroutine(DashCoroutine());
            }
        }
        else
        {
            shouldDash = false;
        }
        #endregion

        #region jumping

        if (!isHitStunned)
        {
            if (doJump && isGrounded)
            {
                //play the jump sequence
                animator.SetTrigger("jump");
                //wait 1 frame then call HandleJump().
                //StartCoroutine(LDUtil.WaitFrames(HandleJump, 1));
                //Instead we're just going to call the Handle Jump method because in the future
                //I'll replace it with a coroutine so we can have frame specific inputs to cancel things
                //or check for up attacks.
                HandleJump();
                /*            if (doJump)
                StartCoroutine(JumpCoroutine(10, jumpHeight));*/
            }
            else
            {
                //if we are air jumping then we don't need a windup frame.
                HandleJump();

                /*if (doJump)
                    StartCoroutine(JumpCoroutine(10, jumpHeight));*/
            }
        }

        #endregion


        ApplyFinalMovements();
    }

    private void HandleState()
    {
        #region Helpless check
        //did we run out of jumps?
        if (jumpCount == 0)
        {
            //set state to helpless.
            state = PlayerState.helpless;
        }
        if (state == PlayerState.helpless && isGrounded)
        {
            state = PlayerState.None;
        }
        #endregion

        #region Shielding
        if (state == PlayerState.shielding)
        {
            //Make shield visible if it isn't already.
            if (!shieldTransform.gameObject.activeSelf)
            {
                shieldTransform.gameObject.SetActive(true);
            }

            //In Ultimate you lose 0.15 per frame.
            //Source: https://www.ssbwiki.com/Shield#Shield_statistics
            if (shieldHealth > 0)
            {
                shieldHealth -= 0.15f;
                shieldHealth = Mathf.Clamp(shieldHealth, 0f, totalShield);
                //Formula comes from here: https://www.ssbwiki.com/Shield#Shield_statistics
                //Assuming the total shield health is 50. 
                shieldTransform.localScale = ogShieldScale * ((shieldHealth / totalShield) * 0.85f + 0.15f);
                
                //Shield break.
                if (shieldHealth == 0f)
                {
                    ShieldBreak();
                }
            }
            
        }
        else
        {
            //hide shield if we shouldn't be shielding.
            if (shieldTransform.gameObject.activeSelf)
            {

                shieldTransform.gameObject.SetActive(false);
            }
        }
        
        if (state != PlayerState.shielding)
        {
            //In Ultimate you regen 0.08 per frame.
            //Source: https://www.ssbwiki.com/Shield#Shield_statistics
            if (shieldHealth < totalShield)
            {
                //Debug.Log("ADDING SHIELD");
                shieldHealth += 0.08f;
                shieldHealth = Mathf.Clamp(shieldHealth, 0f, totalShield);
                shieldTransform.localScale = ogShieldScale * ((shieldHealth / totalShield) * 0.85f + 0.15f);
            }
        }
        #endregion
    }

    private void ShieldBreak()
    {
        state = PlayerState.launched;
        jumpCoroutine = StartCoroutine(JumpCoroutine(40, 5));
        //after your shield breaks it goes back to 37.5f health.
        shieldHealth = 37.5f;
    }

    /*private IEnumerator DashCoroutine()
    {
        //this is what causes the smash attack charge to occur so
        //we need to turn it off when we start dashing.
        shouldAttackContinuous = false;
        //We also don't want them to wait to do an attack
        shouldWaitToAttack = false;

        //Same reasons as before, we shouldn't be doing any attacks 
        //that were input during dodging unless we are doing a dash attack.
        shouldSmash = false;
        //because dashing should cancel that check for a smash attack
        //if we are already dashing.
        shouldSmashContinuous = false;

        shouldDash = false;
        
        int frames = dashFrames;
        state = PlayerState.dashing;
        Debug.Log("Dash!".Color("cyan"));

        //const float delta = 1f / 60f;
        float timeToDash = frames / 60f;
        //float timeToDash = frames * Time.deltaTime;

        float modDashDist = dashDist * dashModifier;

        //This helped me solve it: https://www.quora.com/Given-time-and-distance-how-do-you-calculate-acceleration
        //distance -> d
        //average velocity = d/t
        //final velocity = 2*d/t
        //acceleration = final velocity / t
        //therefore acceleration - 2*d/t^2
        float acceleration = 2f * modDashDist / Mathf.Pow(timeToDash, 2f);

        float dashForce = Mathf.Sqrt(2f * acceleration * modDashDist) * rb.mass;

        float initVel = Mathf.Sqrt(2f * acceleration * modDashDist);

        //Time at max distance = v0 / acceleration
        Debug.Log((initVel / acceleration).ToString().Color("lime"));

        Debug.Log("Dash Force: " + dashForce);
        //velocity = force / mass * time
        //float dashVelocity = dashForce / rb.mass * timeToDash;

        //make sure to rotate before we dash.
        HandleRotation();

        rb.AddForce(playerSprite.transform.right.normalized * dashForce, ForceMode2D.Impulse);
        rb.AddForce(-playerSprite.transform.right.normalized * acceleration * rb.mass);
        //frames--;
        Debug.Log(("RBVel: " + rb.velocity).ToString().Color("purple"));
        //calling add force here and then again in the while loop is fine
        //accept when it's the first iteration of the loop and it hasn't
        //returned to execution. 

        //When 2 addforce calls are made during the same frame only one seems
        //to be applied.
        yield return new WaitForFixedUpdate();

        float currentTime = timeToDash;

        //Debug.Break();
        launchParticles.Play();
        while (*//*currentTime > 0*//*frames > 0)
        {
            if (!isHitStunned)
            {
                
                Debug.DrawRay(transform.position, rb.velocity, Color.red);
                Debug.Log("InitVel: " + initVel + " Acceleration: " + acceleration);
                Debug.Log("CurrentTime: " + currentTime + " FrameCount: " + frames);
                //rb.velocity = playerSprite.transform.right.normalized * initVel;
                //rb.velocity = playerSprite.transform.right * dashVelocity;

                //decelerate to reach distance.
                *//* if (Mathf.Abs(rb.velocity.x) > 0)
                     rb.AddForce(-transform.right * acceleration * rb.mass);*//*

                
                //Decelerate so we reach desired distance.
                //This makes the player stop for a moment before continuing running though.
                rb.AddForce(-playerSprite.transform.right.normalized * acceleration * rb.mass);
                Debug.Log(("RBVel: " + rb.velocity).ToString().Color("cyan"));



                //decrement.
                frames--;
                currentTime -= Time.deltaTime;
                if (currentTime < 0.0001)
                {
                    currentTime = 0;
                }
                yield return new WaitForFixedUpdate();
                
            }
        }

        //say we are no longer tapping
        didTap = false;
        

        //rb.velocity = new Vector2(0f, rb.velocity.y);
        Debug.Log("Done!");
        Debug.Log("Dist: " + dashDist + "\nDist Reached: " + transform.position.x + "\nScale to reach desired: " + dashDist / transform.position.x + "\nTimeToDash: " + timeToDash + "\ncurrentTime: " + currentTime);
        
        //Debug.Break();

        launchParticles.Stop();
        //go back to base state.
        //yield return new WaitForEndOfFrame();
        state = PlayerState.None;
        //set dashCoroutine back to null after finishing
        //so we don't think we are still running.
        dashCoroutine = null;
        
    }*/

    private IEnumerator DashCoroutine()
    {
        //this is what causes the smash attack charge to occur so
        //we need to turn it off when we start dashing.
        shouldAttackContinuous = false;
        //We also don't want them to wait to do an attack
        shouldWaitToAttack = false;

        //Same reasons as before, we shouldn't be doing any attacks 
        //that were input during dodging unless we are doing a dash attack.
        shouldSmash = false;
        //because dashing should cancel that check for a smash attack
        //if we are already dashing.
        shouldSmashContinuous = false;

        shouldDash = false;

        int frames = dashFrames;
        state = PlayerState.dashing;
        Debug.Log(("Dash! " + (isFacingLeft ? "Left" : "Right")).Color("cyan"));

        //const float delta = 1f / 60f;
        float timeToDash = frames / 60f;
        //float timeToDash = frames * Time.deltaTime;

        //dash modifier = dash dist / actual distance reached prior to dash modifier application.
        float modDashDist = dashDist * dashModifier;

        //This helped me solve it: https://www.quora.com/Given-time-and-distance-how-do-you-calculate-acceleration
        //distance -> d
        //average velocity = d/t
        //final velocity = 2*d/t
        //acceleration = final velocity / t
        //therefore acceleration - 2*d/t^2
        float acceleration = 2f * modDashDist / Mathf.Pow(timeToDash, 2f);

        float dashForce = Mathf.Sqrt(2f * acceleration * modDashDist) * rb.mass;

        float initVel = Mathf.Sqrt(2f * acceleration * modDashDist);

        float curVel = initVel;

        //Time at max distance = v0 / acceleration
        Debug.Log((initVel / acceleration).ToString().Color("lime"));

        Debug.Log("Dash Force: " + dashForce);
        //velocity = force / mass * time
        //float dashVelocity = dashForce / rb.mass * timeToDash;

        //make sure to rotate before we dash.
        //HandleRotation();
        HandleInstantaneousRotation();

        //rb.AddForce(playerSprite.transform.right.normalized * dashForce, ForceMode2D.Impulse);
        //rb.AddForce(-playerSprite.transform.right.normalized * acceleration * rb.mass);
        //frames--;
        Debug.Log(("RBVel: " + rb.velocity).ToString().Color("purple"));
        //calling add force here and then again in the while loop is fine
        //accept when it's the first iteration of the loop and it hasn't
        //returned to execution. 

        //When 2 addforce calls are made during the same frame only one seems
        //to be applied.
        //calling this/not calling this here had no affect on the distance
        //reached, only the number of frames it took to reach the distance.
        //yield return new WaitForFixedUpdate();

        float currentTime = timeToDash;

        //Debug.Break();
        launchParticles.Play();
        while (/*currentTime > 0*/frames > 0)
        {
            
            if (!isHitStunned)
            {


                //if we are rotating,
                //wait until after to 
                //start dashing.
                /*                while (rotateCoroutine != null)
                                {
                                    yield return new WaitForFixedUpdate();
                                }*/

                //Debug.DrawRay(transform.position, rb.velocity, Color.red);
                //Debug.Log("InitVel: " + initVel + " Acceleration: " + acceleration);
                //Debug.Log("CurrentTime: " + currentTime + " FrameCount: " + frames);
                //rb.velocity = playerSprite.transform.right.normalized * initVel;
                //rb.velocity = playerSprite.transform.right * dashVelocity;

                //decelerate to reach distance.
                /* if (Mathf.Abs(rb.velocity.x) > 0)
                     rb.AddForce(-transform.right * acceleration * rb.mass);*/


                //Decelerate so we reach desired distance.
                //This makes the player stop for a moment before continuing running though.
                //rb.AddForce(-playerSprite.transform.right.normalized * acceleration * rb.mass);

                //function to get displacement
                //

                //current velocity = playerSprite.transform.right.normalized * (initVel * currentTime + 1/2 * -acceleration * currentTime * currentTime)
                //V = u + a * t :https://www.ncl.ac.uk/webtemplate/ask-assets/external/maths-resources/mechanics/kinematics/equations-of-motion.html#:~:text=at2.-,v%20%3D%20u%20%2B%20a%20t%20%2C%20s%20%3D%20(%20u%20%2B,represents%20the%20direction%20of%20motion.
                //V is current velocity
                //u is initial velocity
                //a is acceleration
                //t is time.

                //handle Rotation
                HandleInstantaneousRotation();

                //Checking if we are in the inital dash.
                //Here: https://www.ssbwiki.com/Dash#Initial_dash
                //Essentially there is no deceleration during the initial dash
                //and there is only deceleration after the first six frames
                //if the user is still holding the same direction then we
                //should break out of the method and let the user begin running.
                //otherwise the user should decelerate to a stop.
                if (frames < dashFrames - 6 && curDirection == dashDirection)
                {
                    break;
                }
                else
                    curVel = initVel - acceleration * (timeToDash - currentTime);
                rb.velocity = spriteParent.transform.right.normalized * curVel;
                //rb.velocity = playerSprite.transform.right.normalized * (initVel - acceleration * (frames / 60f));
                //Debug.Log(("RBVel: " + rb.velocity).ToString().Color("cyan"));

                //decrease by acceleration
                //curVel = initVel + acceleration * (timeToDash - currentTime);

                //decrement.
                frames--;
                currentTime -= Time.deltaTime;
                if (currentTime < 0.0001)
                {
                    currentTime = 0;
                }
                yield return new WaitForFixedUpdate();

            }
        }

        //reset dash Direction
        dashDirection = Direction.None;
        //coroutine over, we should set the x velocity to be whatever the player's current x input is. 
        //rb.velocity = new Vector2(xAxis * moveSpeed, rb.velocity.y);

        //say we are no longer tapping
        didTap = false;


        //rb.velocity = new Vector2(0f, rb.velocity.y);
        Debug.Log("Done!");
        Debug.Log("Dist: " + dashDist + "\nDist Reached: " + transform.position.x + "\nScale to reach desired: " + dashDist / transform.position.x + "\nTimeToDash: " + timeToDash + "\ncurrentTime: " + currentTime);

        //Debug.Break();

        launchParticles.Stop();
        //go back to base state.
        //yield return new WaitForEndOfFrame();
        state = PlayerState.None;
        //set dashCoroutine back to null after finishing
        //so we don't think we are still running.
        dashCoroutine = null;

    }

    private void CancelDashing()
    {
        if (dashCoroutine != null)
        {
            StopCoroutine(dashCoroutine);

            #region The Code that normally gets called at the end of the dash coroutine.
            //reset dash Direction
            dashDirection = Direction.None;
            //coroutine over, we should set the x velocity to be whatever the player's current x input is. 
            //rb.velocity = new Vector2(xAxis * moveSpeed, rb.velocity.y);

            //say we are no longer tapping
            didTap = false;

            //Debug.Break();

            launchParticles.Stop();
            //go back to base state.
            state = PlayerState.None;
            //set dashCoroutine back to null after finishing
            //so we don't think we are still running.
            dashCoroutine = null;
            #endregion
        }
    }

    private IEnumerator RotateCoroutine(float start, float end, int totalFrames)
    {
        int frames = 0;
        float current = start;
        while (frames < totalFrames)
        {
            //If the game is paused make this coroutine 
            //infinite loop here until unpaused.
            while (GameManager.instance.isPaused)
            {
                yield return null;
            }

            current = Mathf.Lerp(start, end, (float)frames / totalFrames);
            Debug.Log((current).ToString().Color("brown"));
            spriteParent.transform.rotation = Quaternion.Euler(0f, current, 0f);
            frames++;
            yield return null;
        }
        //set the rotation to be the end value.
        spriteParent.transform.rotation = Quaternion.Euler(0f, end, 0f);

        //set rotate coroutine to be null
        rotateCoroutine = null;
    }

    private IEnumerator DodgeCoroutine()
    {
        //this is only executed when the 
        //coroutine starts, for the rest
        //of coroutine execution it will
        //be inside the while loop then 
        //exit it.
        if (state == PlayerState.None)
        {   
            dodging = true;
            Debug.Log("Should Dodge".Color("Purple"));

            //The total dodge frames.
            int frames = dodgeFrames;
            state = PlayerState.intangible;

            #region setting up vars
            //const float delta = 1f / 60f;
            float timeToDodge = frames / 60f;
            //float timeToDash = frames * Time.deltaTime;

            //dash modifier = dash dist / actual distance reached prior to dash modifier application.
            float modDashDodge = dodgeDist * dodgeModifier;

            //This helped me solve it: https://www.quora.com/Given-time-and-distance-how-do-you-calculate-acceleration
            //distance -> d
            //average velocity = d/t
            //final velocity = 2*d/t
            //acceleration = final velocity / t
            //therefore acceleration - 2*d/t^2
            float acceleration = 2f * modDashDodge / Mathf.Pow(timeToDodge, 2f);

            float dodgeForce = Mathf.Sqrt(2f * acceleration * modDashDodge) * rb.mass;

            float initVel = Mathf.Sqrt(2f * acceleration * modDashDodge);

            float curVel = initVel;

            float currentTime = timeToDodge;
            #endregion

            //TODO:
            //Replace this with different FX later.
            launchParticles.Play();

            //If the game is paused make this coroutine 
            //infinite loop here until unpaused.
            //Otherwise if the game is paused while inside the
            //other while loop it'll wait for FixedUpdate for
            //quite a long time.
            while (GameManager.instance.isPaused)
            {
                yield return null;
            }

            while (frames > 0)
            {


                if (frames < intangibilityFrames)
                {
                    state = PlayerState.helpless;
                }
                else
                {
                    state = PlayerState.intangible;
                }

                curVel = initVel - acceleration * (timeToDodge - currentTime);
                //rb.velocity = playerSprite.transform.right.normalized * curVel;
                //this dodges in the direction of movement
                rb.velocity = camRelInput.normalized * curVel;
                //do the physics for dodging.
                //TODO:
                //code spot dodging https://www.ssbwiki.com/Spot_dodge.
                frames--;
                //we wait for fixed update because we need to be in there to mess with physics.
                yield return new WaitForFixedUpdate();
            }
            
            //TODO:
            //Replace this with different FX later.
            launchParticles.Stop();
            rb.velocity = Vector2.zero;

            //go into freefall after dodging.
            //also set jump count to zero.
            dodging = false;
            jumpCount = 0;
            state = PlayerState.helpless;
        }
    }

    //this coroutine is currently used to launch the player when their shield breaks.
    private IEnumerator JumpCoroutine(int framesTotal, float height)
    {
        //Debug.Break();
        int frames = framesTotal;
        //this coroutine is currently used to launch the player when their shield breaks.
        state = PlayerState.launched;

        //const float delta = 1f / 60f;
        float timeToJump = frames / 60f;
        //float timeToDash = frames * Time.deltaTime;

        float modJumpHeight = height * dashModifier;

        //This helped me solve it: https://www.quora.com/Given-time-and-distance-how-do-you-calculate-acceleration
        //distance -> d
        //average velocity = d/t
        //final velocity = 2*d/t
        //acceleration = final velocity / t
        //therefore acceleration - 2*d/t^2
        float acceleration = 2f * modJumpHeight / Mathf.Pow(timeToJump, 2f);

        float jumpForce = Mathf.Sqrt(2f * acceleration * modJumpHeight) * rb.mass;

        float initVel = Mathf.Sqrt(2f * acceleration * modJumpHeight);

        //Time at max distance = v0 / acceleration
        Debug.Log((initVel / acceleration).ToString().Color("lime"));

        Debug.Log("Jump Force: " + jumpForce);
        //velocity = force / mass * time
        //float dashVelocity = dashForce / rb.mass * timeToDash;

        rb.AddForce(spriteParent.transform.up.normalized * jumpForce, ForceMode2D.Impulse);
        rb.AddForce(-spriteParent.transform.up.normalized * acceleration * rb.mass);
        //frames--;
        Debug.Log(("RBVel: " + rb.velocity).ToString().Color("purple"));
        //calling add force here and then again in the while loop is fine
        //accept when it's the first iteration of the loop and it hasn't
        //returned to execution. 

        //When 2 addforce calls are made during the same frame only one seems
        //to be applied.
        yield return new WaitForFixedUpdate();

        float currentTime = timeToJump;

        //Debug.Break();
        launchParticles.Play();
        while (/*currentTime > 0*/frames > 0)
        {
            if (!isHitStunned)
            {

                //If the game is paused make this coroutine 
                //infinite loop here until unpaused.
                while (GameManager.instance.isPaused)
                {
                    yield return null;
                }

                Debug.DrawRay(transform.position, rb.velocity, Color.red);
                Debug.Log("InitVel: " + initVel + " Acceleration: " + acceleration);
                Debug.Log("CurrentTime: " + currentTime + " FrameCount: " + frames);
                //rb.velocity = playerSprite.transform.right.normalized * initVel;
                //rb.velocity = playerSprite.transform.right * dashVelocity;

                //decelerate to reach distance.
                /* if (Mathf.Abs(rb.velocity.x) > 0)
                     rb.AddForce(-transform.right * acceleration * rb.mass);*/


                rb.AddForce(-spriteParent.transform.up.normalized * acceleration * rb.mass);
                Debug.Log(("RBVel: " + rb.velocity).ToString().Color("cyan"));



                //decrement.
                frames--;
                currentTime -= Time.deltaTime;
                if (currentTime < 0.0001)
                {
                    currentTime = 0;
                }
                yield return new WaitForFixedUpdate();

            }
        }

        Debug.Log("Done!");
        Debug.Log("Dist: " + dashDist + "\nDist Reached: " + transform.position.x + "\nScale to reach desired: " + dashDist / transform.position.x + "\nTimeToDash: " + timeToJump + "\ncurrentTime: " + currentTime);

        //Debug.Break();

        launchParticles.Stop();
        //go back to base state.
        //yield return new WaitForEndOfFrame();
        state = PlayerState.None;
        //set dashCoroutine back to null after finishing
        //so we don't think we are still running.
        jumpCoroutine = null;

    }

    private void HandleUI()
    {
        if (characterIcon)
        {
            if (characterIcon.GetPercent() != damagePercent)
                characterIcon.SetPercent(damagePercent);
            //this is what determines how many stocks are displayed in the UI.
            if (GameManager.instance.gameMode != null)
            {
                //if (GameManager.instance.gameMode.players[characterIndex] != null)
                characterIcon.stockCount = GameManager.instance.gameMode.players[characterIndex].stock;
            }
        }
        else
        {
            Debug.LogWarning("This character has no icon assigned!");
        }
    }

    private void HandlePassiveAnimation()
    {
        Vector2 localVel = transform.InverseTransformDirection(rb.velocity);

        animator.SetBool("inAir", inAir);
        //only when we are falling do we turn this var on.
        falling = localVel.y < 0;
        animator.SetBool("falling", falling);
       
        //only set speed based off of horizontal movement.

        //for some reason inputting up then trying to move
        //left or right only plays the animation and doesn't
        //let you move left or right. This is only after 
        //inputting up and attack to perform a smash attack.

        //we need to check if the up attack/smash/special input
        //is occuring then set speed to zero so pushing left 
        //or right doesn't influence the animation.
        animator.SetFloat("speed", Mathf.Abs(moveInput.x) > 0.7 ? 1f : Mathf.Abs(moveInput.x) < 0.1f ? 0f : 0.5f);

        animator.SetBool("holdAttack", shouldAttackContinuous);
    }

    private void HandleRotation()
    {

        //We do all rotations after
        //input so that the back 
        //aerial can be registered.
        //if the player is moving the stick in the same direction for more than one frame,
        //set the direction the player is facing.
        if (playerInput.currentControlScheme.Equals("Gamepad") && (moveInput.x > 0 && lastXinput > 0 || moveInput.x < 0 && lastXinput < 0) && Mathf.Abs(moveInput.x) - Mathf.Abs(lastXinput) > 0)
        {
/*            if (rotateCoroutine != null)
            {
                return;
            }*/
            Debug.Log("Will Rotate".Color("green"));
            //isFacingLeft = xAxis < 0 ? true : false;
            //this needs a deadzone because otherwise
            //up/down directional attacks
            //will switch directions for no 'intended' reason.
            float deadzone = 0.1f;
            bool prevFacing = isFacingLeft;
            if (xAxis > 0)
            {
                isFacingLeft = false;
            }
            else if (xAxis < 0)
            {
                isFacingLeft = true;
            }
            if (prevFacing != isFacingLeft)
            {
                Debug.Log("Current Direction: " + curDirection + " : " + isFacingLeft);
                if (rotateCoroutine != null)
                {
                    StopCoroutine(rotateCoroutine);
                    rotateCoroutine = null;
                }
                /*                if (dashCoroutine != null)
                                    StopCoroutine(dashCoroutine);
                                dashCoroutine = null;*/
                Debug.Log("Starting Rotation Coroutine");
                rotateCoroutine = StartCoroutine(RotateCoroutine(isFacingLeft ? 0 : 180, isFacingLeft ? 180 : 0, 10));
            }
            //playerSprite.transform.rotation = Quaternion.Euler(1, isFacingLeft ? 180 : 0, 1);
        }

        /*        if (lastDirection != Direction.None && playerInput.currentControlScheme.Equals("Gamepad") && lastDirection != curDirection)
                {

                    float deadzone = 0.1f;
                    if (xAxis > 0)
                    {
                        isFacingLeft = false;
                    }
                    else if (xAxis < 0)
                    {
                        Debug.Log("Here: ");
                        isFacingLeft = true;
                    }
                    if (lastDirection != curDirection)
                        rotateCoroutine = StartCoroutine(RotateCoroutine(isFacingLeft ? 0 : 180, isFacingLeft ? 180 : 0, 10));
                }*/

        //if user is inputting via keyboard
        if (playerInput.currentControlScheme.Equals("Keyboard&Mouse") && Mathf.Abs(moveInput.x) > 0)
        {
            Debug.Log("Will Rotate".Color("green"));
            spriteParent.transform.rotation = Quaternion.Euler(1, xAxis < 0 ? 180 : 0, 1);
            isFacingLeft = xAxis < 0 ? true : false;
        }
    }

    private void HandleInstantaneousRotation()
    {

        //We do all rotations after
        //input so that the back 
        //aerial can be registered.
        //if the player is moving the stick in the same direction for more than one frame,
        //set the direction the player is facing.
        if (playerInput.currentControlScheme.Equals("Gamepad") && (moveInput.x > 0 && lastXinput > 0 || moveInput.x < 0 && lastXinput < 0) && Mathf.Abs(moveInput.x)/* - Mathf.Abs(lastXinput) */> 0)
        {
            /*            if (rotateCoroutine != null)
                        {
                            return;
                        }*/
            Debug.Log("Will Rotate".Color("green"));
            //isFacingLeft = xAxis < 0 ? true : false;
            //this needs a deadzone because otherwise
            //up/down directional attacks
            //will switch directions for no 'intended' reason.
            float deadzone = 0.1f;
            bool prevFacing = isFacingLeft;
            if (xAxis > 0)
            {
                isFacingLeft = false;
            }
            else if (xAxis < 0)
            {
                
                isFacingLeft = true;
            }
            /*if (prevFacing != isFacingLeft)
            {
                Debug.Log("Current Direction: " + curDirection + " : " + isFacingLeft);
                if (rotateCoroutine != null)
                    StopCoroutine(rotateCoroutine);
                *//*if (dashCoroutine != null)
                    StopCoroutine(dashCoroutine);
                dashCoroutine = StartCoroutine(DashCoroutine());*//*
                playerSprite.transform.rotation = Quaternion.Euler(0, isFacingLeft ? 180 : 0, 0);
            }*/
            Debug.Log("Current Direction: " + curDirection + " : " + isFacingLeft);
            if (rotateCoroutine != null)
            {
                StopCoroutine(rotateCoroutine);
                rotateCoroutine = null;
            }

            /*if (dashCoroutine != null)
                StopCoroutine(dashCoroutine);
            dashCoroutine = StartCoroutine(DashCoroutine());*/
            spriteParent.transform.rotation = Quaternion.Euler(0, isFacingLeft ? 180 : 0, 0);
            //playerSprite.transform.rotation = Quaternion.Euler(1, isFacingLeft ? 180 : 0, 1);
        }

        /*        if (lastDirection != Direction.None && playerInput.currentControlScheme.Equals("Gamepad") && lastDirection != curDirection)
                {

                    float deadzone = 0.1f;
                    if (xAxis > 0)
                    {
                        isFacingLeft = false;
                    }
                    else if (xAxis < 0)
                    {
                        Debug.Log("Here: ");
                        isFacingLeft = true;
                    }
                    if (lastDirection != curDirection)
                        rotateCoroutine = StartCoroutine(RotateCoroutine(isFacingLeft ? 0 : 180, isFacingLeft ? 180 : 0, 10));
                }*/

        //if user is inputting via keyboard
        if (playerInput.currentControlScheme.Equals("Keyboard&Mouse") && Mathf.Abs(moveInput.x) > 0)
        {
            Debug.Log("Will Rotate".Color("green"));
            spriteParent.transform.rotation = Quaternion.Euler(0, xAxis < 0 ? 180 : 0, 0);
            isFacingLeft = xAxis < 0 ? true : false;
        }
    }

    private void HandleJump()
    {


        if (doJump && state != PlayerState.shielding)
        {
            //Cancel out of dash and begin jumping
            if (state == PlayerState.dashing)
            {
                CancelDashing();
            }

            //this constant (1.2) was discovered
            //by dividing the desired jump height by
            //the height actually reached.
            //this was the value I got regardless of the 
            //jump time. I also think that the timeToApex
            //probably affects this value, for this constant
            //the timeToApex was set to 0.01f.
            //I will look more into this at some other point.

            //I ACTUALLY GOT TO A HEIGHT OF 5 WHEN I MULTIPLIED 
            //BY THIS CONSTANT. 

            //I am genuinely impressed how accurate this formula
            //now is.


            //make the jump height 1/60 
            //then take the actual value reached by the jump
            //then do 1/60 / actual value
            //then do Desired height / actual value
            //and that gives you the most accurate
            //value to input as height for the jump.

            //OR set jump height to 1
            //and take the jump height reached by the jump
            //and do Desired Height / jump height reached for desired height of 1
            //and that gives you the properly scaled value.
            //timeToApex affects this so for a timeToApex
            //of 0.01 the jumpHeight scale modifier is 1.2f.

            //float modifier = 1.2f;//timeToApex / 0.00833333333f;
            float modifiedJumpHeight = (float)jumpHeight * jumpHeightModifier; //* modifier;


            //play crouch animation.
            animator.ResetTrigger("jump");
            doJump = false;
            jumpCount--;
            ogJump = transform.position;
            float jumpForce;


            //I did the work out and 2 * h / t = gravity so I'm going to do that.
            gravity = 2 * modifiedJumpHeight / timeToApex;
            fallGravity = 2 * modifiedJumpHeight / timeToFall;

            float projectedHeight = timeToApex * gravity / 2f;
            Debug.Log(timeToApex + " " + projectedHeight + " " + gravity);
            Debug.Log(("Projected Height " + projectedHeight).ToString().Color("Cyan"));


            //set gravity so that we jump in the amount of time we want
            //Gravity = 2 * height / time^2
            //gravity = 2 * jumpHeight / timeToApex * timeToApex;

            jumpForce = Mathf.Sqrt(2f * gravity * modifiedJumpHeight) * rb.mass; //multiply by mass at the

            //end so that it reaches the height regardless of weight.
            buttonTime = (jumpForce / (rb.mass * gravity)); //initial velocity divided by player accel for gravity gives us the amount of time it will take to reach the apex.

            /*            Debug.Log(("Force: " + jumpForce + " " + "Time: " + buttonTime).ToString().Color("white"));


                        jumpForce = Mathf.Sqrt(2f * gravity * jumpHeight * 2f) * rb.mass;*/

            //get the new button time.
            //buttonTime /= 2;

            Debug.Log(("Force: " + jumpForce + " " + "Time: " + buttonTime).ToString().Color("orange"));

            rb.velocity = new Vector2(rb.velocity.x, 0f); //Reset y velocity before we jump so it is always reaching desired height.

            rb.AddForce(transform.up * jumpForce, ForceMode2D.Impulse); //don't normalize transform.up cus it makes jumping more inconsistent.


            jumpTime = 0;
            jumping = true;
            jumpCanceled = false;
        }

        //Where I learned this https://www.youtube.com/watch?v=7KiK0Aqtmzc
        //This is what gives us consistent fall velocity so that jumping has the correct arc.
        Vector2 localVel = transform.InverseTransformDirection(rb.velocity);

        if (localVel.y < 0 && inAir) //If we are in the air and at the top of the arc then apply our fall speed to make falling more game-like
        {
            //animator.SetBool("falling", true);
            //we don't multiply by mass because forceMode2D.Force includes that in it's calculation.
            //set gravity to be fallGravity.
            gravity = fallGravity;
            Vector2 jumpVec = -transform.up * (fallMultiplier - 1)/* * 100f * Time.deltaTime*/;
            rb.AddForce(jumpVec, ForceMode2D.Force);
        }
        /*        else if (localVel.y > 0 && !jumpAction.IsPressed() && inAir) //If we stop before reaching the top of our arc then apply enough downward velocity to stop moving, then proceed falling down to give us a variable jump.
                {
                    Debug.Log("Low Jump".Color("cyan"));
                    //animator.SetBool("falling", true);
                    //change to falling gravity
                    gravity = fallGravity;
                    Vector2 jumpVec = -transform.up * (lowJumpMultiplier - 1) *//* * 100f * Time.deltaTime*//*;
                    rb.AddForce(jumpVec, ForceMode2D.Force);
                    Debug.Log(rb.velocity);
                }*/

        /*        if (localVel.y > 0 && jumpTime >= buttonTime)
                {
                    //rb.AddForce(-transform.up * Mathf.Sqrt(2f * Physics2D.gravity.magnitude * jumpHeight) * rb.mass);
                    rb.AddForce(-transform.up * Physics2D.gravity.magnitude);
                }*/

    }

    private void HandleAttack()
    {
        //TODO: 
        //Code an if statement for each attack input, a neutral and 4 directions.
        //Make sure to change this if we are handling air attacks.
        if (shouldAttack || shouldWaitToAttack || doDelayedAttack)
        {
            if (doDelayedAttack)
            {
                doDelayedAttack = false;
            }
            shouldSmash = didTap && attackAction.IsPressed();

            //if the player inputted the attack button recently and
            //they haven't tapped give them a small window to tap
            //so that they can smash attack. 
            if (!didTap && shouldWaitToAttack)
            {
                //Debug.Log("Should wait");
                return;
            }
            else if (didTap && shouldWaitToAttack)
            {
                //Debug.Log("Should Attack");
                shouldSmash = true;
                shouldWaitToAttack = false;
                doDelayedAttack = false;
                shouldAttack = false;
            }

            //turn off the didtap var.
            didTap = false;

            if (curDirection == Direction.Left || curDirection == Direction.Right)
            {
                //Forward/Side attack
                if (shouldSmash)
                {
                    ForwardSmash();
                }
                else
                {
                    ForwardTilt();
                }
            }
            else if (curDirection == Direction.Up)
            {
                //Up attack
                if (shouldSmash)
                {
                    UpSmash();
                }
                else
                {
                    UpTilt();
                }
            }
            else if (curDirection == Direction.Down)
            {
                //Down attack
                if (shouldSmash)
                {
                    DownSmash();
                }
                else
                {
                    DownTilt();
                }
            }
            else
            {
                //neutral
                Neutral();
            }
        }
    }

    private void HandleAerial()
    {
        //TODO: 
        //Code an if statement for each attack input, a neutral and 4 directions.
        //Make sure to change this if we are handling air attacks.
        if (shouldAttack)
        {
            Vector2 directionInput = new Vector2(xAxis, yAxis);


            if (curDirection == Direction.Up)
            {
                //Up air
                UpAerial();
            }
            else if (curDirection == Direction.Down)
            {
                //Down air
                DownAerial();
            }
            else if (curDirection == Direction.Right || curDirection == Direction.Left)
            {
                //Forward/Back air

                //Forward Aerial
                //only if our sprite is 
                //facing the same direction of 
                //our current input.
                if (Vector2.Dot(spriteParent.transform.right, directionInput) > 0)
                {
                    ForwardAerial();
                }
                //We are inputting the opposite of 
                //our facing direction. 
                else
                {
                    BackAerial();
                }
            }
            else
            {
                //neutral
                NeutralAerial();
            }

        }
    }

    private void HandleSpecial()
    {
        //TODO: 
        //Code an if statement for each attack input, a neutral and 4 directions.
        //Make sure to change this if we are handling air attacks.
        if (shouldSpecial)
        {
            Vector2 directionInput = new Vector2(xAxis, yAxis);
            Vector2 dotVector = new Vector2(Vector2.Dot(Vector2.right, directionInput), Vector2.Dot(Vector2.up, directionInput));

            if (dotVector.x != 0 && dotVector.x == dotVector.y)
            {
                //I think if they're the same I'm just going to 
                //make it do up/down attacks depending on if y is positive or negative.
                Debug.LogWarning("The user input equal weight on both the x and y axes when attacking. Please figure out how to avoid this happening.");
            }
            //if we have a mixed input, let's see which is greater.
            else if (dotVector.x != 0 && dotVector.y != 0)
            {
                //Choose horizontal attack
                if (Mathf.Abs(dotVector.x) > Mathf.Abs(dotVector.y))
                {
                    //Forward Special
                    ForwardSpecial();
                }//choose vertical attack.
                else
                {
                    //Up Special
                    if (dotVector.y > 0)
                    {
                        UpSpecial();
                    }//Down Special
                    else
                    {
                        DownSpecial();
                    }
                }
            }
            //Horizontal attacking (Left & Right Special)
            else if (xAxis != 0 && yAxis == 0)
            {
                //Forward Special
                ForwardSpecial();
            }
            //Vertical attacking (Up & Down Special)
            else if (xAxis == 0 && yAxis != 0)
            {
                //Up Special
                if (dotVector.y > 0)
                {
                    UpSpecial();
                }//Down Special
                else
                {
                    DownSpecial();
                }
            }
            //Neutral attacking
            else
            {
                NeutralSpecial();
            }
        }
    }

    private void ApplyFinalMovements() //Step 1
    {
        //Do not let player use inputs when launched.
        if (state == PlayerState.launched || state == PlayerState.grabbed)
        {
            return;
        }

        //TODO:
        //we need to check if we are helpless here and only apply Directional Influence (DI) 
        //instead of the normal movement input.


        //if we are dashing we shouldn't set 
        //the x velocity.
        //if we aren't rotating allow player to move.
        //We should not be able to move while shielding.
        if (state != PlayerState.dashing && state != PlayerState.shielding && rotateCoroutine == null && !dodging)
        {
            //set velocity directly, don't override y velocity.
            rb.velocity = new Vector2(xAxis * moveSpeed, rb.velocity.y);
        }

        //Apply gravity, because gravity is not affected by mass and 
        //we can't use ForceMode.acceleration with 2D just multiply
        //by mass at the end. It's basically the same.
        //In unity it factors in mass for this calculation so 
        //multiplying by mass cancels out mass entirely.
            rb.AddForce(-transform.up * gravity * rb.mass);
    }

    #region Attack Methods

    public virtual void Neutral()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: Neutral ".Color("yellow"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.neutral);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void DashAttack()
    {
        /*if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }*/

        Debug.Log("Player 1: Dash Attack ".Color("lime"));

        //handle rotation
        //we don't do that here because they should 
        //only attack in the direction they were dashing.
        //HandleRotation();

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.dashAttack);

        //lastly set the playerState back to none.
        //After they do a dash attack they should stop dashing.

        state = PlayerState.None;
    }

    public virtual void ForwardTilt()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: ForwardTilt ".Color("yellow"));

        //handle rotation

        //HandleRotation();

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.forwardTilt);

        //lastly set the playerState back to none.
        state = PlayerState.None;
    }

    public virtual void UpTilt()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: UpTilt ".Color("yellow"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.upTilt);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void DownTilt()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: DownTilt ".Color("yellow"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.downTilt);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    #endregion

    #region Aerial Methods

    public virtual void NeutralAerial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: NeutralAerial ".Color("white"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.neutralAerial);


        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void ForwardAerial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: ForwardAerial ".Color("white"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.forwardAerial);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void BackAerial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        //the only time we rotate when jumping
        //is if we do a back aerial.
        //back aerial is always an input opposite of 
        //the direction the player is facing so we always
        //invert rotation on this attack.
        spriteParent.transform.rotation = Quaternion.Euler(0f, spriteParent.transform.rotation.eulerAngles.y == 0 ? 180f : 0f, 0f);
        Debug.Log("Player 1: BackAerial ".Color("white"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.backAerial);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void UpAerial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: UpAerial ".Color("white"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.upAerial);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void DownAerial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: DownAerial ".Color("white"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.downAerial);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    #endregion

    #region Special Attack Methods

    public virtual void NeutralSpecial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: NeutralSpecial ".Color("orange"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.neutralSpecial);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void ForwardSpecial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: ForwardSpecial ".Color("orange"));

        //Handle switching facing directions
        //HandleRotation();

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.forwardSpecial);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void UpSpecial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: UpSpecial ".Color("orange"));

        //TODO: Actually code this attack.

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    public virtual void DownSpecial()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: DownSpecial ".Color("orange"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.downSpecial);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    #endregion

    #region Smash Attack Methods

    public virtual void ForwardSmash()
    {
        //please call "HandleRotation" before calling this method.
        //it is imperative so that your character is facing forward. (the direction of input horizontally)

        //you cannot smash attack while in the air.
        if (inAir)
        {
            return;
        }

        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }


        Debug.Log("Player 1: ForwardSmash ".Color("purple"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.forwardSmash);

        //lastly set the playerState back to none.
        state = PlayerState.None;
    }

    public virtual void UpSmash()
    {
        //you cannot smash attack while in the air.
        if (inAir)
        {
            return;
        }

        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }
        else
        {
            state = PlayerState.attacking;
        }

        Debug.Log("Player 1: UpSmash ".Color("purple"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.upSmash);

        //lastly set the playerState back to none.
        state = PlayerState.None;
    }

    public virtual void DownSmash()
    {
        if (state == PlayerState.attacking)
        {
            //cannot attack because we are already attacking.
            return;
        }

        state = PlayerState.attacking;
        Debug.Log("Player 1: DownSmash ".Color("purple"));

        //TODO: Actually code this attack.
        SetHurtboxAttackInfo(moveset.downSmash);

        //lastly set the playerState back to none.
        state = PlayerState.None;

    }

    #endregion


    public void Launch(float angleDeg, Vector2 attackerDirection, float damageDelt, float baseKnockback, float knockbackScale, int hitLag)
    {
        state = PlayerState.launched;
        //rb.AddForce(direction * SmashKnockback(damageDelt, damagePercent, baseKnockback, knockbackScale), ForceMode2D.Impulse);
        //apparently I'm supposed to multiply the knockback value by 0.03 for the launch but it isn't the right value
        //I don't think. I'm very tired so I'll have to come back and work on this.

        //Ok, so I'm pretty sure that they don't use a weight while applying forces to the character,
        //as in they only add the weight in the formula and don't account for it later because their 
        //formula does 200 / w + 100 which scales the weight to be a 0-2f value. 
        //float totalKB = SmashKnockback(damageDelt, damagePercent, baseKnockback, knockbackScale);
        //rb.velocity = Vector2.one * totalKB * 0.03f * 10f;
        //Debug.Log(totalKB);
        //rb.velocity = angleDeg == 361f ? RadiansToVector(Mathf.Deg2Rad * SakuraiAngle(totalKB, false)) : RadiansToVector(Mathf.Deg2Rad * angleDeg) * SmashKnockback(damageDelt, damagePercent, baseKnockback, knockbackScale) * 0.03f * 10f;
        //rb.AddForce(direction * Mathf.Sqrt(2 * 9.81f * SmashKnockback(damageDelt, damagePercent, baseKnockback, knockbackScale)), ForceMode2D.Impulse);
        StartCoroutine(LaunchCoroutine(angleDeg, attackerDirection, damageDelt, damagePercent, baseKnockback, knockbackScale, hitLag));
        //Debug.Log(rb.velocity + " " + direction * SmashKnockback(damageDelt, damagePercent, baseKnockback, knockbackScale));
        //rb.mass = 1f;
        //rb.AddForce(direction.normalized * SmashKnockback(damageDelt, damagePercent, baseKnockback, knockbackScale) * 0.03f * 10f, ForceMode2D.Impulse);
    }


    //https://www.ssbwiki.com/Knockback#Formula
    public float SmashKnockback(float damageDelt, float currentDamage, float baseKnockback, float knockbackScale)
    {
        float knockback = 0f;
        float p = currentDamage;
        float d = damageDelt;
        //if an attack is weight independent set this value to 100.
        float w = rb.mass;
        //knockback scaling (s / 100) so s = 110 would be 110/100 = 1.1 scale.
        float s = knockbackScale;
        s /= 100f;
        //the attack's base knockback.
        float b = baseKnockback;
        //we aren't going to use the r yet as it is overly complex for our current design.

        //SUPER IMPORTANT NOTE:
        //To determine how far a character is launched away, the numerical amount of knockback caused is multiplied by 0.03 to
        //calculate launch speed, and the initial value of launch speed then decays by 0.051 every frame, so that the character
        //eventually loses all momentum from the knockback. During this time, character-specific attributes such as air friction
        //are disabled; however, falling speed still takes effect, giving fast fallers better endurance against vertical knockback
        //than others of their weight.

        //because weight is input to our rigidbody by the default physics we have to modify this a little bit.
        knockback = ((((p / 10f + p * d / 20f) * 200f / (w + 100f) * 1.4f) + 18) * s) + b;
        Debug.Log(knockback.ToString().Color("blue"));
        return knockback;
    }

    public void Knockback(Vector2 hitDirection, float damageDelt, float currentDamage, float baseKnockback, float knockbackScale)
    {
        float knockback = 0f;
        float p = currentDamage;
        float d = damageDelt;
        //if an attack is weight independent set this value to 100.
        float w = rb.mass;
        //knockback scaling (s / 100) so s = 110 would be 110/100 = 1.1 scale.
        float s = knockbackScale;
        s /= 100f;
        //the attack's base knockback.
        float b = baseKnockback;
        //we aren't going to use the r yet as it is overly complex for our current design.

        //SUPER IMPORTANT NOTE:
        //To determine how far a character is launched away, the numerical amount of knockback caused is multiplied by 0.03 to
        //calculate launch speed, and the initial value of launch speed then decays by 0.051 every frame, so that the character
        //eventually loses all momentum from the knockback. During this time, character-specific attributes such as air friction
        //are disabled; however, falling speed still takes effect, giving fast fallers better endurance against vertical knockback
        //than others of their weight.

        //because weight is input to our rigidbody by the default physics we have to modify this a little bit.
        knockback = ((((p / 10f + p * d / 20f) * 200f / (w + 100f) * 1.4f) + 18) * s) + b;
        Debug.Log(knockback.ToString().Color("green"));

        // Apply the force to the Rigidbody in the hitDirection
        //launch speed is calculated by multiplying knockback by 0.3. 
        rb.velocity = hitDirection.normalized * knockback * 0.03f;

        //Directly from Unity's rb.AddForce docs:
        //"Apply the impulse force instantly with a single function call. This mode depends on the mass of rigidbody so more force must be applied to push or twist higher-mass objects the same amount as lower-mass objects. This mode is useful for applying forces that happen instantly, such as forces from explosions or collisions. In this mode, the unit of the force parameter is applied to the rigidbody as mass*distance/time."

        //mass*distance/time is the important part.
        //What does it mean distance? the magnitude of the vector?
    }

    public IEnumerator LaunchCoroutine(float angleDeg, Vector2 hitDirection, float damageDelt, float currentDamage, float baseKnockback, float knockbackScale, int hitLag)
    {
        //I need to make this coroutine exit when we start a new LaunchCoroutine.

        state = PlayerState.launched;
        float knockback = 0f;
        float p = currentDamage;
        float d = damageDelt;
        //if an attack is weight independent set this value to 100.
        float w = rb.mass;
        //knockback scaling (s / 100) so s = 110 would be 110/100 = 1.1 scale.
        float s = knockbackScale;
        s /= 100f;
        //the attack's base knockback.
        float b = baseKnockback;
        //we aren't going to use the r yet as it is overly complex for our current design.

        //SUPER IMPORTANT NOTE:
        //To determine how far a character is launched away, the numerical amount of knockback caused is multiplied by 0.03 to
        //calculate launch speed, and the initial value of launch speed then decays by 0.051 every frame, so that the character
        //eventually loses all momentum from the knockback. During this time, character-specific attributes such as air friction
        //are disabled; however, falling speed still takes effect, giving fast fallers better endurance against vertical knockback
        //than others of their weight.

        //because weight is input to our rigidbody by the default physics we have to modify this a little bit.
        knockback = ((((p / 10f + p * d / 20f) * 200f / (w + 100f) * 1.4f) + 18) * s) + b;
        Debug.Log(knockback.ToString().Color("red"));
        Debug.Log(angleDeg);
        float angleRad = Mathf.Deg2Rad * angleDeg;
        Debug.Log(angleRad + ":" + angleDeg);

        Vector2 newDirection;

        //Sakurai angle check
        if (Mathf.Abs(angleDeg) == 361f)
        {
            //for now, we don't know if the other player did this as an aerial so input false.
            angleRad = Mathf.Deg2Rad * SakuraiAngle(knockback, false);
            Debug.Log(angleRad + ":" + angleRad * Mathf.Rad2Deg);
            newDirection = RadiansToVector(angleRad);
            //if the angle is negative flip it over the x axis.
            /*            if (angleDeg < 0)
                        {
                            hitDirection = new Vector2(-hitDirection.x, hitDirection.y);
                        }*/
        }
        else
        {
            Debug.Log("Shouldn't be here!");
            newDirection = RadiansToVector(angleRad);
        }
        Debug.DrawRay(transform.position, hitDirection * 5f, Color.blue, 1.5f);

        //reflect over x axis if the direction goes
        //to the left.
        if (hitDirection.x < 0)
        {
            Debug.Log("SWITCH");
            newDirection.x = -newDirection.x;
        }


        //we multiply by 10f because in smash the game unit is actually 1 decimeter, https://www.ssbwiki.com/Distance_unit
        //so if we want to keep a "normal" sized character to be 1x1 unity units we need to multiply by 10
        //to scale our formula properly. 
        //multiply by the launch speed factor 0.03 just like smash.
        //but here it would be 0.3 because we already multiplied by 10
        //but I think it's actually 0.2 because the knockback values only
        //look accurate to the smash ultimate calculator: https://rubendal.github.io/SSBU-Calculator/
        //at this value. Not sure why.
        float launchSpeed = knockback * 0.2f;
        float mass = rb.mass;
        //rb.mass = 1f;

        //used for adding gravity.
        float t = 0f;

        //Do launch particles when launch 
        //is strong enough.
        if (knockback > 30f)
        {
            launchParticles.Play();
        }
        else
        {
            launchParticles.Stop();
        }

        float waitTime = launchSpeed;

        //launch for this many frames.
        hitStunFrames = Hitstun(knockback, false);

        while (/*launchSpeed > 0*/ hitStunFrames > 0)
        {

            //If you decide not to apply gravity to the y axis during a launch don't forget
            //to remove these floats and just do rb.velocity = hitDirection * launchSpeed;

            //you need to look into how coroutines work and make sure this while loop isn't
            //updated every frame and that you properly applying this formula.
            //you may need to deprecate the angle so that it applies the force as an arc
            //or do the thing where you apply gravity again.

            //OG
            //float horizontalLaunchSpeed = launchSpeed * Mathf.Cos(angleRad);
            //float verticalLaunchSpeed = launchSpeed * Mathf.Sin(angleRad) - Physics2D.gravity.magnitude * t;

            //NEW
            //float horizontalLaunchSpeed = launchSpeed * Mathf.Cos(angleRad) * t;
            //float verticalLaunchSpeed = launchSpeed * Mathf.Sin(angleRad) * t - (9.8f*t*t)/2;

            //Debug.Log(new Vector2(horizontalLaunchSpeed, verticalLaunchSpeed).ToString().Color("cyan"));
            //rb.velocity = new Vector2(hitDirection.x * Mathf.Clamp(horizontalLaunchSpeed, 0f, Mathf.Infinity), hitDirection.y * verticalLaunchSpeed);

            rb.velocity = newDirection * launchSpeed;//new Vector2(horizontalLaunchSpeed, verticalLaunchSpeed);
            //apply gravity.
            Debug.DrawRay(transform.position, rb.velocity, Color.red, 1.5f);
            if (!isGrounded)
                rb.velocity = rb.velocity + new Vector2(0f, -baseGravity /** t*/);
            launchParticles.gameObject.transform.rotation = Quaternion.LookRotation(rb.velocity);
            launchSpeed -= 0.51f;
            hitStunFrames--;
            //waitTime -= 0.51f;
            t += Time.deltaTime;
            yield return new WaitForFixedUpdate();
        }
        //stop doing launch particles.
        launchParticles.Stop();
        Debug.Log("CoroutineStop");
        state = PlayerState.None;
    }

    //this is from https://github.com/rubendal/SSBU-Calculator/blob/gh-pages/js/formulas.js#L162
    private int Hitstun(float kb, bool windbox)
    {
        if (windbox)
        {
            return 0;
        }
        var hitstun = (kb * /*parameters.hitstun*/ 0.4);
        if (hitstun < 0)
        {
            return 0;
        }

        //Minimum hitstun for non-windbox hitboxes
        if (hitstun < 5)
            hitstun = 5;

        //convert from double to int.
        return (int)Math.Floor(hitstun) - 1;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        //if we are hit by a hurtbox that isn't a child of us then calculate damage and launch.
        if (collision.CompareTag("Hurtbox") && !collision.transform.IsChildOf(this.transform))
        {
            //when intangible we ignore attacks.
            //if we can't be damaged don't calculate this.
            if (state != PlayerState.intangible && state != PlayerState.shielding)
            {

                Debug.Log("We were Hit!".Color("red"));
                //get hurtbox
                Hurtbox h = collision.gameObject.GetComponent<Hurtbox>();
                //add damage delt to the total percent
                //https://rubendal.github.io/SSBU-Calculator/
                //Set the damage of a custom attack to 1 and you'll get the constant
                //1.26 and it doesn't scale with percent, the attach damage is always
                //multiplied by this value.
                damagePercent += (h.attackInfo.attackDamage * 1.26f);

                //get the vector that the player should be sent in relative 
                //to the hurtbox 
                Vector3 dir = h.transform.position - this.transform.position;
                dir.z = 0;
                Debug.DrawRay(transform.position, dir.normalized, Color.yellow, 1f);
                Debug.DrawRay(transform.position, -dir.normalized, Color.cyan, 1f);
                Debug.DrawRay(transform.position, new Vector3(-dir.normalized.x, 0f, 0f), Color.magenta, 1f);
                Vector3 wishDir = RadiansToVector(Mathf.Deg2Rad * (h.attackInfo.launchAngle));
                //wishDir.x = -dir.x;
                Debug.DrawRay(transform.position, wishDir.normalized * 5f, Color.green, 1f);

                if (Vector2.Dot(collision.transform.position - transform.position, spriteParent.transform.right) < 0)
                {
                    Debug.Log("The other player is hitting me left!".Color("lime"));
                }
                else if (Vector2.Dot(collision.transform.position - transform.position, spriteParent.transform.right) > 0)
                {
                    Debug.Log("The other player is hitting me right!".Color("cyan"));
                }

                //launch the player based off of the attack damage.
                //old
                //Launch(h.attackInfo.launchAngle, RadiansToVector(Mathf.Deg2Rad * (h.attackInfo.launchAngle)), h.attackInfo.attackDamage, h.attackInfo.baseKnockback, h.attackInfo.knockbackScale, h.attackInfo.hitLag);
                //new
                Launch(h.attackInfo.launchAngle, -dir.normalized, h.attackInfo.attackDamage, h.attackInfo.baseKnockback, h.attackInfo.knockbackScale, h.attackInfo.hitLag);
                //Play the audio for getting hit.
                AudioManager.instance.globalSource.PlayOneShot(hitSounds[UnityEngine.Random.Range(0, hitSounds.Count)]);
            }
            else if (state == PlayerState.shielding)
            {
                Debug.Log((this.name + " Can't be damaged, they are shielding!").Color("red"));
                //TODO: 
                //Decrement the player's shield health by however much damage this attack does.
                //Push the player back a small amount even though they are shielding.

                //also do a check where if the player's shield is broken by this attack we launch them here.
            }
        }
        //the player entered the kill trigger. (kill bounds).
        else if (collision.gameObject.CompareTag("Kill"))
        {
            Debug.Log(gameObject.name + " was Knocked Out!");
            //kill player.
            //we destroy both player and the icon for it
            //because I am too lazy to just make code
            //to re-assign it.

            //We no longer do that, the GameMode and CharacterManager now handle that.
            //Destroy(characterIcon.gameObject);
            Destroy(gameObject);
        }
    }


    //Use the following methods below if you ever decide to add
    //the warning for the player that they've gone too close to the
    //kill area like in smash where it shows a little circle.
    private void OnBecameInvisible()
    {
        Debug.Log("Player is Offscreen!");
    }

    private void OnBecameVisible()
    {
        Debug.Log("Player is Onscreen!");
    }

    //https://github.com/rubendal/SSBU-Calculator
    private float SakuraiAngle(float kb, bool aerial)
    {
        if (aerial)
        {
            //I don't know why they used this calculation 
            //when they could've printed this value and
            //just put a constant here.
            return Mathf.Rad2Deg * 0.663225f;
        }
        if (kb < 60)
        {
            return 0;
        }
        if (kb >= 88)
        {
            return 38;
        }
        return Mathf.Min((kb - 60) / (88 - 60) * 38 + 1, 38); //https://twitter.com/BenArthur_7/status/956316733597503488
    }

    private Vector2 RadiansToVector(float radians)
    {
        return new Vector2((float)Math.Cos(radians), (float)Math.Sin(radians));
    }

    private void SetHurtboxAttackInfo(AttackInfo attackInfo)
    {
        if (hurtbox != null)
        {
            if (isFacingLeft)
            {
                //flip angle over x axis.
                float angle = -attackInfo.launchAngle;

                AttackInfo invertedX = new AttackInfo(angle, attackInfo.attackDamage, attackInfo.baseKnockback, attackInfo.knockbackScale, attackInfo.hitLag);

                hurtbox.attackInfo = invertedX;
            }
            else
            {
                hurtbox.attackInfo = attackInfo;
            }
        }
    }

    private void OnDestroy()
    {
        if (GameManager.instance.characterManager != null)
        {
            //play death sound
            if (AudioManager.instance != null && AudioManager.instance.globalSource != null)
            AudioManager.instance.globalSource.PlayOneShot(deathSounds[UnityEngine.Random.Range(0, deathSounds.Count)]);
            //TODO:
            //Spawn the death particle system and make it face the center of the map then play it.
            //if this is the last death of the match the characterManager or GameMode should call that coroutine that slows down the game. 

            GameManager.instance.characterManager.PlayerDied(characterIndex);
            //Call handle UI one last time so that our stock count is accurate. 
            HandleUI();
        }
    }

    /// <summary>
    /// Gives a direction based off of a vector.
    /// </summary>
    /// <param name="inputVector"></param>
    /// <returns></returns>
    public Direction GetDirection(Vector2 inputVector)
    {

        float deadZone = 0.3f;
        // Check if input vector magnitude is within the dead zone
        if (inputVector.magnitude < deadZone)
        {
            return Direction.None; // Input is within the dead zone
        }

        if (Math.Abs(inputVector.x) > Math.Abs(inputVector.y))
        {
            // More horizontal movement
            if (inputVector.x > 0)
                return Direction.Right;
            else if (inputVector.x < 0)
                return Direction.Left;
        }
        else
        {
            // More vertical movement
            if (inputVector.y > 0)
                return Direction.Up;
            else if (inputVector.y < 0)
                return Direction.Down;
        }

        return Direction.None;
    }

#if UNITY_EDITOR
    public void HandleDebug()
    {
        if (debugTextObj == null)
        {
            //Create a new GameObject with a TextMeshPro component
            debugTextObj = new GameObject("Debug State Text", typeof(TextMeshPro));
            debugStateText = debugTextObj.GetComponent<TextMeshPro>();
            //Set the parent of the debug text to be this transform.
            debugTextObj.transform.SetParent(this.transform, false);
            //Set the position of the debug text relative to it's parent.
            debugTextObj.transform.localPosition = debugTextPosition;

            debugStateText.fontSize = 6;
            debugStateText.alignment = TextAlignmentOptions.Center;

            //Set the debug texts text to be the current state.
            debugStateText.text = state.ToString();
        }
        else if (debugTextObj != null)
        {
            //Set the debug texts text to be the current state.
            debugStateText.text = state.ToString();
        }
    }
#endif

}

//used for storing the data of attacks in 
//the player's attack data dictionary.
[Serializable]
public struct AttackInfo
{
    public AttackInfo(float launchAngle, float attackDamage, float baseKnockback, float knockbackScale, int hitLag)
    {
        this.launchAngle = launchAngle;
        this.attackDamage = attackDamage;
        this.baseKnockback = baseKnockback;
        this.knockbackScale = knockbackScale;
        this.hitLag = hitLag;
    }

    /// <summary>
    /// The percentage of damage added to the player's damage meter upon a successful hit.
    /// </summary>
    public float attackDamage;

    /// <summary>
    /// The amount of additional hitlag 
    /// Applied by this attack when 
    /// launching.
    /// </summary>
    public int hitLag;

    /// <summary>
    /// The direction the enemy is sent in if this attack lands. In Degrees.
    /// </summary>
    public float launchAngle;

    /// <summary>
    /// The base knockback of this attack, regardless of the player's percentage.
    /// </summary>
    public float baseKnockback;

    /// <summary>
    /// Describes how much knockback and percent scale.
    /// </summary>
    public float knockbackScale;

    


}