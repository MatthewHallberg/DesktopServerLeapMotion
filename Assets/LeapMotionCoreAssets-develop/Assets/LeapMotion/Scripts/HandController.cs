﻿/******************************************************************************\
* Copyright (C) Leap Motion, Inc. 2011-2014.                                   *
* Leap Motion proprietary. Licensed under Apache 2.0                           *
* Available at http://www.apache.org/licenses/LICENSE-2.0.html                 *
\******************************************************************************/

using UnityEngine;
using System.Collections.Generic;
using Leap;
using System;

/**
* The Controller object that instantiates hands and tools to represent the hands and tools tracked
* by the Leap Motion device.
*
* HandController is a Unity MonoBehavior instance that serves as the interface between your Unity application
* and the Leap Motion service.
*
* The HandController script is attached to the HandController prefab. Drop a HandController prefab 
* into a scene to add 3D, motion-controlled hands. The hands are placed above the prefab at their 
* real-world relationship to the physical Leap device. You can change the transform of the prefab to 
* adjust the orientation and the size of the hands in the scene. You can change the 
* HandController.handMovementScale property to change the range
* of motion of the hands without changing the apparent model size.
*
* When the HandController is active in a scene, it adds the specified 3D models for the hands to the
* scene whenever physical hands are tracked by the Leap Motion hardware. By default, these objects are
* destroyed when the physical hands are lost and recreated when tracking resumes. The asset package
* provides a variety of hands that you can use in conjunction with the hand controller. 
*/
public class HandController : MonoBehaviour {
  protected static List<HandController> _mains = new List<HandController>();

  /* The HandController.Main property returns an instance of a HandController with it's isMain property set
   * to true.  If there are multiple main Hand Controllers this method will choose one in an undefined way.
   */
  public static HandController Main {
    get {
      if (_mains.Count == 0) {
        Debug.LogWarning("Could not find an active main Hand Controller.  One may not exist, or may not have been enabled yet.");
        return null;
      }
      return _mains[0];
    }
  }

  protected static List<HandController> _all = new List<HandController>();

  /* Returns a list of all currently active HandController instances.
   */
  public static List<HandController> All {
    get {
      return _all;
    }
  }

  // Reference distance from thumb base to pinky base in mm.
  protected const float GIZMO_SCALE = 5.0f;
  /** Conversion factor for millimeters to meters. */
  protected const float MM_TO_M = 1e-3f;
  /** Conversion factor for nanoseconds to seconds. */
  protected const float NS_TO_S = 1e-6f;
  /** Conversion factor for seconds to nanoseconds. */
  protected const float S_TO_NS = 1e6f;
  /** How much smoothing to use when calculating the FixedUpdate offset. */
  protected const float FIXED_UPDATE_OFFSET_SMOOTHING_DELAY = 0.1f;

  /** There always should be exactly one main HandController in the scene, which is reffered to by the HandController.Main getter. */
  public bool isMain = true;

  /** Whether to use a separate model for left and right hands (true); or mirror the same model for both hands (false). */
  [Space(8)]
  public bool separateLeftRight = false;
  /** The GameObject containing graphics to use for the left hand or both hands if separateLeftRight is false. */
  public HandModel leftGraphicsModel;
  /** The graphics hand model to use for the right hand. */
  public HandModel rightGraphicsModel;
  /** The GameObject containing colliders to use for the left hand or both hands if separateLeftRight is false. */
  public HandModel leftPhysicsModel;
  /** The physics hand model to use for the right hand. */
  public HandModel rightPhysicsModel;

  /** The GameObject containing both graphics and colliders for tools. */
  public ToolModel toolModel;

  /** Set true if the Leap Motion hardware is mounted on an HMD; otherwise, leave false. */
  [Space(8)]
  public bool isHeadMounted = false;
  /** Reverses the z axis. */
  public bool mirrorZAxis = false;

  /** If hands are in charge of Destroying themselves, make this false. */
  public bool destroyHands = true;

  /** The scale factors for hand movement. Set greater than 1 to give the hands a greater range of motion. */
  public Vector3 handMovementScale = Vector3.one;

  /** If enabled, do not query the controller to determine device type, but instead always return a specific device. */
  [Space(8)]
  public bool overrideDeviceType = false;

  /** If overrideDeviceType is enabled, the hand controller will return a device of this type. */
  public LeapDeviceType overrideDeviceTypeWith = LeapDeviceType.Peripheral;

  // Recording parameters.
  /** Set true to enable recording. */
  [Space(8)]
  public bool enableRecordPlayback = false;
  /** The file to record or playback from. */
  public TextAsset recordingAsset;
  /** Playback speed. Set to 1.0 for normal speed. */
  public float recorderSpeed = 1.0f;
  /** Whether to loop the playback. */
  public bool recorderLoop = true;

  public delegate void LifecycleEventHandler(HandController handController);
  /* Called at the end of the MonoBehavior Start() function */
  public event LifecycleEventHandler onStart;

  public delegate void handEvent(HandModel hand);
  /** Called in the Update cycle in which a hand has been created, after initialization. */
  public event handEvent onCreateHand;
  /** Called in the Update cycle in which a hand will be destroyed, before destruciton. */
  public event handEvent onDestroyHand;

  /** The object used to control recording and playback.*/
  protected LeapRecorder recorder_;

  /** The underlying Leap Motion Controller object.*/
  protected Controller leap_controller_;

  /** The list of all hand graphic objects owned by this HandController.*/
  protected Dictionary<int, HandModel> hand_graphics_ = new Dictionary<int, HandModel>();
  /** The list of all hand physics objects owned by this HandController.*/
  protected Dictionary<int, HandModel> hand_physics_ = new Dictionary<int, HandModel>();
  /** The list of all tool objects owned by this HandController.*/
  protected Dictionary<int, ToolModel> tools_ = new Dictionary<int, ToolModel>();

  protected bool graphicsEnabled = true;
  protected bool physicsEnabled = true;

  public bool GraphicsEnabled {
    get {
      return graphicsEnabled;
    }
    set {
      graphicsEnabled = value;
      if (!graphicsEnabled) {
        DestroyGraphicsHands();
      }
    }
  }

  public bool PhysicsEnabled {
    get {
      return physicsEnabled;
    }
    set {
      physicsEnabled = value;
      if (!physicsEnabled) {
        DestroyPhysicsHands();
      }
    }
  }

  private bool flag_initialized_ = false;

  private int curr_frame_count = -1;
  private Frame curr_image_frame = null;
  private Frame curr_frame = null;

  private long prev_graphics_id_ = 0;
  private long prev_physics_id_ = 0;

  /** The smoothed offset between the FixedUpdate timeline and the Leap timeline.  
   * Used to provide temporally correct frames within FixedUpdate */
  private SmoothedFloat smoothedFixedUpdateOffset_ = new SmoothedFloat();
  /** The maximum offset calculated per frame */
  private float perFrameFixedUpdateOffset_;

  /** Draws the Leap Motion gizmo when in the Unity editor. */
  void OnDrawGizmos() {
    // Draws the little Leap Motion Controller in the Editor view.
    Gizmos.matrix = Matrix4x4.Scale(GIZMO_SCALE * Vector3.one);
    Gizmos.DrawIcon(transform.position, "leap_motion.png");
  }

  /** 
  * Initializes the Leap Motion policy flags.
  * The POLICY_OPTIMIZE_HMD flag improves tracking for head-mounted devices.
  */
  void InitializeFlags() {
    // Optimize for top-down tracking if on head mounted display.
    Controller.PolicyFlag policy_flags = leap_controller_.PolicyFlags;
    if (isHeadMounted)
      policy_flags |= Controller.PolicyFlag.POLICY_OPTIMIZE_HMD;
    else
      policy_flags &= ~Controller.PolicyFlag.POLICY_OPTIMIZE_HMD;

    leap_controller_.SetPolicyFlags(policy_flags);
  }

  /** Creates a new Leap Controller object. */
  void Awake() {
    leap_controller_ = new Controller();
    recorder_ = new LeapRecorder();
  }

  void OnEnable() {
    if (isMain) {
      _mains.Add(this);
    }
    _all.Add(this);
  }

  /** Initalizes the hand and tool lists and recording, if enabled.*/
  void Start() {
    smoothedFixedUpdateOffset_.delay = FIXED_UPDATE_OFFSET_SMOOTHING_DELAY;

    if (enableRecordPlayback && recordingAsset != null)
      recorder_.Load(recordingAsset);

    LifecycleEventHandler handler = onStart;
    if (handler != null) {
      handler(this);
    }
  }

  /* Calling this sets this Hand Controller as the main Hand Controller.  If there was a previous main 
   * Hand Controller it is demoted and is no longer the main Hand Controller.
   */
  public void SetMain(bool shouldBeMain) {
    if (isMain != shouldBeMain) {
      isMain = shouldBeMain;
      if (isMain) {
        _mains.Add(this);
      } else {
        _mains.Remove(this);
      }
    }
  }

  /**
  * Turns off collisions between the specified GameObject and all hands.
  * Subject to the limitations of Unity Physics.IgnoreCollisions(). 
  * See http://docs.unity3d.com/ScriptReference/Physics.IgnoreCollision.html.
  */
  public void IgnoreCollisionsWithHands(GameObject to_ignore, bool ignore = true) {
    foreach (HandModel hand in hand_physics_.Values)
      Leap.Utils.IgnoreCollisions(hand.gameObject, to_ignore, ignore);
  }

  /** Creates a new HandModel instance. */
  protected HandModel CreateHand(Hand leap_hand, HandModel model) {
    HandModel hand_model = Instantiate(model, transform.position, transform.rotation)
                           as HandModel;
    hand_model.gameObject.SetActive(true);
    Leap.Utils.IgnoreCollisions(hand_model.gameObject, gameObject);
    hand_model.transform.SetParent(transform);
    hand_model.SetLeapHand(leap_hand);
    hand_model.MirrorZAxis(mirrorZAxis);
    hand_model.SetController(this);

    handEvent handHandler = onCreateHand;
    if (handHandler != null) {
      handHandler(hand_model);
    }

    return hand_model;
  }

  /** 
  * Destroys a HandModel instance if HandController.destroyHands is true (the default).
  * If you set destroyHands to false, you must destroy the hand instances elsewhere in your code.
  */
  protected void DestroyHand(HandModel hand_model) {
    handEvent handHandler = onDestroyHand;
    if (handHandler != null) {
      handHandler(hand_model);
    }
    if (destroyHands)
      Destroy(hand_model.gameObject);
    else
      hand_model.SetLeapHand(null);
  }

  /** 
  * Updates hands based on tracking data in the specified Leap HandList object.
  * Active HandModel instances are updated if the hand they represent is still
  * present in the Leap HandList; otherwise, the HandModel is removed. If new
  * Leap Hand objects are present in the Leap HandList, new HandModels are 
  * created and added to the HandController hand list. 
  * @param all_hands The dictionary containing the HandModels to update.
  * @param leap_hands The list of hands from the a Leap Frame instance.
  * @param left_model The HandModel instance to use for new left hands.
  * @param right_model The HandModel instance to use for new right hands.
  */
  protected void UpdateHandModels(Dictionary<int, HandModel> all_hands,
                                  HandList leap_hands,
                                  HandModel left_model, HandModel right_model) {
    List<int> ids_to_check = new List<int>(all_hands.Keys);

    // Go through all the active hands and update them.
    int num_hands = leap_hands.Count;
    for (int h = 0; h < num_hands; ++h) {
      Hand leap_hand = leap_hands[h];

      HandModel model = (mirrorZAxis != leap_hand.IsLeft) ? left_model : right_model;

      // If we've mirrored since this hand was updated, destroy it.
      if (all_hands.ContainsKey(leap_hand.Id) &&
          all_hands[leap_hand.Id].IsMirrored() != mirrorZAxis) {
        DestroyHand(all_hands[leap_hand.Id]);
        all_hands.Remove(leap_hand.Id);
      }

      // Only create or update if the hand is enabled.
      if (model != null) {
        ids_to_check.Remove(leap_hand.Id);

        // Create the hand and initialized it if it doesn't exist yet.
        if (!all_hands.ContainsKey(leap_hand.Id)) {
          HandModel new_hand = CreateHand(leap_hand, model);

          // Set scaling based on reference hand.
          float hand_scale = MM_TO_M * leap_hand.PalmWidth / new_hand.handModelPalmWidth;
          new_hand.transform.localScale = hand_scale * Vector3.one;

          new_hand.InitHand();
          new_hand.UpdateHand();
          all_hands[leap_hand.Id] = new_hand;
        } else {
          // Make sure we update the Leap Hand reference.
          HandModel hand_model = all_hands[leap_hand.Id];
          hand_model.SetLeapHand(leap_hand);
          hand_model.MirrorZAxis(mirrorZAxis);

          // Set scaling based on reference hand.
          float hand_scale = MM_TO_M * leap_hand.PalmWidth / hand_model.handModelPalmWidth;
          hand_model.transform.localScale = hand_scale * Vector3.one;
          hand_model.UpdateHand();
        }
      }
    }

    // Destroy all hands with defunct IDs.
    for (int i = 0; i < ids_to_check.Count; ++i) {
      DestroyHand(all_hands[ids_to_check[i]]);
      all_hands.Remove(ids_to_check[i]);
    }
  }

  /** Creates a ToolModel instance. */
  protected ToolModel CreateTool(ToolModel model) {
    ToolModel tool_model = Instantiate(model, transform.position, transform.rotation) as ToolModel;
    tool_model.gameObject.SetActive(true);
    Leap.Utils.IgnoreCollisions(tool_model.gameObject, gameObject);
    return tool_model;
  }

  /** 
  * Updates tools based on tracking data in the specified Leap ToolList object.
  * Active ToolModel instances are updated if the tool they represent is still
  * present in the Leap ToolList; otherwise, the ToolModel is removed. If new
  * Leap Tool objects are present in the Leap ToolList, new ToolModels are 
  * created and added to the HandController tool list. 
  * @param all_tools The dictionary containing the ToolModels to update.
  * @param leap_tools The list of tools from the a Leap Frame instance.
  * @param model The ToolModel instance to use for new tools.
  */
  protected void UpdateToolModels(Dictionary<int, ToolModel> all_tools,
                                  ToolList leap_tools, ToolModel model) {
    List<int> ids_to_check = new List<int>(all_tools.Keys);

    // Go through all the active tools and update them.
    int num_tools = leap_tools.Count;
    for (int h = 0; h < num_tools; ++h) {
      Tool leap_tool = leap_tools[h];

      // Only create or update if the tool is enabled.
      if (model) {

        ids_to_check.Remove(leap_tool.Id);

        // Create the tool and initialized it if it doesn't exist yet.
        if (!all_tools.ContainsKey(leap_tool.Id)) {
          ToolModel new_tool = CreateTool(model);
          new_tool.SetController(this);
          new_tool.SetLeapTool(leap_tool);
          new_tool.InitTool();
          all_tools[leap_tool.Id] = new_tool;
        }

        // Make sure we update the Leap Tool reference.
        ToolModel tool_model = all_tools[leap_tool.Id];
        tool_model.SetLeapTool(leap_tool);
        tool_model.MirrorZAxis(mirrorZAxis);

        // Set scaling.
        tool_model.transform.localScale = Vector3.one;

        tool_model.UpdateTool();
      }
    }

    // Destroy all tools with defunct IDs.
    for (int i = 0; i < ids_to_check.Count; ++i) {
      Destroy(all_tools[ids_to_check[i]].gameObject);
      all_tools.Remove(ids_to_check[i]);
    }
  }

  /** Returns the Leap Controller instance. */
  public Controller GetLeapController() {
#if UNITY_EDITOR
    //Do a null check to deal with hot reloading
    if(leap_controller_ == null) {
      leap_controller_ = new Controller();
      InitializeFlags();
    }
#endif
    return leap_controller_;
  }

  /** Returns the Leap Recorder instance used by this Hand Controller. */
  public LeapRecorder GetLeapRecorder() {
    return recorder_;
  }

  /**
  * Returns the latest frame object.
  *
  * If the recorder object is playing a recording, then the frame is taken from the recording.
  * Otherwise, the frame comes from the Leap Motion Controller itself.
  * 
  * The returned frame does not contain any image data, use GetImageFrame() for that.
  */
  public virtual Frame GetFrame() {
    if (enableRecordPlayback && (recorder_.state == RecorderState.Playing || recorder_.state == RecorderState.Paused))
      return recorder_.GetCurrentFrame();

    ensureFramesUpToDate();
    return curr_frame;
  }

  /* Returns the latest frame object. 
   * 
   * This method returns a frame object that contains Image data.  It is the users responsibility to make sure
   * they dispose any objects they obtain from this frame, since it is linked to the images and could result
   * in large memory increase if they are not disposed of properly.  
   * 
   * If the recorder object is playing a recording, then this method will return null.
   */
  public virtual Frame GetImageFrame() {
    if (enableRecordPlayback && (recorder_.state == RecorderState.Playing || recorder_.state == RecorderState.Paused))
      return null;

    ensureFramesUpToDate();
    return curr_image_frame;
  }

  protected virtual void ensureFramesUpToDate() {
    //Ensure that we update curr_frame every Update cycle.  curr_frame stays the same until the next Update.
    if (curr_frame == null || curr_image_frame == null || Time.frameCount != curr_frame_count) {
      if (curr_image_frame != null) {
        curr_image_frame.Dispose();
        curr_image_frame = null;
      }

      curr_image_frame = GetLeapController().Frame();
      curr_frame = GetImagelessFrame(curr_image_frame, false);
      curr_frame_count = Time.frameCount;
    }
  }

  /**
   * NOTE: This method should ONLY be called from within a FixedUpdate callback.
   * 
   * Unity Physics runs at a constant frame step, where the physics time between each FixedUpdate is the same. However
   * there is a big difference between the physics timeline and the real timeline.  In Unity, these timelines can be
   * very skewed, where the actual times FixedUpdate is called can vary greatly.  For example, the graph below
   * shows the real times when FixedUpdate was called.  
   * 
   * \image html images/GetFixedFrame_FixedUpdateCluster_Graph.png
   * 
   * The graph shows major clustering occuring of FixedUpdate calls, rather than an even spread.  Specifically, Unity
   * always executes all of the FixedUpdate calls at the *begining* of an Update frame, and then performs interpolation
   * to convert physics objects from the physics timeline to the real timeline.
   * 
   * This causes an issue when we need to aquire a Leap Frame from within FixedUpdate, since we need to provide a Frame
   * to the physics timeline, but the Leap provides frames from the real timeline.  The image below shows what happens
   * when we simply sample controller.Frame() from within FixedUpdate.  The X axis represents Time.fixedTime, and the Y
   * axis represents the Frame.Timestamp
   * 
   * \image html images/GetFixedFrame_Naive_Graph.png
   * 
   * The graph shows how, from the perspective of the physics timeline, the Frames are arriving in a jagged way, staying
   * the same for a large amount of time before jumping a large amount forward.  Ideally we would be able to take advantage
   * of the full 120FPS of frames the service provides, properly interpolated into the physics timeline.  
   * 
   * GetFixedFrame attempts to establish a conversion from the real timeline to the physics timeline, to provide both
   * uniformly sampled Frames, as well as not introducing latency.  The graph below shows a comparison between the naive
   * method of sampling the most recent frame (red), and the usage of GetFixedFrame (green).  The X axis represents Time.fixedTime
   * while the Y axis represents the Frame.Timestamp obtained by the 2 methods
   * 
   * \image html images/GetFixedFrame_Comparison_Graph.png
   * 
   * As the graph shows, the GetFixedFrame method can significantly help solve the judder that can occur when sampling
   * controller.Frame while in FixedUpdate.
   * 
   * ALSO: If the recorder object is playing a recording, then the frame is taken directly from the recording,
   * with no timeline synchronization performed.
   */
  public virtual Frame GetFixedFrame() {
    if (enableRecordPlayback && (recorder_.state == RecorderState.Playing || recorder_.state == RecorderState.Paused))
      return recorder_.GetCurrentFrame();

    //Aproximate the correct timestamp given the current fixed time
    float correctedTimestamp = (Time.fixedTime + smoothedFixedUpdateOffset_.value) * S_TO_NS;

    //Search the leap history for a frame with a timestamp closest to the corrected timestamp
    Frame closestFrame = GetLeapController().Frame();
    for (int searchHistoryIndex = 1; searchHistoryIndex < 60; searchHistoryIndex++) {
      Frame historyFrame = GetLeapController().Frame(searchHistoryIndex);

      //If we reach an invalid frame, terminate the search
      if (!historyFrame.IsValid) {
        historyFrame.Dispose();
        break;
      }

      if (Mathf.Abs(historyFrame.Timestamp - correctedTimestamp) < Mathf.Abs(closestFrame.Timestamp - correctedTimestamp)) {
        closestFrame.Dispose();
        closestFrame = historyFrame;
      } else {
        //Since frames are always reported in order, we can terminate the search once we stop finding a closer frame
        historyFrame.Dispose();
        break;
      }
    }

    return GetImagelessFrame(closestFrame, true);
  }

  /** Updates the graphics objects. */
  protected virtual void Update() {
    UpdateRecorder();
    Frame frame = GetFrame();

    if (frame != null && !flag_initialized_) {
      InitializeFlags();
    }

    if (frame.Id != prev_graphics_id_ && graphicsEnabled) {
      UpdateHandModels(hand_graphics_, frame.Hands, leftGraphicsModel, rightGraphicsModel);
      prev_graphics_id_ = frame.Id;
    }

    //perFrameFixedUpdateOffset_ contains the maximum offset of this Update cycle
    smoothedFixedUpdateOffset_.Update(perFrameFixedUpdateOffset_, Time.deltaTime);
  }

  /** Updates the physics objects */
  protected virtual void FixedUpdate() {
    //All FixedUpdates of a frame happen before Update, so only the last of these calculations is passed
    //into Update for smoothing.
    using (var latestFrame = GetLeapController().Frame()) {
      perFrameFixedUpdateOffset_ = latestFrame.Timestamp * NS_TO_S - Time.fixedTime;
    }

    Frame frame = GetFixedFrame();

    if (frame.Id != prev_physics_id_ && physicsEnabled) {
      UpdateHandModels(hand_physics_, frame.Hands, leftPhysicsModel, rightPhysicsModel);
      UpdateToolModels(tools_, frame.Tools, toolModel);
      prev_physics_id_ = frame.Id;
    }
  }

  /** True, if the Leap Motion hardware is plugged in and this application is connected to the Leap Motion service. */
  public bool IsConnected() {
    return GetLeapController().IsConnected;
  }

  /** Returns information describing the device hardware. */
  public LeapDeviceInfo GetDeviceInfo() {
    if (overrideDeviceType) {
      return new LeapDeviceInfo(overrideDeviceTypeWith);
    }

    DeviceList devices = GetLeapController().Devices;
    if (devices.Count == 1) {
      LeapDeviceInfo info = new LeapDeviceInfo(LeapDeviceType.Invalid);
      // TODO: DeviceList does not tell us the device type. Dragonfly serial starts with "LE" and peripheral starts with "LP"
      if (devices[0].SerialNumber.Length >= 2) {
        switch (devices[0].SerialNumber.Substring(0, 2)) {
          case ("LP"):
            info = new LeapDeviceInfo(LeapDeviceType.Peripheral);
            break;
          case ("LE"):
            info = new LeapDeviceInfo(LeapDeviceType.Dragonfly);
            break;
          default:
            break;
        }
      }

      // TODO: Add baseline & offset when included in API
      // NOTE: Alternative is to use device type since all parameters are invariant
      info.isEmbedded = devices[0].IsEmbedded;
      info.horizontalViewAngle = devices[0].HorizontalViewAngle * Mathf.Rad2Deg;
      info.verticalViewAngle = devices[0].VerticalViewAngle * Mathf.Rad2Deg;
      info.trackingRange = devices[0].Range / 1000f;
      info.serialID = devices[0].SerialNumber;
      return info;
    } else if (devices.Count > 1) {
      return new LeapDeviceInfo(LeapDeviceType.Peripheral);
    }
    return new LeapDeviceInfo(LeapDeviceType.Invalid);
  }

  /** Returns a copy of the hand model list. */
  public HandModel[] GetAllGraphicsHands() {
    if (hand_graphics_ == null)
      return new HandModel[0];

    HandModel[] models = new HandModel[hand_graphics_.Count];
    hand_graphics_.Values.CopyTo(models, 0);
    return models;
  }

  /** Returns a copy of the physics model list. */
  public HandModel[] GetAllPhysicsHands() {
    if (hand_physics_ == null)
      return new HandModel[0];

    HandModel[] models = new HandModel[hand_physics_.Count];
    hand_physics_.Values.CopyTo(models, 0);
    return models;
  }

  /** Destroys all hands owned by this HandController instance. */
  public void DestroyAllHands() {
    DestroyGraphicsHands();
    DestroyPhysicsHands();
  }

  public void DestroyGraphicsHands() {
    if (hand_graphics_ != null) {
      foreach (HandModel model in hand_graphics_.Values)
        Destroy(model.gameObject);

      hand_graphics_.Clear();
    }
  }

  public void DestroyPhysicsHands() {
    if (hand_physics_ != null) {
      foreach (HandModel model in hand_physics_.Values)
        Destroy(model.gameObject);

      hand_physics_.Clear();
    }
  }

  void OnDisable() {
    if (isMain) {
      _mains.Remove(this);
    }

    _all.Remove(this);

    DestroyAllHands();
  }

  void OnDestroy() {
    DestroyAllHands();
  }

  /** The current frame position divided by the total number of frames in the recording. */
  public float GetRecordingProgress() {
    return recorder_.GetProgress();
  }

  /** Stops recording or playback and resets the frame counter to the beginning. */
  public void StopRecording() {
    recorder_.Stop();
  }

  /** Start getting frames from the LeapRecorder object rather than the Leap service. */
  public void PlayRecording() {
    recorder_.Play();
  }

  /** Stops playback or recording without resetting the frame counter. */
  public void PauseRecording() {
    recorder_.Pause();
  }

  /** 
  * Saves the current recording to a new file, returns the path, and starts playback.
  * @return string The path to the saved recording.
  */
  public string FinishAndSaveRecording() {
    string path = recorder_.SaveToNewFile();
    recorder_.Play();
    return path;
  }

  /** Discards any frames recorded so far. */
  public void ResetRecording() {
    recorder_.Reset();
  }

  /** Starts saving frames. */
  public void Record() {
    recorder_.Record();
  }

  /** Called in Update() to send frames to the recorder. */
  protected void UpdateRecorder() {
    if (!enableRecordPlayback)
      return;

    recorder_.speed = recorderSpeed;
    recorder_.loop = recorderLoop;

    if (recorder_.state == RecorderState.Recording) {
      recorder_.AddFrame(GetLeapController().Frame());
    } else if (recorder_.state == RecorderState.Playing) {
      recorder_.NextFrame();
    }
  }

  public Vector3 ToUnitySpace(Vector vector) {
    return transform.TransformPoint(vector.ToUnityScaled());
  }

  public Vector3 ToUnityDir(Vector direction) {
    return transform.TransformDirection(direction.ToUnity());
  }

  public Quaternion ToUnityRot(Matrix basis) {
    return transform.rotation * basis.Rotation(false);
  }

  private static byte[] _cachedImagelessFrameByteArray = new byte[4096];
  public static Frame GetImagelessFrame(Frame original, bool disposeOriginal) {
    int length = original.SerializeLength;
    if (length > _cachedImagelessFrameByteArray.Length) {
      _cachedImagelessFrameByteArray = new byte[length * 2];
    }

    original.SerializeWithArg(_cachedImagelessFrameByteArray);
    if (disposeOriginal) {
      original.Dispose();
    }

    Frame imagelessFrame = new Frame();
    imagelessFrame.DeserializeWithLength(_cachedImagelessFrameByteArray, length);
    return imagelessFrame;
  }
}
