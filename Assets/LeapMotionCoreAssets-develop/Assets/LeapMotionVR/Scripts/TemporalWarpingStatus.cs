using UnityEngine;
using UnityEngine.UI;
using Leap;
using System.Collections;

public class TemporalWarpingStatus : MonoBehaviour {
  public LeapVRTemporalWarping cameraAlignment;

  protected Text textField;

  protected SmoothedFloat _imageLatency = new SmoothedFloat();
  protected SmoothedFloat _frameDelta = new SmoothedFloat();

  void Start () {
    textField = GetComponent<Text> ();
    if (textField == null) {
      gameObject.SetActive(false);
    }

    _imageLatency.delay = 0.1f;
    _frameDelta.delay = 0.1f;
  }
	
	// Update is called once per frame
  void Update () {
    if (cameraAlignment == null) {
      Debug.Log("TemporalWarpingStatus requires LeapCameraAlignment reference -> status will be disabled");
      gameObject.SetActive(false);
      return;
    }

    if (!cameraAlignment.isActiveAndEnabled) {
      return;
    }

    using(ImageList list = HandController.Main.GetImageFrame().Images){
      using (Leap.Image image = list[0]) {
        float latency = HandController.Main.GetLeapController().Now() - image.Timestamp;
        _imageLatency.Update(latency, Time.deltaTime);
      }
    }

    _frameDelta.Update(Time.deltaTime, Time.deltaTime);

    string statusText = "IMAGE LATENCY: " + (_imageLatency.value / 1000f).ToString("#00.0") + " ms\n";
    statusText += "FRAME DELTA: " + (_frameDelta.value * 1000).ToString ("#00.0") + " ms\n";
    statusText += "REWIND ADJUST: " + (cameraAlignment.RewindAdjust).ToString ("#00.0") + " ms\n";

    textField.text = statusText;
	}
}
